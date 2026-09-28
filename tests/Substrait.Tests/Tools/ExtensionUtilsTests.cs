// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Tools;

namespace Substrait.Tests.Tools;

[TestClass]
public sealed class ExtensionUtilsTests
{
    [TestMethod]
    public void LoadDefaultsLoadsPackagedExtensionsWithDeclaredUrns()
    {
        ExtensionsCollection extensions = ExtensionUtils.LoadDefaults();

        Assert.IsTrue(extensions.ScalarFunctionImpls.Count > 0);
        Assert.IsTrue(extensions.AggregateFunctionImpls.Count > 0);
        Assert.IsTrue(extensions.WindowFunctionImpls.Count > 0);
        Assert.AreEqual(0, extensions.TypeVariationImpls.Count);
        Assert.IsTrue(extensions.TryGetScalarFunction(
            new FunctionImplAnchor("extension:io.substrait:functions_arithmetic", "add:i64_i64"),
            ExtensionsDictionary.StrictMode.STRICT,
            out ScalarFunctionImpl? function));
        Assert.IsNotNull(function);
        Assert.AreSame(extensions, ExtensionUtils.LoadDefaults());
    }

    [TestMethod]
    public void LoadUsesDeclaredUrnWhenNamespaceIsEmpty()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(
            "urn: extension:example:test\nscalar_functions:\n  - name: identity\n    impls:\n      - args:\n          - value: i64\n        return: i64\n"));

        ExtensionsCollection extensions = ExtensionUtils.Load(string.Empty, stream);

        Assert.AreEqual("extension:example:test", extensions.ScalarFunctionImpls[0].Uri);
    }

    [TestMethod]
    public void LoadRejectsMissingNamespaceAndUrn()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("scalar_functions: []\n"));

        Assert.ThrowsException<ArgumentException>(() => ExtensionUtils.Load(string.Empty, stream));
    }

    [TestMethod]
    public void LoadPreservesImplementationDeprecation()
    {
        ExtensionsCollection extensions = ExtensionUtils.LoadDefaults();
        AggregateFunctionImpl aggregate = extensions.AggregateFunctionImpls.First(function => function.Deprecated is not null);
        Assert.AreEqual("0.88.0", aggregate.Deprecated!.Since);
        Assert.IsFalse(string.IsNullOrEmpty(aggregate.Deprecated.Reason));
        WindowFunctionImpl window = extensions.WindowFunctionImpls
            .Where(function => function.Name == aggregate.Name && function.Uri == aggregate.Uri)
            .Single(function => function.Anchor.Equals(aggregate.Anchor));
        Assert.AreEqual(aggregate.Deprecated, window.Deprecated);

        ScalarFunctionImpl scalar = extensions.ScalarFunctionImpls.First(function => function.Deprecated is not null);
        Assert.AreEqual("0.100.0", scalar.Deprecated!.Since);
    }

    [TestMethod]
    public void FileSystemResolverLoadsExtensionFile()
    {
        const string yaml = "scalar_functions:\n  - name: identity\n    impls:\n      - args:\n          - value: i64\n            name: value\n        return: i64\n";
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, yaml, Encoding.UTF8);
            ExtensionsCollection extensions = ExtensionUtils.Load(
                [new ExtensionUtils.ExtensionFile("/test.yaml", path)],
                new ExtensionUtils.FileSystemResolver());

            Assert.AreEqual(1, extensions.ScalarFunctionImpls.Count);
            Assert.AreEqual("identity:i64", extensions.ScalarFunctionImpls[0].Key);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
