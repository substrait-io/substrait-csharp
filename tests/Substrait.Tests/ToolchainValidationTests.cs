// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Antlr4.Runtime;
using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Antlr.SubstraitType;
using Substrait.Extensions;
using Substrait.Protobuf;

namespace Substrait.Tests;

[TestClass]
public sealed class ToolchainValidationTests
{
    [TestMethod]
    public void PlanRoundTripsThroughBinaryAndJson()
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

        Assert.AreEqual(expected, binaryRoundTrip);
        Assert.AreEqual(expected, jsonRoundTrip);
    }

    [TestMethod]
    public void TypeGrammarParsesScalarType()
    {
        AntlrInputStream input = new("i32");
        SubstraitTypeLexer lexer = new(input);
        SubstraitTypeParser parser = new(new CommonTokenStream(lexer));

        parser.startRule();

        Assert.AreEqual(0, parser.NumberOfSyntaxErrors);
    }

    [TestMethod]
    public void StandardExtensionsComeFromPackage()
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
            Assert.IsTrue(SubstraitExtensions.ExtensionFiles.Contains(name));
            Assert.IsFalse(string.IsNullOrWhiteSpace(SubstraitExtensions.ReadExtensionFile(name)));
        }

        Assert.AreEqual("Substrait.Net.Protobuf", typeof(Plan).Assembly.GetName().Name);
        Assert.AreEqual("Substrait.Net.Antlr", typeof(SubstraitTypeParser).Assembly.GetName().Name);
        Assert.AreEqual("Substrait.Net.Extensions", typeof(SubstraitExtensions).Assembly.GetName().Name);
    }

    [TestMethod]
    public void SpecificationPackagesMatchCurrentPlanVersion()
    {
        System.Version expected = new(
            (int)Substrait.Core.Plan.Version.Current.MajorNumber,
            (int)Substrait.Core.Plan.Version.Current.MinorNumber,
            (int)Substrait.Core.Plan.Version.Current.PatchNumber,
            0);

        Assert.AreEqual(expected, typeof(Plan).Assembly.GetName().Version);
        Assert.AreEqual(expected, typeof(SubstraitTypeParser).Assembly.GetName().Version);
        Assert.AreEqual(expected, typeof(SubstraitExtensions).Assembly.GetName().Version);
    }
}
