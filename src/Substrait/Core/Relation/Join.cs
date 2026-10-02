// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Protobuf;
using Substrait.Tools;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the logical binary JOIN relational operator, <see cref="JoinRel"/>.
/// </summary>
public sealed class Join : AbstractJoin
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Join"/> class.
    /// </summary>
    /// <param name="left">Left input relation.</param>
    /// <param name="right">Right input relation.</param>
    /// <param name="type">Type of join.</param>
    /// <param name="condition">Join condition.</param>
    /// <param name="postJoinFilter">Post-join filter.</param>
    public Join(IRel left, IRel right, JoinType type, IExpression? condition, IExpression? postJoinFilter)
        : this(left, right, type, condition, postJoinFilter, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Join"/> class.
    /// </summary>
    /// <param name="left">Left input relation.</param>
    /// <param name="right">Right input relation.</param>
    /// <param name="type">Type of join.</param>
    /// <param name="condition">Join condition.</param>
    /// <param name="postJoinFilter">Post-join filter.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    public Join(IRel left, IRel right, JoinType type, IExpression? condition, IExpression? postJoinFilter, Remap? transmute)
        : base(left, right, type, postJoinFilter, transmute)
    {
        this.Condition = condition;
    }

    /// <summary>
    /// Gets join condition.
    /// </summary>
    public IExpression? Condition { get; }

    /// <summary>Initializes a logical join with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="left">Left input.</param>
    /// <param name="right">Right input.</param>
    /// <param name="type">Join type.</param>
    /// <param name="condition">Optional join condition.</param>
    /// <param name="postJoinFilter">Optional post-join filter.</param>
    public Join(RelationMetadata metadata, IRel left, IRel right, JoinType type, IExpression? condition, IExpression? postJoinFilter)
        : base(metadata, left, right, type, postJoinFilter)
    {
        this.Condition = condition;
    }

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return (other is Join o) && this.Type == o.Type && this.Condition.EqualsWithNull(o.Condition) && this.PostJoinFilter.EqualsWithNull(o.PostJoinFilter) && this.Transmute.EqualsWithNull(other.Transmute);
    }

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(nameof(Join), this.Type, this.Condition, this.PostJoinFilter, this.Transmute);
    }
}
