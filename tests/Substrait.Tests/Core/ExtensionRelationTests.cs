// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
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
using TUnit.Assertions.Enums;
using Any = Google.Protobuf.WellKnownTypes.Any;
using ProtoPlan = Substrait.Protobuf.Plan;
using ProtoRel = Substrait.Protobuf.Rel;
using StringValue = Google.Protobuf.WellKnownTypes.StringValue;
using Struct = Substrait.Core.Type.ParameterizedType.Struct;

namespace Substrait.Tests.Core;

public sealed class ExtensionRelationTests
{
    [Test]
    [Arguments(ExtensionRelationKind.Leaf, false)]
    [Arguments(ExtensionRelationKind.Leaf, true)]
    [Arguments(ExtensionRelationKind.Single, false)]
    [Arguments(ExtensionRelationKind.Single, true)]
    [Arguments(ExtensionRelationKind.Multi, false)]
    [Arguments(ExtensionRelationKind.Multi, true)]
    public async Task MessageConstructorsSnapshotPayloadsAndPreserveAlreadyPackedAny(ExtensionRelationKind kind, bool explicitMetadata)
    {
        Struct schema = RelationMetadataConversionTests.Schema().Struct;
        RelationMetadata? metadata = explicitMetadata ? RelationMetadata.FromRemap(new Remap([0, 0])) : null;
        IMessage[] messages = [new StringValue { Value = "original" }, MetadataFacadeTests.CreateCommon(), UnknownPayload(), new Any()];
        foreach (IMessage message in messages)
        {
            Any expected = message is Any packed ? packed.Clone() : Any.Pack(message);
            IRel relation = CreateFromMessage(kind, message, schema, metadata);
            IRel fromFacade = Create(kind, ReadOnlyAny.FromProto(expected), schema, metadata);
            ProtoRel wire = new RelToProtoConverter().From(relation);
            int hash = relation.GetHashCode();
            await Assert.That(Detail(wire)).IsEqualTo(expected);
            await Assert.That(relation).IsEqualTo(fromFacade);
            await Assert.That(hash).IsEqualTo(fromFacade.GetHashCode());
            await Assert.That(UnmappedSchema(relation)).IsSameReferenceAs(schema);
            await Assert.That(relation.RecordType.Fields.Count).IsEqualTo(explicitMetadata ? 2 : 1);

            switch (message)
            {
                case StringValue text:
                    await Assert.That(Detail(wire)!.TypeUrl).IsEqualTo("type.googleapis.com/google.protobuf.StringValue");
                    text.Value = "mutated";
                    break;
                case RelCommon common:
                    common.Hint.Alias = "mutated";
                    common.AdvancedExtension.Enhancement.Value = ByteString.Empty;
                    common.Emit.OutputMapping.Clear();
                    break;
                case Any any:
                    any.TypeUrl = "mutated";
                    any.Value = ByteString.Empty;
                    break;
            }

            await Assert.That(Detail(new RelToProtoConverter().From(relation))).IsEqualTo(expected);
            await Assert.That(relation.GetHashCode()).IsEqualTo(hash);
            IRel imported = Reader(new Resolver((_, _, _) => schema)).ToRel(wire);
            await Assert.That(imported).IsEqualTo(relation);
            ProtoPlan plan = RelationMetadataConversionTests.WirePlan(wire);
            IPlan importedPlan = Decoder(new Resolver((_, _, _) => schema)).FromBytes(plan.ToByteArray());
            await Assert.That(importedPlan.Relations[0].Input).IsEqualTo(relation);

            Detail(wire)!.TypeUrl = "export-mutated";
            Detail(wire)!.Value = ByteString.Empty;
            await Assert.That(Detail(new RelToProtoConverter().From(relation))).IsEqualTo(expected);
        }

        ArgumentNullException error = await Assert.That(() => CreateFromMessage(kind, null!, schema, metadata))
            .ThrowsExactly<ArgumentNullException>().And.IsNotNull();
        await Assert.That(error.ParamName).IsEqualTo("message");
    }

    [Test]
    public async Task MessageConstructorsDoNotInferOutputSchemas()
    {
        StringValue message = new() { Value = "schema is not the payload's protobuf type" };
        IRel[] relations =
        [
            new ExtensionLeaf(message),
            new ExtensionLeaf(RelationMetadata.Empty, message),
            new ExtensionSingle(RelationMetadataConversionTests.Read(), message),
            new ExtensionSingle(RelationMetadata.Empty, RelationMetadataConversionTests.Read(), message),
            new ExtensionMulti([], message),
            new ExtensionMulti(RelationMetadata.Empty, [], message),
        ];

        foreach (IRel relation in relations)
        {
            await Assert.That(UnmappedSchema(relation)).IsNull();
            await Assert.That(() => relation.RecordType).ThrowsExactly<ExtensionSchemaUnavailableException>();
            await Assert.That(Detail(new RelToProtoConverter().From(relation))).IsEqualTo(Any.Pack(message));
        }
    }

    [Test]
    [Arguments(ExtensionRelationKind.Leaf)]
    [Arguments(ExtensionRelationKind.Single)]
    [Arguments(ExtensionRelationKind.Multi)]
    public async Task OpaquePayloadsAndCommonMetadataRoundTripWithoutSchemas(ExtensionRelationKind kind)
    {
        Any?[] payloads = [null, new(), UnknownPayload()];
        RelCommon?[] commons = [null, new(), new() { Direct = new() }, new() { Emit = new() }, MetadataFacadeTests.CreateCommon()];
        foreach (Any? payload in payloads)
        {
            foreach (RelCommon? common in commons)
            {
                RelationMetadata metadata = new(common is null ? null : ReadOnlyRelCommon.FromProto(common));
                IRel original = Create(kind, payload is null ? null : ReadOnlyAny.FromProto(payload), metadata: metadata);
                ProtoRel wire = new RelToProtoConverter().From(original);
                IRel imported = Reader().ToRel(wire);
                await Assert.That(imported).IsEqualTo(original);
                await Assert.That(imported.GetHashCode()).IsEqualTo(original.GetHashCode());
                await Assert.That(new RelToProtoConverter().From(imported).ToByteArray()).IsEquivalentTo(wire.ToByteArray(), CollectionOrdering.Matching);
                await Assert.That(() => imported.RecordType).ThrowsExactly<ExtensionSchemaUnavailableException>();

                ProtoPlan plan = RelationMetadataConversionTests.WirePlan(wire);
                IPlan converted = Decoder().FromBytes(plan.ToByteArray());
                await Assert.That(converted.Relations[0].Input).IsEqualTo(original);
                await Assert.That(new PlanToProtoConverter().From(converted).ToByteArray()).IsEquivalentTo(plan.ToByteArray(), CollectionOrdering.Matching);
            }
        }
    }

    [Test]
    [Arguments(ExtensionRelationKind.Leaf)]
    [Arguments(ExtensionRelationKind.Single)]
    [Arguments(ExtensionRelationKind.Multi)]
    public async Task InputsPayloadAndExportsAreDetached(ExtensionRelationKind kind)
    {
        Any payload = UnknownPayload();
        ReadOnlyAny detail = ReadOnlyAny.FromProto(payload);
        List<IRel> inputs = [RelationMetadataConversionTests.Read(), new ExtensionLeaf(detail: null)];
        IRel relation = kind == ExtensionRelationKind.Multi
            ? new ExtensionMulti(inputs, detail) : Create(kind, detail);
        ProtoRel wire = new RelToProtoConverter().From(relation);
        ProtoRel expected = wire.Clone();
        IRel imported = Reader().ToRel(wire);
        IPlan importedPlan = Decoder().From(RelationMetadataConversionTests.WirePlan(wire));
        int hash = imported.GetHashCode();

        inputs.Clear();
        payload.Value = ByteString.Empty;
        Detail(wire)!.TypeUrl = "mutated";
        Detail(wire)!.Value = ByteString.Empty;
        Common(wire)!.Hint = new() { Alias = "mutated" };
        await Assert.That(new RelToProtoConverter().From(relation)).IsEqualTo(expected);
        await Assert.That(new RelToProtoConverter().From(imported)).IsEqualTo(expected);
        await Assert.That(new RelToProtoConverter().From(importedPlan.Relations[0].Input)).IsEqualTo(expected);
        await Assert.That(imported.GetHashCode()).IsEqualTo(hash);

        ProtoRel exported = new RelToProtoConverter().From(imported);
        Detail(exported)!.TypeUrl = "export-mutated";
        Detail(exported)!.Value = ByteString.Empty;
        await Assert.That(new RelToProtoConverter().From(imported)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(ExtensionRelationKind.Leaf)]
    [Arguments(ExtensionRelationKind.Single)]
    [Arguments(ExtensionRelationKind.Multi)]
    public async Task ExplicitSchemasAreStoredAndRemappedExactlyOnce(ExtensionRelationKind kind)
    {
        Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, TypeFactory.NULLABLE.STR, TypeFactory.REQUIRED.BOOL]);
        RelationMetadata metadata = RelationMetadata.FromRemap(new Remap([2, 0, 2]));
        IRel relation = Create(kind, null, schema, metadata);
        await Assert.That(UnmappedSchema(relation)).IsSameReferenceAs(schema);
        await Assert.That(relation.RecordType.Fields).IsEquivalentTo(
            new IType[] { TypeFactory.REQUIRED.BOOL, TypeFactory.REQUIRED.I64, TypeFactory.REQUIRED.BOOL }, CollectionOrdering.Matching);
        await Assert.That(Create(kind, null, schema, RelationMetadata.FromRemap(new Remap([]))).RecordType.Fields.Count).IsEqualTo(0);
        await Assert.That(Create(kind, null, Struct.Empty).RecordType.Fields.Count).IsEqualTo(0);

        ProtoRel wire = new RelToProtoConverter().From(relation);
        IRel unresolved = Reader().ToRel(wire);
        await Assert.That(UnmappedSchema(unresolved)).IsNull();
        await Assert.That(unresolved).IsNotEqualTo(relation);
        await Assert.That(Create(kind, null, schema, metadata)).IsEqualTo(relation);
        await Assert.That(Create(kind, null, schema, metadata).GetHashCode()).IsEqualTo(relation.GetHashCode());
        await Assert.That(Create(kind, null, Struct.Empty, metadata)).IsNotEqualTo(relation);
        await Assert.That(Create(kind, ReadOnlyAny.FromProto(UnknownPayload()), schema, metadata)).IsNotEqualTo(relation);
        await Assert.That(Create(kind, null, schema)).IsNotEqualTo(relation);

        var resolver = new Resolver((_, _, _) => schema);
        IRel resolved = Reader(resolver).ToRel(wire);
        await Assert.That(resolved).IsEqualTo(relation);
        await Assert.That(resolved.RecordType).IsEqualTo(relation.RecordType);
        await Assert.That(new RelToProtoConverter().From(resolved)).IsEqualTo(wire);
        await Assert.That(resolver.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task ResolverSeesImmutableInputsInOrderAndTheirFinalSchemas()
    {
        Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, TypeFactory.NULLABLE.STR]);
        ExtensionLeaf left = new(RelationMetadata.FromRemap(new Remap([1])), Payload("left"), schema);
        ExtensionLeaf right = new(Payload("right"), TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.BOOL]));
        ExtensionMulti multi = new(RelationMetadata.FromRemap(new Remap([1, 0])), [left, right], Payload("multi"));
        List<string> order = [];
        IReadOnlyList<IRel>? capturedInputs = null;
        var resolver = new Resolver((kind, detail, inputs) =>
        {
            order.Add(detail!.TypeUrl);
            return detail.TypeUrl switch
            {
                "left" when kind == ExtensionRelationKind.Leaf => schema,
                "right" when kind == ExtensionRelationKind.Leaf => right.UnmappedRecordType,
                "multi" when kind == ExtensionRelationKind.Multi => Combine(inputs),
                _ => throw new SerializationException("Unexpected extension."),
            };

            Struct Combine(IReadOnlyList<IRel> relations)
            {
                capturedInputs = relations;
                return TypeFactory.REQUIRED.Struct(relations.SelectMany(input => input.RecordType.Fields));
            }
        });

        ExtensionMulti imported = (ExtensionMulti)Reader(resolver).ToRel(new RelToProtoConverter().From(multi));
        await Assert.That(order).IsEquivalentTo(["left", "right", "multi"], CollectionOrdering.Matching);
        await Assert.That(capturedInputs).IsAssignableTo<ImmutableList<IRel>>();
        await Assert.That(imported.UnmappedRecordType!.Fields).IsEquivalentTo(new IType[] { TypeFactory.NULLABLE.STR, TypeFactory.REQUIRED.BOOL }, CollectionOrdering.Matching);
        await Assert.That(imported.RecordType.Fields).IsEquivalentTo(new IType[] { TypeFactory.REQUIRED.BOOL, TypeFactory.NULLABLE.STR }, CollectionOrdering.Matching);
        await Assert.That(resolver.Calls).IsEqualTo(3);
        resolver.Callback = (_, _, _) => throw new InvalidOperationException("Must not be called again.");
        _ = imported.RecordType;
        _ = imported.GetHashCode();
        _ = new RelToProtoConverter().From(imported);
        await Assert.That(resolver.Calls).IsEqualTo(3);
    }

    [Test]
    public async Task FixedSchemaDoesNotRequireInputSchemasEvenThroughReferences()
    {
        ExtensionLeaf unknown = new(Payload("unknown"));
        ExtensionSingle known = new(unknown, Payload("known"), Struct.Empty);
        Reference reference = new(1, known);
        ProtoPlan wire = RelationMetadataConversionTests.WirePlan(
            new() { Reference = new() { SubtreeOrdinal = 1 } },
            new RelToProtoConverter().From(known));
        var resolver = new Resolver((_, detail, _) => detail?.TypeUrl == "known" ? Struct.Empty : null);
        IPlan imported = Decoder(resolver).From(wire);

        await Assert.That(reference.RecordType).IsEqualTo(Struct.Empty);
        await Assert.That(imported.Relations[0].Input.RecordType).IsEqualTo(Struct.Empty);
        await Assert.That(imported.Relations[1].Input.RecordType).IsEqualTo(Struct.Empty);
        await Assert.That(() => imported.Relations[1].Input.Inputs[0].RecordType).ThrowsExactly<ExtensionSchemaUnavailableException>();
        await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(wire);
    }

    [Test]
    public async Task UnknownSchemasFailOnlyWhenNeededAndIncludeConversionContext()
    {
        ExtensionLeaf leaf = new(Payload("unknown"));
        ExtensionSchemaUnavailableException error = await Assert.That(() => leaf.RecordType)
            .ThrowsExactly<ExtensionSchemaUnavailableException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("Leaf");
        await Assert.That(error.Message).Contains("unknown");
        await Assert.That(error.Message).Contains("resolver");
        ExtensionSingle single = new(RelationMetadataConversionTests.Read(), Payload("unknown"));
        await Assert.That(() => single.RecordType).ThrowsExactly<ExtensionSchemaUnavailableException>();

        Filter filter = new(leaf, new Literal.BoolLiteral(true));
        ProtoRel wire = new RelToProtoConverter().From(filter);
        SerializationException conversionError = await Assert.That(() => Reader().ToRel(wire))
            .ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(conversionError.Message).Contains("Filter");
        await Assert.That(conversionError.InnerException).IsAssignableTo<ExtensionSchemaUnavailableException>();

        SerializationException planError = await Assert.That(() => Decoder().From(RelationMetadataConversionTests.WirePlan(wire)))
            .ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(planError.Message).Contains("ordinal 0");
        await Assert.That(planError.Message).Contains("unknown");
        await Assert.That(Reader(new Resolver((_, _, _) => Struct.Empty)).ToRel(wire).RecordType).IsEqualTo(Struct.Empty);
    }

    [Test]
    public async Task RecognizedInvalidExtensionsAreNotTreatedAsUnknown()
    {
        var resolver = new Resolver((_, _, _) => throw new SerializationException("Known extension has invalid detail."));
        ProtoRel wire = new RelToProtoConverter().From(new ExtensionLeaf(Payload("known")));
        SerializationException error = await Assert.That(() => Decoder(resolver).From(RelationMetadataConversionTests.WirePlan(wire)))
            .ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("ordinal 0");
        await Assert.That(error.Message).Contains("invalid detail");
    }

    [Test]
    public async Task MultiInputCountIsDeterminedByTheExtensionContract()
    {
        foreach (int count in new[] { 0, 1, 2, 3 })
        {
            ExtensionMulti relation = new(Enumerable.Range(0, count).Select(_ => new ExtensionLeaf(detail: null)), detail: null);
            IRel imported = Reader().ToRel(new RelToProtoConverter().From(relation));
            await Assert.That(imported.Inputs.Count).IsEqualTo(count);
            await Assert.That(imported).IsEqualTo(relation);
        }
    }

    [Test]
    public async Task InvalidConstructionAndMissingSingleInputFailExplicitly()
    {
        RelationMetadata unsupported = new(advancedExtension: ReadOnlyAdvancedExtension.FromProto(new()));
        foreach (ExtensionRelationKind kind in Enum.GetValues<ExtensionRelationKind>())
        {
            await Assert.That(() => Create(kind, null, metadata: unsupported)).ThrowsExactly<ArgumentException>();
        }

        await Assert.That(() => new ExtensionLeaf(metadata: null!, detail: null)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new ExtensionSingle(null!, detail: null)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new ExtensionMulti(null!, detail: null)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new ExtensionMulti([null!], detail: null)).ThrowsExactly<ArgumentException>();
        ProtoRel missingInput = new() { ExtensionSingle = new() };
        await Assert.That(() => Reader().ToRel(missingInput)).ThrowsExactly<SerializationException>();
        await Assert.That(() => Decoder().From(RelationMetadataConversionTests.WirePlan(missingInput))).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task InputOrderAndArityParticipateInEqualityWithoutResolvingSchemas()
    {
        ExtensionLeaf left = new(Payload("left"));
        ExtensionLeaf right = new(Payload("right"));
        ExtensionMulti multi = new([left, right], detail: null);
        await Assert.That(multi).IsEqualTo(new ExtensionMulti([new ExtensionLeaf(Payload("left")), new ExtensionLeaf(Payload("right"))], detail: null));
        await Assert.That(multi).IsNotEqualTo(new ExtensionMulti([right, left], detail: null));
        await Assert.That(multi).IsNotEqualTo(new ExtensionMulti([left], detail: null));
        await Assert.That(new ExtensionSingle(left, detail: null)).IsNotEqualTo(new ExtensionSingle(right, detail: null));
        await Assert.That((IRel)new ExtensionLeaf(detail: null)).IsNotEqualTo(new ExtensionMulti([], detail: null));
    }

    [Test]
    public async Task PlanBuilderAndImportValidateReferencesAndAnchorsInsideExtensions()
    {
        PlanBuilder builder = new();
        Reference shared = builder.RegisterSubplan(new ExtensionLeaf(Payload("source"), Struct.Empty));
        builder.AddRoot(new ExtensionMulti([new ExtensionSingle(shared, detail: null, Struct.Empty), shared], detail: null, Struct.Empty), []);
        ProtoPlan wire = new PlanToProtoConverter().From(builder.Build());
        IPlan imported = Decoder(new Resolver((_, _, _) => Struct.Empty)).From(wire);
        await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(wire);
        await Assert.That(((Reference)imported.Roots[0].Input.Inputs[1]).Target).IsSameReferenceAs(imported.Relations[0].Input);

        wire.Relations[1].Root.Input.ExtensionMulti.Inputs[1].Reference.SubtreeOrdinal = 50;
        await Assert.That(() => Decoder().From(wire)).ThrowsExactly<SerializationException>();

        ExtensionLeaf anchored = new(new RelationMetadata(ReadOnlyRelCommon.FromProto(new() { RelAnchor = 9 })), detail: null);
        ProtoRel duplicate = new RelToProtoConverter().From(new ExtensionMulti([anchored, anchored], detail: null));
        await Assert.That(() => Decoder().From(RelationMetadataConversionTests.WirePlan(duplicate))).ThrowsExactly<SerializationException>();
        await Assert.That(() => RelationMetadataConversionTests.Plan(new ExtensionMulti([anchored, anchored], detail: null))).ThrowsExactly<ArgumentException>();

        foreach (ExtensionRelationKind kind in Enum.GetValues<ExtensionRelationKind>())
        {
            ProtoRel zeroAnchor = new RelToProtoConverter().From(Create(kind, null));
            Common(zeroAnchor)!.RelAnchor = 0;
            await Assert.That(() => Reader().ToRel(zeroAnchor)).ThrowsExactly<SerializationException>();
            await Assert.That(() => Decoder().From(RelationMetadataConversionTests.WirePlan(zeroAnchor))).ThrowsExactly<SerializationException>();
        }

        ProtoRel cycle = new() { ExtensionSingle = new() };
        cycle.ExtensionSingle.Input = cycle;
        await Assert.That(() => Decoder().From(RelationMetadataConversionTests.WirePlan(cycle))).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task SchemaResolverReachesSubqueriesAndEveryPlanImportEntryPoint()
    {
        ExtensionLeaf leaf = new(ReadOnlyAny.FromProto(Any.Pack(new StringValue { Value = "known" })));
        ProtoRel nested = new RelToProtoConverter().From(leaf);
        for (int kind = 0; kind < 4; ++kind)
        {
            ProtoRel relation = new()
            {
                Project = new()
                {
                    Input = new RelToProtoConverter().From(RelationMetadataConversionTests.Read()),
                    Expressions = { PlanReferenceConversionTests.WireSubquery(kind, nested) },
                },
            };
            var resolver = new Resolver((_, _, _) => RelationMetadataConversionTests.Schema().Struct);
            ProtoPlan wire = RelationMetadataConversionTests.WirePlan(relation);
            IPlan imported = Decoder(resolver).From(wire);
            await Assert.That(resolver.Calls).IsEqualTo(1);
            IRel subquery = PlanReferenceConversionTests.GetSubquery(((Project)imported.Relations[0].Input).Expressions[0]);
            await Assert.That(subquery.RecordType).IsEqualTo(RelationMetadataConversionTests.Schema().Struct);
            await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(wire);
        }

        var entryPointResolver = new Resolver((_, _, _) => Struct.Empty);
        ProtoToPlanConverter converter = Decoder(entryPointResolver);
        ProtoPlan plan = RelationMetadataConversionTests.WirePlan(nested);
        byte[] bytes = plan.ToByteArray();
        using MemoryStream stream = new(bytes);
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            TypeRegistry registry = TypeRegistry.FromMessages(StringValue.Descriptor);
            string json = new JsonFormatter(JsonFormatter.Settings.Default.WithTypeRegistry(registry)).Format(plan);
            JsonParser parser = new(JsonParser.Settings.Default.WithTypeRegistry(registry));
            IPlan[] imports = [converter.From(plan), converter.FromBytes(bytes), converter.FromStream(stream), converter.FromFile(path), converter.FromJson(json, parser: parser)];
            foreach (IPlan imported in imports)
            {
                await Assert.That(imported.Relations[0].Input.RecordType).IsEqualTo(Struct.Empty);
                await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(plan);
            }

            await Assert.That(entryPointResolver.Calls).IsEqualTo(5);
            await Assert.That(stream.CanRead).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IRel CreateFromMessage(ExtensionRelationKind kind, IMessage message, Struct schema, RelationMetadata? metadata) => (kind, metadata) switch
    {
        (ExtensionRelationKind.Leaf, null) => new ExtensionLeaf(message, schema),
        (ExtensionRelationKind.Leaf, _) => new ExtensionLeaf(metadata, message, schema),
        (ExtensionRelationKind.Single, null) => new ExtensionSingle(RelationMetadataConversionTests.Read(), message, schema),
        (ExtensionRelationKind.Single, _) => new ExtensionSingle(metadata, RelationMetadataConversionTests.Read(), message, schema),
        (ExtensionRelationKind.Multi, null) => new ExtensionMulti([RelationMetadataConversionTests.Read(), RelationMetadataConversionTests.Read()], message, schema),
        (ExtensionRelationKind.Multi, _) => new ExtensionMulti(metadata, [RelationMetadataConversionTests.Read(), RelationMetadataConversionTests.Read()], message, schema),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static IRel Create(ExtensionRelationKind kind, ReadOnlyAny? detail, Struct? schema = null, RelationMetadata? metadata = null) => kind switch
    {
        ExtensionRelationKind.Leaf => new ExtensionLeaf(metadata ?? RelationMetadata.Direct, detail, schema),
        ExtensionRelationKind.Single => new ExtensionSingle(metadata ?? RelationMetadata.Direct, RelationMetadataConversionTests.Read(), detail, schema),
        ExtensionRelationKind.Multi => new ExtensionMulti(metadata ?? RelationMetadata.Direct, [RelationMetadataConversionTests.Read(), RelationMetadataConversionTests.Read()], detail, schema),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static Struct? UnmappedSchema(IRel relation) => relation switch
    {
        ExtensionLeaf leaf => leaf.UnmappedRecordType,
        ExtensionSingle single => single.UnmappedRecordType,
        ExtensionMulti multi => multi.UnmappedRecordType,
        _ => throw new ArgumentException("Expected an extension relation.", nameof(relation)),
    };

    private static Any? Detail(ProtoRel relation) => relation.RelTypeCase switch
    {
        ProtoRel.RelTypeOneofCase.ExtensionLeaf => relation.ExtensionLeaf.Detail,
        ProtoRel.RelTypeOneofCase.ExtensionSingle => relation.ExtensionSingle.Detail,
        ProtoRel.RelTypeOneofCase.ExtensionMulti => relation.ExtensionMulti.Detail,
        _ => throw new ArgumentException("Expected an extension relation.", nameof(relation)),
    };

    private static RelCommon? Common(ProtoRel relation) => relation.RelTypeCase switch
    {
        ProtoRel.RelTypeOneofCase.ExtensionLeaf => relation.ExtensionLeaf.Common,
        ProtoRel.RelTypeOneofCase.ExtensionSingle => relation.ExtensionSingle.Common,
        ProtoRel.RelTypeOneofCase.ExtensionMulti => relation.ExtensionMulti.Common,
        _ => throw new ArgumentException("Expected an extension relation.", nameof(relation)),
    };

    private static Any UnknownPayload() => MetadataFacadeTests.CreateCommon().AdvancedExtension.Enhancement.Clone();

    private static ReadOnlyAny Payload(string typeUrl) => ReadOnlyAny.FromProto(new Any { TypeUrl = typeUrl });

    private static ProtoToRelConverter Reader(IExtensionRelationSchemaResolver? resolver = null) =>
        new(new ExtensionsDictionary.Builder().Build(), new ExtensionsCollection(), resolver);

    private static ProtoToPlanConverter Decoder(IExtensionRelationSchemaResolver? resolver = null) =>
        new(new ExtensionsCollection(), resolver);

    private sealed class Resolver(Func<ExtensionRelationKind, ReadOnlyAny?, IReadOnlyList<IRel>, Struct?> callback) : IExtensionRelationSchemaResolver
    {
        public Func<ExtensionRelationKind, ReadOnlyAny?, IReadOnlyList<IRel>, Struct?> Callback { get; set; } = callback;

        public int Calls { get; private set; }

        public Struct? Resolve(ExtensionRelationKind kind, ReadOnlyAny? detail, IReadOnlyList<IRel> inputs)
        {
            this.Calls++;
            return this.Callback(kind, detail, inputs);
        }
    }
}
