// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using static Substrait.Core.Type.ParameterizedType;
using static Substrait.Core.Type.PrimitiveType;

namespace Substrait.Core.Type;

/// <summary>
/// A default visitor for processing types in a tree,
/// where all visit methods delegate to a single fallback method.
/// </summary>
/// <typeparam name="TContext">The type of the context used during visitation.</typeparam>
/// <typeparam name="TOutput">The type of the output produced by the visitation.</typeparam>
public abstract class DefaultTypeVisitor<TContext, TOutput> : TypeVisitor<TContext, TOutput>
{
    /// <inheritdoc/>
    public override TOutput Visit(Bool type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(I8 type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(I16 type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(I32 type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(I64 type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(FP32 type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(FP64 type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Str type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Binary type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Date type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Uuid type, TContext context) => this.DefaultVisit(type, context);

    /// <inheritdoc/>
    public override TOutput Visit(IntervalCompound type, TContext context) => this.DefaultVisit(type, context);

    /// <inheritdoc/>
    public override TOutput Visit(Time type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(IntervalYear type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(IntervalDay type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(PrecisionTimestamp type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(PrecisionTimestampTZ type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(FixedChar type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(VarChar type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(FixedBinary type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(ParameterizedType.Decimal type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Struct type, TContext context)
    {
        return this.DefaultVisit(type, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(IType other, TContext context)
    {
        return this.DefaultVisit(other, context);
    }

    /// <summary>
    /// Default visit method.
    /// </summary>
    /// <param name="type">The node to be processed.</param>
    /// <param name="context">The initial context for the visitation.</param>
    /// <returns>The output produced after processing the node.</returns>
    protected abstract TOutput DefaultVisit(IType type, TContext context);
}
