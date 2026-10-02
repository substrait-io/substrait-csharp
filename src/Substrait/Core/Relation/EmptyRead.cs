// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Tools;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the READ relational operator for an empty read.
/// </summary>
public sealed class EmptyRead : Read
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmptyRead"/> class.
    /// </summary>
    /// <param name="initialSchema">The schema for the empty read.</param>
    /// <param name="filter">Filter expression to apply.</param>
    public EmptyRead(NamedStruct initialSchema, IExpression? filter)
    {
        this.InitialSchema = initialSchema;
        this.Filter = filter;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmptyRead"/> class.
    /// </summary>
    /// <param name="initialSchema">The schema for the empty read.</param>
    /// <param name="filter">Filter expression to apply.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    public EmptyRead(NamedStruct initialSchema, IExpression? filter, Remap? transmute)
      : this(initialSchema, filter)
    {
        this.Metadata = RelationMetadata.FromRemap(transmute);
    }

    /// <summary>Initializes an empty read with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="initialSchema">Read schema.</param>
    /// <param name="filter">Optional filter.</param>
    public EmptyRead(RelationMetadata metadata, NamedStruct initialSchema, IExpression? filter)
        : this(initialSchema, filter)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <inheritdoc/>
    public override NamedStruct InitialSchema { get; }

    /// <inheritdoc/>
    public override IExpression? Filter { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; } = RelationMetadata.Direct;

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(nameof(EmptyRead), this.InitialSchema, this.Filter);
    }

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return other is EmptyRead o && this.InitialSchema.Equals(o.InitialSchema) && this.Filter.EqualsWithNull(o.Filter);
    }
}
