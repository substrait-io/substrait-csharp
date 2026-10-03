// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

namespace Substrait.Core.Relation;

/// <summary>
/// A default visitor for processing relational operators in a tree,
/// where all visit methods delegate to a single fallback method.
/// </summary>
/// <typeparam name="TContext">The type of the context used during visitation.</typeparam>
/// <typeparam name="TOutput">The type of the output produced by the visitation.</typeparam>
public abstract class DefaultRelVisitor<TContext, TOutput> : RelVisitor<TContext, TOutput>
{
    /// <inheritdoc/>
    public override TOutput Visit(ExtensionLeaf op, TContext context) => this.DefaultVisit(op, context);

    /// <inheritdoc/>
    public override TOutput Visit(ExtensionSingle op, TContext context) => this.DefaultVisit(op, context);

    /// <inheritdoc/>
    public override TOutput Visit(ExtensionMulti op, TContext context) => this.DefaultVisit(op, context);

    /// <inheritdoc/>
    public override TOutput Visit(Reference op, TContext context) => this.DefaultVisit(op, context);

    /// <inheritdoc/>
    public override TOutput Visit(Aggregate op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Filter op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Cross op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Join op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(HashJoin op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Project op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(NamedTableRead op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(VirtualTableRead op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(EmptyRead read, TContext context)
    {
        return this.DefaultVisit(read, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Sort op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Fetch op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(Set op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(ScatterExchange op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(SingleBucketExchange op, TContext context)
    {
        return this.DefaultVisit(op, context);
    }

    /// <inheritdoc/>
    public override TOutput Visit(IRel other, TContext context)
    {
        return this.DefaultVisit(other, context);
    }

    /// <summary>
    /// Default visit method.
    /// </summary>
    /// <param name="rel">The node to be processed.</param>
    /// <param name="context">The initial context for the visitation.</param>
    /// <returns>The output produced after processing the node.</returns>
    protected abstract TOutput DefaultVisit(IRel rel, TContext context);
}
