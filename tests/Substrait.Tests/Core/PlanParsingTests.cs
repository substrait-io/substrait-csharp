// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation.Converters;
using Substrait.Protobuf;
using static Substrait.Tests.Core.RelationMetadataConversionTests;
using ProtoPlan = Substrait.Protobuf.Plan;
using StringValue = Google.Protobuf.WellKnownTypes.StringValue;

namespace Substrait.Tests.Core;

public sealed class PlanParsingTests
{
    [Test]
    public async Task BytesStreamsAndFilesPreserveOpaqueMetadata()
    {
        var relation = new RelToProtoConverter().From(Read());
        relation.Read.Common = MetadataFacadeTests.CreateCommon();
        relation.Read.Common.Emit = null;
        relation.Read.AdvancedExtension = relation.Read.Common.Hint.AdvancedExtension.Clone();
        relation.Read.NamedTable.AdvancedExtension = relation.Read.Common.Hint.Constraint.AdvancedExtension.Clone();
        ProtoPlan original = WirePlan(relation);
        byte[] bytes = original.ToByteArray();
        ProtoToPlanConverter converter = Decoder();
        IPlan fromBytes = converter.FromBytes(bytes, ExtensionsDictionary.StrictMode.OFF);
        using MemoryStream stream = new(bytes);
        IPlan fromStream = converter.FromStream(stream, ExtensionsDictionary.StrictMode.OFF);
        await Assert.That(stream.CanRead).IsTrue();
        await Assert.That(stream.Position).IsEqualTo(stream.Length);
        await Assert.That(fromStream).IsEqualTo(fromBytes);
        Array.Clear(bytes, 0, bytes.Length);
        await Assert.That(new PlanToProtoConverter().From(fromBytes)).IsEqualTo(original);

        string path = Path.Combine(Path.GetTempPath(), nameof(PlanParsingTests) + "-" + Guid.NewGuid().ToString("N") + ".pb");
        try
        {
            File.WriteAllBytes(path, original.ToByteArray());
            await Assert.That(converter.FromFile(path, ExtensionsDictionary.StrictMode.OFF)).IsEqualTo(fromBytes);
            using FileStream exclusive = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await Assert.That(exclusive.CanWrite).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task PrivateConversionOwnsMetadataWhilePublicProtoConversionCopies()
    {
        ProtoPlan proto = WirePlan(new RelToProtoConverter().From(Read()));
        ProtoToPlanConverter converter = Decoder();
        MethodInfo ownedConversion = typeof(ProtoToPlanConverter).GetMethod("FromCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IPlan owned = (IPlan)ownedConversion.Invoke(converter, [proto, ExtensionsDictionary.StrictMode.OFF, true])!;
        IPlan copied = converter.From(proto, ExtensionsDictionary.StrictMode.OFF);
        FieldInfo backing = typeof(ReadOnlyRelCommon).GetField("value", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Assert.That(backing.GetValue(owned.Relations[0].Input.Metadata.Common)).IsSameReferenceAs(proto.Relations[0].Rel.Read.Common);
        await Assert.That(backing.GetValue(copied.Relations[0].Input.Metadata.Common)).IsNotSameReferenceAs(proto.Relations[0].Rel.Read.Common);
        await Assert.That(new PlanToProtoConverter().From(owned).Relations[0].Rel.Read.Common).IsNotSameReferenceAs(proto.Relations[0].Rel.Read.Common);
    }

    [Test]
    public async Task JsonAcceptsExplicitPayloadRegistryAndDoesNotChangeBinaryPolicy()
    {
        ProtoPlan original = WirePlan(new RelToProtoConverter().From(Read()));
        original.Relations[0].Rel.Read.AdvancedExtension = new()
        {
            Enhancement = Any.Pack(new StringValue { Value = "semantics" }),
        };
        TypeRegistry registry = TypeRegistry.FromMessages(StringValue.Descriptor);
        JsonFormatter formatter = new(JsonFormatter.Settings.Default.WithTypeRegistry(registry));
        JsonParser parser = new(JsonParser.Settings.Default.WithTypeRegistry(registry));
        string json = formatter.Format(original);
        IPlan converted = Decoder().FromJson(json, ExtensionsDictionary.StrictMode.OFF, parser);
        await Assert.That(new PlanToProtoConverter().From(converted)).IsEqualTo(original);
        await Assert.That(() => Decoder().FromJson(json, ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(Decoder().FromBytes(original.ToByteArray(), ExtensionsDictionary.StrictMode.OFF)).IsEqualTo(converted);
    }

    [Test]
    public async Task FailuresArePropagatedAndCallerStreamsRemainOpen()
    {
        ProtoToPlanConverter converter = Decoder();
        using MemoryStream malformed = new([255]);
        await Assert.That(() => converter.FromStream(malformed, ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<InvalidProtocolBufferException>();
        await Assert.That(malformed.CanRead).IsTrue();
        await Assert.That(() => converter.FromBytes([255], ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<InvalidProtocolBufferException>();
        await Assert.That(() => converter.FromFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pb"))).ThrowsExactly<FileNotFoundException>();

        ProtoPlan invalid = WirePlan(new RelToProtoConverter().From(Read()));
        invalid.Relations[0].Rel.Read.Common.RelAnchor = 0;
        using MemoryStream validBytesInvalidPlan = new(invalid.ToByteArray());
        await Assert.That(() => converter.FromStream(validBytesInvalidPlan, ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
        await Assert.That(validBytesInvalidPlan.CanRead).IsTrue();

        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pb");
        try
        {
            File.WriteAllBytes(path, invalid.ToByteArray());
            await Assert.That(() => converter.FromFile(path, ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
            using FileStream exclusive = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await Assert.That(exclusive.CanWrite).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }

    }

    [Test]
    public async Task EmptyInputsReportMissingVersionInsteadOfDereferencingNull()
    {
        ProtoToPlanConverter converter = Decoder();
        await Assert.That(() => converter.FromBytes([], ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
        await Assert.That(() => converter.FromJson("{}", ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
        await Assert.That(() => converter.From(new ProtoPlan(), ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
        await Assert.That(() => converter.From(null!, ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => converter.FromBytes(null!, ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => converter.FromStream(null!, ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(converter.FromBytes(WirePlan().ToByteArray(), ExtensionsDictionary.StrictMode.OFF).Relations.Count).IsEqualTo(0);
    }
}
