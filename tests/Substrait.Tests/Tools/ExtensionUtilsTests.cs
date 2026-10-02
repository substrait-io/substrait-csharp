// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Tools;

namespace Substrait.Tests.Tools;

public sealed class ExtensionUtilsTests
{
    [Test]
    public async Task LoadDefaultsLoadsPackagedExtensionsWithDeclaredUrns()
    {
        ExtensionsCollection extensions = ExtensionUtils.LoadDefaults();

        await Assert.That(extensions.ScalarFunctionImpls.Count > 0).IsTrue();
        await Assert.That(extensions.AggregateFunctionImpls.Count > 0).IsTrue();
        await Assert.That(extensions.WindowFunctionImpls.Count > 0).IsTrue();
        await Assert.That(extensions.TypeVariationImpls.Count).IsEqualTo(0);
        await Assert.That(extensions.TryGetScalarFunction(
            new FunctionImplAnchor("extension:io.substrait:functions_arithmetic", "add:i64_i64"),
            ExtensionsDictionary.StrictMode.STRICT,
            out ScalarFunctionImpl? function)).IsTrue();
        await Assert.That(function).IsNotNull();
        await Assert.That(ExtensionUtils.LoadDefaults()).IsSameReferenceAs(extensions);
    }

    [Test]
    public async Task LoadUsesDeclaredUrnWhenNamespaceIsEmpty()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(
            "urn: extension:example:test\nscalar_functions:\n  - name: identity\n    impls:\n      - args:\n          - value: i64\n        return: i64\n"));

        ExtensionsCollection extensions = ExtensionUtils.Load(string.Empty, stream);

        await Assert.That(extensions.ScalarFunctionImpls[0].Uri).IsEqualTo("extension:example:test");
    }

    [Test]
    public async Task LoadRejectsMissingNamespaceAndUrn()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("scalar_functions: []\n"));

        await Assert.That(() => ExtensionUtils.Load(string.Empty, stream)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task LoadPreservesImplementationDeprecation()
    {
        ExtensionsCollection extensions = ExtensionUtils.LoadDefaults();
        AggregateFunctionImpl aggregate = extensions.AggregateFunctionImpls.First(function => function.Deprecated is not null);
        await Assert.That(aggregate.Deprecated!.Since).IsEqualTo("0.88.0");
        await Assert.That(string.IsNullOrEmpty(aggregate.Deprecated.Reason)).IsFalse();
        WindowFunctionImpl window = extensions.WindowFunctionImpls
            .Where(function => function.Name == aggregate.Name && function.Uri == aggregate.Uri)
            .Single(function => function.Anchor.Equals(aggregate.Anchor));
        await Assert.That(window.Deprecated).IsEqualTo(aggregate.Deprecated);

        ScalarFunctionImpl scalar = extensions.ScalarFunctionImpls.First(function => function.Deprecated is not null);
        await Assert.That(scalar.Deprecated!.Since).IsEqualTo("0.100.0");
    }

    [Test]
    public async Task FileSystemResolverLoadsExtensionFile()
    {
        const string yaml = "scalar_functions:\n  - name: identity\n    impls:\n      - args:\n          - value: i64\n            name: value\n        return: i64\n";
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, yaml, Encoding.UTF8);
            ExtensionsCollection extensions = ExtensionUtils.Load(
                [new ExtensionUtils.ExtensionFile("/test.yaml", path)],
                new ExtensionUtils.FileSystemResolver());

            await Assert.That(extensions.ScalarFunctionImpls.Count).IsEqualTo(1);
            await Assert.That(extensions.ScalarFunctionImpls[0].Key).IsEqualTo("identity:i64");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
