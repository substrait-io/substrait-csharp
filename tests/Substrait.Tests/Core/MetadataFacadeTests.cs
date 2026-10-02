// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Metadata;
using ProtoAdvanced = Substrait.Protobuf.AdvancedExtension;
using ProtoCommon = Substrait.Protobuf.RelCommon;

namespace Substrait.Tests.Core;

[TestClass]
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

    [TestMethod]
    public void EveryMetadataFieldHasATypedReadOnlyProperty()
    {
        foreach (var (descriptor, facade) in Facades)
        {
            Assert.IsTrue(facade.IsSealed);
            Assert.IsFalse(typeof(IMessage).IsAssignableFrom(facade));
            Assert.AreEqual(0, facade.GetConstructors().Length);
            Assert.IsNull(facade.GetMethod("FromOwnedProto", BindingFlags.Public | BindingFlags.Static));
            Assert.AreEqual(
                descriptor.Fields.InFieldNumberOrder().Count + descriptor.RealOneofCount,
                facade.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length,
                descriptor.FullName);
            foreach (FieldDescriptor field in descriptor.Fields.InFieldNumberOrder())
            {
                PropertyInfo? property = facade.GetProperty(field.PropertyName);
                Assert.IsNotNull(property, field.FullName);
                Assert.IsFalse(property.CanWrite, field.FullName);

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

                Assert.AreEqual(expected, property.PropertyType, field.FullName);
            }
        }
    }

    [TestMethod]
    public void BinaryRoundTripPreservesEveryMetadataFieldAndUnknownData()
    {
        ProtoCommon original = CreateCommon();
        ReadOnlyRelCommon facade = ReadOnlyRelCommon.FromProto(ProtoCommon.Parser.ParseFrom(original.ToByteArray()));
        ProtoCommon roundTrip = ProtoCommon.Parser.ParseFrom(facade.ToProto().ToByteArray());

        Assert.AreEqual(original, roundTrip);
        CollectionAssert.AreEqual(original.ToByteArray(), roundTrip.ToByteArray());
        Assert.AreEqual(uint.MaxValue, facade.RelAnchor);
        Assert.AreEqual(ProtoCommon.EmitKindOneofCase.Emit, facade.EmitKindCase);
        CollectionAssert.AreEqual(ExpectedMapping, facade.Emit!.OutputMapping.ToArray());
        Assert.AreEqual("orders", facade.Hint!.Alias);
        Assert.AreEqual(12.5, facade.Hint.Stats!.RowCount);
        Assert.AreEqual(64.0, facade.Hint.Stats.RecordSize);
        CollectionAssert.AreEqual(ExpectedNames, facade.Hint.OutputNames.ToArray());
        Assert.AreEqual(7, facade.Hint.SavedComputations[0].ComputationId);
        Assert.AreEqual(7, facade.Hint.LoadedComputations[0].ComputationIdReference);
        Assert.AreEqual(123456, (int)facade.Hint.SavedComputations[0].Type);

        AssertExtensions(original.AdvancedExtension, facade.AdvancedExtension!);
        AssertExtensions(original.Hint.AdvancedExtension, facade.Hint.AdvancedExtension!);
        AssertExtensions(original.Hint.Stats.AdvancedExtension, facade.Hint.Stats.AdvancedExtension!);
        AssertExtensions(original.Hint.Constraint.AdvancedExtension, facade.Hint.Constraint!.AdvancedExtension!);
        AssertExtensions(original.Hint.SavedComputations[0].AdvancedExtension, facade.Hint.SavedComputations[0].AdvancedExtension!);
        AssertExtensions(original.Hint.LoadedComputations[0].AdvancedExtension, facade.Hint.LoadedComputations[0].AdvancedExtension!);
    }

    [TestMethod]
    public void MutatingSourceMessagesAndCollectionsCannotChangeSnapshotOrHash()
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

        Assert.AreEqual(expected, facade.ToProto());
        Assert.AreEqual(hash, facade.GetHashCode());
        Assert.AreEqual("stable", dictionary[ReadOnlyRelCommon.FromProto(expected)]);
    }

    [TestMethod]
    public void ExportsAtEveryLevelAreDetachedAndCollectionsRejectMutation()
    {
        ProtoCommon original = CreateCommon();
        ReadOnlyRelCommon facade = ReadOnlyRelCommon.FromProto(original);
        ProtoCommon exported = facade.ToProto();
        exported.Hint.OutputNames.Clear();
        exported.AdvancedExtension.Enhancement.TypeUrl = "changed";
        facade.Hint!.ToProto().Stats.RecordSize = 0;
        facade.Hint.SavedComputations[0].ToProto().AdvancedExtension.Optimization.Clear();
        facade.AdvancedExtension!.Optimization[0].ToProto().Value = ByteString.Empty;

        Assert.ThrowsException<NotSupportedException>(() => ((IList<int>)facade.Emit!.OutputMapping).Add(99));
        Assert.ThrowsException<NotSupportedException>(() => ((IList<string>)facade.Hint.OutputNames)[0] = "changed");
        Assert.ThrowsException<NotSupportedException>(() => ((IList<ReadOnlyAny>)facade.AdvancedExtension.Optimization).Clear());
        Assert.ThrowsException<NotSupportedException>(() => ((IList<ReadOnlyRelCommonHintSavedComputation>)facade.Hint.SavedComputations).Clear());
        Assert.AreEqual(original, facade.ToProto());
        Assert.AreSame(facade.Hint, facade.Hint);
        Assert.AreSame(facade.Hint.OutputNames, facade.Hint.OutputNames);
        Assert.AreSame(facade.AdvancedExtension.Optimization[0], facade.AdvancedExtension.Optimization[0]);
    }

    [TestMethod]
    public void OptionalFieldsEmptyMessagesAndEmitCasesRetainPresence()
    {
        ReadOnlyRelCommon absent = ReadOnlyRelCommon.FromProto(new());
        Assert.IsNull(absent.RelAnchor);
        Assert.IsNull(absent.Hint);
        Assert.IsNull(absent.AdvancedExtension);
        Assert.IsNull(absent.Direct);
        Assert.IsNull(absent.Emit);
        Assert.AreEqual(ProtoCommon.EmitKindOneofCase.None, absent.EmitKindCase);
        Assert.IsFalse(absent.ToProto().HasRelAnchor);

        ReadOnlyRelCommon empty = ReadOnlyRelCommon.FromProto(new()
        {
            RelAnchor = 0,
            Hint = new() { Stats = new(), Constraint = new() },
            AdvancedExtension = new() { Enhancement = new() },
            Emit = new(),
        });
        Assert.AreEqual(0U, empty.RelAnchor);
        Assert.IsTrue(empty.ToProto().HasRelAnchor);
        Assert.IsNotNull(empty.Hint!.Stats);
        Assert.IsNotNull(empty.Hint.Constraint);
        Assert.IsNotNull(empty.AdvancedExtension!.Enhancement);
        Assert.AreEqual(0, empty.Emit!.OutputMapping.Count);
        Assert.AreEqual(ProtoCommon.EmitKindOneofCase.Emit, empty.EmitKindCase);
        Assert.IsNull(empty.Direct);

        ProtoCommon direct = new() { Direct = AddUnknownField(new ProtoCommon.Types.Direct(), ProtoCommon.Types.Direct.Parser) };
        ReadOnlyRelCommon explicitDirect = ReadOnlyRelCommon.FromProto(direct);
        Assert.AreEqual(ProtoCommon.EmitKindOneofCase.Direct, explicitDirect.EmitKindCase);
        Assert.IsNotNull(explicitDirect.Direct);
        Assert.IsNull(explicitDirect.Emit);
        Assert.AreEqual(direct, explicitDirect.ToProto());
    }

    [TestMethod]
    public void EqualityAndHashingFollowTheCompleteBackingMessages()
    {
        ProtoCommon original = CreateCommon();
        ReadOnlyRelCommon first = ReadOnlyRelCommon.FromProto(original);
        ReadOnlyRelCommon second = ReadOnlyRelCommon.FromProto(original);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.IsTrue(first.Equals((object)second));
        Assert.IsFalse(first.Equals(null));
        Assert.IsFalse(first.Equals((object)original));

        original.Hint.Alias = "different";
        Assert.AreNotEqual(first, ReadOnlyRelCommon.FromProto(original));
        Assert.AreNotEqual(
            ReadOnlyRelCommon.FromProto(new()),
            ReadOnlyRelCommon.FromProto(AddUnknownField(new ProtoCommon(), ProtoCommon.Parser)));
        Assert.AreNotEqual(
            ReadOnlyAny.FromProto(new() { TypeUrl = "unknown", Value = ByteString.CopyFrom([1]) }),
            ReadOnlyAny.FromProto(new() { TypeUrl = "unknown", Value = ByteString.CopyFrom([2]) }));
    }

    [TestMethod]
    public void EveryFacadeHasSafeCopyingFactoriesAndDetachedExports()
    {
        foreach (var (descriptor, facade) in Facades)
        {
            IMessage original = AddUnknownField(descriptor.Parser.ParseFrom([]), descriptor.Parser);
            MethodInfo factory = facade.GetMethod("FromProto")!;
            MethodInfo export = facade.GetMethod("ToProto")!;
            object snapshot = factory.Invoke(null, [original])!;
            IMessage copy = (IMessage)export.Invoke(snapshot, null)!;
            Assert.AreNotSame(original, copy);
            Assert.AreEqual(original, copy);
            Assert.AreEqual(snapshot, factory.Invoke(null, [copy]));
            TargetInvocationException error = Assert.ThrowsException<TargetInvocationException>(
                () => factory.Invoke(null, [null]));
            Assert.IsInstanceOfType<ArgumentNullException>(error.InnerException);
        }
    }

    [TestMethod]
    public void OwnedConstructionSharesOnlyPrivatelyHeldStorage()
    {
        ProtoCommon original = CreateCommon();
        ReadOnlyRelCommon owned = (ReadOnlyRelCommon)typeof(ReadOnlyRelCommon)
            .GetMethod("FromOwnedProto", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [original])!;
        ReadOnlyRelCommon copied = ReadOnlyRelCommon.FromProto(original);

        Assert.AreSame(original, Backing(owned));
        Assert.AreSame(original.Hint, Backing(owned.Hint!));
        Assert.AreSame(original.Hint.SavedComputations[0], Backing(owned.Hint!.SavedComputations[0]));
        Assert.AreSame(original.AdvancedExtension.Optimization[0], Backing(owned.AdvancedExtension!.Optimization[0]));
        Assert.AreNotSame(original, Backing(copied));
        Assert.AreNotSame(original.Hint, Backing(copied.Hint!));
        Assert.AreEqual(copied, owned);
    }

    [TestMethod]
    public void JsonRoundTripUsesRegisteredPayloadDescriptors()
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
        Assert.AreEqual(original, parser.Parse<ProtoCommon>(formatter.Format(facade.ToProto())));
    }

    private static object? Backing(object facade) =>
        facade.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(facade);

    private static void AssertExtensions(ProtoAdvanced original, ReadOnlyAdvancedExtension facade)
    {
        Assert.AreEqual(original, facade.ToProto());
        Assert.AreEqual(2, facade.Optimization.Count);
        for (int index = 0; index < original.Optimization.Count; ++index)
        {
            Assert.AreEqual(original.Optimization[index].TypeUrl, facade.Optimization[index].TypeUrl);
            Assert.AreEqual(original.Optimization[index].Value, facade.Optimization[index].Value);
        }

        Assert.AreEqual(original.Enhancement, facade.Enhancement!.ToProto());
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
