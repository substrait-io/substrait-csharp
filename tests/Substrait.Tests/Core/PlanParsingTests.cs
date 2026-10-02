// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation.Converters;
using Substrait.Protobuf;
using static Substrait.Tests.Core.RelationMetadataConversionTests;
using ProtoPlan = Substrait.Protobuf.Plan;

namespace Substrait.Tests.Core;

[TestClass]
public sealed class PlanParsingTests
{
    [TestMethod]
    public void BytesStreamsAndFilesPreserveOpaqueMetadata()
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
        Assert.IsTrue(stream.CanRead);
        Assert.AreEqual(stream.Length, stream.Position);
        Assert.AreEqual(fromBytes, fromStream);
        Array.Clear(bytes, 0, bytes.Length);
        Assert.AreEqual(original, new PlanToProtoConverter().From(fromBytes));

        string path = Path.Combine(Path.GetTempPath(), nameof(PlanParsingTests) + "-" + Guid.NewGuid().ToString("N") + ".pb");
        try
        {
            File.WriteAllBytes(path, original.ToByteArray());
            Assert.AreEqual(fromBytes, converter.FromFile(path, ExtensionsDictionary.StrictMode.OFF));
            using FileStream exclusive = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.IsTrue(exclusive.CanWrite);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void PrivateConversionOwnsMetadataWhilePublicProtoConversionCopies()
    {
        ProtoPlan proto = WirePlan(new RelToProtoConverter().From(Read()));
        ProtoToPlanConverter converter = Decoder();
        MethodInfo ownedConversion = typeof(ProtoToPlanConverter).GetMethod("FromCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IPlan owned = (IPlan)ownedConversion.Invoke(converter, [proto, ExtensionsDictionary.StrictMode.OFF, true])!;
        IPlan copied = converter.From(proto, ExtensionsDictionary.StrictMode.OFF);
        FieldInfo backing = typeof(ReadOnlyRelCommon).GetField("value", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.AreSame(proto.Relations[0].Rel.Read.Common, backing.GetValue(owned.Relations[0].Input.Metadata.Common));
        Assert.AreNotSame(proto.Relations[0].Rel.Read.Common, backing.GetValue(copied.Relations[0].Input.Metadata.Common));
        Assert.AreNotSame(proto.Relations[0].Rel.Read.Common, new PlanToProtoConverter().From(owned).Relations[0].Rel.Read.Common);
    }

    [TestMethod]
    public void JsonAcceptsExplicitPayloadRegistryAndDoesNotChangeBinaryPolicy()
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
        Assert.AreEqual(original, new PlanToProtoConverter().From(converted));
        Assert.ThrowsException<InvalidOperationException>(() => Decoder().FromJson(json, ExtensionsDictionary.StrictMode.OFF));
        Assert.AreEqual(converted, Decoder().FromBytes(original.ToByteArray(), ExtensionsDictionary.StrictMode.OFF));
    }

    [TestMethod]
    public void FailuresArePropagatedAndCallerStreamsRemainOpen()
    {
        ProtoToPlanConverter converter = Decoder();
        using MemoryStream malformed = new([255]);
        Assert.ThrowsException<InvalidProtocolBufferException>(() => converter.FromStream(malformed, ExtensionsDictionary.StrictMode.OFF));
        Assert.IsTrue(malformed.CanRead);
        Assert.ThrowsException<InvalidProtocolBufferException>(() => converter.FromBytes([255], ExtensionsDictionary.StrictMode.OFF));
        Assert.ThrowsException<FileNotFoundException>(() => converter.FromFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pb")));

        ProtoPlan invalid = WirePlan(new RelToProtoConverter().From(Read()));
        invalid.Relations[0].Rel.Read.Common.RelAnchor = 0;
        using MemoryStream validBytesInvalidPlan = new(invalid.ToByteArray());
        Assert.ThrowsException<SerializationException>(() => converter.FromStream(validBytesInvalidPlan, ExtensionsDictionary.StrictMode.OFF));
        Assert.IsTrue(validBytesInvalidPlan.CanRead);

        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pb");
        try
        {
            File.WriteAllBytes(path, invalid.ToByteArray());
            Assert.ThrowsException<SerializationException>(() => converter.FromFile(path, ExtensionsDictionary.StrictMode.OFF));
            using FileStream exclusive = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.IsTrue(exclusive.CanWrite);
        }
        finally
        {
            File.Delete(path);
        }

    }

    [TestMethod]
    public void EmptyInputsReportMissingVersionInsteadOfDereferencingNull()
    {
        ProtoToPlanConverter converter = Decoder();
        Assert.ThrowsException<SerializationException>(() => converter.FromBytes([], ExtensionsDictionary.StrictMode.OFF));
        Assert.ThrowsException<SerializationException>(() => converter.FromJson("{}", ExtensionsDictionary.StrictMode.OFF));
        Assert.ThrowsException<SerializationException>(() => converter.From(new ProtoPlan(), ExtensionsDictionary.StrictMode.OFF));
        Assert.ThrowsException<ArgumentNullException>(() => converter.From(null!, ExtensionsDictionary.StrictMode.OFF));
        Assert.ThrowsException<ArgumentNullException>(() => converter.FromBytes(null!, ExtensionsDictionary.StrictMode.OFF));
        Assert.ThrowsException<ArgumentNullException>(() => converter.FromStream(null!, ExtensionsDictionary.StrictMode.OFF));
        Assert.AreEqual(0, converter.FromBytes(WirePlan().ToByteArray(), ExtensionsDictionary.StrictMode.OFF).Relations.Count);
    }
}
