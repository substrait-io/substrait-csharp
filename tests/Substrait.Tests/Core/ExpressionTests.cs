// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Expression;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using static Substrait.Core.Expression.Expression;
using static Substrait.Core.Expression.Expression.Cast;
using static Substrait.Core.Expression.Literal;

namespace Substrait.Tests.Core;

/// <summary>
/// Tests expressions.
/// </summary>
public class ExpressionTests
{
    private static readonly string[] EnumOptions = ["a", "b", "c"];

    /// <summary>
    /// Tests cast expression equality.
    /// </summary>
    [Test]
    public async Task TestCastEquals()
    {
        object e1 = new Cast(TypeFactory.REQUIRED.I64, new StrLiteral("123"), FailureBehavior.ThrowException);
        object e2 = new Cast(TypeFactory.REQUIRED.I64, new StrLiteral("123"), FailureBehavior.ThrowException);
        object e3 = new Cast(TypeFactory.REQUIRED.I64, new StrLiteral("456"), FailureBehavior.ThrowException);
        object e4 = new Cast(TypeFactory.REQUIRED.I32, new StrLiteral("123"), FailureBehavior.ThrowException);
        object e5 = new Cast(TypeFactory.REQUIRED.I64, new StrLiteral("123"), FailureBehavior.ReturnNull);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsNotEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);
        await Assert.That(e5).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e5.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests scalar function invocation equality.
    /// </summary>
    [Test]
    public async Task TestScalarFunctionInvocationEquals()
    {
        var scalarFunctionImpl = new ScalarFunctionImpl("urix", "fname1", "fdesc", FunctionImpl.NullabilityMode.Mirror, new[] { new ValueArgument("i64", "vname", "vdesc", true), new ValueArgument("i64", "vname2", "vdesc2", false) }, ImmutableDictionary<string, IOption>.Empty, null, null, "i64");
        var scalarFunctionImpl2 = new ScalarFunctionImpl("urix", "fname1", "fdesc", FunctionImpl.NullabilityMode.Mirror, new[] { new ValueArgument("i64", "vname", "vdesc", true), new ValueArgument("i64", "vname2", "vdesc2", false) }, ImmutableDictionary<string, IOption>.Empty, null, null, "i64");

        object e1 = new ScalarFunctionInvocation(scalarFunctionImpl.Uri, scalarFunctionImpl.Key, ImmutableList.Create(new I64Literal(1), new I64Literal(2)), TypeFactory.REQUIRED.I64, scalarFunctionImpl);
        object e2 = new ScalarFunctionInvocation(scalarFunctionImpl.Uri, scalarFunctionImpl.Key, ImmutableList.Create(new I64Literal(1), new I64Literal(2)), TypeFactory.REQUIRED.I64, scalarFunctionImpl);
        object e3 = new ScalarFunctionInvocation(scalarFunctionImpl.Uri, scalarFunctionImpl.Key, ImmutableList.Create(new I64Literal(1), new I64Literal(1)), TypeFactory.REQUIRED.I64, scalarFunctionImpl);
        object e4 = new ScalarFunctionInvocation(scalarFunctionImpl.Uri, scalarFunctionImpl.Key, ImmutableList.Create(new I64Literal(1), new I64Literal(2)), TypeFactory.REQUIRED.I32, scalarFunctionImpl);
        object e5 = new ScalarFunctionInvocation(scalarFunctionImpl.Uri, scalarFunctionImpl.Key, ImmutableList.Create(new I64Literal(1)), TypeFactory.REQUIRED.I64, scalarFunctionImpl);
        object e6 = new ScalarFunctionInvocation(scalarFunctionImpl2.Uri, scalarFunctionImpl2.Key, ImmutableList.Create(new I64Literal(1), new I64Literal(2)), TypeFactory.REQUIRED.I64, scalarFunctionImpl2);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsNotEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);
        await Assert.That(e5).IsNotEqualTo(e1);
        await Assert.That(e6).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e5.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e6.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests aggregate function invocation equality.
    /// </summary>
    [Test]
    public async Task TestAggregateFunctionInvocationEquals()
    {
        var aggrImpl = new AggregateFunctionImpl("urix", "agg1", "desc1", FunctionImpl.NullabilityMode.Mirror, new[] { new ValueArgument("i64", "vn1", "vd1", true) }, ImmutableDictionary<string, IOption>.Empty, null, null, "i64", FunctionImpl.DecomposabilityMode.None, "i64");
        var aggrImpl2 = new AggregateFunctionImpl("urix", "agg1", "desc1", FunctionImpl.NullabilityMode.Mirror, new[] { new ValueArgument("i64", "vn1", "vd1", true) }, ImmutableDictionary<string, IOption>.Empty, null, null, "i64", FunctionImpl.DecomposabilityMode.None, "i64");

        object e1 = new AggregateFunctionInvocation(aggrImpl.Uri, aggrImpl.Key, new[] { new FieldReference(TypeFactory.REQUIRED.I64, 0) }, TypeFactory.REQUIRED.I64, AggregateFunctionInvocation.AggregationPhase.InitialToIntermediate, AggregateFunctionInvocation.AggregationInvocation.All, aggrImpl);
        object e2 = new AggregateFunctionInvocation(aggrImpl.Uri, aggrImpl.Key, new[] { new FieldReference(TypeFactory.REQUIRED.I64, 0) }, TypeFactory.REQUIRED.I64, AggregateFunctionInvocation.AggregationPhase.InitialToIntermediate, AggregateFunctionInvocation.AggregationInvocation.All, aggrImpl);
        object e3 = new AggregateFunctionInvocation(aggrImpl.Uri, aggrImpl.Key, new[] { new FieldReference(TypeFactory.REQUIRED.I32, 0) }, TypeFactory.REQUIRED.I64, AggregateFunctionInvocation.AggregationPhase.InitialToIntermediate, AggregateFunctionInvocation.AggregationInvocation.All, aggrImpl);
        object e4 = new AggregateFunctionInvocation(aggrImpl.Uri, aggrImpl.Key, new[] { new FieldReference(TypeFactory.REQUIRED.I64, 0) }, TypeFactory.REQUIRED.I32, AggregateFunctionInvocation.AggregationPhase.InitialToIntermediate, AggregateFunctionInvocation.AggregationInvocation.All, aggrImpl);
        object e5 = new AggregateFunctionInvocation(aggrImpl.Uri, aggrImpl.Key, new[] { new FieldReference(TypeFactory.REQUIRED.I64, 0) }, TypeFactory.REQUIRED.I64, AggregateFunctionInvocation.AggregationPhase.InitialToResult, AggregateFunctionInvocation.AggregationInvocation.All, aggrImpl);
        object e6 = new AggregateFunctionInvocation(aggrImpl.Uri, aggrImpl.Key, new[] { new FieldReference(TypeFactory.REQUIRED.I64, 0) }, TypeFactory.REQUIRED.I64, AggregateFunctionInvocation.AggregationPhase.InitialToIntermediate, AggregateFunctionInvocation.AggregationInvocation.Distinct, aggrImpl);
        object e7 = new AggregateFunctionInvocation(aggrImpl2.Uri, aggrImpl2.Key, new[] { new FieldReference(TypeFactory.REQUIRED.I64, 0) }, TypeFactory.REQUIRED.I64, AggregateFunctionInvocation.AggregationPhase.InitialToIntermediate, AggregateFunctionInvocation.AggregationInvocation.All, aggrImpl2);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsNotEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);
        await Assert.That(e5).IsNotEqualTo(e1);
        await Assert.That(e6).IsNotEqualTo(e1);
        await Assert.That(e7).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e5.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e6.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e7.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests if-then equality.
    /// </summary>
    [Test]
    public async Task TestIfThenEquals()
    {
        (IExpression, IExpression) if1 = (new BoolLiteral(true), new I64Literal(1));
        (IExpression, IExpression) if2 = (new BoolLiteral(false), new I64Literal(2));
        (IExpression, IExpression) if3 = (new BoolLiteral(false), new I64Literal(1));

        object e1 = new IfThen(new[] { if1, if2 }.Cast<(IExpression Condition, IExpression Then)>(), new I64Literal(1234));
        object e2 = new IfThen(new[] { if1, if2 }.Cast<(IExpression Condition, IExpression Then)>(), new I64Literal(1234));
        object e3 = new IfThen(new[] { if1, if3 }.Cast<(IExpression Condition, IExpression Then)>(), new I64Literal(1234));
        object e4 = new IfThen(new[] { if1, if2 }.Cast<(IExpression Condition, IExpression Then)>(), new I64Literal(123));
        object e5 = new IfThen(new[] { if1 }.Cast<(IExpression Condition, IExpression Then)>(), new I64Literal(1234));

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsNotEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);
        await Assert.That(e5).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e5.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests field reference equality.
    /// </summary>
    [Test]
    public async Task TestFieldReferenceEquals()
    {
        object e1 = new FieldReference(TypeFactory.REQUIRED.I64, 1, 2);
        object e2 = new FieldReference(TypeFactory.REQUIRED.I64, 1, 2);
        object e3 = new FieldReference(TypeFactory.REQUIRED.I64, 2, 2);
        object e4 = new FieldReference(TypeFactory.REQUIRED.I64, 1, 0);
        object e5 = new FieldReference(TypeFactory.REQUIRED.I64, 0, 2);
        object e6 = new FieldReference(TypeFactory.REQUIRED.I32, 0, 2);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsNotEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);
        await Assert.That(e5).IsNotEqualTo(e1);
        await Assert.That(e6).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e5.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e6.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests enum argument equality.
    /// </summary>
    [Test]
    public async Task TestEnumArgumentValueEquals()
    {
        var arg1 = new EnumArgument(EnumOptions, "enum1", "desc1", true);
        var arg2 = new EnumArgument(EnumOptions, "enum1", "desc1", true);

        object e1 = new EnumArgumentValue(arg1, "a");
        object e2 = new EnumArgumentValue(arg1, "a");
        object e3 = new EnumArgumentValue(arg1, "b");
        object e4 = new EnumArgumentValue(arg1, null);
        object e5 = new EnumArgumentValue(arg1, null);
        object e6 = new EnumArgumentValue(arg2, "a");

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsNotEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);
        await Assert.That(e6).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e6.GetHashCode()).IsNotEqualTo(e1.GetHashCode());

        await Assert.That(e5).IsEqualTo(e4);
        await Assert.That(e5.GetHashCode()).IsEqualTo(e4.GetHashCode());
    }

    /// <summary>
    /// Tests enum argument equality.
    /// </summary>
    [Test]
    public async Task TestSortFieldEquals()
    {
        object e1 = new SortField(new I64Literal(1), SortField.SortDirection.SortDirectionAscNullsFirst);
        object e2 = new SortField(new I64Literal(1), SortField.SortDirection.SortDirectionAscNullsFirst);
        object e3 = new SortField(new I64Literal(2), SortField.SortDirection.SortDirectionAscNullsFirst);
        object e4 = new SortField(new I64Literal(1), SortField.SortDirection.SortDirectionDescNullsFirst);
        object e5 = new SortField(new I32Literal(1), SortField.SortDirection.SortDirectionAscNullsFirst);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsNotEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);
        await Assert.That(e5).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e5.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests enum argument equality.
    /// </summary>
    [Test]
    public async Task TestStructEquals()
    {
        object e1 = new Struct([new I64Literal(1), new I64Literal(2)]);
        object e2 = new Struct([new I64Literal(1), new I64Literal(2)]);
        object e3 = new Struct([new I64Literal(2), new I64Literal(1)]);
        object e4 = new Struct([new I64Literal(1), new I64Literal(2), new StrLiteral("a")]);
        object e5 = new Struct([new I32Literal(1), new I64Literal(2)]);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsNotEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);
        await Assert.That(e5).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e5.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests scalar subquery equality.
    /// </summary>
    [Test]
    public async Task TestScalarSubqueryEquals()
    {
        var rel1 = CreateVirtualTableRead(["a"], [TypeFactory.REQUIRED.I64], [new Struct([new I64Literal(1)])]);
        var rel2 = CreateVirtualTableRead(["a"], [TypeFactory.REQUIRED.I64], [new Struct([new I64Literal(1)])]);

        object e1 = new ScalarSubquery(rel1, TypeFactory.REQUIRED.I64);
        object e2 = new ScalarSubquery(rel1, TypeFactory.REQUIRED.I64);
        object e3 = new ScalarSubquery(rel2, TypeFactory.REQUIRED.I64);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests in predicate subquery equality.
    /// </summary>
    [Test]
    public async Task TestInPredicateSubqueryEquals()
    {
        var rel1 = CreateVirtualTableRead(["a"], [TypeFactory.REQUIRED.I64], [new Struct([new I64Literal(1)])]);
        var rel2 = CreateVirtualTableRead(["a"], [TypeFactory.REQUIRED.I64], [new Struct([new I64Literal(1)])]);

        object e1 = new InPredicateSubquery(rel1, [new I64Literal(1), new I64Literal(2)]);
        object e2 = new InPredicateSubquery(rel1, [new I64Literal(1), new I64Literal(2)]);
        object e3 = new InPredicateSubquery(rel2, [new I64Literal(1), new I64Literal(2)]);
        object e4 = new InPredicateSubquery(rel1, [new I64Literal(1)]);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests set predicate subquery equality.
    /// </summary>
    [Test]
    public async Task TestSetPredicateSubqueryEquals()
    {
        var rel1 = CreateVirtualTableRead(["a"], [TypeFactory.REQUIRED.I64], [new Struct([new I64Literal(1)])]);
        var rel2 = CreateVirtualTableRead(["a"], [TypeFactory.REQUIRED.I64], [new Struct([new I64Literal(1)])]);

        object e1 = new SetPredicateSubquery(rel1, SetPredicateSubquery.PredicateOp.Exists);
        object e2 = new SetPredicateSubquery(rel1, SetPredicateSubquery.PredicateOp.Exists);
        object e3 = new SetPredicateSubquery(rel2, SetPredicateSubquery.PredicateOp.Exists);
        object e4 = new SetPredicateSubquery(rel1, SetPredicateSubquery.PredicateOp.Unique);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    /// <summary>
    /// Tests set comparison predicate subquery equality.
    /// </summary>
    [Test]
    public async Task TestSetComparisonSubqueryEquals()
    {
        var rel1 = CreateVirtualTableRead(["a"], [TypeFactory.REQUIRED.I64], [new Struct([new I64Literal(1)])]);
        var rel2 = CreateVirtualTableRead(["a"], [TypeFactory.REQUIRED.I64], [new Struct([new I64Literal(1)])]);

        object e1 = new SetComparisonSubquery(new I64Literal(1), SetComparisonSubquery.ComparisonOp.NotEqual, SetComparisonSubquery.ReductionOp.All, rel1);
        object e2 = new SetComparisonSubquery(new I64Literal(1), SetComparisonSubquery.ComparisonOp.NotEqual, SetComparisonSubquery.ReductionOp.All, rel1);
        object e3 = new SetComparisonSubquery(new I64Literal(1), SetComparisonSubquery.ComparisonOp.NotEqual, SetComparisonSubquery.ReductionOp.All, rel2);
        object e4 = new SetComparisonSubquery(new I64Literal(2), SetComparisonSubquery.ComparisonOp.NotEqual, SetComparisonSubquery.ReductionOp.All, rel1);
        object e5 = new SetComparisonSubquery(new I64Literal(1), SetComparisonSubquery.ComparisonOp.LessThan, SetComparisonSubquery.ReductionOp.All, rel1);
        object e6 = new SetComparisonSubquery(new I64Literal(1), SetComparisonSubquery.ComparisonOp.NotEqual, SetComparisonSubquery.ReductionOp.Any, rel1);

        await Assert.That(e2).IsEqualTo(e1);
        await Assert.That(e3).IsEqualTo(e1);
        await Assert.That(e4).IsNotEqualTo(e1);
        await Assert.That(e5).IsNotEqualTo(e1);
        await Assert.That(e6).IsNotEqualTo(e1);

        await Assert.That(e2.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e3.GetHashCode()).IsEqualTo(e1.GetHashCode());
        await Assert.That(e4.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e5.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
        await Assert.That(e6.GetHashCode()).IsNotEqualTo(e1.GetHashCode());
    }

    private static VirtualTableRead CreateVirtualTableRead(IEnumerable<string> names, IEnumerable<IType> types, IEnumerable<Struct> rows)
    {
        return new VirtualTableRead(new NamedStruct(names, TypeFactory.REQUIRED.Struct(types)), rows, filter: null);
    }
}
