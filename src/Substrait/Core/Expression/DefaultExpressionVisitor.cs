// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using static Substrait.Core.Expression.Expression;
using static Substrait.Core.Expression.Literal;

namespace Substrait.Core.Expression;

/// <summary>
/// A default visitor for processing expressions in a tree,
/// where all visit methods delegate to a single fallback method.
/// </summary>
/// <typeparam name="TContext">The type of the context used during visitation.</typeparam>
/// <typeparam name="TOutput">The type of the output produced by the visitation.</typeparam>
public abstract class DefaultExpressionVisitor<TContext, TOutput> : ExpressionVisitor<TContext, TOutput>
{
    /// <inheritdoc/>
    public override TOutput Visit(FieldReference expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(NullLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(BoolLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(I8Literal expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(I16Literal expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(I32Literal expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(I64Literal expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(FP32Literal expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(FP64Literal expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(StrLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(BinaryLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(PrecisionTimestampLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(PrecisionTimestampTZLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(TimeLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(DateLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(IntervalYearLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(IntervalDayLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(FixedCharLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(VarCharLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(FixedBinaryLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(DecimalLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(StructLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(UuidLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(IntervalCompoundLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(ListLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(MapLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(EmptyListLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(EmptyMapLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(UserDefinedLiteral expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(ScalarFunctionInvocation expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Cast expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(IfThen expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(ScalarSubquery expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(InPredicateSubquery expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(SetPredicateSubquery expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(SetComparisonSubquery expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Struct expr, TContext context)
    {
        return this.DefaultVisit(expr, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(IExpression other, TContext context)
    {
        return this.DefaultVisit(other, context);
    }

    /// <summary>
    /// Default visit method.
    /// </summary>
    /// <param name="expr">The node to be processed.</param>
    /// <param name="context">The initial context for the visitation.</param>
    /// <returns>The output produced after processing the node.</returns>
    protected abstract TOutput DefaultVisit(IExpression expr, TContext context);
}
