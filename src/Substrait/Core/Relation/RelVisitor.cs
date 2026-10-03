// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Tools.Visitor;

namespace Substrait.Core.Relation;

/// <summary>
/// Visitor for processing relational operators in a tree.
/// </summary>
/// <typeparam name="TContext">The type of the context used during visitation.</typeparam>
/// <typeparam name="TOutput">The type of the output produced by the visitation.</typeparam>
public abstract class RelVisitor<TContext, TOutput> : IVisitor<IRel, TContext, TOutput>
{
    /// <summary>Visits a custom leaf relation.</summary>
    /// <param name="op">The relation.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The visitor output.</returns>
    public virtual TOutput Visit(ExtensionLeaf op, TContext context) => this.Visit((IRel)op, context);

    /// <summary>Visits a custom single-input relation.</summary>
    /// <param name="op">The relation.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The visitor output.</returns>
    public virtual TOutput Visit(ExtensionSingle op, TContext context) => this.Visit((IRel)op, context);

    /// <summary>Visits a custom multi-input relation.</summary>
    /// <param name="op">The relation.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The visitor output.</returns>
    public virtual TOutput Visit(ExtensionMulti op, TContext context) => this.Visit((IRel)op, context);

    /// <summary>Visits a reference leaf without traversing its target.</summary>
    /// <param name="op">The reference.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The visitor output.</returns>
    public virtual TOutput Visit(Reference op, TContext context) => this.Visit((IRel)op, context);

    /// <summary>
    /// Visits an <see cref="Aggregate"/> operator.
    /// </summary>
    /// <param name="op">Aggregate operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Aggregate op, TContext context);

    /// <summary>
    /// Visits a <see cref="Filter"/> operator.
    /// </summary>
    /// <param name="op">Filter operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Filter op, TContext context);

    /// <summary>
    /// Visits a <see cref="Cross"/> operator.
    /// </summary>
    /// <param name="op">Cross operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Cross op, TContext context);

    /// <summary>
    /// Visits a <see cref="Join"/> operator.
    /// </summary>
    /// <param name="op">Join operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Join op, TContext context);

    /// <summary>
    /// Visits a <see cref="HashJoin"/> operator.
    /// </summary>
    /// <param name="op">HashJoin operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(HashJoin op, TContext context);

    /// <summary>
    /// Visits a <see cref="Project"/> operator.
    /// </summary>
    /// <param name="op">Project operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Project op, TContext context);

    /// <summary>
    /// Visits a <see cref="NamedTableRead"/> operator.
    /// </summary>
    /// <param name="op">Named table read operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(NamedTableRead op, TContext context);

    /// <summary>
    /// Visits a <see cref="VirtualTableRead"/> operator.
    /// </summary>
    /// <param name="op">Virtual table read operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(VirtualTableRead op, TContext context);

    /// <summary>
    /// Visits a <see cref="EmptyRead"/> operator.
    /// </summary>
    /// <param name="read">Empty read operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(EmptyRead read, TContext context);

    /// <summary>
    /// Visits a <see cref="Sort"/> operator.
    /// </summary>
    /// <param name="op">Sort operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Sort op, TContext context);

    /// <summary>
    /// Visits a <see cref="Fetch"/> operator.
    /// </summary>
    /// <param name="op">Fetch operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Fetch op, TContext context);

    /// <summary>
    /// Visits a <see cref="Set"/> operator.
    /// </summary>
    /// <param name="op">Set operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Set op, TContext context);

    /// <summary>
    /// Visits a <see cref="ScatterExchange"/> operator.
    /// </summary>
    /// <param name="op">Scatter exchange operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(ScatterExchange op, TContext context);

    /// <summary>
    /// Visits a <see cref="SingleBucketExchange"/> operator.
    /// </summary>
    /// <param name="op">Single bucket exchange operator.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(SingleBucketExchange op, TContext context);

    /// <inheritdoc/>
    public abstract TOutput Visit(IRel other, TContext context);
}
