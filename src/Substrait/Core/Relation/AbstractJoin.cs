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
/// An abstract binary JOIN relational operator, <see cref="JoinRel"/>.
/// </summary>
public abstract class AbstractJoin : BiInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AbstractJoin"/> class.
    /// </summary>
    /// <param name="left">Left input relation.</param>
    /// <param name="right">Right input relation.</param>
    /// <param name="type">Type of join.</param>
    /// <param name="postJoinFilter">Post-join filter.</param>
    public AbstractJoin(IRel left, IRel right, JoinType type, IExpression? postJoinFilter)
        : this(left, right, type, postJoinFilter, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AbstractJoin"/> class.
    /// </summary>
    /// <param name="left">Left input relation.</param>
    /// <param name="right">Right input relation.</param>
    /// <param name="type">Type of join.</param>
    /// <param name="postJoinFilter">Post-join filter.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    public AbstractJoin(IRel left, IRel right, JoinType type, IExpression? postJoinFilter, Remap? transmute)
    {
        this.Left = left;
        this.Right = right;
        this.Type = type;
        this.PostJoinFilter = postJoinFilter;
        this.Metadata = RelationMetadata.FromRemap(transmute);
    }

    /// <summary>Initializes a join with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="left">Left input.</param>
    /// <param name="right">Right input.</param>
    /// <param name="type">Join type.</param>
    /// <param name="postJoinFilter">Optional post-join filter.</param>
    protected AbstractJoin(RelationMetadata metadata, IRel left, IRel right, JoinType type, IExpression? postJoinFilter)
        : this(left, right, type, postJoinFilter)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>
    /// Join type.
    /// </summary>
    public enum JoinType
    {
        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.Inner"/>.
        /// </summary>
        Inner = JoinRel.Types.JoinType.Inner,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.Outer"/>.
        /// </summary>
        Outer = JoinRel.Types.JoinType.Outer,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.Left"/>.
        /// </summary>
        Left = JoinRel.Types.JoinType.Left,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.Right"/>.
        /// </summary>
        Right = JoinRel.Types.JoinType.Right,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.LeftSemi"/>.
        /// </summary>
        LeftSemi = JoinRel.Types.JoinType.LeftSemi,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.LeftAnti"/>.
        /// </summary>
        LeftAnti = JoinRel.Types.JoinType.LeftAnti,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.LeftSingle"/>.
        /// </summary>
        LeftSingle = JoinRel.Types.JoinType.LeftSingle,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.LeftMark"/>.
        /// </summary>
        LeftMark = JoinRel.Types.JoinType.LeftMark,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.RightSemi"/>.
        /// </summary>
        RightSemi = JoinRel.Types.JoinType.RightSemi,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.RightAnti"/>.
        /// </summary>
        RightAnti = JoinRel.Types.JoinType.RightAnti,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.RightSingle"/>.
        /// </summary>
        RightSingle = JoinRel.Types.JoinType.RightSingle,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.RightMark"/>.
        /// </summary>
        RightMark = JoinRel.Types.JoinType.RightMark,

        /// <summary>
        /// <see cref="JoinRel.Types.JoinType.Unspecified"/>.
        /// </summary>
        Unspecified = JoinRel.Types.JoinType.Unspecified,
    }

    /// <inheritdoc/>
    public override IRel Left { get; }

    /// <inheritdoc/>
    public override IRel Right { get; }

    /// <summary>
    /// Gets post-join filter condition.
    /// </summary>
    public IExpression? PostJoinFilter { get; }

    /// <summary>
    /// Gets type of join.
    /// </summary>
    public JoinType Type { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; }

    /// <inheritdoc/>
    protected override Type.ParameterizedType.Struct DeriveRecordType()
    {
        var typesBuilder = ImmutableList.CreateBuilder<IType>();

        // Add fields from the left relation based on the join type.
        switch (this.Type)
        {
            case JoinType.Inner:
            case JoinType.Left:
            case JoinType.LeftSemi:
            case JoinType.LeftAnti:
            case JoinType.LeftSingle:
            case JoinType.LeftMark:
                // For certain join types (INNER, LEFT, etc.), left fields are unchanged.
                typesBuilder.AddRange(this.Left.RecordType.Fields);
                if (this.Type == JoinType.LeftMark)
                {
                    typesBuilder.Add(TypeFactory.NULLABLE.BOOL);
                }

                break;

            case JoinType.Right:
            case JoinType.Outer:
            case JoinType.RightSingle:
                // For RIGHT, OUTER, SINGLE joins, left fields can be nullable.
                typesBuilder.AddRange(this.Left.RecordType.Fields.Select(TypeFactory.NULLABLE.ResolveTypeWithNullability));
                break;

            case JoinType.RightSemi:
            case JoinType.RightAnti:
            case JoinType.RightMark:
                // For ANTI and SEMI joins, we don't include the right fields.
                break;

            default:
                throw new NotImplementedException(this.Type.ToString());
        }

        // Add fields from the right relation based on the join type.
        switch (this.Type)
        {
            case JoinType.Inner:
            case JoinType.Right:
            case JoinType.RightSemi:
            case JoinType.RightAnti:
            case JoinType.RightSingle:
            case JoinType.RightMark:
                // For certain join types (INNER, RIGHT), right fields are unchanged.
                typesBuilder.AddRange(this.Right.RecordType.Fields);
                if (this.Type == JoinType.RightMark)
                {
                    typesBuilder.Add(TypeFactory.NULLABLE.BOOL);
                }

                break;

            case JoinType.Left:
            case JoinType.Outer:
            case JoinType.LeftSingle:
                // For LEFT, OUTER, SINGLE joins, right fields can be nullable.
                typesBuilder.AddRange(this.Right.RecordType.Fields.Select(TypeFactory.NULLABLE.ResolveTypeWithNullability));
                break;

            case JoinType.LeftAnti:
            case JoinType.LeftSemi:
            case JoinType.LeftMark:
                // For ANTI and SEMI joins, we don't include the right fields.
                break;

            default:
                throw new NotImplementedException(this.Type.ToString());
        }

        return TypeFactory.REQUIRED.Struct(typesBuilder.ToImmutable());
    }
}
