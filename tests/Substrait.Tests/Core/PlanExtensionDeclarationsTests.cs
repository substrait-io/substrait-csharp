// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Types;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using Substrait.Protobuf;
using Substrait.Tools;
using CoreNamedStruct = Substrait.Core.Type.NamedStruct;
using CorePlan = Substrait.Core.Plan.Plan;
using CoreVersion = Substrait.Core.Plan.Version;
using ProtoPlan = Substrait.Protobuf.Plan;
using ProtoType = Substrait.Protobuf.Type;
using Rel = Substrait.Protobuf.Rel;
using StrictMode = Substrait.Core.Extension.ExtensionsDictionary.StrictMode;
using StringValue = Google.Protobuf.WellKnownTypes.StringValue;

namespace Substrait.Tests.Core;

public sealed class PlanExtensionDeclarationsTests
{
    private const string Urn = "extension:example:types";

    [Test]
    public async Task RetainsOpaqueDependenciesAndAliasesWithoutPerNodeAnchorProvenance()
    {
        ProtoPlan wire = CreateWirePlan();
        wire.ExtensionUrns[0].MergeFrom(new byte[] { 0xa0, 0x06, 0x01 });
        wire.Extensions[0].MergeFrom(new byte[] { 0xa0, 0x06, 0x01 });
        wire.Extensions[0].ExtensionType.MergeFrom(new byte[] { 0xa0, 0x06, 0x01 });
        wire.AdvancedExtensions = new()
        {
            Enhancement = new Any { TypeUrl = "type.example/ReferencesExtensionAnchor", Value = ByteString.CopyFromUtf8("29") },
        };
        wire.Relations[0].Root.Input.Read.AdvancedExtension = wire.AdvancedExtensions.Clone();
        ProtoPlan expected = wire.Clone();
        expected.Relations[0].Root.Input.Read.BaseSchema.Struct.Types_[0].UserDefined.TypeReference = 7;
        var decoder = new ProtoToPlanConverter(new ExtensionsCollection());
        using var stream = new MemoryStream(wire.ToByteArray());
        IPlan[] imports =
        [
            decoder.From(wire, StrictMode.OFF),
            decoder.FromBytes(wire.ToByteArray(), StrictMode.OFF),
            decoder.FromStream(stream, StrictMode.OFF),
        ];
        wire.ExtensionUrns[0].Urn = "changed";
        wire.Extensions[0].ExtensionType.Name = "changed";
        wire.Extensions.Clear();
        wire.AdvancedExtensions.Enhancement.Value = ByteString.Empty;

        foreach (IPlan imported in imports)
        {
            var fields = ((NamedTableRead)imported.Relations[0].Input).InitialSchema.Struct.Fields;
            await Assert.That(fields[0].Equals(fields[1], ITypeComparison.Strict)).IsTrue();
            var exporter = new PlanToProtoConverter();
            ProtoPlan exported = exporter.From(imported);
            await Assert.That(exported).IsEqualTo(expected);
            exported.ExtensionUrns.Clear();
            exported.Extensions[0].ExtensionType.TypeAnchor = 0;
            await Assert.That(exporter.From(imported)).IsEqualTo(expected);
            await Assert.That(imported.Metadata.Extensions.Count).IsEqualTo(5);
            await Assert.That(imported.Metadata.ExtensionUrns.Count).IsEqualTo(3);
        }
    }

    [Test]
    public async Task BuilderEditsKeepTablesAndNewDependenciesAvoidReservedAnchors()
    {
        ProtoPlan wire = CreateWirePlan();
        IPlan imported = new ProtoToPlanConverter(new ExtensionsCollection()).From(wire, StrictMode.OFF);
        var builder = new PlanBuilder(imported.Metadata, imported.Version);
        builder.AddRoot(imported.Relations[0].Input, ["first", "second"]);
        CorePlan before = builder.Build();
        builder.SetEnhancement(new StringValue { Value = "enhancement" });
        builder.AddOptimization(new StringValue { Value = "optimization" });
        builder.SetExpectedTypeUrls(["type.example/Value"]);
        builder.ClearAdvancedExtensions();
        builder.ClearExpectedTypeUrls();
        var variation = new TypeVariationImpl(Urn, "fresh", "custom", string.Empty, FunctionBehavior.INHERITS);
        var function = new Substrait.Core.Expression.Expression.ScalarFunctionInvocation(
            Urn, "fresh", [], TypeFactory.REQUIRED.I64, declaration: null);
        builder.AddRoot(new Project(Read("fresh", Urn, variation), [function]), ["value", "function"]);
        builder.AddRoot(Read("fresh", "extension:example:new"), ["value"]);
        CorePlan after = builder.Build();
        var encoder = new PlanToProtoConverter();
        ProtoPlan result = encoder.From(after);
        await Assert.That(after.Metadata.ExtensionUrns).IsSameReferenceAs(before.Metadata.ExtensionUrns);
        await Assert.That(after.Metadata.Extensions).IsSameReferenceAs(before.Metadata.Extensions);
        await Assert.That(result.ExtensionUrns.Take(3).SequenceEqual(wire.ExtensionUrns)).IsTrue();
        await Assert.That(result.Extensions.Take(5).SequenceEqual(wire.Extensions)).IsTrue();
        await Assert.That(result.ExtensionUrns.Last().ExtensionUrnAnchor).IsEqualTo(1U);
        await Assert.That(result.Extensions[5].ExtensionType.TypeAnchor).IsEqualTo(1U);
        await Assert.That(result.Extensions[5].ExtensionType.ExtensionUrnReference).IsEqualTo(5U);
        await Assert.That(result.Extensions[6].ExtensionType.TypeAnchor).IsEqualTo(2U);
        await Assert.That(result.Extensions[6].ExtensionType.ExtensionUrnReference).IsEqualTo(1U);
        await Assert.That(result.Extensions[7].ExtensionTypeVariation.TypeVariationAnchor).IsEqualTo(2U);
        await Assert.That(result.Extensions[8].ExtensionFunction.FunctionAnchor).IsEqualTo(1U);
        await Assert.That(result.Relations[1].Root.Input.Project.Input.Read.BaseSchema.Struct.Types_[0].UserDefined.TypeReference).IsEqualTo(1U);
        await Assert.That(result.Relations[1].Root.Input.Project.Input.Read.BaseSchema.Struct.Types_[0].UserDefined.TypeVariationReference).IsEqualTo(2U);
        await Assert.That(result.Relations[1].Root.Input.Project.Expressions[0].ScalarFunction.FunctionReference).IsEqualTo(1U);
        await Assert.That(result.Relations[2].Root.Input.Read.BaseSchema.Struct.Types_[0].UserDefined.TypeReference).IsEqualTo(2U);
        await Assert.That(encoder.From(after)).IsEqualTo(result);
        await Assert.That(encoder.From(before).Extensions.Count).IsEqualTo(5);
    }

    [Test]
    [Arguments(42U, false)]
    [Arguments(uint.MaxValue, false)]
    [Arguments(uint.MaxValue, true)]
    public async Task PreservesFullWidthAnchorsForEveryReferenceNamespace(uint anchor, bool aliases)
    {
        ProtoPlan wire = CreateWirePlan();
        wire.ExtensionUrns.Clear();
        wire.ExtensionUrns.Add(new SimpleExtensionURN { ExtensionUrnAnchor = anchor, Urn = Urn });
        wire.Extensions.Clear();
        wire.Extensions.Add(TypeDeclaration(anchor, anchor, "shape"));
        wire.Extensions.Add(new SimpleExtensionDeclaration
        {
            ExtensionTypeVariation = new() { TypeVariationAnchor = anchor, ExtensionUrnReference = anchor, Name = "custom" },
        });
        const string functionUrn = "extension:io.substrait:functions_arithmetic";
        wire.ExtensionUrns.Add(new SimpleExtensionURN { ExtensionUrnAnchor = 0, Urn = functionUrn });
        wire.Extensions.Add(new SimpleExtensionDeclaration
        {
            ExtensionFunction = new() { FunctionAnchor = anchor, ExtensionUrnReference = 0, Name = "add:i64_i64" },
        });
        if (aliases)
        {
            wire.Extensions.Add(TypeDeclaration(7, anchor, "shape"));
            wire.Extensions.Add(new SimpleExtensionDeclaration
            {
                ExtensionTypeVariation = new() { TypeVariationAnchor = 7, ExtensionUrnReference = anchor, Name = "custom" },
            });
            wire.Extensions.Add(new SimpleExtensionDeclaration
            {
                ExtensionFunction = new() { FunctionAnchor = 7, ExtensionUrnReference = 0, Name = "add:i64_i64" },
            });
        }

        foreach (ProtoType type in wire.Relations[0].Root.Input.Read.BaseSchema.Struct.Types_)
        {
            type.UserDefined.TypeReference = anchor;
            type.UserDefined.TypeVariationReference = anchor;
        }

        wire.Relations[0].Root.Input = new Rel
        {
            Project = new()
            {
                Input = wire.Relations[0].Root.Input,
                Common = new() { Direct = new() },
                Expressions =
                {
                    new Protobuf.Expression
                    {
                        ScalarFunction = new()
                        {
                            FunctionReference = anchor,
                            OutputType = new ProtoType { I64 = new() { Nullability = ProtoType.Types.Nullability.Required } },
                            Arguments =
                            {
                                new FunctionArgument { Value = new() { Literal = new() { I64 = 1 } } },
                                new FunctionArgument { Value = new() { Literal = new() { I64 = 2 } } },
                            },
                        },
                    },
                },
            },
        };
        wire.Relations[0].Root.Names.Add("sum");
        var extensions = ExtensionUtils.LoadDefaults().Merge(new ExtensionsCollection(
            [new TypeDefinition(new TypeAnchor(Urn, "shape"))],
            [new TypeVariationImpl(Urn, "shape", "custom", string.Empty, FunctionBehavior.INHERITS)], [], [], []));
        var decoder = new ProtoToPlanConverter(extensions);
        ProtoPlan expected = wire.Clone();
        if (aliases)
        {
            foreach (ProtoType type in expected.Relations[0].Root.Input.Project.Input.Read.BaseSchema.Struct.Types_)
            {
                type.UserDefined.TypeReference = 7;
                type.UserDefined.TypeVariationReference = 7;
            }

            expected.Relations[0].Root.Input.Project.Expressions[0].ScalarFunction.FunctionReference = 7;
        }

        IPlan[] imports = [decoder.From(wire), decoder.FromBytes(wire.ToByteArray()), decoder.FromJson(JsonFormatter.Default.Format(wire))];
        foreach (IPlan imported in imports)
        {
            await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task AggregateReferencesUseSeededUnsignedAnchors()
    {
        ProtoPlan wire = CreateWirePlan();
        wire.Extensions.Add(new SimpleExtensionDeclaration
        {
            ExtensionFunction = new() { FunctionAnchor = uint.MaxValue, ExtensionUrnReference = 999, Name = "aggregate" },
        });
        wire.Relations[0].Root.Input = new Rel
        {
            Aggregate = new()
            {
                Common = new() { Direct = new() },
                Input = wire.Relations[0].Root.Input,
                Groupings = { new AggregateRel.Types.Grouping() },
                Measures =
                {
                    new AggregateRel.Types.Measure
                    {
                        Measure_ = new()
                        {
                            FunctionReference = uint.MaxValue,
                            OutputType = new ProtoType { I64 = new() { Nullability = ProtoType.Types.Nullability.Required } },
                            Phase = AggregationPhase.InitialToResult,
                            Invocation = AggregateFunction.Types.AggregationInvocation.All,
                        },
                    },
                },
            },
        };
        wire.Relations[0].Root.Names.Clear();
        wire.Relations[0].Root.Names.Add("value");
        IPlan imported = new ProtoToPlanConverter(new ExtensionsCollection()).From(wire, StrictMode.OFF);
        wire.Relations[0].Root.Input.Aggregate.Input.Read.BaseSchema.Struct.Types_[0].UserDefined.TypeReference = 7;
        await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(wire);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public async Task RejectsInvalidRetainedTablesOnImportAndExport(int variant)
    {
        ProtoPlan wire = CreateWirePlan();
        switch (variant)
        {
            case 0: wire.ExtensionUrns.Add(wire.ExtensionUrns[0].Clone()); break;
            case 1: wire.Extensions.Add(wire.Extensions[0].Clone()); break;
            case 2: wire.Extensions[0].ExtensionType.ExtensionUrnReference = 123; break;
            case 3: wire.Extensions.Add(new SimpleExtensionDeclaration()); break;
            case 4: wire.Extensions[3].ExtensionTypeVariation.TypeVariationAnchor = 0; break;
            case 5: wire.Extensions[0].ExtensionType.Name = string.Empty; break;
            case 6: wire.ExtensionUrns[0].Urn = " "; break;
            case 7: wire.Extensions.Add(wire.Extensions[3].Clone()); break;
            case 8: wire.Extensions.Add(wire.Extensions[4].Clone()); break;
            default: throw new ArgumentOutOfRangeException(nameof(variant));
        }

        await Assert.That(() => new ProtoToPlanConverter().From(wire, StrictMode.OFF)).ThrowsExactly<ArgumentException>();
        var metadata = new PlanMetadata(null, null,
            wire.ExtensionUrns.Select(ReadOnlySimpleExtensionURN.FromProto),
            wire.Extensions.Select(ReadOnlySimpleExtensionDeclaration.FromProto));
        var plan = new CorePlan(metadata, [new CorePlan.Root(Read("shape", Urn), ["value"])], CoreVersion.Current);
        await Assert.That(() => new PlanToProtoConverter().From(plan)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task DeclarationBindingsParticipateInMetadataEquality()
    {
        ProtoPlan wire = CreateWirePlan();
        var decoder = new ProtoToPlanConverter(new ExtensionsCollection());
        IPlan first = decoder.From(wire, StrictMode.OFF);
        IPlan equal = decoder.From(wire.Clone(), StrictMode.OFF);
        await Assert.That(first.Metadata).IsEqualTo(equal.Metadata);
        await Assert.That(first.Metadata.GetHashCode()).IsEqualTo(equal.Metadata.GetHashCode());
        wire.Extensions[2].ExtensionType.Name = "different-opaque-dependency";
        IPlan different = decoder.From(wire, StrictMode.OFF);
        await Assert.That(first.Metadata.Equals(different.Metadata)).IsFalse();
        await Assert.That(first.Equals(different)).IsFalse();
    }

    [Test]
    public async Task MetadataConstructorSnapshotsDeclarationListsAndRejectsNullEntries()
    {
        ProtoPlan wire = CreateWirePlan();
        List<ReadOnlySimpleExtensionURN> urns = [.. wire.ExtensionUrns.Select(ReadOnlySimpleExtensionURN.FromProto)];
        List<ReadOnlySimpleExtensionDeclaration> declarations = [.. wire.Extensions.Select(ReadOnlySimpleExtensionDeclaration.FromProto)];
        var metadata = new PlanMetadata(null, null, urns, declarations);
        int hash = metadata.GetHashCode();
        urns.Clear();
        declarations.Clear();
        await Assert.That(metadata.ExtensionUrns.Select(urn => urn.ToProto()).SequenceEqual(wire.ExtensionUrns)).IsTrue();
        await Assert.That(metadata.Extensions.Select(declaration => declaration.ToProto()).SequenceEqual(wire.Extensions)).IsTrue();
        await Assert.That(metadata.GetHashCode()).IsEqualTo(hash);
        await Assert.That(new PlanMetadata(null, null, null, null)).IsEqualTo(PlanMetadata.Empty);
        await Assert.That(() => new PlanMetadata(null, null, [null!], null)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => new PlanMetadata(null, null, null, [null!])).ThrowsExactly<ArgumentException>();
    }

    private static NamedTableRead Read(string name, string urn, ITypeVariation? variation = null) =>
        new(new CoreNamedStruct(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.UserDefined(new TypeAnchor(urn, name), [], variation)])),
            ["shapes"], null);

    private static SimpleExtensionDeclaration TypeDeclaration(uint anchor, uint urn, string name) =>
        new() { ExtensionType = new() { TypeAnchor = anchor, ExtensionUrnReference = urn, Name = name } };

    private static ProtoPlan CreateWirePlan() => new()
    {
        Version = new() { MajorNumber = 1, Producer = "tests" },
        ExtensionUrns =
        {
            new SimpleExtensionURN { ExtensionUrnAnchor = 80, Urn = Urn },
            new SimpleExtensionURN { ExtensionUrnAnchor = 5, Urn = Urn },
            new SimpleExtensionURN { ExtensionUrnAnchor = 999, Urn = "extension:example:opaque" },
        },
        Extensions =
        {
            TypeDeclaration(29, 80, "shape"),
            TypeDeclaration(7, 5, "shape"),
            TypeDeclaration(0, 999, "opaque"),
            new SimpleExtensionDeclaration { ExtensionTypeVariation = new() { TypeVariationAnchor = 1, ExtensionUrnReference = 999, Name = "opaque" } },
            new SimpleExtensionDeclaration { ExtensionFunction = new() { FunctionAnchor = 0, ExtensionUrnReference = 999, Name = "opaque" } },
        },
        Relations =
        {
            new PlanRel
            {
                Root = new()
                {
                    Names = { "first", "second" },
                    Input = new Rel
                    {
                        Read = new()
                        {
                            Common = new() { Direct = new() },
                            NamedTable = new() { Names = { "shapes" } },
                            BaseSchema = new()
                            {
                                Names = { "first", "second" },
                                Struct = new()
                                {
                                    Nullability = ProtoType.Types.Nullability.Required,
                                    Types_ =
                                    {
                                        new ProtoType { UserDefined = new() { TypeReference = 29, Nullability = ProtoType.Types.Nullability.Required } },
                                        new ProtoType { UserDefined = new() { TypeReference = 7, Nullability = ProtoType.Types.Nullability.Required } },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        },
    };
}
