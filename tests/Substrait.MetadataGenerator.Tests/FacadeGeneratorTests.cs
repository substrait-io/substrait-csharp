// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf.WellKnownTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.MetadataGenerator;
using Substrait.Protobuf;

namespace Substrait.MetadataGenerator.Tests;

[TestClass]
public sealed class FacadeGeneratorTests
{
    [TestMethod]
    public void GenerationIsDeterministicAndRestrictedToMetadataClosure()
    {
        var first = FacadeGenerator.Generate([RelCommon.Descriptor, AdvancedExtension.Descriptor]);
        var second = FacadeGenerator.Generate([AdvancedExtension.Descriptor, RelCommon.Descriptor, RelCommon.Descriptor]);
        string[] expected =
        [
            "ReadOnlyAdvancedExtension.g.cs",
            "ReadOnlyAny.g.cs",
            "ReadOnlyRelCommon.g.cs",
            "ReadOnlyRelCommonDirect.g.cs",
            "ReadOnlyRelCommonEmit.g.cs",
            "ReadOnlyRelCommonHint.g.cs",
            "ReadOnlyRelCommonHintLoadedComputation.g.cs",
            "ReadOnlyRelCommonHintRuntimeConstraint.g.cs",
            "ReadOnlyRelCommonHintSavedComputation.g.cs",
            "ReadOnlyRelCommonHintStats.g.cs",
        ];

        CollectionAssert.AreEquivalent(expected, first.Keys.ToArray());
        CollectionAssert.AreEquivalent(first.Keys.ToArray(), second.Keys.ToArray());
        foreach (var (name, source) in first)
        {
            Assert.AreEqual(source, second[name]);
            Assert.IsFalse(source.Contains('\r'));
            StringAssert.EndsWith(source, "\n");
        }
    }

    [TestMethod]
    public void MapsAreRejectedRatherThanSilentlyOmitted()
    {
        NotSupportedException error = Assert.ThrowsException<NotSupportedException>(
            () => FacadeGenerator.Generate([Struct.Descriptor]));
        StringAssert.Contains(error.Message, "google.protobuf.Struct.fields");
    }

    [TestMethod]
    public void GeneratedMembersPreserveOptionalAndOneofPresence()
    {
        var files = FacadeGenerator.Generate([RelCommon.Descriptor]);
        StringAssert.Contains(files["ReadOnlyRelCommon.g.cs"], "uint? RelAnchor => this.value.HasRelAnchor");
        StringAssert.Contains(files["ReadOnlyRelCommon.g.cs"], "ReadOnlyRelCommonDirect? Direct");
        StringAssert.Contains(files["ReadOnlyRelCommon.g.cs"], "EmitKindCase => this.value.EmitKindCase");
        Assert.IsFalse(files["ReadOnlyRelCommon.g.cs"].Contains("RelAnchorCase", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CheckDetectsMissingStaleAndUnexpectedFilesWithoutWriting()
    {
        string directory = Path.Combine(Path.GetTempPath(), nameof(FacadeGeneratorTests), Guid.NewGuid().ToString("N"));
        var files = FacadeGenerator.Generate([RelCommon.Descriptor, AdvancedExtension.Descriptor]);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        try
        {
            Assert.AreEqual(1, GeneratedFiles.Synchronize(files, directory, false, output, errors));
            Assert.IsFalse(Directory.Exists(directory));
            Assert.AreEqual(0, GeneratedFiles.Synchronize(files, directory, true, output, errors));
            Assert.AreEqual(0, GeneratedFiles.Synchronize(files, directory, false, output, errors));

            string stalePath = Path.Combine(directory, "ReadOnlyRelCommon.g.cs");
            File.WriteAllText(stalePath, "stale");
            Assert.AreEqual(1, GeneratedFiles.Synchronize(files, directory, false, output, errors));
            Assert.AreEqual("stale", File.ReadAllText(stalePath));
            Assert.AreEqual(0, GeneratedFiles.Synchronize(files, directory, true, output, errors));

            string unexpectedPath = Path.Combine(directory, "Unexpected.g.cs");
            File.WriteAllText(unexpectedPath, "keep");
            Assert.AreEqual(1, GeneratedFiles.Synchronize(files, directory, false, output, errors));
            Assert.AreEqual(1, GeneratedFiles.Synchronize(files, directory, true, output, errors));
            Assert.AreEqual("keep", File.ReadAllText(unexpectedPath));
            File.Delete(unexpectedPath);

            string nestedDirectory = Path.Combine(directory, "nested");
            Directory.CreateDirectory(nestedDirectory);
            string nestedPath = Path.Combine(nestedDirectory, "ReadOnlyRelCommon.g.cs");
            File.WriteAllText(nestedPath, "keep");
            Assert.AreEqual(1, GeneratedFiles.Synchronize(files, directory, false, output, errors));
            Assert.AreEqual(1, GeneratedFiles.Synchronize(files, directory, true, output, errors));
            Assert.AreEqual("keep", File.ReadAllText(nestedPath));
            File.Delete(nestedPath);
            Directory.Delete(nestedDirectory);

            File.Delete(stalePath);
            Assert.AreEqual(1, GeneratedFiles.Synchronize(files, directory, false, output, errors));
            Assert.IsFalse(File.Exists(stalePath));
            Assert.AreEqual(0, GeneratedFiles.Synchronize(files, directory, true, output, errors));
            Assert.AreEqual(0, GeneratedFiles.Synchronize(files, directory, false, output, errors));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
