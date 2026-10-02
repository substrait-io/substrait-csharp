// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Expression.Converters;
using Substrait.Core.Extension;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;

namespace Substrait.Tests.Core;

public sealed class ExpressionToProtoConverterTests
{
    private readonly ExpressionToProtoConverter converter = new(new TypeToProtoConverter());

    [Test]
    public async Task ConvertsNestedStructLiteralAndConditional()
    {
        var structure = new Literal.StructLiteral(
        [
            new Literal.I64Literal(42),
            new Literal.StructLiteral([new Literal.StrLiteral("value")]),
        ]);
        var expression = new Substrait.Core.Expression.Expression.IfThen(
            [(new Literal.BoolLiteral(true), structure)],
            new Literal.StructLiteral([new Literal.I64Literal(0), new Literal.StructLiteral([new Literal.StrLiteral("other")])]));

        Protobuf.Expression result = this.converter.From(expression);

        await Assert.That(result.IfThen.Ifs.Count).IsEqualTo(1);
        await Assert.That(result.IfThen.Ifs[0].Then.Literal.Struct.Fields[0].I64).IsEqualTo(42L);
        await Assert.That(result.IfThen.Ifs[0].Then.Literal.Struct.Fields[1].Struct.Fields[0].String).IsEqualTo("value");
    }

    [Test]
    public async Task CollectsScalarFunctionAnchor()
    {
        var expression = new Substrait.Core.Expression.Expression.ScalarFunctionInvocation(
            "/functions.yaml",
            "add:i64_i64",
            [new Literal.I64Literal(1), new Literal.I64Literal(2)],
            TypeFactory.REQUIRED.I64,
            null);
        var context = new PlanToProtoConverter.ConverterContext();

        Protobuf.Expression result = this.converter.From(expression, context);

        await Assert.That(result.ScalarFunction.FunctionReference).IsEqualTo(0U);
        await Assert.That(result.ScalarFunction.Arguments.Count).IsEqualTo(2);
        await Assert.That(context.ExtensionsCollector.Extensions[0].Type).IsEqualTo(ExtensionsCollector.ExtensionType.Function);
        await Assert.That(context.ExtensionsCollector.Extensions[0].Name).IsEqualTo("add:i64_i64");
    }
}
