// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Protobuf;
using TUnit.Assertions.Enums;
using Any = Google.Protobuf.WellKnownTypes.Any;
using CorePlan = Substrait.Core.Plan.Plan;
using PlanVersion = Substrait.Core.Plan.Version;
using ProtoPlan = Substrait.Protobuf.Plan;
using StringValue = Google.Protobuf.WellKnownTypes.StringValue;

namespace Substrait.Tests.Core;

public sealed class PlanMetadataTests
{
    private static readonly string[] ExpectedUrls = ["https://example.invalid/unused", "", "type.example/Case", "type.example/case", "type.example/Case"];

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task RoundTripsPresenceOpaquePayloadsAndExpectedUrls(int variant)
    {
        AdvancedExtension? extension = variant switch
        {
            0 => null,
            1 => new(),
            2 => new() { Enhancement = new() },
            3 => new() { Optimization = { new Any(), new Any() } },
            4 => new() { Enhancement = UnknownExtensions().Enhancement },
            5 => UnknownExtensions(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };

        foreach (string[] urls in new[] { Array.Empty<string>(), ExpectedUrls })
        {
            ProtoPlan wire = WirePlan();
            wire.AdvancedExtensions = extension;
            wire.ExpectedTypeUrls.AddRange(urls);
            PlanMetadata expected = new(
                extension is null ? null : ReadOnlyAdvancedExtension.FromProto(extension), urls);
            foreach (ExtensionsDictionary.StrictMode mode in Enum.GetValues<ExtensionsDictionary.StrictMode>())
            {
                IPlan[] imports = [Decoder().From(wire, mode), Decoder().FromBytes(wire.ToByteArray(), mode)];
                foreach (IPlan imported in imports)
                {
                    await Assert.That(imported.Metadata).IsEqualTo(expected);
                    await Assert.That(imported.Metadata.ExpectedTypeUrls).IsEquivalentTo(urls, CollectionOrdering.Matching);
                    await Assert.That(imported.Metadata.AdvancedExtensions?.ToProto()).IsEqualTo(extension);
                    await Assert.That(new PlanToProtoConverter().From(imported).ToByteArray())
                        .IsEquivalentTo(wire.ToByteArray(), CollectionOrdering.Matching);
                }
            }
        }
    }

    [Test]
    public async Task FacadeAndProtobufConstructorsSnapshotCallerOwnedData()
    {
        AdvancedExtension source = UnknownExtensions();
        AdvancedExtension expected = source.Clone();
        List<string> urls = [.. ExpectedUrls];
        ReadOnlyAdvancedExtension facade = ReadOnlyAdvancedExtension.FromProto(source);
        PlanMetadata fromFacade = new(facade, urls);
        PlanMetadata fromMessage = new(source, urls);
        int hash = fromMessage.GetHashCode();

        source.Enhancement.TypeUrl = "changed";
        source.Optimization[0].Value = ByteString.Empty;
        source.Optimization.Clear();
        urls.Clear();

        await Assert.That(fromFacade.AdvancedExtensions).IsSameReferenceAs(facade);
        await Assert.That(fromMessage).IsEqualTo(fromFacade);
        await Assert.That(fromMessage.GetHashCode()).IsEqualTo(hash);
        await Assert.That(fromMessage.GetHashCode()).IsEqualTo(fromFacade.GetHashCode());
        await Assert.That(fromMessage.AdvancedExtensions!.ToProto()).IsEqualTo(expected);
        await Assert.That(fromMessage.ExpectedTypeUrls).IsEquivalentTo(ExpectedUrls, CollectionOrdering.Matching);
        await Assert.That(fromMessage.ExpectedTypeUrls).IsAssignableTo<ImmutableList<string>>();

        AdvancedExtension exported = fromMessage.AdvancedExtensions.ToProto();
        exported.Enhancement.Value = ByteString.Empty;
        exported.Optimization.Clear();
        await Assert.That(fromMessage.AdvancedExtensions.ToProto()).IsEqualTo(expected);
    }

    [Test]
    public async Task PlanImportsAndExportsAreDetachedAndDoNotModifyRelationMetadata()
    {
        RelCommon common = MetadataFacadeTests.CreateCommon();
        common.Emit = null;
        RelationMetadata relationMetadata = new(
            ReadOnlyRelCommon.FromProto(common),
            ReadOnlyAdvancedExtension.FromProto(common.Hint.AdvancedExtension));
        NamedTableRead read = RelationMetadataConversionTests.Read(relationMetadata);
        ProtoPlan wire = RelationMetadataConversionTests.WirePlan(new RelToProtoConverter().From(read));
        wire.AdvancedExtensions = UnknownExtensions();
        wire.ExpectedTypeUrls.AddRange(ExpectedUrls);
        ProtoPlan expected = wire.Clone();
        IPlan imported = Decoder().From(wire);
        int hash = imported.GetHashCode();

        wire.AdvancedExtensions.Enhancement.TypeUrl = "source-changed";
        wire.AdvancedExtensions.Optimization[0].Value = ByteString.Empty;
        wire.ExpectedTypeUrls.Clear();
        wire.Relations[0].Rel.Read.AdvancedExtension.Optimization.Clear();

        await Assert.That(imported.Relations[0].Input.Metadata).IsEqualTo(relationMetadata);
        await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(expected);
        await Assert.That(imported.GetHashCode()).IsEqualTo(hash);
        ProtoPlan exported = new PlanToProtoConverter().From(imported);
        exported.AdvancedExtensions.Enhancement.Value = ByteString.Empty;
        exported.AdvancedExtensions.Optimization.Clear();
        exported.ExpectedTypeUrls[0] = "export-changed";
        await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(expected);
    }

    [Test]
    public async Task BinaryStreamAndFileImportsPreserveOpaqueDataAndLeaveStreamsOpen()
    {
        ProtoPlan wire = WirePlan();
        wire.AdvancedExtensions = UnknownExtensions();
        wire.ExpectedTypeUrls.AddRange(ExpectedUrls);
        byte[] bytes = wire.ToByteArray();
        using MemoryStream stream = new(bytes);
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            IPlan[] imports = [Decoder().FromBytes(bytes), Decoder().FromStream(stream), Decoder().FromFile(path)];
            Array.Clear(bytes);
            await Assert.That(stream.CanRead).IsTrue();
            foreach (IPlan imported in imports)
            {
                await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(wire);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task JsonImportsUseRegisteredPayloadDescriptorsButDoNotResolveExpectedUrls()
    {
        ProtoPlan wire = WirePlan();
        wire.AdvancedExtensions = new()
        {
            Enhancement = Any.Pack(new StringValue { Value = "enhancement" }),
            Optimization =
            {
                Any.Pack(new StringValue { Value = "first" }),
                Any.Pack(new StringValue { Value = "second" }),
            },
        };
        wire.ExpectedTypeUrls.AddRange(ExpectedUrls);
        TypeRegistry registry = TypeRegistry.FromMessages(StringValue.Descriptor);
        JsonFormatter formatter = new(JsonFormatter.Settings.Default.WithTypeRegistry(registry));
        JsonParser parser = new(JsonParser.Settings.Default.WithTypeRegistry(registry));
        string json = formatter.Format(wire);
        IPlan imported = Decoder().FromJson(json, parser: parser);
        await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(wire);
        await Assert.That(parser.Parse<ProtoPlan>(formatter.Format(new PlanToProtoConverter().From(imported)))).IsEqualTo(wire);
        await Assert.That(() => Decoder().FromJson(json)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MetadataFirstConstructionAndBuilderPreserveMixedPlansAndReferences()
    {
        AdvancedExtension extensions = UnknownExtensions();
        List<string> urls = [.. ExpectedUrls];
        PlanMetadata metadata = new(extensions, urls);
        PlanVersion version = new(1, 2, 3, "hash", "plan-extensions-tests");
        NamedTableRead read = RelationMetadataConversionTests.Read();
        PlanBuilder builder = new(metadata, version);
        Reference shared = builder.RegisterSubplan(read);
        builder.AddRoot(shared, ["first"]);
        CorePlan first = builder.Build();
        int hash = first.GetHashCode();
        ProtoPlan wire = new PlanToProtoConverter().From(first);
        builder.AddRoot(shared, ["second"]);
        extensions.Optimization.Clear();
        urls.Clear();
        CorePlan second = builder.Build();

        await Assert.That(first.Metadata).IsSameReferenceAs(metadata);
        await Assert.That(second.Metadata).IsSameReferenceAs(metadata);
        await Assert.That(second.Version).IsSameReferenceAs(version);
        await Assert.That(first.Relations.Count).IsEqualTo(2);
        await Assert.That(second.Relations.Count).IsEqualTo(3);
        await Assert.That(first.GetHashCode()).IsEqualTo(hash);
        await Assert.That(new PlanToProtoConverter().From(first)).IsEqualTo(wire);
        await Assert.That(Decoder().From(wire)).IsEqualTo(first);
        IPlan imported = Decoder().From(new PlanToProtoConverter().From(second));
        await Assert.That(imported).IsEqualTo(second);
        await Assert.That(((Reference)imported.Roots[1].Input).Target).IsSameReferenceAs(imported.Relations[0].Input);

        List<IPlan.IRelation> entries = [new CorePlan.Relation(read), new CorePlan.Root(new Reference(0, read), ["output"])];
        CorePlan fromRelations = CorePlan.FromRelations(metadata, entries, version);
        entries.Clear();
        await Assert.That(fromRelations.Metadata).IsSameReferenceAs(metadata);
        await Assert.That(fromRelations.Relations.Count).IsEqualTo(2);
        await Assert.That(Decoder().From(new PlanToProtoConverter().From(fromRelations))).IsEqualTo(fromRelations);
        CorePlan fromRoots = new(metadata, [new CorePlan.Root(read, ["output"])], version);
        await Assert.That(Decoder().From(new PlanToProtoConverter().From(fromRoots))).IsEqualTo(fromRoots);
    }

    [Test]
    public async Task LegacyConstructionKeepsMetadataAbsentAndEmptyPlansUnserializable()
    {
        NamedTableRead read = RelationMetadataConversionTests.Read();
        CorePlan[] legacy =
        [
            new([new CorePlan.Root(read, [])], PlanVersion.Current),
            CorePlan.FromRelations([new CorePlan.Relation(read)], PlanVersion.Current),
        ];
        foreach (CorePlan plan in legacy)
        {
            await Assert.That(plan.Metadata).IsSameReferenceAs(PlanMetadata.Empty);
            ProtoPlan wire = new PlanToProtoConverter().From(plan);
            await Assert.That(wire.AdvancedExtensions).IsNull();
            await Assert.That(wire.ExpectedTypeUrls.Count).IsEqualTo(0);
            await Assert.That(Decoder().From(wire)).IsEqualTo(plan);
        }

        await Assert.That(new PlanBuilder(null).Build().Metadata).IsSameReferenceAs(PlanMetadata.Empty);
        PlanMetadata metadata = new(UnknownExtensions(), ExpectedUrls);
        CorePlan[] empty =
        [
            new(metadata, [], PlanVersion.Current),
            CorePlan.FromRelations(metadata, [], PlanVersion.Current),
            new PlanBuilder(metadata, version: null).Build(),
        ];
        foreach (CorePlan plan in empty)
        {
            await Assert.That(plan.Metadata).IsSameReferenceAs(metadata);
            await Assert.That(() => new PlanToProtoConverter().From(plan)).ThrowsExactly<ArgumentException>();
        }

        ProtoPlan noRelations = new() { Version = new(), AdvancedExtensions = UnknownExtensions(), ExpectedTypeUrls = { ExpectedUrls } };
        IPlan importedEmpty = Decoder().From(noRelations);
        await Assert.That(importedEmpty.Metadata).IsEqualTo(metadata);
        await Assert.That(importedEmpty.Relations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task EqualityIncludesPresencePayloadOrderAndExactExpectedUrls()
    {
        PlanMetadata baseline = new(UnknownExtensions(), ExpectedUrls);
        PlanMetadata equivalent = new(UnknownExtensions(), ExpectedUrls);
        await Assert.That(equivalent).IsEqualTo(baseline);
        await Assert.That(equivalent.GetHashCode()).IsEqualTo(baseline.GetHashCode());
        await Assert.That((object)equivalent).IsEqualTo((object)baseline);
        await Assert.That(new PlanMetadata().Equals((object?)null)).IsFalse();
        await Assert.That(baseline.Equals(new object())).IsFalse();
        await Assert.That(new PlanMetadata()).IsEqualTo(PlanMetadata.Empty);
        await Assert.That(new PlanMetadata(advancedExtensions: null, expectedTypeUrls: [])).IsEqualTo(PlanMetadata.Empty);
        await Assert.That(new PlanMetadata(new AdvancedExtension())).IsNotEqualTo(PlanMetadata.Empty);

        AdvancedExtension changedPayload = UnknownExtensions();
        changedPayload.Enhancement.Value = ByteString.Empty;
        AdvancedExtension changedOrder = UnknownExtensions();
        Any first = changedOrder.Optimization[0];
        changedOrder.Optimization[0] = changedOrder.Optimization[1];
        changedOrder.Optimization[1] = first;
        PlanMetadata[] different =
        [
            PlanMetadata.Empty,
            new(changedPayload, ExpectedUrls),
            new(changedOrder, ExpectedUrls),
            new(UnknownExtensions(), ExpectedUrls.Reverse()),
            new(UnknownExtensions(), ExpectedUrls.Distinct()),
            new(UnknownExtensions(), ExpectedUrls.Select(url => url.ToUpperInvariant())),
        ];
        CorePlan originalPlan = Plan(baseline);
        await Assert.That(Plan(equivalent)).IsEqualTo(originalPlan);
        await Assert.That(Plan(equivalent).GetHashCode()).IsEqualTo(originalPlan.GetHashCode());
        foreach (PlanMetadata metadata in different)
        {
            await Assert.That(metadata).IsNotEqualTo(baseline);
            await Assert.That(Plan(metadata)).IsNotEqualTo(originalPlan);
        }
    }

    [Test]
    public async Task InvalidMetadataIsRejectedAndCustomPlanMetadataIsSerialized()
    {
        await Assert.That(() => new PlanMetadata(message: null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new PlanMetadata(expectedTypeUrls: [null!])).ThrowsExactly<ArgumentException>();
        await Assert.That(() => new CorePlan(null!, [], PlanVersion.Current)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => CorePlan.FromRelations(null!, [], PlanVersion.Current)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new PlanBuilder(null!, version: null)).ThrowsExactly<ArgumentNullException>();

        CorePlan original = Plan(new(UnknownExtensions(), ExpectedUrls));
        CustomPlan custom = new(original, original.Metadata);
        await Assert.That(new PlanToProtoConverter().From(custom)).IsEqualTo(new PlanToProtoConverter().From(original));
        await Assert.That(() => new PlanToProtoConverter().From(new CustomPlan(original, null!))).ThrowsExactly<ArgumentException>();
    }

    private static CorePlan Plan(PlanMetadata metadata) =>
        new(metadata, [new CorePlan.Root(RelationMetadataConversionTests.Read(), ["value"])], PlanVersion.Current);

    private static ProtoPlan WirePlan() => new PlanToProtoConverter().From(Plan(PlanMetadata.Empty));

    private static AdvancedExtension UnknownExtensions() => MetadataFacadeTests.CreateCommon().AdvancedExtension;

    private static ProtoToPlanConverter Decoder() => new(new ExtensionsCollection());

    private sealed class CustomPlan(IPlan source, PlanMetadata metadata) : IPlan
    {
        public PlanMetadata Metadata => metadata;

        public IReadOnlyList<IPlan.IRelation> Relations => source.Relations;

        public IReadOnlyList<IPlan.IRoot> Roots => source.Roots;

        public IVersion Version => source.Version;
    }
}
