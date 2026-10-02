// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Tools;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the EXCHANGE relational operator for a scatter exchange.
/// </summary>
public sealed class ScatterExchange : Exchange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScatterExchange"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="partitionCount">Number of partitions targeted for output.</param>
    /// <param name="fields">Fields to scatter by.</param>
    public ScatterExchange(IRel input, int partitionCount, IEnumerable<FieldReference> fields)
        : base(input, partitionCount)
    {
        this.Fields = fields.ToImmutableList();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScatterExchange"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="partitionCount">Number of partitions targeted for output.</param>
    /// <param name="fields">Fields to scatter by.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    public ScatterExchange(IRel input, int partitionCount, IEnumerable<FieldReference> fields, Remap? transmute)
        : base(input, partitionCount, transmute)
    {
        this.Fields = fields.ToImmutableList();
    }

    /// <summary>
    /// Gets fields to scatter by.
    /// </summary>
    public IReadOnlyList<FieldReference> Fields { get; }

    /// <summary>Initializes a scatter exchange with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="input">Input relation.</param>
    /// <param name="partitionCount">Partition count.</param>
    /// <param name="fields">Fields to scatter by.</param>
    public ScatterExchange(RelationMetadata metadata, IRel input, int partitionCount, IEnumerable<FieldReference> fields)
        : base(metadata, input, partitionCount)
    {
        this.Fields = fields.ToImmutableList();
    }

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(nameof(ScatterExchange), this.PartitionCount, this.Fields.CombineHashCodes());
    }

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return other is ScatterExchange o && this.PartitionCount == o.PartitionCount && Enumerable.SequenceEqual(this.Fields, o.Fields);
    }
}
