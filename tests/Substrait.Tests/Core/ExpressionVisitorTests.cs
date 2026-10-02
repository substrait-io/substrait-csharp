// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Substrait.Core.Expression;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using Substrait.Tools.Visitor;
using static Substrait.Core.Expression.Expression;
using static Substrait.Core.Expression.Literal;
using Assembly = System.Reflection.Assembly;

namespace Substrait.Tests.Core.Expression;

/// <summary>
/// Tests for expression visitors.
/// </summary>
public class ExpressionVisitorTests
{
    private readonly ExpressionTopDownDispatcher<StringBuilderContext, VoidOutput> topDownDispatcher;
    private readonly ExpressionBottomUpDispatcher<StringBuilderContext, VoidOutput> bottomUpDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpressionVisitorTests"/> class.
    /// </summary>
    public ExpressionVisitorTests()
    {
        this.topDownDispatcher = new ExpressionTopDownDispatcher<StringBuilderContext, VoidOutput>(new ExpressionPrinter());
        this.bottomUpDispatcher = new ExpressionBottomUpDispatcher<StringBuilderContext, VoidOutput>(new ExpressionPrinter());
    }

    /// <summary>
    /// Verifies that all sealed classes that implement IExpression have a corresponding Visit method in ExpressionVisitor.
    /// </summary>
    [Test]
    public async Task TestExpressionVisitorContainsAllSealedClasses()
    {
        // Get all types that implement IExpression
        var iexpressionTypes = Assembly.GetAssembly(typeof(IExpression))!
            .GetTypes()
            .Where(t => typeof(IExpression).IsAssignableFrom(t) && t.IsClass && t.IsSealed && t.Namespace == "Substrait.Core.Expression")
            .ToList();

        // Get all methods in ExpressionVisitor
        var expressionVisitorMethods = typeof(ExpressionVisitor<,>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "Visit" && m.GetParameters().Length == 2)
            .Select(m => m.GetParameters()[0].ParameterType)
            .ToList();

        // Check if all IExpression types have corresponding Visit methods in ExpressionVisitor
        foreach (var type in iexpressionTypes)
        {
            await Assert.That(expressionVisitorMethods.Contains(type)).IsTrue().Because($"ExpressionVisitor does not contain a Visit method for {type.Name}");
        }
    }

    /// <summary>
    /// Tests the expression visitor with a cast expression.
    /// </summary>
    [Test]
    public async Task TestCastExpression()
    {
        var castExpr = new Cast(TypeFactory.REQUIRED.I64, new StrLiteral("123"), Cast.FailureBehavior.ThrowException);
        await this.CheckTopDownTraversal(castExpr, "Cast|StrLiteral|");
    }

    /// <summary>
    /// Tests the expression visitor with a scalar function invocation.
    /// </summary>
    [Test]
    public async Task TestScalarFunctionInvocation()
    {
        var scalarFunctionImpl = new ScalarFunctionImpl("urix", "fname1", "fdesc", FunctionImpl.NullabilityMode.Mirror, new[] { new ValueArgument("i64", "vname", "vdesc", true) }, ImmutableDictionary<string, IOption>.Empty, null, null, "i64");
        var scalarFunctionInvocation = new ScalarFunctionInvocation(scalarFunctionImpl.Uri, scalarFunctionImpl.Key, ImmutableList.Create(new I64Literal(1)), TypeFactory.REQUIRED.I64, scalarFunctionImpl);
        await this.CheckTopDownTraversal(scalarFunctionInvocation, "ScalarFunctionInvocation|I64Literal|");
    }

    /// <summary>
    /// Tests the expression visitor with an if-then expression.
    /// </summary>
    [Test]
    public async Task TestIfThenExpression()
    {
        (IExpression, IExpression) ifClause = (new BoolLiteral(true), new I64Literal(1));
        var ifThenExpr = new IfThen(new[] { ifClause }.Cast<(IExpression Condition, IExpression Then)>(), new I64Literal(1234));
        await this.CheckTopDownTraversal(ifThenExpr, "IfThen|BoolLiteral|I64Literal|I64Literal|");
    }

    /// <summary>
    /// Tests the expression visitor with a field reference.
    /// </summary>
    [Test]
    public async Task TestFieldReference()
    {
        var fieldRef = new FieldReference(TypeFactory.REQUIRED.I64, 1, 2);
        await this.CheckTopDownTraversal(fieldRef, "FieldReference|");
    }

    /// <summary>
    /// Tests the expression visitor with various literals.
    /// </summary>
    [Test]
    public async Task TestLiterals()
    {
        var boolLiteral = new BoolLiteral(true);
        await this.CheckTopDownTraversal(boolLiteral, "BoolLiteral|");

        var i64Literal = new I64Literal(123);
        await this.CheckTopDownTraversal(i64Literal, "I64Literal|");

        var strLiteral = new StrLiteral("test");
        await this.CheckTopDownTraversal(strLiteral, "StrLiteral|");
    }

    /// <summary>
    /// Tests the expression visitor with a struct expression.
    /// </summary>
    [Test]
    public async Task TestStructExpression()
    {
        var structExpr = new Struct(new IExpression[]
        {
            new BoolLiteral(true),
            new I64Literal(123),
            new StrLiteral("test"),
        });

        string topDownExpected = "Struct|BoolLiteral|I64Literal|StrLiteral|";
        string bottomUpExpected = "BoolLiteral|I64Literal|StrLiteral|Struct|";

        await this.CheckTopDownTraversal(structExpr, topDownExpected);
        await this.CheckBottomUpTraversal(structExpr, bottomUpExpected);
    }

    /// <summary>
    /// Tests the expression visitor with a set predicate subquery expression.
    /// </summary>
    [Test]
    public async Task TestSetPredicateSubquery()
    {
        var setPredicateExpr = new SetPredicateSubquery(
            new VirtualTableRead(new NamedStruct(["a"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])), [new Struct([new I64Literal(456)])], filter: null),
            SetPredicateSubquery.PredicateOp.Exists);

        string topDownExpected = "SetPredicateSubquery|";
        string bottomUpExpected = "SetPredicateSubquery|";

        await this.CheckTopDownTraversal(setPredicateExpr, topDownExpected);
        await this.CheckBottomUpTraversal(setPredicateExpr, bottomUpExpected);
    }

    /// <summary>
    /// Tests the expression visitor with a set comparison subquery expression.
    /// </summary>
    [Test]
    public async Task TestSetComparisonSubquery()
    {
        var setComparisonExpr = new SetComparisonSubquery(
            new I64Literal(123),
            SetComparisonSubquery.ComparisonOp.Equal,
            SetComparisonSubquery.ReductionOp.All,
            new VirtualTableRead(new NamedStruct(["a"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])), [new Struct([new I64Literal(456)])], filter: null));

        string topDownExpected = "SetComparisonSubquery|I64Literal|";
        string bottomUpExpected = "I64Literal|SetComparisonSubquery|";

        await this.CheckTopDownTraversal(setComparisonExpr, topDownExpected);
        await this.CheckBottomUpTraversal(setComparisonExpr, bottomUpExpected);
    }

    private async Task CheckTopDownTraversal(IExpression expr, string expected)
    {
        StringBuilderContext context = new StringBuilderContext();
        this.topDownDispatcher.Dispatch(expr, context);
        await Assert.That(context.ToString()).IsEqualTo(expected);
    }

    private async Task CheckBottomUpTraversal(IExpression expr, string expected)
    {
        StringBuilderContext context = new StringBuilderContext();
        this.bottomUpDispatcher.Dispatch(expr, context);
        await Assert.That(context.ToString()).IsEqualTo(expected);
    }

    private sealed class StringBuilderContext : NoOpContext<IExpression, VoidOutput>
    {
        private readonly StringBuilder builder = new StringBuilder();

        public void Append(string value) => this.builder.Append(value).Append('|');

        public override string ToString() => this.builder.ToString();
    }

    private sealed class ExpressionPrinter : DefaultExpressionVisitor<StringBuilderContext, VoidOutput>
    {
        public override VoidOutput Visit(IExpression other, StringBuilderContext context)
        {
            throw new NotSupportedException($"Unable to print expression {other.GetType().Name}");
        }

        protected override VoidOutput DefaultVisit(IExpression expr, StringBuilderContext context)
        {
            context.Append(expr.GetType().Name);
            return VoidOutput.Instance;
        }
    }
}
