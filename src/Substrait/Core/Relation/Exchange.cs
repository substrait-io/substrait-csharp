// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Core.Type;

namespace Substrait.Core.Relation;

/// <summary>
/// The EXCHANGE relational operator representing data exchange, <see cref="Protobuf.ExchangeRel"/>.
/// </summary>
public abstract class Exchange : SingleInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Exchange"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="partitionCount">Number of partitions targeted for output.</param>
    public Exchange(IRel input, int partitionCount)
    {
        this.Input = input;
        this.PartitionCount = partitionCount;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Exchange"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="partitionCount">Number of partitions targeted for output.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    public Exchange(IRel input, int partitionCount, Remap? transmute)
      : this(input, partitionCount)
    {
        this.Metadata = RelationMetadata.FromRemap(transmute);
    }

    /// <summary>Initializes an exchange with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="input">Input relation.</param>
    /// <param name="partitionCount">Partition count.</param>
    protected Exchange(RelationMetadata metadata, IRel input, int partitionCount)
        : this(input, partitionCount)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <inheritdoc/>
    public override IRel Input { get; }

    /// <summary>
    /// Gets the number of partitions targeted for output.
    /// </summary>
    public int PartitionCount { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; } = RelationMetadata.Direct;

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType() => this.Input.RecordType;
}
