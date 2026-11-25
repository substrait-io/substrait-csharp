// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Tools.Visitor;
using static Substrait.Core.Expression.Expression;
using static Substrait.Core.Expression.Literal;

namespace Substrait.Core.Expression;

/// <summary>
/// Visitor for processing expressions in a tree.
/// </summary>
/// <typeparam name="TContext">The type of the context used during visitation.</typeparam>
/// <typeparam name="TOutput">The type of the output produced by the visitation.</typeparam>
public abstract class ExpressionVisitor<TContext, TOutput> : IVisitor<IExpression, TContext, TOutput>
{
    /// <summary>
    /// Visits an <see cref="FieldReference"/> expression.
    /// </summary>
    /// <param name="expr">Field reference expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(FieldReference expr, TContext context);

    /// <summary>
    /// Visits an <see cref="NullLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Null literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(NullLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="BoolLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Bool literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(BoolLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="I8Literal"/> expression.
    /// </summary>
    /// <param name="expr">I8 literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(I8Literal expr, TContext context);

    /// <summary>
    /// Visits an <see cref="I16Literal"/> expression.
    /// </summary>
    /// <param name="expr">I16 literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(I16Literal expr, TContext context);

    /// <summary>
    /// Visits an <see cref="I32Literal"/> expression.
    /// </summary>
    /// <param name="expr">I32 literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(I32Literal expr, TContext context);

    /// <summary>
    /// Visits an <see cref="I64Literal"/> expression.
    /// </summary>
    /// <param name="expr">I64 literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(I64Literal expr, TContext context);

    /// <summary>
    /// Visits an <see cref="FP32Literal"/> expression.
    /// </summary>
    /// <param name="expr">FP32 literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(FP32Literal expr, TContext context);

    /// <summary>
    /// Visits an <see cref="FP64Literal"/> expression.
    /// </summary>
    /// <param name="expr">FP64 literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(FP64Literal expr, TContext context);

    /// <summary>
    /// Visits an <see cref="StrLiteral"/> expression.
    /// </summary>
    /// <param name="expr">String literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(StrLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="BinaryLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Binary literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(BinaryLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="PrecisionTimestampLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Precision timestamp literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(PrecisionTimestampLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="PrecisionTimestampTZLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Precision timestamp with timezone literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(PrecisionTimestampTZLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="TimeLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Time literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(TimeLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="DateLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Date literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(DateLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="IntervalYearLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Interval year literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(IntervalYearLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="IntervalDayLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Interval day literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(IntervalDayLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="FixedCharLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Fixed char literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(FixedCharLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="VarCharLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Varchar literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(VarCharLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="FixedBinaryLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Fixed binary literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(FixedBinaryLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="DecimalLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Decimal literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(DecimalLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="StructLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Struct literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(StructLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="UuidLiteral"/> expression.
    /// </summary>
    /// <param name="expr">UUID literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(UuidLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="IntervalCompoundLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Interval compound literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(IntervalCompoundLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="ListLiteral"/> expression.
    /// </summary>
    /// <param name="expr">List literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(ListLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="MapLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Map literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(MapLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="EmptyListLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Empty list literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(EmptyListLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="EmptyMapLiteral"/> expression.
    /// </summary>
    /// <param name="expr">Empty map literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(EmptyMapLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="UserDefinedLiteral"/> expression.
    /// </summary>
    /// <param name="expr">User-defined literal expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(UserDefinedLiteral expr, TContext context);

    /// <summary>
    /// Visits an <see cref="ScalarFunctionInvocation"/> expression.
    /// </summary>
    /// <param name="expr">Scalar function invocation expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(ScalarFunctionInvocation expr, TContext context);

    /// <summary>
    /// Visits an <see cref="Cast"/> expression.
    /// </summary>
    /// <param name="expr">Cast expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Cast expr, TContext context);

    /// <summary>
    /// Visits an <see cref="IfThen"/> expression.
    /// </summary>
    /// <param name="expr">If-then expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(IfThen expr, TContext context);

    /// <summary>
    /// Visits an <see cref="ScalarSubquery"/> expression.
    /// </summary>
    /// <param name="expr">Scalar subquery expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(ScalarSubquery expr, TContext context);

    /// <summary>
    /// Visits an <see cref="InPredicateSubquery"/> expression.
    /// </summary>
    /// <param name="expr">In-predicate subquery expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(InPredicateSubquery expr, TContext context);

    /// <summary>
    /// Visits an <see cref="SetPredicateSubquery"/> expression.
    /// </summary>
    /// <param name="expr">Set-predicate subquery expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(SetPredicateSubquery expr, TContext context);

    /// <summary>
    /// Visits an <see cref="SetComparisonSubquery"/> expression.
    /// </summary>
    /// <param name="expr">Set-comparison subquery expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(SetComparisonSubquery expr, TContext context);

    /// <summary>
    /// Visits an <see cref="Struct"/> expression.
    /// </summary>
    /// <param name="expr">Struct expression.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Struct expr, TContext context);

    /// <inheritdoc/>
    public abstract TOutput Visit(IExpression other, TContext context);
}
