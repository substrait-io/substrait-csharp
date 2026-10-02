// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf.WellKnownTypes;
using Substrait.MetadataGenerator;
using Substrait.Protobuf;

namespace Substrait.MetadataGenerator.Tests;

public sealed class FacadeGeneratorTests
{
    [Test]
    public async Task GenerationIsDeterministicAndRestrictedToMetadataClosure()
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

        await Assert.That(first.Keys.ToArray()).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Any);
        await Assert.That(second.Keys.ToArray()).IsEquivalentTo(first.Keys.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Any);
        foreach (var (name, source) in first)
        {
            await Assert.That(second[name]).IsEqualTo(source);
            await Assert.That(source.Contains('\r')).IsFalse();
            await Assert.That(source).EndsWith("\n");
        }
    }

    [Test]
    public async Task MapsAreRejectedRatherThanSilentlyOmitted()
    {
        NotSupportedException error = await Assert.That(() => FacadeGenerator.Generate([Struct.Descriptor])).ThrowsExactly<NotSupportedException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("google.protobuf.Struct.fields");
    }

    [Test]
    public async Task GeneratedMembersPreserveOptionalAndOneofPresence()
    {
        var files = FacadeGenerator.Generate([RelCommon.Descriptor]);
        string common = files["ReadOnlyRelCommon.g.cs"];
        await Assert.That(common).Contains("uint? RelAnchor => this.value.HasRelAnchor");
        await Assert.That(common).Contains("ReadOnlyRelCommonDirect? Direct");
        await Assert.That(common).Contains("EmitKindCase => this.value.EmitKindCase");
        await Assert.That(common.Contains("RelAnchorCase", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task CheckDetectsMissingStaleAndUnexpectedFilesWithoutWriting()
    {
        string directory = Path.Combine(Path.GetTempPath(), nameof(FacadeGeneratorTests), Guid.NewGuid().ToString("N"));
        var files = FacadeGenerator.Generate([RelCommon.Descriptor, AdvancedExtension.Descriptor]);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        try
        {
            await Assert.That(GeneratedFiles.Synchronize(files, directory, false, output, errors)).IsEqualTo(1);
            await Assert.That(Directory.Exists(directory)).IsFalse();
            await Assert.That(GeneratedFiles.Synchronize(files, directory, true, output, errors)).IsEqualTo(0);
            await Assert.That(GeneratedFiles.Synchronize(files, directory, false, output, errors)).IsEqualTo(0);

            string stalePath = Path.Combine(directory, "ReadOnlyRelCommon.g.cs");
            File.WriteAllText(stalePath, "stale");
            await Assert.That(GeneratedFiles.Synchronize(files, directory, false, output, errors)).IsEqualTo(1);
            await Assert.That(File.ReadAllText(stalePath)).IsEqualTo("stale");
            await Assert.That(GeneratedFiles.Synchronize(files, directory, true, output, errors)).IsEqualTo(0);

            string unexpectedPath = Path.Combine(directory, "Unexpected.g.cs");
            File.WriteAllText(unexpectedPath, "keep");
            await Assert.That(GeneratedFiles.Synchronize(files, directory, false, output, errors)).IsEqualTo(1);
            await Assert.That(GeneratedFiles.Synchronize(files, directory, true, output, errors)).IsEqualTo(1);
            await Assert.That(File.ReadAllText(unexpectedPath)).IsEqualTo("keep");
            File.Delete(unexpectedPath);

            string nestedDirectory = Path.Combine(directory, "nested");
            Directory.CreateDirectory(nestedDirectory);
            string nestedPath = Path.Combine(nestedDirectory, "ReadOnlyRelCommon.g.cs");
            File.WriteAllText(nestedPath, "keep");
            await Assert.That(GeneratedFiles.Synchronize(files, directory, false, output, errors)).IsEqualTo(1);
            await Assert.That(GeneratedFiles.Synchronize(files, directory, true, output, errors)).IsEqualTo(1);
            await Assert.That(File.ReadAllText(nestedPath)).IsEqualTo("keep");
            File.Delete(nestedPath);
            Directory.Delete(nestedDirectory);

            File.Delete(stalePath);
            await Assert.That(GeneratedFiles.Synchronize(files, directory, false, output, errors)).IsEqualTo(1);
            await Assert.That(File.Exists(stalePath)).IsFalse();
            await Assert.That(GeneratedFiles.Synchronize(files, directory, true, output, errors)).IsEqualTo(0);
            await Assert.That(GeneratedFiles.Synchronize(files, directory, false, output, errors)).IsEqualTo(0);
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
