// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Core.Type;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the FILTER relational operator, <see cref="Protobuf.FilterRel"/>.
/// </summary>
public sealed class Filter : SingleInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Filter"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="condition">Condition to apply.</param>
    public Filter(IRel input, IExpression condition)
    {
        this.Input = input;
        this.Condition = condition;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Filter"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="condition">Condition to apply.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    public Filter(IRel input, IExpression condition, Remap? transmute)
      : this(input, condition)
    {
        this.Metadata = RelationMetadata.FromRemap(transmute);
    }

    /// <summary>Initializes a filter with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="input">Input relation.</param>
    /// <param name="condition">Filter condition.</param>
    public Filter(RelationMetadata metadata, IRel input, IExpression condition)
        : this(input, condition)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <inheritdoc/>
    public override IRel Input { get; }

    /// <summary>
    /// Gets filter condition.
    /// </summary>
    public IExpression Condition { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; } = RelationMetadata.Direct;

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(nameof(Filter), this.Condition);
    }

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return other is Filter o && this.Condition.Equals(o.Condition);
    }

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType() => this.Input.RecordType;
}
