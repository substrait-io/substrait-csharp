// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Tools;
using Struct = Substrait.Core.Expression.Expression.Struct;

namespace Substrait.Core.Relation;

/// <summary>
/// Immutable implementation of the READ relational operator for a virtual table, <see cref="Protobuf.ReadRel.VirtualTable"/>.
/// </summary>
public sealed class VirtualTableRead : Read
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualTableRead"/> class.
    /// </summary>
    /// <param name="initialSchema">The schema for the virtual table read.</param>
    /// <param name="rows">Rows of the virtual table.</param>
    /// <param name="filter">Filter expression to apply.</param>
    public VirtualTableRead(NamedStruct initialSchema, IEnumerable<Struct> rows, IExpression? filter)
    {
        this.InitialSchema = initialSchema;
        this.Rows = rows.ToImmutableList();
        this.Filter = filter;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualTableRead"/> class.
    /// </summary>
    /// <param name="initialSchema">The schema for the virtual table read.</param>
    /// <param name="rows">Rows of the virtual table.</param>
    /// <param name="filter">Filter expression to apply.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    public VirtualTableRead(NamedStruct initialSchema, IEnumerable<Struct> rows, IExpression? filter, Remap? transmute)
        : this(initialSchema, rows, filter)
    {
        this.Metadata = RelationMetadata.FromRemap(transmute);
    }

    /// <summary>Initializes a virtual read with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="initialSchema">Read schema.</param>
    /// <param name="rows">Virtual rows.</param>
    /// <param name="filter">Optional filter.</param>
    public VirtualTableRead(RelationMetadata metadata, NamedStruct initialSchema, IEnumerable<Struct> rows, IExpression? filter)
        : this(initialSchema, rows, filter)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <inheritdoc/>
    public override NamedStruct InitialSchema { get; }

    /// <summary>
    /// Gets rows.
    /// </summary>
    public IReadOnlyList<Struct> Rows { get; }

    /// <inheritdoc/>
    public override IExpression? Filter { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; } = RelationMetadata.Direct;

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(nameof(VirtualTableRead), this.Rows.CombineHashCodes(), this.InitialSchema, this.Filter);
    }

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return other is VirtualTableRead o && Enumerable.SequenceEqual(this.Rows, o.Rows) && this.InitialSchema.Equals(o.InitialSchema) && this.Filter.EqualsWithNull(o.Filter);
    }
}
