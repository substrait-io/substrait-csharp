// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Extensions;
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
    [Arguments("")]
    [Arguments("description: >\n  Example file-level\n  description.\n")]
    public async Task LoadUsesDeclaredUrnWhenNamespaceIsEmpty(string description)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(
            "urn: extension:example:test\n" + description +
            "scalar_functions:\n  - name: identity\n    impls:\n      - args:\n          - value: i64\n        return: i64\n"));

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
        const string yaml = """
            urn: extension:example:deprecated
            scalar_functions:
              - name: identity
                impls:
                  - args:
                      - value: i64
                    deprecated:
                      since: "0.100.0"
                      reason: "Use the replacement overload."
                    return: i64
            aggregate_functions:
              - name: sum
                impls:
                  - args:
                      - value: i64
                    deprecated:
                      since: "0.88.0"
                      reason: "Use the replacement aggregate."
                    return: i64
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(yaml));
        ExtensionsCollection extensions = ExtensionUtils.Load(string.Empty, stream);
        ScalarFunctionImpl scalar = extensions.ScalarFunctionImpls.Single();
        await Assert.That(scalar.Deprecated).IsNotNull();
        await Assert.That(scalar.Deprecated!.Since).IsEqualTo("0.100.0");
        await Assert.That(scalar.Deprecated.Reason).IsEqualTo("Use the replacement overload.");
        AggregateFunctionImpl aggregate = extensions.AggregateFunctionImpls.Single();
        await Assert.That(aggregate.Deprecated).IsNotNull();
        await Assert.That(aggregate.Deprecated!.Since).IsEqualTo("0.88.0");
        await Assert.That(aggregate.Deprecated.Reason).IsEqualTo("Use the replacement aggregate.");
        WindowFunctionImpl window = extensions.WindowFunctionImpls.Single();
        await Assert.That(window.Anchor).IsEqualTo(aggregate.Anchor);
        await Assert.That(window.Deprecated).IsSameReferenceAs(aggregate.Deprecated);
    }

    [Test]
    public async Task PackagedDatetimeFunctionsUseUpdatedSignatures()
    {
        const string urn = "extension:io.substrait:functions_datetime";
        ExtensionsCollection extensions = ExtensionUtils.LoadDefaults();
        ScalarFunctionImpl subtract = extensions.ScalarFunctionImpls.Single(function =>
            function.Uri == urn && function.Name == "subtract" &&
            function.Args[0] is ValueArgument { Value: "precision_timestamp_tz<P>" } &&
            function.Args[1] is ValueArgument { Value: "interval_year" });
        await Assert.That(subtract.Args.Count).IsEqualTo(3);
        await Assert.That(subtract.Args[2].Name).IsEqualTo("timezone");
        await Assert.That(((ValueArgument)subtract.Args[2]).Value).IsEqualTo("string");

        foreach (string type in new[] { "date", "precision_time<P>" })
        {
            ScalarFunctionImpl round = extensions.ScalarFunctionImpls.Single(function =>
                function.Uri == urn && function.Name == "round_calendar" &&
                function.Args[0] is ValueArgument argument && argument.Value == type);
            await Assert.That(round.Args.Count).IsEqualTo(5);
            await Assert.That(round.Args.Count(argument => argument.Name == "origin")).IsEqualTo(1);
            await Assert.That(round.Args[3] is EnumArgument { Name: "origin" }).IsTrue();
            await Assert.That(round.Args[4] is ValueArgument { Name: "multiple", Value: "i64" }).IsTrue();
        }

        await Assert.That(extensions.TryGetScalarFunction(
            new FunctionImplAnchor(urn, "round_calendar:date_req_req_req_i64"),
            ExtensionsDictionary.StrictMode.STRICT, out _)).IsTrue();
        await Assert.That(extensions.TryGetScalarFunction(
            new FunctionImplAnchor(urn, "round_calendar:date_req_req_req_i64_date"),
            ExtensionsDictionary.StrictMode.OFF, out _)).IsFalse();
    }

    [Test]
    public async Task PackagedDecimalAverageHasNullableReturn()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(
            SubstraitExtensions.ReadExtensionFile("functions_arithmetic_decimal.yaml")));
        ExtensionsCollection extensions = ExtensionUtils.Load(string.Empty, stream);
        AggregateFunctionImpl average = extensions.AggregateFunctionImpls.Single(function => function.Name == "avg");
        await Assert.That(average.Return).IsEqualTo("DECIMAL?<38,S>");
        await Assert.That(average.Nullability).IsEqualTo(FunctionImpl.NullabilityMode.Declared_output);
        WindowFunctionImpl window = extensions.WindowFunctionImpls.Single(function => function.Name == "avg");
        await Assert.That(window.Return).IsEqualTo(average.Return);
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
