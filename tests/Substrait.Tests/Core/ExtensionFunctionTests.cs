// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Type;

namespace Substrait.Tests.Core;

public sealed class ExtensionFunctionTests
{
    [Test]
    public async Task TypeExpressionParserCreatesScalarTypes()
    {
        ITypeExpression parsed = TypeExpressionParser.Parse("i64");

        await Assert.That(parsed).IsEqualTo(TypeFactory.REQUIRED.I64);
    }

    [Test]
    public async Task FunctionKeyUsesParsedArgumentSignatures()
    {
        IArgument[] arguments =
        [
            new ValueArgument("i64", "left", "Left operand.", required: true),
            new ValueArgument("string", "right", "Right operand.", required: true),
        ];

        ScalarFunctionImpl function = new(
            "https://example.test/functions",
            "compare",
            "Compares two values.",
            FunctionImpl.NullabilityMode.Mirror,
            arguments,
            ImmutableDictionary<string, IOption>.Empty,
            ordered: null,
            variadic: null,
            returnType: "boolean");

        await Assert.That(function.Key).IsEqualTo("compare:i64_str");
        await Assert.That(function.Anchor.Namespace).IsEqualTo(function.Uri);
        await Assert.That(function.Anchor.Key).IsEqualTo(function.Key);
    }

    [Test]
    public async Task FunctionRangeUsesDeclaredArgumentCountForNonVariadicFunction()
    {
        IArgument[] arguments =
        [
            new ValueArgument("i64", "required", "Required operand.", required: true),
            new ValueArgument("i64", "optional", "Optional operand.", required: false),
        ];
        ScalarFunctionImpl function = CreateFunction(arguments, variadic: null);

        await Assert.That(function.GetRange()).IsEqualTo(new Tuple<int, int>(1, 2));
    }

    [Test]
    public async Task FunctionRangeUsesVariadicOccurrenceBounds()
    {
        IArgument[] arguments =
        [
            new ValueArgument("i64", "fixed", "Fixed operand.", required: true),
            new ValueArgument("i64", "repeated", "Repeated operand.", required: true),
        ];

        ScalarFunctionImpl bounded = CreateFunction(arguments, new VariadicBehavior(min: 2, max: 4));
        ScalarFunctionImpl unbounded = CreateFunction(arguments, new VariadicBehavior(min: 0));

        await Assert.That(bounded.GetRange()).IsEqualTo(new Tuple<int, int>(3, 5));
        await Assert.That(unbounded.GetRange()).IsEqualTo(new Tuple<int, int>(1, int.MaxValue));
    }

    private static ScalarFunctionImpl CreateFunction(IEnumerable<IArgument> arguments, IVariadicBehavior? variadic)
    {
        return new ScalarFunctionImpl(
            "https://example.test/functions",
            "function",
            "Test function.",
            FunctionImpl.NullabilityMode.Mirror,
            arguments,
            ImmutableDictionary<string, IOption>.Empty,
            ordered: null,
            variadic,
            returnType: "boolean");
    }
}
