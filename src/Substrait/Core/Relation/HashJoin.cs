// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Protobuf;
using Substrait.Tools;
using ProtoBuildInput = Substrait.Protobuf.HashJoinRel.Types.BuildInput;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the binary HASH JOIN relational operator, <see cref="HashJoinRel"/>.
/// </summary>
/// <remarks>In Substrait, left is probe and right is build.</remarks>
public sealed class HashJoin : PhysicalJoin
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HashJoin"/> class.
    /// </summary>
    /// <param name="left">Left input relation.</param>
    /// <param name="right">Right input relation.</param>
    /// <param name="type">Type of join.</param>
    /// <param name="keys">Join key comparisons.</param>
    /// <param name="postJoinFilter">Post-join filter.</param>
    /// <param name="buildInput">Build input.</param>
    public HashJoin(IRel left, IRel right, JoinType type, IEnumerable<ComparisonJoinKey> keys, IExpression? postJoinFilter, BuildInput buildInput = BuildInput.Unspecified)
        : this(left, right, type, keys, postJoinFilter, null, buildInput)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HashJoin"/> class.
    /// </summary>
    /// <param name="left">Left input relation.</param>
    /// <param name="right">Right input relation.</param>
    /// <param name="type">Type of join.</param>
    /// <param name="keys">Join key comparisons.</param>
    /// <param name="postJoinFilter">Post-join filter.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    /// <param name="buildInput">Build input.</param>
    public HashJoin(IRel left, IRel right, JoinType type, IEnumerable<ComparisonJoinKey> keys, IExpression? postJoinFilter, Remap? transmute, BuildInput buildInput)
        : base(left, right, type, postJoinFilter, transmute)
    {
        this.Keys = keys.ToImmutableList();
        this.BuildLeft = buildInput == BuildInput.Left;
    }

    /// <summary>Initializes a hash join with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="left">Left input.</param>
    /// <param name="right">Right input.</param>
    /// <param name="type">Join type.</param>
    /// <param name="keys">Join keys.</param>
    /// <param name="postJoinFilter">Optional post-join filter.</param>
    /// <param name="buildInput">Build input.</param>
    public HashJoin(RelationMetadata metadata, IRel left, IRel right, JoinType type, IEnumerable<ComparisonJoinKey> keys, IExpression? postJoinFilter, BuildInput buildInput = BuildInput.Unspecified)
        : base(metadata, left, right, type, postJoinFilter)
    {
        this.Keys = keys.ToImmutableList();
        this.BuildLeft = buildInput == BuildInput.Left;
    }

    /// <summary>
    /// Hasjoin build input.
    /// </summary>
    public enum BuildInput
    {
        /// <summary>
        /// Unspecified.
        /// </summary>
        Unspecified = ProtoBuildInput.Unspecified,

        /// <summary>
        /// Build left child.
        /// </summary>
        Left = ProtoBuildInput.Left,

        /// <summary>
        /// Build right child (default).
        /// </summary>
        Right = ProtoBuildInput.Right,
    }

    /// <summary>
    /// Gets the build input.
    /// </summary>
    public IRel Build => this.BuildLeft ? this.Left : this.Right;

    /// <summary>
    /// Gets the probe input.
    /// </summary>
    public IRel Probe => this.BuildLeft ? this.Right : this.Left;

    /// <summary>
    /// Gets join keys.
    /// </summary>
    public IReadOnlyList<ComparisonJoinKey> Keys { get; }

    /// <summary>
    /// Gets a value indicating whether to build left input. True if we build left.
    /// </summary>
    public bool BuildLeft { get; }

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return (other is HashJoin o)
            && this.Type == o.Type
            && this.BuildLeft == o.BuildLeft
            && Enumerable.SequenceEqual(this.Keys, o.Keys)
            && this.Transmute.EqualsWithNull(other.Transmute)
            && this.PostJoinFilter.EqualsWithNull(o.PostJoinFilter);
    }

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(nameof(HashJoin), this.Type, this.BuildLeft, this.Keys.CombineHashCodes(), this.PostJoinFilter, this.Transmute);
    }
}
