// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Metadata;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the EXCHANGE relational operator for a single bucket exchange.
/// </summary>
public sealed class SingleBucketExchange : Exchange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SingleBucketExchange"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="partitionCount">Number of partitions targeted for output.</param>
    /// <param name="expression">Expression that provides the bucket number.</param>
    public SingleBucketExchange(IRel input, int partitionCount, IExpression expression)
        : base(input, partitionCount)
    {
        this.Expression = expression;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleBucketExchange"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="partitionCount">Number of partitions targeted for output.</param>
    /// <param name="expression">Expression that provides the bucket number.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    public SingleBucketExchange(IRel input, int partitionCount, IExpression expression, Remap? transmute)
        : base(input, partitionCount, transmute)
    {
        this.Expression = expression;
    }

    /// <summary>
    /// Gets expression that provides the bucket number.
    /// </summary>
    public IExpression Expression { get; }

    /// <summary>Initializes a single-bucket exchange with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="input">Input relation.</param>
    /// <param name="partitionCount">Partition count.</param>
    /// <param name="expression">Bucket expression.</param>
    public SingleBucketExchange(RelationMetadata metadata, IRel input, int partitionCount, IExpression expression)
        : base(metadata, input, partitionCount)
    {
        this.Expression = expression;
    }

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(nameof(SingleBucketExchange), this.PartitionCount, this.Expression);
    }

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return other is SingleBucketExchange o && this.PartitionCount == o.PartitionCount && this.Expression.Equals(o.Expression);
    }
}
