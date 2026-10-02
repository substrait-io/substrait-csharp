// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Expression;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using Substrait.Protobuf;
using CorePlan = Substrait.Core.Plan.Plan;
using CoreSortField = Substrait.Core.Expression.SortField;
using NamedStruct = Substrait.Core.Type.NamedStruct;
using ProtoPlan = Substrait.Protobuf.Plan;
using ProtoRel = Substrait.Protobuf.Rel;

namespace Substrait.Tests.Core;

[TestClass]
public sealed class RelationMetadataConversionTests
{
    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public void AllSupportedRelationsPreserveMetadataAndPresence(int variant)
    {
        RelCommon? common = variant switch
        {
            0 => null,
            1 => new(),
            2 => new() { Direct = new() },
            3 => new() { Emit = new() },
            4 => new() { Hint = new() { Stats = new(), Constraint = new() }, AdvancedExtension = new() },
            5 => MetadataFacadeTests.CreateCommon(),
            6 => new() { Emit = new() { OutputMapping = { 0, 0 } } },
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        AdvancedExtension? extension = variant >= 4 ? MetadataFacadeTests.CreateCommon().AdvancedExtension : null;
        RelationMetadata metadata = new(
            common is null ? null : ReadOnlyRelCommon.FromProto(common),
            extension is null ? null : ReadOnlyAdvancedExtension.FromProto(extension));
        if (variant == 5)
        {
            // The all-fields fixture maps a three-column output; these operators need only one.
            RelCommon adjusted = common!.Clone();
            adjusted.Emit.OutputMapping.Clear();
            adjusted.Emit.OutputMapping.Add(0);
            metadata = new(ReadOnlyRelCommon.FromProto(adjusted), metadata.AdvancedExtension);
        }

        foreach (IRel relation in Operators(metadata))
        {
            ProtoPlan wire = new PlanToProtoConverter().From(Plan(relation));
            IPlan parsed = Decoder().FromBytes(wire.ToByteArray(), ExtensionsDictionary.StrictMode.OFF);
            IRel converted = parsed.Relations[0].Input;
            string name = relation.GetType().Name;

            Assert.AreEqual(relation, converted, name);
            Assert.AreEqual(relation.GetHashCode(), converted.GetHashCode(), name);
            Assert.AreEqual(relation.RecordType, converted.RecordType, name);
            Assert.AreEqual(metadata, converted.Metadata, name);
            Assert.AreEqual(wire, new PlanToProtoConverter().From(parsed), name);

            ProtoRel standaloneWire = new RelToProtoConverter().From(relation);
            IRel standalone = Reader().ToRel(standaloneWire);
            Assert.AreEqual(relation, standalone, name);
            Assert.AreEqual(standaloneWire, new RelToProtoConverter().From(standalone), name);
        }
    }

    [TestMethod]
    public void OperatorFixturesCoverEverySerializableConcreteRelation()
    {
        System.Type[] actual = Operators(RelationMetadata.Direct).Select(op => op.GetType()).ToArray();
        System.Type[] expected = typeof(IRel).Assembly.GetTypes().Where(type =>
            type.IsSealed && typeof(IRel).IsAssignableFrom(type) && type != typeof(Reference)).ToArray();
        CollectionAssert.AreEquivalent(expected, actual);
    }

    [TestMethod]
    public void NamedTableExtensionsStayAtTheirDistinctLocations()
    {
        RelCommon common = MetadataFacadeTests.CreateCommon();
        common.Emit = null;
        var metadata = new RelationMetadata(ReadOnlyRelCommon.FromProto(common),
            ReadOnlyAdvancedExtension.FromProto(common.Hint.AdvancedExtension));
        NamedTableRead read = new(metadata, Schema(), ["orders"], null,
            ReadOnlyAdvancedExtension.FromProto(common.Hint.Constraint.AdvancedExtension));

        ProtoRel wire = new RelToProtoConverter().From(read);
        Assert.AreEqual(common, wire.Read.Common);
        Assert.AreEqual(common.Hint.AdvancedExtension, wire.Read.AdvancedExtension);
        Assert.AreEqual(common.Hint.Constraint.AdvancedExtension, wire.Read.NamedTable.AdvancedExtension);
        NamedTableRead converted = (NamedTableRead)Reader().ToRel(wire);
        Assert.AreEqual(read, converted);
        Assert.AreEqual(read.GetHashCode(), converted.GetHashCode());
        Assert.AreEqual(Schema(), converted.InitialSchema);
        Assert.AreNotEqual(read, new NamedTableRead(metadata, Schema(), ["orders"], null));
    }

    [TestMethod]
    public void CallerOwnedImportsAndAllExportsAreDetached()
    {
        RelCommon common = MetadataFacadeTests.CreateCommon();
        common.Emit = null;
        var metadata = new RelationMetadata(ReadOnlyRelCommon.FromProto(common), ReadOnlyAdvancedExtension.FromProto(common.AdvancedExtension));
        NamedTableRead read = new(metadata, Schema(), ["orders"], null, metadata.AdvancedExtension);
        ProtoPlan wire = new PlanToProtoConverter().From(Plan(read));
        ProtoPlan expected = wire.Clone();
        IPlan converted = Decoder().From(wire, ExtensionsDictionary.StrictMode.OFF);
        IRel standalone = Reader().ToRel(wire.Relations[0].Root.Input);
        int hash = converted.GetHashCode();
        wire.Relations[0].Root.Input.Read.Common.Hint.Alias = "changed";
        wire.Relations[0].Root.Input.Read.Common.RelAnchor = 0;
        wire.Relations[0].Root.Input.Read.AdvancedExtension.Optimization.Clear();
        wire.Relations[0].Root.Input.Read.NamedTable.AdvancedExtension.Enhancement.Value = ByteString.Empty;

        Assert.AreEqual(expected, new PlanToProtoConverter().From(converted));
        Assert.AreEqual(expected.Relations[0].Root.Input, new RelToProtoConverter().From(standalone));
        Assert.AreEqual(hash, converted.GetHashCode());
        ProtoPlan exported = new PlanToProtoConverter().From(converted);
        exported.Relations[0].Root.Input.Read.Common.Hint.Alias = "export changed";
        exported.Relations[0].Root.Input.Read.NamedTable.AdvancedExtension.Optimization.Clear();
        Assert.AreEqual(expected, new PlanToProtoConverter().From(converted));
    }

    [TestMethod]
    public void MetadataIsTheOnlySourceOfEmitBehavior()
    {
        Remap remap = new([0, 0]);
        RelationMetadata metadata = RelationMetadata.FromRemap(remap);
        NamedTableRead read = Read(metadata);
        Assert.AreSame(metadata.Transmute, read.Transmute);
        Assert.AreEqual(2, read.RecordType.Fields.Count);
        Assert.AreEqual(remap, read.Transmute);
        Assert.AreEqual(RelCommon.EmitKindOneofCase.Direct, Read().Metadata.Common!.EmitKindCase);
        Assert.AreEqual(RelCommon.EmitKindOneofCase.Direct, new NamedTableRead(Schema(), ["orders"], null, null).Metadata.Common!.EmitKindCase);

        RelCommon common = new() { Hint = new() { Alias = "hint-only", OutputNames = { "hinted_name" } } };
        NamedTableRead hinted = Read(new(ReadOnlyRelCommon.FromProto(common)));
        Assert.AreEqual(Read().RecordType, hinted.RecordType);
        CollectionAssert.AreEqual(Read().InitialSchema.Names.ToArray(), hinted.InitialSchema.Names.ToArray());
        Assert.AreNotEqual(Read(), hinted);
        Assert.IsNull(new Reference(0, hinted).Metadata.Common);
    }

    [TestMethod]
    public void StandaloneConvertersRejectExplicitZeroAnchor()
    {
        NamedTableRead read = Read(new(ReadOnlyRelCommon.FromProto(new() { RelAnchor = 0 })));
        Assert.ThrowsException<ArgumentException>(() => new RelToProtoConverter().From(read));
        ProtoRel wire = new RelToProtoConverter().From(Read());
        wire.Read.Common.RelAnchor = 0;
        Assert.ThrowsException<SerializationException>(() => Reader().ToRel(wire));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void MetadataSurvivesInsideEverySubqueryKind(int kind)
    {
        ProtoRel nested = new RelToProtoConverter().From(Read());
        nested.Read.Common = MetadataFacadeTests.CreateCommon();
        nested.Read.Common.Emit = null;
        nested.Read.AdvancedExtension = nested.Read.Common.Hint.AdvancedExtension.Clone();
        ProtoRel relation = new()
        {
            Project = new()
            {
                Input = new RelToProtoConverter().From(Read()),
                Expressions = { PlanReferenceConversionTests.WireSubquery(kind, nested) },
            },
        };
        ProtoPlan wire = WirePlan(relation);
        IPlan plan = Decoder().FromBytes(wire.ToByteArray(), ExtensionsDictionary.StrictMode.OFF);
        IRel subquery = PlanReferenceConversionTests.GetSubquery(((Project)plan.Relations[0].Input).Expressions[0]);
        Assert.AreEqual(nested.Read.Common, subquery.Metadata.Common!.ToProto());
        Assert.AreEqual(nested.Read.AdvancedExtension, subquery.Metadata.AdvancedExtension!.ToProto());
        Assert.AreEqual(wire, new PlanToProtoConverter().From(plan));
    }

    internal static ProtoToPlanConverter Decoder() => new(new ExtensionsCollection());

    internal static ProtoToRelConverter Reader() => new(
        new ExtensionsDictionary.Builder().Build(), new ExtensionsCollection(), ExtensionsDictionary.StrictMode.OFF);

    internal static NamedStruct Schema() => new(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]));

    internal static NamedTableRead Read(RelationMetadata? metadata = null) =>
        new(metadata ?? RelationMetadata.Direct, Schema(), ["orders"], null);

    internal static CorePlan Plan(IRel relation) => new([new CorePlan.Root(relation, ["value"])], Substrait.Core.Plan.Version.Current);

    internal static ProtoPlan WirePlan(params ProtoRel[] relations)
    {
        ProtoPlan plan = new() { Version = new() { MinorNumber = 104, Producer = "metadata-tests" } };
        plan.Relations.AddRange(relations.Select(relation => new PlanRel { Rel = relation }));
        return plan;
    }

    private static IEnumerable<IRel> Operators(RelationMetadata metadata)
    {
        NamedTableRead input = Read();
        FieldReference field = new(TypeFactory.REQUIRED.I64, 0);
        Literal.BoolLiteral predicate = new(true);
        yield return Read(metadata);
        yield return new EmptyRead(metadata, Schema(), null);
        yield return new VirtualTableRead(metadata, Schema(), [new Substrait.Core.Expression.Expression.Struct([new Literal.I64Literal(1)])], null);
        yield return new Filter(metadata, input, predicate);
        yield return new Project(metadata, input, [field]);
        yield return new Fetch(metadata, input, new Literal.I64Literal(10), new Literal.I64Literal(0));
        yield return new Sort(metadata, input, [new CoreSortField(field, CoreSortField.SortDirection.SortDirectionAscNullsFirst)]);
        yield return new Aggregate(metadata, input, [field], [new Aggregate.Grouping([0])], []);
        yield return new Cross(metadata, input, Read());
        yield return new Join(metadata, input, Read(), AbstractJoin.JoinType.Inner, predicate, null);
        yield return new HashJoin(metadata, input, Read(), AbstractJoin.JoinType.Inner, [], null, HashJoin.BuildInput.Left);
        yield return new Set(metadata, Set.SetOp.UnionAll, [input, Read()]);
        yield return new ScatterExchange(metadata, input, 2, [field]);
        yield return new SingleBucketExchange(metadata, input, 2, new Literal.I64Literal(1));
    }
}
