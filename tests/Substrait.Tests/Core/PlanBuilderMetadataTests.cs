// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Protobuf;
using TUnit.Assertions.Enums;
using Any = Google.Protobuf.WellKnownTypes.Any;
using CorePlan = Substrait.Core.Plan.Plan;
using StringValue = Google.Protobuf.WellKnownTypes.StringValue;

namespace Substrait.Tests.Core;

public sealed class PlanBuilderMetadataTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task PayloadEditsSnapshotMessagesAndPreserveUnchangedFields(int operation, bool useFacade)
    {
        IMessage[] messages = [new StringValue { Value = "configuration" }, MetadataFacadeTests.CreateCommon(), UnknownExtensions().Enhancement, new Any()];
        foreach (IMessage message in messages)
        {
            PlanMetadata original = new(UnknownExtensions(), ["unused", "unused"]);
            PlanBuilder builder = Builder(original);
            CorePlan before = builder.Build();
            int beforeHash = before.GetHashCode();
            Any expectedPayload = message is Any any ? any.Clone() : Any.Pack(message);
            AdvancedExtension expected = original.AdvancedExtensions!.ToProto();
            Action<IMessage>[] messageEdits = [builder.SetEnhancement, builder.AddOptimization, value => builder.ReplaceOptimization(0, value)];
            Action<ReadOnlyAny>[] facadeEdits = [builder.SetEnhancement, builder.AddOptimization, value => builder.ReplaceOptimization(0, value)];
            if (useFacade)
            {
                facadeEdits[operation](ReadOnlyAny.FromProto(expectedPayload));
            }
            else
            {
                messageEdits[operation](message);
            }

            switch (operation)
            {
                case 0:
                    expected.Enhancement = expectedPayload;
                    break;
                case 1:
                    expected.Optimization.Add(expectedPayload);
                    break;
                case 2:
                    expected.Optimization[0] = expectedPayload;
                    break;
            }

            switch (message)
            {
                case StringValue text:
                    await Assert.That(expectedPayload.TypeUrl).IsEqualTo("type.googleapis.com/google.protobuf.StringValue");
                    text.Value = "changed";
                    break;
                case RelCommon common:
                    common.Hint.Alias = "changed";
                    common.AdvancedExtension.Optimization.Clear();
                    break;
                case Any payload:
                    payload.TypeUrl = "changed";
                    payload.Value = ByteString.Empty;
                    break;
            }

            PlanMetadata after = builder.Metadata;
            await Assert.That(after.AdvancedExtensions!.ToProto()).IsEqualTo(expected);
            await Assert.That(after.ExpectedTypeUrls).IsEquivalentTo(original.ExpectedTypeUrls, CollectionOrdering.Matching);
            await Assert.That(before.Metadata).IsSameReferenceAs(original);
            await Assert.That(before.GetHashCode()).IsEqualTo(beforeHash);
            await Assert.That(before.Metadata.AdvancedExtensions!.ToProto()).IsEqualTo(UnknownExtensions());
            await Assert.That(builder.Build().Metadata).IsSameReferenceAs(after);
            IPlan built = builder.Build();
            IPlan imported = Decoder().FromBytes(new PlanToProtoConverter().From(built).ToByteArray());
            await Assert.That(imported).IsEqualTo(built);

            AdvancedExtension exported = after.AdvancedExtensions.ToProto();
            exported.Enhancement = null;
            exported.Optimization.Clear();
            await Assert.That(builder.Metadata.AdvancedExtensions!.ToProto()).IsEqualTo(expected);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WholeMessageReplacementPreservesUnknownFieldsAndExpectedUrls(bool useFacade)
    {
        PlanBuilder builder = Builder();
        builder.AddExpectedTypeUrl("keep");
        AdvancedExtension source = UnknownExtensions();
        AdvancedExtension expected = source.Clone();
        ReadOnlyAdvancedExtension facade = ReadOnlyAdvancedExtension.FromProto(source);
        if (useFacade)
        {
            builder.SetAdvancedExtensions(facade);
            await Assert.That(builder.Metadata.AdvancedExtensions).IsSameReferenceAs(facade);
        }
        else
        {
            builder.SetAdvancedExtensions(source);
        }

        source.Enhancement.TypeUrl = "changed";
        source.Optimization.Clear();
        CorePlan original = builder.Build();
        PlanMetadata snapshot = builder.Metadata;
        builder.SetAdvancedExtensions(new AdvancedExtension());

        await Assert.That(snapshot.AdvancedExtensions!.ToProto()).IsEqualTo(expected);
        await Assert.That(original.Metadata).IsSameReferenceAs(snapshot);
        await Assert.That(builder.Metadata.AdvancedExtensions!.ToProto()).IsEqualTo(new AdvancedExtension());
        await Assert.That(builder.Metadata.ExpectedTypeUrls).IsEquivalentTo(["keep"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task RemovalPreservesPresenceAndUnknownFieldsUntilWholeMessageIsCleared()
    {
        PlanBuilder builder = Builder();
        builder.AddExpectedTypeUrl("independent");
        PlanMetadata absent = builder.Metadata;
        builder.ClearEnhancement();
        builder.ClearOptimizations();
        builder.ClearAdvancedExtensions();
        await Assert.That(builder.Metadata).IsSameReferenceAs(absent);

        builder.SetEnhancement(new Any());
        builder.ClearEnhancement();
        await Assert.That(builder.Metadata.AdvancedExtensions!.ToProto()).IsEqualTo(new AdvancedExtension());
        CorePlan presentEmpty = builder.Build();
        builder.ClearAdvancedExtensions();
        await Assert.That(builder.Metadata.AdvancedExtensions).IsNull();
        await Assert.That(presentEmpty.Metadata.AdvancedExtensions).IsNotNull();
        builder.AddOptimization(new Any());
        builder.RemoveOptimizationAt(0);
        await Assert.That(builder.Metadata.AdvancedExtensions!.ToProto()).IsEqualTo(new AdvancedExtension());
        builder.AddOptimization(new Any());
        builder.ClearOptimizations();
        await Assert.That(builder.Metadata.AdvancedExtensions!.ToProto()).IsEqualTo(new AdvancedExtension());

        AdvancedExtension expected = UnknownExtensions();
        builder.SetAdvancedExtensions(expected);
        builder.RemoveOptimizationAt(0);
        expected.Optimization.RemoveAt(0);
        await Assert.That(builder.Metadata.AdvancedExtensions!.ToProto()).IsEqualTo(expected);
        builder.ClearOptimizations();
        expected.Optimization.Clear();
        await Assert.That(builder.Metadata.AdvancedExtensions!.ToProto()).IsEqualTo(expected);
        builder.ClearEnhancement();
        expected.Enhancement = null;
        await Assert.That(builder.Metadata.AdvancedExtensions!.ToProto()).IsEqualTo(expected);
        builder.ClearAdvancedExtensions();
        await Assert.That(builder.Metadata.AdvancedExtensions).IsNull();
        await Assert.That(builder.Metadata.ExpectedTypeUrls).IsEquivalentTo(["independent"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExpectedUrlEditsPreserveOrderDuplicatesAndEarlierSnapshots()
    {
        PlanBuilder builder = Builder();
        builder.SetEnhancement(new StringValue { Value = "payload" });
        ReadOnlyAdvancedExtension? extension = builder.Metadata.AdvancedExtensions;
        await Assert.That(builder.Metadata.ExpectedTypeUrls.Count).IsEqualTo(0);
        List<string> urls = ["Case", "", "Case", "case"];
        builder.SetExpectedTypeUrls(urls);
        urls.Clear();
        CorePlan first = builder.Build();
        int hash = first.GetHashCode();
        builder.AddExpectedTypeUrl("last");
        builder.ReplaceExpectedTypeUrl(1, "replacement");
        builder.RemoveExpectedTypeUrlAt(2);
        await Assert.That(builder.Metadata.ExpectedTypeUrls).IsEquivalentTo(["Case", "replacement", "case", "last"], CollectionOrdering.Matching);
        await Assert.That(builder.Metadata.AdvancedExtensions).IsSameReferenceAs(extension);
        CorePlan second = builder.Build();
        builder.ClearExpectedTypeUrls();
        await Assert.That(builder.Metadata.ExpectedTypeUrls.Count).IsEqualTo(0);
        await Assert.That(builder.Metadata.AdvancedExtensions).IsSameReferenceAs(extension);
        await Assert.That(first.Metadata.ExpectedTypeUrls).IsEquivalentTo(["Case", "", "Case", "case"], CollectionOrdering.Matching);
        await Assert.That(first.GetHashCode()).IsEqualTo(hash);
        await Assert.That(second.Metadata.ExpectedTypeUrls).IsEquivalentTo(["Case", "replacement", "case", "last"], CollectionOrdering.Matching);
        await Assert.That(Decoder().From(new PlanToProtoConverter().From(second))).IsEqualTo(second);
    }

    [Test]
    public async Task FailedEditsDoNotChangeMetadataOrPreviouslyBuiltPlans()
    {
        PlanBuilder builder = Builder(new(UnknownExtensions(), ["one", "two"]));
        PlanMetadata original = builder.Metadata;
        CorePlan plan = builder.Build();
        int hash = plan.GetHashCode();
        Action[] nullEdits =
        [
            () => builder.SetAdvancedExtensions(message: null!),
            () => builder.SetAdvancedExtensions(extensions: null!),
            () => builder.SetEnhancement(message: null!),
            () => builder.SetEnhancement(detail: null!),
            () => builder.AddOptimization(message: null!),
            () => builder.AddOptimization(detail: null!),
            () => builder.ReplaceOptimization(0, message: null!),
            () => builder.ReplaceOptimization(0, detail: null!),
            () => builder.SetExpectedTypeUrls(null!),
            () => builder.AddExpectedTypeUrl(null!),
            () => builder.ReplaceExpectedTypeUrl(0, null!),
        ];
        foreach (Action edit in nullEdits)
        {
            await Assert.That(edit).ThrowsExactly<ArgumentNullException>();
            await Assert.That(builder.Metadata).IsSameReferenceAs(original);
        }

        foreach (int index in new[] { -1, 2, int.MaxValue })
        {
            Action[] invalidIndices =
            [
                () => builder.ReplaceOptimization(index, new Any()),
                () => builder.RemoveOptimizationAt(index),
                () => builder.ReplaceExpectedTypeUrl(index, "replacement"),
                () => builder.RemoveExpectedTypeUrlAt(index),
            ];
            foreach (Action edit in invalidIndices)
            {
                await Assert.That(edit).ThrowsExactly<ArgumentOutOfRangeException>();
                await Assert.That(builder.Metadata).IsSameReferenceAs(original);
            }
        }

        await Assert.That(() => builder.SetExpectedTypeUrls(["valid", null!])).ThrowsExactly<ArgumentException>();
        await Assert.That(builder.Metadata).IsSameReferenceAs(original);
        await Assert.That(() => builder.SetExpectedTypeUrls(FailingUrls())).ThrowsExactly<InvalidOperationException>();
        await Assert.That(builder.Metadata).IsSameReferenceAs(original);
        await Assert.That(plan.GetHashCode()).IsEqualTo(hash);

        builder.ClearAdvancedExtensions();
        PlanMetadata absent = builder.Metadata;
        await Assert.That(() => builder.RemoveOptimizationAt(0)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => builder.ReplaceOptimization(0, new Any())).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(builder.Metadata).IsSameReferenceAs(absent);
    }

    private static IEnumerable<string> FailingUrls()
    {
        yield return "must-not-be-applied";
        throw new InvalidOperationException("Source enumeration failed.");
    }

    private static AdvancedExtension UnknownExtensions() => MetadataFacadeTests.CreateCommon().AdvancedExtension;

    private static ProtoToPlanConverter Decoder() => new(new ExtensionsCollection());

    private static PlanBuilder Builder(PlanMetadata? metadata = null)
    {
        PlanBuilder builder = new(metadata ?? PlanMetadata.Empty, version: null);
        builder.AddRoot(RelationMetadataConversionTests.Read(), ["value"]);
        return builder;
    }
}
