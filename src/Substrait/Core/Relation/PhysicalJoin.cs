// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Protobuf;

namespace Substrait.Core.Relation;

/// <summary>
/// An abstract physical binary JOIN relational operator, <see cref="JoinRel"/>.
/// </summary>
public abstract class PhysicalJoin : AbstractJoin
{
    /// <summary>Initializes a physical join.</summary>
    /// <param name="left">Left input.</param>
    /// <param name="right">Right input.</param>
    /// <param name="type">Join type.</param>
    /// <param name="postJoinFilter">Optional post-join filter.</param>
    /// <param name="transmute">Output mapping.</param>
    public PhysicalJoin(IRel left, IRel right, JoinType type, IExpression? postJoinFilter, Remap? transmute)
        : base(left, right, type, postJoinFilter, transmute)
    {
    }

    /// <summary>Initializes a physical join with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="left">Left input.</param>
    /// <param name="right">Right input.</param>
    /// <param name="type">Join type.</param>
    /// <param name="postJoinFilter">Optional post-join filter.</param>
    protected PhysicalJoin(RelationMetadata metadata, IRel left, IRel right, JoinType type, IExpression? postJoinFilter)
        : base(metadata, left, right, type, postJoinFilter)
    {
    }

    /// <summary>
    /// Join key comparison.
    /// </summary>
    public sealed class ComparisonJoinKey : IEquatable<ComparisonJoinKey>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ComparisonJoinKey"/> class.
        /// </summary>
        /// <param name="left">Left join key.</param>
        /// <param name="right">Right join key.</param>
        /// <param name="comparison">Comparison.</param>
        public ComparisonJoinKey(FieldReference left, FieldReference right, ComparisonType comparison)
        {
            this.Left = left;
            this.Right = right;
            this.Comparison = comparison;
        }

        /// <summary>
        /// Simple join key comparison types.
        /// </summary>
        public enum SimpleComparisonType
        {
            /// <summary>
            /// Unspecified.
            /// </summary>
            Unspecified = Substrait.Protobuf.ComparisonJoinKey.Types.SimpleComparisonType.Unspecified,

            /// <summary>
            /// EQUAL.
            /// </summary>
            Eq = Substrait.Protobuf.ComparisonJoinKey.Types.SimpleComparisonType.Eq,

            /// <summary>
            /// IS NOT DISTINCT FROM.
            /// </summary>
            IsNotDistinctFrom = Substrait.Protobuf.ComparisonJoinKey.Types.SimpleComparisonType.IsNotDistinctFrom,

            /// <summary>
            /// Both not null and equal or one of them is NULL.
            /// </summary>
            MightEqual = Substrait.Protobuf.ComparisonJoinKey.Types.SimpleComparisonType.MightEqual,
        }

        /// <summary>
        /// Gets left join key field.
        /// </summary>
        public FieldReference Left { get; }

        /// <summary>
        /// Gets right join key field.
        /// </summary>
        public FieldReference Right { get; }

        /// <summary>
        /// Gets the comparison type.
        /// </summary>
        public ComparisonType Comparison { get; }

        /// <inheritdoc/>
        public bool Equals(ComparisonJoinKey? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other is not null && this.Comparison.Equals(other.Comparison) && this.Left.Equals(other.Left) && this.Right.Equals(other.Right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as ComparisonJoinKey);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.Left, this.Right, this.Comparison);
        }

        /// <summary>
        /// Join key comparison type.
        /// </summary>
        /// <param name="type">comparison type.</param>
        /// <param name="customFunctionReference">custom function reference anchor.</param>
        public sealed class ComparisonType(SimpleComparisonType type, uint customFunctionReference) : IEquatable<ComparisonType>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ComparisonType"/> class.
            /// </summary>
            /// <param name="type">comparison type.</param>
            public ComparisonType(SimpleComparisonType type)
                : this(type, 0)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="ComparisonType"/> class.
            /// </summary>
            /// <param name="customFunctionReference">custom function reference anchor.</param>
            public ComparisonType(uint customFunctionReference)
                : this(SimpleComparisonType.Unspecified, customFunctionReference)
            {
            }

            /// <summary>
            /// Gets simple comparison type.
            /// </summary>
            public SimpleComparisonType Simple => type;

            /// <summary>
            /// Gets custom comparison function reference anchor.
            /// </summary>
            public uint CustomFunctionReference => customFunctionReference;

            /// <inheritdoc/>
            public bool Equals(ComparisonType? other)
            {
                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                return other is not null && this.Simple == other.Simple && this.CustomFunctionReference == other.CustomFunctionReference;
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return this.Equals(obj as ComparisonType);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return HashCode.Combine(this.Simple, this.CustomFunctionReference);
            }
        }
    }
}
