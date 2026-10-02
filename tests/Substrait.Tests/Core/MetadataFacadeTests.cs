// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Metadata;
using ProtoAdvanced = Substrait.Protobuf.AdvancedExtension;
using ProtoCommon = Substrait.Protobuf.RelCommon;
using StringValue = Google.Protobuf.WellKnownTypes.StringValue;

namespace Substrait.Tests.Core;

public sealed class MetadataFacadeTests
{
    private static readonly int[] ExpectedMapping = [2, 0, 2];
    private static readonly string[] ExpectedNames = ["third", "first", "third_again"];
    private static readonly (MessageDescriptor Descriptor, System.Type Facade)[] Facades =
    [
        (ProtoCommon.Descriptor, typeof(ReadOnlyRelCommon)),
        (ProtoCommon.Types.Direct.Descriptor, typeof(ReadOnlyRelCommonDirect)),
        (ProtoCommon.Types.Emit.Descriptor, typeof(ReadOnlyRelCommonEmit)),
        (ProtoCommon.Types.Hint.Descriptor, typeof(ReadOnlyRelCommonHint)),
        (ProtoCommon.Types.Hint.Types.Stats.Descriptor, typeof(ReadOnlyRelCommonHintStats)),
        (ProtoCommon.Types.Hint.Types.RuntimeConstraint.Descriptor, typeof(ReadOnlyRelCommonHintRuntimeConstraint)),
        (ProtoCommon.Types.Hint.Types.SavedComputation.Descriptor, typeof(ReadOnlyRelCommonHintSavedComputation)),
        (ProtoCommon.Types.Hint.Types.LoadedComputation.Descriptor, typeof(ReadOnlyRelCommonHintLoadedComputation)),
        (ProtoAdvanced.Descriptor, typeof(ReadOnlyAdvancedExtension)),
        (Any.Descriptor, typeof(ReadOnlyAny)),
    ];

    [Test]
    public async Task EveryMetadataFieldHasATypedReadOnlyProperty()
    {
        foreach (var (descriptor, facade) in Facades)
        {
            await Assert.That(facade.IsSealed).IsTrue();
            await Assert.That(typeof(IMessage).IsAssignableFrom(facade)).IsFalse();
            await Assert.That(facade.GetConstructors().Length).IsEqualTo(0);
            await Assert.That(facade.GetMethod("FromOwnedProto", BindingFlags.Public | BindingFlags.Static)).IsNull();
            await Assert.That(facade.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length).IsEqualTo(descriptor.Fields.InFieldNumberOrder().Count + descriptor.RealOneofCount).Because(descriptor.FullName);
            foreach (FieldDescriptor field in descriptor.Fields.InFieldNumberOrder())
            {
                PropertyInfo? property = facade.GetProperty(field.PropertyName);
                await Assert.That(property).IsNotNull().Because(field.FullName);
                await Assert.That(property.CanWrite).IsFalse().Because(field.FullName);

                System.Type expected = field.FieldType == FieldType.Message
                    ? Facades.Single(pair => pair.Descriptor == field.MessageType).Facade
                    : descriptor.ClrType.GetProperty(field.PropertyName)!.PropertyType;
                if (field.IsRepeated)
                {
                    if (field.FieldType != FieldType.Message)
                    {
                        expected = expected.GetGenericArguments()[0];
                    }

                    expected = typeof(IReadOnlyList<>).MakeGenericType(expected);
                }
                else if (field.HasPresence && expected.IsValueType)
                {
                    expected = typeof(Nullable<>).MakeGenericType(expected);
                }

                await Assert.That(property.PropertyType).IsEqualTo(expected).Because(field.FullName);
            }
        }
    }

    [Test]
    public async Task BinaryRoundTripPreservesEveryMetadataFieldAndUnknownData()
    {
        ProtoCommon original = CreateCommon();
        ReadOnlyRelCommon facade = ReadOnlyRelCommon.FromProto(ProtoCommon.Parser.ParseFrom(original.ToByteArray()));
        ProtoCommon roundTrip = ProtoCommon.Parser.ParseFrom(facade.ToProto().ToByteArray());

        await Assert.That(roundTrip).IsEqualTo(original);
        await Assert.That(roundTrip.ToByteArray()).IsEquivalentTo(original.ToByteArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(facade.RelAnchor).IsEqualTo(uint.MaxValue);
        await Assert.That(facade.EmitKindCase).IsEqualTo(ProtoCommon.EmitKindOneofCase.Emit);
        await Assert.That(facade.Emit!.OutputMapping.ToArray()).IsEquivalentTo(ExpectedMapping, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(facade.Hint!.Alias).IsEqualTo("orders");
        await Assert.That(facade.Hint.Stats!.RowCount).IsEqualTo(12.5);
        await Assert.That(facade.Hint.Stats.RecordSize).IsEqualTo(64.0);
        await Assert.That(facade.Hint.OutputNames.ToArray()).IsEquivalentTo(ExpectedNames, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(facade.Hint.SavedComputations[0].ComputationId).IsEqualTo(7);
        await Assert.That(facade.Hint.LoadedComputations[0].ComputationIdReference).IsEqualTo(7);
        await Assert.That((int)facade.Hint.SavedComputations[0].Type).IsEqualTo(123456);

        await AssertExtensions(original.AdvancedExtension, facade.AdvancedExtension!);
        await AssertExtensions(original.Hint.AdvancedExtension, facade.Hint.AdvancedExtension!);
        await AssertExtensions(original.Hint.Stats.AdvancedExtension, facade.Hint.Stats.AdvancedExtension!);
        await AssertExtensions(original.Hint.Constraint.AdvancedExtension, facade.Hint.Constraint!.AdvancedExtension!);
        await AssertExtensions(original.Hint.SavedComputations[0].AdvancedExtension, facade.Hint.SavedComputations[0].AdvancedExtension!);
        await AssertExtensions(original.Hint.LoadedComputations[0].AdvancedExtension, facade.Hint.LoadedComputations[0].AdvancedExtension!);
    }

    [Test]
    public async Task MutatingSourceMessagesAndCollectionsCannotChangeSnapshotOrHash()
    {
        ProtoCommon source = CreateCommon();
        ProtoCommon expected = source.Clone();
        ReadOnlyRelCommon facade = ReadOnlyRelCommon.FromProto(source);
        int hash = facade.GetHashCode();
        var dictionary = new Dictionary<ReadOnlyRelCommon, string> { [facade] = "stable" };
        Any aliasedPayload = source.Hint.SavedComputations[0].AdvancedExtension.Enhancement;

        source.RelAnchor = 3;
        source.Emit.OutputMapping.Clear();
        source.Hint.Alias = "changed";
        source.Hint.OutputNames.Add("changed");
        source.Hint.Stats.RowCount = 0;
        source.Hint.Constraint.AdvancedExtension.Optimization.Clear();
        source.AdvancedExtension.Optimization[0].TypeUrl = "changed";
        aliasedPayload.Value = ByteString.Empty;
        source.Hint.SavedComputations.Clear();
        source.Hint.LoadedComputations[0].ComputationIdReference = 8;
        source.Hint = new();

        await Assert.That(facade.ToProto()).IsEqualTo(expected);
        await Assert.That(facade.GetHashCode()).IsEqualTo(hash);
        await Assert.That(dictionary[ReadOnlyRelCommon.FromProto(expected)]).IsEqualTo("stable");
    }

    [Test]
    public async Task ExportsAtEveryLevelAreDetachedAndCollectionsRejectMutation()
    {
        ProtoCommon original = CreateCommon();
        ReadOnlyRelCommon facade = ReadOnlyRelCommon.FromProto(original);
        ProtoCommon exported = facade.ToProto();
        exported.Hint.OutputNames.Clear();
        exported.AdvancedExtension.Enhancement.TypeUrl = "changed";
        facade.Hint!.ToProto().Stats.RecordSize = 0;
        facade.Hint.SavedComputations[0].ToProto().AdvancedExtension.Optimization.Clear();
        facade.AdvancedExtension!.Optimization[0].ToProto().Value = ByteString.Empty;

        await Assert.That(() => ((IList<int>)facade.Emit!.OutputMapping).Add(99)).ThrowsExactly<NotSupportedException>();
        await Assert.That(() => ((IList<string>)facade.Hint.OutputNames)[0] = "changed").ThrowsExactly<NotSupportedException>();
        await Assert.That(() => ((IList<ReadOnlyAny>)facade.AdvancedExtension.Optimization).Clear()).ThrowsExactly<NotSupportedException>();
        await Assert.That(() => ((IList<ReadOnlyRelCommonHintSavedComputation>)facade.Hint.SavedComputations).Clear()).ThrowsExactly<NotSupportedException>();
        await Assert.That(facade.ToProto()).IsEqualTo(original);
        await Assert.That(facade.Hint).IsSameReferenceAs(facade.Hint);
        await Assert.That(facade.Hint.OutputNames).IsSameReferenceAs(facade.Hint.OutputNames);
        await Assert.That(facade.AdvancedExtension.Optimization[0]).IsSameReferenceAs(facade.AdvancedExtension.Optimization[0]);
    }

    [Test]
    public async Task OptionalFieldsEmptyMessagesAndEmitCasesRetainPresence()
    {
        ReadOnlyRelCommon absent = ReadOnlyRelCommon.FromProto(new());
        await Assert.That(absent.RelAnchor).IsNull();
        await Assert.That(absent.Hint).IsNull();
        await Assert.That(absent.AdvancedExtension).IsNull();
        await Assert.That(absent.Direct).IsNull();
        await Assert.That(absent.Emit).IsNull();
        await Assert.That(absent.EmitKindCase).IsEqualTo(ProtoCommon.EmitKindOneofCase.None);
        await Assert.That(absent.ToProto().HasRelAnchor).IsFalse();

        ReadOnlyRelCommon empty = ReadOnlyRelCommon.FromProto(new()
        {
            RelAnchor = 0,
            Hint = new() { Stats = new(), Constraint = new() },
            AdvancedExtension = new() { Enhancement = new() },
            Emit = new(),
        });
        await Assert.That(empty.RelAnchor).IsEqualTo(0U);
        await Assert.That(empty.ToProto().HasRelAnchor).IsTrue();
        await Assert.That(empty.Hint!.Stats).IsNotNull();
        await Assert.That(empty.Hint.Constraint).IsNotNull();
        await Assert.That(empty.AdvancedExtension!.Enhancement).IsNotNull();
        await Assert.That(empty.Emit!.OutputMapping.Count).IsEqualTo(0);
        await Assert.That(empty.EmitKindCase).IsEqualTo(ProtoCommon.EmitKindOneofCase.Emit);
        await Assert.That(empty.Direct).IsNull();

        ProtoCommon direct = new() { Direct = AddUnknownField(new ProtoCommon.Types.Direct(), ProtoCommon.Types.Direct.Parser) };
        ReadOnlyRelCommon explicitDirect = ReadOnlyRelCommon.FromProto(direct);
        await Assert.That(explicitDirect.EmitKindCase).IsEqualTo(ProtoCommon.EmitKindOneofCase.Direct);
        await Assert.That(explicitDirect.Direct).IsNotNull();
        await Assert.That(explicitDirect.Emit).IsNull();
        await Assert.That(explicitDirect.ToProto()).IsEqualTo(direct);
    }

    [Test]
    public async Task EqualityAndHashingFollowTheCompleteBackingMessages()
    {
        ProtoCommon original = CreateCommon();
        ReadOnlyRelCommon first = ReadOnlyRelCommon.FromProto(original);
        ReadOnlyRelCommon second = ReadOnlyRelCommon.FromProto(original);
        await Assert.That(second).IsEqualTo(first);
        await Assert.That(second.GetHashCode()).IsEqualTo(first.GetHashCode());
        await Assert.That(first.Equals((object)second)).IsTrue();
        await Assert.That(first.Equals((object)original)).IsFalse();
        await Assert.That(first.Equals(null)).IsFalse();

        original.Hint.Alias = "different";
        await Assert.That(ReadOnlyRelCommon.FromProto(original)).IsNotEqualTo(first);
        await Assert.That(ReadOnlyRelCommon.FromProto(AddUnknownField(new ProtoCommon(), ProtoCommon.Parser))).IsNotEqualTo(ReadOnlyRelCommon.FromProto(new()));
        await Assert.That(ReadOnlyAny.FromProto(new() { TypeUrl = "unknown", Value = ByteString.CopyFrom([2]) })).IsNotEqualTo(ReadOnlyAny.FromProto(new() { TypeUrl = "unknown", Value = ByteString.CopyFrom([1]) }));
    }

    [Test]
    public async Task EveryFacadeHasSafeCopyingFactoriesAndDetachedExports()
    {
        foreach (var (descriptor, facade) in Facades)
        {
            IMessage original = AddUnknownField(descriptor.Parser.ParseFrom([]), descriptor.Parser);
            MethodInfo factory = facade.GetMethod("FromProto")!;
            MethodInfo export = facade.GetMethod("ToProto")!;
            object snapshot = factory.Invoke(null, [original])!;
            IMessage copy = (IMessage)export.Invoke(snapshot, null)!;
            await Assert.That(copy).IsNotSameReferenceAs(original);
            await Assert.That(copy).IsEqualTo(original);
            await Assert.That(factory.Invoke(null, [copy])).IsEqualTo(snapshot);
            TargetInvocationException error = await Assert.That(() => factory.Invoke(null, [null])).ThrowsExactly<TargetInvocationException>().And.IsNotNull();
            await Assert.That(error.InnerException).IsAssignableTo<ArgumentNullException>();
        }
    }

    [Test]
    public async Task OwnedConstructionSharesOnlyPrivatelyHeldStorage()
    {
        ProtoCommon original = CreateCommon();
        ReadOnlyRelCommon owned = (ReadOnlyRelCommon)typeof(ReadOnlyRelCommon)
            .GetMethod("FromOwnedProto", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [original])!;
        ReadOnlyRelCommon copied = ReadOnlyRelCommon.FromProto(original);

        await Assert.That(Backing(owned)).IsSameReferenceAs(original);
        await Assert.That(Backing(owned.Hint!)).IsSameReferenceAs(original.Hint);
        await Assert.That(Backing(owned.Hint!.SavedComputations[0])).IsSameReferenceAs(original.Hint.SavedComputations[0]);
        await Assert.That(Backing(owned.AdvancedExtension!.Optimization[0])).IsSameReferenceAs(original.AdvancedExtension.Optimization[0]);
        await Assert.That(Backing(copied)).IsNotSameReferenceAs(original);
        await Assert.That(Backing(copied.Hint!)).IsNotSameReferenceAs(original.Hint);
        await Assert.That(owned).IsEqualTo(copied);
    }

    [Test]
    public async Task JsonRoundTripUsesRegisteredPayloadDescriptors()
    {
        var registry = TypeRegistry.FromMessages(StringValue.Descriptor);
        JsonFormatter formatter = new(JsonFormatter.Settings.Default.WithTypeRegistry(registry));
        JsonParser parser = new(JsonParser.Settings.Default.WithTypeRegistry(registry));
        ProtoCommon original = new()
        {
            RelAnchor = 42,
            Hint = new() { Alias = "orders" },
            AdvancedExtension = new()
            {
                Enhancement = Any.Pack(new StringValue { Value = "enhancement" }),
                Optimization = { Any.Pack(new StringValue { Value = "optimization" }) },
            },
        };

        ReadOnlyRelCommon facade = ReadOnlyRelCommon.FromProto(parser.Parse<ProtoCommon>(formatter.Format(original)));
        await Assert.That(parser.Parse<ProtoCommon>(formatter.Format(facade.ToProto()))).IsEqualTo(original);
    }

    private static object? Backing(object facade) =>
        facade.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(facade);

    private static async Task AssertExtensions(ProtoAdvanced original, ReadOnlyAdvancedExtension facade)
    {
        await Assert.That(facade.ToProto()).IsEqualTo(original);
        await Assert.That(facade.Optimization.Count).IsEqualTo(2);
        for (int index = 0; index < original.Optimization.Count; ++index)
        {
            await Assert.That(facade.Optimization[index].TypeUrl).IsEqualTo(original.Optimization[index].TypeUrl);
            await Assert.That(facade.Optimization[index].Value).IsEqualTo(original.Optimization[index].Value);
        }

        await Assert.That(facade.Enhancement!.ToProto()).IsEqualTo(original.Enhancement);
    }

    internal static ProtoCommon CreateCommon() => AddUnknownField(new ProtoCommon
    {
        RelAnchor = uint.MaxValue,
        Emit = new() { OutputMapping = { 2, 0, 2 } },
        AdvancedExtension = Extensions("common"),
        Hint = new()
        {
            Alias = "orders",
            OutputNames = { "third", "first", "third_again" },
            AdvancedExtension = Extensions("hint"),
            Stats = new() { RowCount = 12.5, RecordSize = 64, AdvancedExtension = Extensions("stats") },
            Constraint = new() { AdvancedExtension = Extensions("constraint") },
            SavedComputations =
            {
                new ProtoCommon.Types.Hint.Types.SavedComputation
                {
                    ComputationId = 7,
                    Type = (ProtoCommon.Types.Hint.Types.ComputationType)123456,
                    AdvancedExtension = Extensions("saved"),
                },
            },
            LoadedComputations =
            {
                new ProtoCommon.Types.Hint.Types.LoadedComputation
                {
                    ComputationIdReference = 7,
                    Type = (ProtoCommon.Types.Hint.Types.ComputationType)123456,
                    AdvancedExtension = Extensions("loaded"),
                },
            },
        },
    }, ProtoCommon.Parser);

    private static ProtoAdvanced Extensions(string location) => AddUnknownField(new ProtoAdvanced
    {
        Enhancement = Payload(location + "/enhancement"),
        Optimization = { Payload(location + "/first"), Payload(location + "/second") },
    }, ProtoAdvanced.Parser);

    private static Any Payload(string location) => AddUnknownField(new Any
    {
        TypeUrl = "https://example.invalid/" + location,
        Value = ByteString.CopyFrom([0, 255, 128, 1]),
    }, Any.Parser);

    private static T AddUnknownField<T>(T message, MessageParser<T> parser)
        where T : IMessage<T> => (T)AddUnknownField(message, (MessageParser)parser);

    private static IMessage AddUnknownField(IMessage message, MessageParser parser)
    {
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        using (var output = new CodedOutputStream(stream, leaveOpen: true))
        {
            output.WriteTag(1000, WireFormat.WireType.Varint);
            output.WriteUInt64(123456);
            output.Flush();
        }

        return parser.ParseFrom(stream.ToArray());
    }
}
