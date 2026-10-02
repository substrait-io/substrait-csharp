// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Google.Protobuf.Collections;
using Substrait.Core.Extension;
using Substrait.Protobuf;
using Substrait.Tools;

namespace Substrait.Tests.Tools;

public sealed class SerializationSupportTests
{
    private static readonly string[] ExpectedExtensionUris = ["functions.yaml", "types.yaml"];
    private static readonly int[] ExpectedSingleRepeatedValue = [1];
    private static readonly int[] ExpectedRepeatedValues = [1, 2, 3];

    [Test]
    public async Task FileUtilsPreservesBinaryAndJsonPlanSemantics()
    {
        Plan expected = new()
        {
            Version = new Substrait.Protobuf.Version
            {
                MajorNumber = 0,
                MinorNumber = 73,
                PatchNumber = 0,
                Producer = "serialization-support-tests",
            },
        };
        string directory = Path.Combine(Path.GetTempPath(), nameof(SerializationSupportTests), Guid.NewGuid().ToString("N"));
        string binaryPath = Path.Combine(directory, "plan.pb");
        string jsonPath = Path.Combine(directory, "plan.json");

        try
        {
            FileUtils.WritePlan(expected, binaryPath, FileUtils.FileType.Protobuf);
            FileUtils.WritePlan(expected, jsonPath, FileUtils.FileType.Json);

            await Assert.That(File.ReadAllBytes(binaryPath)).IsEquivalentTo(expected.ToByteArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(FileUtils.FetchPlan(binaryPath, FileUtils.FileType.Protobuf)).IsEqualTo(expected);
            await Assert.That(FileUtils.FetchPlan(jsonPath, FileUtils.FileType.Json)).IsEqualTo(expected);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task ExtensionsCollectorAssignsStableAnchors()
    {
        ExtensionsCollector.Builder builder = new();

        await Assert.That(builder.Collect(ExtensionsCollector.ExtensionType.Function, "functions.yaml", "add")).IsEqualTo(0);
        await Assert.That(builder.Collect(ExtensionsCollector.ExtensionType.Function, "functions.yaml", "add")).IsEqualTo(0);
        await Assert.That(builder.Collect(ExtensionsCollector.ExtensionType.Function, "functions.yaml", "subtract")).IsEqualTo(1);
        await Assert.That(builder.Collect(ExtensionsCollector.ExtensionType.TypeVariation, "types.yaml", "unsigned")).IsEqualTo(1);

        ExtensionsCollector collector = builder.Build();
        await Assert.That(collector.ExtensionUris.ToArray()).IsEquivalentTo(ExpectedExtensionUris, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(collector.Extensions.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AllocateAndAddRangePreservesExistingValues()
    {
        RepeatedField<int> values = [1];

        values.AllocateAndAddRange(2, [2, 3]);

        await Assert.That(values.ToArray()).IsEquivalentTo(ExpectedRepeatedValues, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(values.Capacity >= 3).IsTrue();
    }

    [Test]
    public async Task AllocateAndAddRangeRejectsNegativeCount()
    {
        RepeatedField<int> values = [1];

        await Assert.That(() => values.AllocateAndAddRange(-1, [])).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(values.ToArray()).IsEquivalentTo(ExpectedSingleRepeatedValue, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task UniqueListUnsupportedMutationsThrowNotSupportedException()
    {
        UniqueList<int> values = [1];

        await Assert.That(() => ((IList<int>)values)[0] = 2).ThrowsExactly<NotSupportedException>();
        await Assert.That(() => ((IList<int>)values).Insert(0, 2)).ThrowsExactly<NotSupportedException>();
        await Assert.That(() => ((IList<int>)values).RemoveAt(0)).ThrowsExactly<NotSupportedException>();
        await Assert.That(() => ((IList<int>)values).Remove(1)).ThrowsExactly<NotSupportedException>();
    }
}
