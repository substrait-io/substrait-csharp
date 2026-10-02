// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Expression;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using Substrait.Protobuf;
using Substrait.Tools;
using ProtoPlan = Substrait.Protobuf.Plan;
using ProtoRel = Substrait.Protobuf.Rel;
using ProtoVersion = Substrait.Protobuf.Version;

namespace Substrait.Tests.Core;

[TestClass]
public sealed class ProtoToPlanConverterTests
{
    private static readonly string[] ExpectedRootNames = ["output"];
    private readonly ProtoToPlanConverter converter = new(new ExtensionsCollection());

    [TestMethod]
    public void ConvertsRootNamesAndVersion()
    {
        ProtoPlan plan = CreatePlan();

        IPlan result = this.converter.From(plan, ExtensionsDictionary.StrictMode.OFF);

        Assert.AreEqual(1, result.Roots.Count);
        Assert.IsInstanceOfType<NamedTableRead>(result.Roots[0].Input);
        CollectionAssert.AreEqual(ExpectedRootNames, result.Roots[0].Names.ToArray());
        Assert.AreEqual(1U, result.Version.MajorNumber);
        Assert.AreEqual(2U, result.Version.MinorNumber);
        Assert.AreEqual(3U, result.Version.PatchNumber);
        Assert.AreEqual("abc", result.Version.GitHash);
        Assert.AreEqual("tests", result.Version.Producer);
    }

    [TestMethod]
    public void ConvertsMultiplePlanRoots()
    {
        ProtoPlan plan = CreatePlan();
        plan.Relations.Add(CreatePlan().Relations[0]);

        IPlan result = this.converter.From(plan, ExtensionsDictionary.StrictMode.OFF);

        Assert.AreEqual(2, result.Relations.Count);
        Assert.AreEqual(2, result.Roots.Count);
        Assert.AreEqual(result.Roots[0], result.Roots[1]);
        Assert.AreEqual(
            result,
            this.converter.From(new PlanToProtoConverter().From(result), ExtensionsDictionary.StrictMode.OFF));
    }

    [TestMethod]
    public void ConvertsNonRootPlanRelation()
    {
        ProtoPlan plan = CreatePlan();
        plan.Relations[0] = new PlanRel { Rel = CreateRead() };

        IPlan result = this.converter.From(plan, ExtensionsDictionary.StrictMode.OFF);

        Assert.AreEqual(1, result.Relations.Count);
        Assert.AreEqual(0, result.Roots.Count);
        Assert.IsInstanceOfType<NamedTableRead>(result.Relations[0].Input);
        Assert.AreEqual(
            result,
            this.converter.From(new PlanToProtoConverter().From(result), ExtensionsDictionary.StrictMode.OFF));
    }

    [TestMethod]
    public void RoundTripsPlanSemantics()
    {
        IPlan original = this.converter.From(CreatePlan(), ExtensionsDictionary.StrictMode.OFF);

        ProtoPlan serialized = new PlanToProtoConverter().From(original);
        IPlan roundTripped = this.converter.From(serialized, ExtensionsDictionary.StrictMode.OFF);

        Assert.AreEqual(original, roundTripped);
    }

    [TestMethod]
    public void RoundTripsStandardFunctionUrns()
    {
        const string urn = "extension:io.substrait:functions_arithmetic";
        var read = new NamedTableRead(
            new Substrait.Core.Type.NamedStruct(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])),
            ["orders"],
            null);
        var function = new Substrait.Core.Expression.Expression.ScalarFunctionInvocation(
            urn, "add:i64_i64", [new Literal.I64Literal(1), new Literal.I64Literal(2)], TypeFactory.REQUIRED.I64, null);
        IPlan plan = new Substrait.Core.Plan.Plan(
            [new Substrait.Core.Plan.Plan.Root(new Project(read, [function]), ["value", "sum"])],
            Substrait.Core.Plan.Version.Current);

        ProtoPlan serialized = new PlanToProtoConverter().From(plan);
        Assert.AreEqual(urn, serialized.ExtensionUrns.Single().Urn);
        Assert.AreEqual(serialized.ExtensionUrns[0].ExtensionUrnAnchor, serialized.Extensions[0].ExtensionFunction.ExtensionUrnReference);

        ProtoPlan[] roundTrips =
        [
            ProtoPlan.Parser.ParseFrom(serialized.ToByteArray()),
            JsonParser.Default.Parse<ProtoPlan>(JsonFormatter.Default.Format(serialized)),
        ];
        foreach (ProtoPlan roundTrip in roundTrips)
        {
            IPlan converted = new ProtoToPlanConverter().From(roundTrip);
            var invocation = (Substrait.Core.Expression.Expression.ScalarFunctionInvocation)((Project)converted.Roots[0].Input).Expressions[0];
            Assert.IsNotNull(invocation.Declaration);
            Assert.AreEqual(urn, invocation.Declaration.Uri);
            Assert.AreEqual(serialized, new PlanToProtoConverter().From(converted));
        }
    }

    [TestMethod]
    public void NumbersFunctionAndTypeVariationAnchorsIndependently()
    {
        var variation = new TypeVariationImpl("extension:example:types", "i64", "custom", string.Empty, FunctionBehavior.INHERITS);
        var schema = new Substrait.Core.Type.NamedStruct(
            ["value"],
            TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64_(variation)]));
        var read = new NamedTableRead(schema, ["orders"], null);
        var function = new Substrait.Core.Expression.Expression.ScalarFunctionInvocation(
            "extension:example:functions",
            "identity:i64",
            [new Literal.I64Literal(1)],
            TypeFactory.REQUIRED.I64,
            null);
        var project = new Project(read, [function]);
        IPlan plan = new Substrait.Core.Plan.Plan(
            [new Substrait.Core.Plan.Plan.Root(project, ["value", "result"])],
            Substrait.Core.Plan.Version.Current);

        ProtoPlan result = new PlanToProtoConverter().From(plan);

        Assert.AreEqual(1U, result.Relations[0].Root.Input.Project.Input.Read.BaseSchema.Struct.Types_[0].I64.TypeVariationReference);
        Assert.AreEqual(0U, result.Relations[0].Root.Input.Project.Expressions[0].ScalarFunction.FunctionReference);
        Assert.AreEqual(1U, result.Extensions.Single(extension => extension.ExtensionTypeVariation is not null).ExtensionTypeVariation.TypeVariationAnchor);
        Assert.AreEqual(0U, result.Extensions.Single(extension => extension.ExtensionFunction is not null).ExtensionFunction.FunctionAnchor);
    }

    [TestMethod]
    public void ResolvesExtendedExpressionUrns()
    {
        ExtendedExpression expression = new()
        {
            ExtensionUrns = { new SimpleExtensionURN { ExtensionUrnAnchor = 1, Urn = "extension:example:functions" } },
            Extensions =
            {
                new SimpleExtensionDeclaration
                {
                    ExtensionFunction = new() { ExtensionUrnReference = 1, FunctionAnchor = 7, Name = "identity:i64" },
                },
            },
        };

        FunctionImplAnchor anchor = new ExtensionsDictionary.Builder(expression).Build().GetFunctionAnchor(7);

        Assert.AreEqual("extension:example:functions", anchor.Namespace);
        Assert.AreEqual("identity:i64", anchor.Key);
    }

    [TestMethod]
    public void ConvertedPlanRoundTripsDeterministicallyThroughBinaryAndJson()
    {
        IPlan original = this.converter.From(CreatePlan(), ExtensionsDictionary.StrictMode.OFF);
        PlanToProtoConverter serializer = new();
        ProtoPlan serialized = serializer.From(original);
        string directory = Path.Combine(Path.GetTempPath(), nameof(ProtoToPlanConverterTests), Guid.NewGuid().ToString("N"));
        string binaryPath = Path.Combine(directory, "plan.pb");
        string jsonPath = Path.Combine(directory, "plan.json");

        try
        {
            FileUtils.WritePlan(serialized, binaryPath, FileUtils.FileType.Protobuf);
            FileUtils.WritePlan(serialized, jsonPath, FileUtils.FileType.Json);

            CollectionAssert.AreEqual(serialized.ToByteArray(), File.ReadAllBytes(binaryPath));
            CollectionAssert.AreEqual(serialized.ToByteArray(), serializer.From(original).ToByteArray());
            Assert.AreEqual(
                original,
                this.converter.From(FileUtils.FetchPlan(binaryPath, FileUtils.FileType.Protobuf), ExtensionsDictionary.StrictMode.OFF));
            Assert.AreEqual(
                original,
                this.converter.From(FileUtils.FetchPlan(jsonPath, FileUtils.FileType.Json), ExtensionsDictionary.StrictMode.OFF));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void FunctionResolutionHonorsStrictMode()
    {
        ProtoPlan plan = CreatePlanWithExtensions(
            new SimpleExtensionDeclaration
            {
                ExtensionFunction = new()
                {
                    ExtensionUrnReference = 1,
                    FunctionAnchor = 0,
                    Name = "missing:i64",
                },
            });
        plan.Relations[0].Root.Input = new ProtoRel
        {
            Project = new ProjectRel
            {
                Input = CreateRead(),
                Expressions =
                {
                    new Protobuf.Expression
                    {
                        ScalarFunction = new Protobuf.Expression.Types.ScalarFunction
                        {
                            FunctionReference = 0,
                            OutputType = RequiredI64(),
                        },
                    },
                },
                Common = new RelCommon(),
            },
        };

        IPlan nonStrict = this.converter.From(plan, ExtensionsDictionary.StrictMode.OFF);

        Assert.IsNull(((Substrait.Core.Expression.Expression.ScalarFunctionInvocation)((Project)nonStrict.Roots[0].Input).Expressions[0]).Declaration);
        Assert.ThrowsException<ArgumentException>(() => this.converter.From(plan, ExtensionsDictionary.StrictMode.FUNCTION));
    }

    [TestMethod]
    public void TypeVariationResolutionHonorsStrictMode()
    {
        ProtoPlan plan = CreatePlanWithExtensions(
            new SimpleExtensionDeclaration
            {
                ExtensionTypeVariation = new()
                {
                    ExtensionUrnReference = 1,
                    TypeVariationAnchor = 1,
                    Name = "missing",
                },
            });
        plan.Relations[0].Root.Input.Read.BaseSchema.Struct.Types_[0].I64.TypeVariationReference = 1;

        IPlan nonStrict = this.converter.From(plan, ExtensionsDictionary.StrictMode.OFF);

        Assert.IsNull(((NamedTableRead)nonStrict.Roots[0].Input).RecordType.Fields[0].TypeVariation);
        Assert.ThrowsException<ArgumentException>(() => this.converter.From(plan, ExtensionsDictionary.StrictMode.TYPE_VARIATION));
    }

    private static ProtoPlan CreatePlan()
    {
        return new ProtoPlan
        {
            Version = new ProtoVersion
            {
                MajorNumber = 1,
                MinorNumber = 2,
                PatchNumber = 3,
                GitHash = "abc",
                Producer = "tests",
            },
            Relations =
            {
                new PlanRel
                {
                    Root = new RelRoot
                    {
                        Input = CreateRead(),
                        Names = { "output" },
                    },
                },
            },
        };
    }

    private static ProtoPlan CreatePlanWithExtensions(SimpleExtensionDeclaration extension)
    {
        ProtoPlan plan = CreatePlan();
        plan.ExtensionUrns.Add(new SimpleExtensionURN { ExtensionUrnAnchor = 1, Urn = "extension:example:missing" });
        plan.Extensions.Add(extension);
        return plan;
    }

    private static Substrait.Protobuf.Type RequiredI64()
    {
        return new Substrait.Protobuf.Type
        {
            I64 = new Substrait.Protobuf.Type.Types.I64
            {
                Nullability = Substrait.Protobuf.Type.Types.Nullability.Required,
            },
        };
    }

    private static ProtoRel CreateRead()
    {
        return new ProtoRel
        {
            Read = new ReadRel
            {
                BaseSchema = new Substrait.Protobuf.NamedStruct
                {
                    Names = { "value" },
                    Struct = new Substrait.Protobuf.Type.Types.Struct
                    {
                        Types_ =
                        {
                            RequiredI64(),
                        },
                        Nullability = Substrait.Protobuf.Type.Types.Nullability.Required,
                    },
                },
                NamedTable = new ReadRel.Types.NamedTable { Names = { "orders" } },
                Common = new RelCommon(),
            },
        };
    }
}
