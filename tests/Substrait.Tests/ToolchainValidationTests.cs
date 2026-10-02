// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Antlr4.Runtime;
using Google.Protobuf;
using Substrait.Antlr.SubstraitType;
using Substrait.Extensions;
using Substrait.Protobuf;

namespace Substrait.Tests;

public sealed class ToolchainValidationTests
{
    [Test]
    public async Task PlanRoundTripsThroughBinaryAndJson()
    {
        Plan expected = new()
        {
            Version = new Substrait.Protobuf.Version
            {
                MajorNumber = 0,
                MinorNumber = 104,
                PatchNumber = 0,
                Producer = "substrait-csharp-tests",
            },
        };

        Plan binaryRoundTrip = Plan.Parser.ParseFrom(expected.ToByteArray());
        Plan jsonRoundTrip = JsonParser.Default.Parse<Plan>(JsonFormatter.Default.Format(expected));

        await Assert.That(binaryRoundTrip).IsEqualTo(expected);
        await Assert.That(jsonRoundTrip).IsEqualTo(expected);
    }

    [Test]
    public async Task TypeGrammarParsesScalarType()
    {
        AntlrInputStream input = new("i32");
        SubstraitTypeLexer lexer = new(input);
        SubstraitTypeParser parser = new(new CommonTokenStream(lexer));

        parser.startRule();

        await Assert.That(parser.NumberOfSyntaxErrors).IsEqualTo(0);
    }

    [Test]
    public async Task StandardExtensionsComeFromPackage()
    {
        string[] expected =
        [
            "functions_aggregate_approx.yaml",
            "functions_aggregate_generic.yaml",
            "functions_arithmetic.yaml",
            "functions_boolean.yaml",
            "functions_comparison.yaml",
            "functions_datetime.yaml",
            "functions_logarithmic.yaml",
            "functions_rounding.yaml",
            "functions_string.yaml",
        ];

        foreach (string name in expected)
        {
            await Assert.That(SubstraitExtensions.ExtensionFiles.Contains(name)).IsTrue();
            await Assert.That(string.IsNullOrWhiteSpace(SubstraitExtensions.ReadExtensionFile(name))).IsFalse();
        }

        await Assert.That(typeof(Plan).Assembly.GetName().Name).IsEqualTo("Substrait.Net.Protobuf");
        await Assert.That(typeof(SubstraitTypeParser).Assembly.GetName().Name).IsEqualTo("Substrait.Net.Antlr");
        await Assert.That(typeof(SubstraitExtensions).Assembly.GetName().Name).IsEqualTo("Substrait.Net.Extensions");
    }

    [Test]
    public async Task SpecificationPackagesMatchCurrentPlanVersion()
    {
        System.Version expected = new(
            (int)Substrait.Core.Plan.Version.Current.MajorNumber,
            (int)Substrait.Core.Plan.Version.Current.MinorNumber,
            (int)Substrait.Core.Plan.Version.Current.PatchNumber,
            0);

        await Assert.That(typeof(Plan).Assembly.GetName().Version).IsEqualTo(expected);
        await Assert.That(typeof(SubstraitTypeParser).Assembly.GetName().Version).IsEqualTo(expected);
        await Assert.That(typeof(SubstraitExtensions).Assembly.GetName().Version).IsEqualTo(expected);
    }
}
