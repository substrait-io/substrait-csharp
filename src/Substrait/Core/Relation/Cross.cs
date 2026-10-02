// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Metadata;
using Substrait.Core.Type;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the CROSS relational operator representing cartesian product, <see cref="Protobuf.CrossRel"/>.
/// </summary>
public sealed class Cross : BiInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Cross"/> class.
    /// </summary>
    /// <param name="left">The left input relation.</param>
    /// <param name="right">The right input relation.</param>
    public Cross(IRel left, IRel right)
    {
        this.Left = left;
        this.Right = right;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Cross"/> class.
    /// </summary>
    /// <param name="left">The left input relation.</param>
    /// <param name="right">The right input relation.</param>
    /// <param name="transmute">The remap of the cross product output.</param>
    public Cross(IRel left, IRel right, Remap? transmute)
      : this(left, right)
    {
        this.Metadata = RelationMetadata.FromRemap(transmute);
    }

    /// <summary>Initializes a cross product with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="left">Left input.</param>
    /// <param name="right">Right input.</param>
    public Cross(RelationMetadata metadata, IRel left, IRel right)
        : this(left, right)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <inheritdoc/>
    public override IRel Left { get; }

    /// <inheritdoc/>
    public override IRel Right { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; } = RelationMetadata.Direct;

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return nameof(Cross).GetHashCode();
    }

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return other is Cross;
    }

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType()
    {
        var typesBuilder = ImmutableList.CreateBuilder<IType>();
        typesBuilder.AddRange(this.Left.RecordType.Fields);
        typesBuilder.AddRange(this.Right.RecordType.Fields);
        return TypeFactory.REQUIRED.Struct(typesBuilder);
    }
}
