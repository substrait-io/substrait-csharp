// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Tools;
using YamlDotNet.Core;

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
    public async Task LoadPreservesNestedCollectionsPolymorphicArgumentsAndAliases()
    {
        const string yaml = """
            urn: extension:example:test
            scalar_functions:
              - name: choose
                impls:
                  - args:
                      - value: i64
                        name: input
                        constant: true
                      - options: [FIRST, LAST]
                        name: direction
                    options:
                      overflow: &overflow
                        values: [ERROR, WRAP]
                        description: Overflow behavior
                      fallback: *overflow
                    variadic:
                      min: 1
                      max: 3
                    return: i64
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(yaml));

        ExtensionsCollection extensions = ExtensionUtils.Load(string.Empty, stream);

        await Assert.That(extensions.ScalarFunctionImpls.Count).IsEqualTo(1);
        ScalarFunctionImpl function = extensions.ScalarFunctionImpls[0];
        await Assert.That(function.Key).IsEqualTo("choose:i64_req");
        await Assert.That(function.Args.Count).IsEqualTo(2);
        var value = (ValueArgument)function.Args[0];
        await Assert.That(value.Name).IsEqualTo("input");
        await Assert.That(value.Value).IsEqualTo("i64");
        await Assert.That(value.Constant).IsTrue();
        var direction = (EnumArgument)function.Args[1];
        await Assert.That(direction.Name).IsEqualTo("direction");
        await Assert.That(direction.Options).IsEquivalentTo(["FIRST", "LAST"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(function.Options.Count).IsEqualTo(2);
        foreach (IOption option in function.Options.Values)
        {
            await Assert.That(option.Values).IsEquivalentTo(["ERROR", "WRAP"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(option.Description).IsEqualTo("Overflow behavior");
        }

        IVariadicBehavior variadic = await Assert.That(function.Variadic).IsNotNull();
        await Assert.That(variadic.Min).IsEqualTo(1);
        await Assert.That(variadic.Max).IsEqualTo(3);
        await Assert.That(function.Return).IsEqualTo("i64");
    }

    [Test]
    [Arguments("unknown_field: true")]
    [Arguments("scalar_functions: invalid")]
    [Arguments("scalar_functions: [")]
    public async Task LoadRejectsInvalidYaml(string definition)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("urn: extension:example:test\n" + definition));

        await Assert.That(() => ExtensionUtils.Load(string.Empty, stream)).Throws<YamlException>();
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
