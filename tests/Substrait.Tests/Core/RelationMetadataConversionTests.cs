// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
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

public sealed class RelationMetadataConversionTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task AllSupportedRelationsPreserveMetadataAndPresence(int variant)
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

            await Assert.That(converted).IsEqualTo(relation).Because(name);
            await Assert.That(converted.GetHashCode()).IsEqualTo(relation.GetHashCode()).Because(name);
            await Assert.That(converted.RecordType).IsEqualTo(relation.RecordType).Because(name);
            await Assert.That(converted.Metadata).IsEqualTo(relation.Metadata).Because(name);
            await Assert.That(new PlanToProtoConverter().From(parsed)).IsEqualTo(wire).Because(name);

            ProtoRel standaloneWire = new RelToProtoConverter().From(relation);
            IRel standalone = Reader().ToRel(standaloneWire);
            await Assert.That(standalone).IsEqualTo(relation).Because(name);
            await Assert.That(new RelToProtoConverter().From(standalone)).IsEqualTo(standaloneWire).Because(name);
        }
    }

    [Test]
    public async Task OperatorFixturesCoverEverySerializableConcreteRelation()
    {
        System.Type[] actual = Operators(RelationMetadata.Direct).Select(op => op.GetType()).ToArray();
        System.Type[] expected = typeof(IRel).Assembly.GetTypes().Where(type =>
            type.IsSealed && typeof(IRel).IsAssignableFrom(type) && type != typeof(Reference)).ToArray();
        await Assert.That(actual).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Any);
    }

    [Test]
    public async Task NamedTableExtensionsStayAtTheirDistinctLocations()
    {
        RelCommon common = MetadataFacadeTests.CreateCommon();
        common.Emit = null;
        var metadata = new RelationMetadata(ReadOnlyRelCommon.FromProto(common),
            ReadOnlyAdvancedExtension.FromProto(common.Hint.AdvancedExtension));
        NamedTableRead read = new(metadata, Schema(), ["orders"], null,
            ReadOnlyAdvancedExtension.FromProto(common.Hint.Constraint.AdvancedExtension));

        ProtoRel wire = new RelToProtoConverter().From(read);
        await Assert.That(wire.Read.Common).IsEqualTo(common);
        await Assert.That(wire.Read.AdvancedExtension).IsEqualTo(common.Hint.AdvancedExtension);
        await Assert.That(wire.Read.NamedTable.AdvancedExtension).IsEqualTo(common.Hint.Constraint.AdvancedExtension);
        NamedTableRead converted = (NamedTableRead)Reader().ToRel(wire);
        await Assert.That(converted).IsEqualTo(read);
        await Assert.That(converted.GetHashCode()).IsEqualTo(read.GetHashCode());
        await Assert.That(converted.InitialSchema).IsEqualTo(Schema());
        await Assert.That(new NamedTableRead(metadata, Schema(), ["orders"], null)).IsNotEqualTo(read);
    }

    [Test]
    public async Task CallerOwnedImportsAndAllExportsAreDetached()
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

        await Assert.That(new PlanToProtoConverter().From(converted)).IsEqualTo(expected);
        await Assert.That(new RelToProtoConverter().From(standalone)).IsEqualTo(expected.Relations[0].Root.Input);
        await Assert.That(converted.GetHashCode()).IsEqualTo(hash);
        ProtoPlan exported = new PlanToProtoConverter().From(converted);
        exported.Relations[0].Root.Input.Read.Common.Hint.Alias = "export changed";
        exported.Relations[0].Root.Input.Read.NamedTable.AdvancedExtension.Optimization.Clear();
        await Assert.That(new PlanToProtoConverter().From(converted)).IsEqualTo(expected);
    }

    [Test]
    public async Task MetadataIsTheOnlySourceOfEmitBehavior()
    {
        Remap remap = new([0, 0]);
        RelationMetadata metadata = RelationMetadata.FromRemap(remap);
        NamedTableRead read = Read(metadata);
        await Assert.That(read.Transmute).IsSameReferenceAs(metadata.Transmute);
        await Assert.That(read.RecordType.Fields.Count).IsEqualTo(2);
        await Assert.That(read.Transmute).IsEqualTo(remap);
        await Assert.That(Read().Metadata.Common!.EmitKindCase).IsEqualTo(RelCommon.EmitKindOneofCase.Direct);
        await Assert.That(new NamedTableRead(Schema(), ["orders"], null, null).Metadata.Common!.EmitKindCase).IsEqualTo(RelCommon.EmitKindOneofCase.Direct);

        RelCommon common = new() { Hint = new() { Alias = "hint-only", OutputNames = { "hinted_name" } } };
        NamedTableRead hinted = Read(new(ReadOnlyRelCommon.FromProto(common)));
        await Assert.That(hinted.RecordType).IsEqualTo(Read().RecordType);
        await Assert.That(hinted.InitialSchema.Names.ToArray()).IsEquivalentTo(Read().InitialSchema.Names.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(hinted).IsNotEqualTo(Read());
        await Assert.That(new Reference(0, hinted).Metadata.Common).IsNull();
    }

    [Test]
    public async Task StandaloneConvertersRejectExplicitZeroAnchor()
    {
        NamedTableRead read = Read(new(ReadOnlyRelCommon.FromProto(new() { RelAnchor = 0 })));
        await Assert.That(() => new RelToProtoConverter().From(read)).ThrowsExactly<ArgumentException>();
        ProtoRel wire = new RelToProtoConverter().From(Read());
        wire.Read.Common.RelAnchor = 0;
        await Assert.That(() => Reader().ToRel(wire)).ThrowsExactly<SerializationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task MetadataSurvivesInsideEverySubqueryKind(int kind)
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
        await Assert.That(subquery.Metadata.Common!.ToProto()).IsEqualTo(nested.Read.Common);
        await Assert.That(subquery.Metadata.AdvancedExtension!.ToProto()).IsEqualTo(nested.Read.AdvancedExtension);
        await Assert.That(new PlanToProtoConverter().From(plan)).IsEqualTo(wire);
    }

    internal static ProtoToPlanConverter Decoder() => new(new ExtensionsCollection(), new ExtensionSchemaResolver());

    internal static ProtoToRelConverter Reader() => new(
        new ExtensionsDictionary.Builder().Build(), new ExtensionsCollection(), new ExtensionSchemaResolver(), ExtensionsDictionary.StrictMode.OFF);

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
        RelationMetadata commonOnly = new(metadata.Common);
        yield return new ExtensionLeaf(commonOnly, detail: null, Schema().Struct);
        yield return new ExtensionSingle(commonOnly, input, detail: null, Schema().Struct);
        yield return new ExtensionMulti(commonOnly, [input, Read()], detail: null, Schema().Struct);
    }

    private sealed class ExtensionSchemaResolver : IExtensionRelationSchemaResolver
    {
        public ParameterizedType.Struct? Resolve(ExtensionRelationKind kind, ReadOnlyAny? detail, IReadOnlyList<IRel> inputs) =>
            Schema().Struct;
    }
}
