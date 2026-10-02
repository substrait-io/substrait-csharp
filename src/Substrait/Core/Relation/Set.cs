// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Protobuf;
using Substrait.Tools;
using static Substrait.Core.Type.IType;
using ParameterizedType = Substrait.Core.Type.ParameterizedType;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the SET relational operator representing (multi)set semantics, <see cref="Protobuf.SetRel"/>.
/// </summary>
public sealed class Set : NInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Set"/> class.
    /// </summary>
    /// <param name="setOp">Set operation.</param>
    /// <param name="inputs">Input relations.</param>
    public Set(SetOp setOp, IEnumerable<IRel> inputs)
    {
        this.SetOperation = setOp;
        this.Inputs = inputs.ToImmutableList();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Set"/> class.
    /// </summary>
    /// <param name="setOp">Set operation.</param>
    /// <param name="inputs">Input relations.</param>
    /// <param name="transmute">The remap to apply on the output.</param>
    public Set(SetOp setOp, IEnumerable<IRel> inputs, Remap? transmute)
      : this(setOp, inputs)
    {
        this.Metadata = RelationMetadata.FromRemap(transmute);
    }

    /// <summary>Initializes a set operation with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="setOp">Set operation.</param>
    /// <param name="inputs">Input relations.</param>
    public Set(RelationMetadata metadata, SetOp setOp, IEnumerable<IRel> inputs)
        : this(setOp, inputs)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>
    /// Set operation.
    /// </summary>
    public enum SetOp
    {
        /// <summary>
        /// <see cref="SetRel.Types.SetOp.Unspecified"/>.
        /// </summary>
        Unspecified = SetRel.Types.SetOp.Unspecified,

        /// <summary>
        /// <see cref="SetRel.Types.SetOp.MinusPrimary"/>.
        /// </summary>
        MinusPrimary = SetRel.Types.SetOp.MinusPrimary,

        /// <summary>
        /// <see cref="SetRel.Types.SetOp.MinusPrimaryAll"/>.
        /// </summary>
        MinusPrimaryAll = SetRel.Types.SetOp.MinusPrimaryAll,

        /// <summary>
        /// <see cref="SetRel.Types.SetOp.MinusMultiset"/>.
        /// </summary>
        MinusMultiSet = SetRel.Types.SetOp.MinusMultiset,

        /// <summary>
        /// <see cref="SetRel.Types.SetOp.IntersectionPrimary"/>.
        /// </summary>
        IntersectionPrimary = SetRel.Types.SetOp.IntersectionPrimary,

        /// <summary>
        /// <see cref="SetRel.Types.SetOp.IntersectionMultiset"/>.
        /// </summary>
        IntersectionMultiset = SetRel.Types.SetOp.IntersectionMultiset,

        /// <summary>
        /// <see cref="SetRel.Types.SetOp.IntersectionMultisetAll"/>.
        /// </summary>
        IntersectionMultisetAll = SetRel.Types.SetOp.IntersectionMultisetAll,

        /// <summary>
        /// <see cref="SetRel.Types.SetOp.UnionDistinct"/>.
        /// </summary>
        UnionDistinct = SetRel.Types.SetOp.UnionDistinct,

        /// <summary>
        /// <see cref="SetRel.Types.SetOp.UnionAll"/>.
        /// </summary>
        UnionAll = SetRel.Types.SetOp.UnionAll,
    }

    /// <summary>
    /// Gets set operation.
    /// </summary>
    public SetOp SetOperation { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; } = RelationMetadata.Direct;

    /// <inheritdoc/>
    public override IReadOnlyList<IRel> Inputs { get; }

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(nameof(Set), this.SetOperation);
    }

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return other is Set o && this.SetOperation == o.SetOperation;
    }

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType()
    {
        var typesBuilder = ImmutableList.CreateBuilder<IType>();

        switch (this.SetOperation)
        {
            case SetOp.MinusPrimary:
            case SetOp.MinusPrimaryAll:
            case SetOp.MinusMultiSet:
                typesBuilder.AddRange(this.Inputs[0].RecordType.Fields);
                break;

            case SetOp.IntersectionPrimary:
                {
                    // Intersect primary has a funky semantic and there is no direct semantically equivalent in SQL.
                    // In this mode, something is not nullable only if the primary field is not nullable and any of the secondaries are not nullable.
                    typesBuilder.AddRange(this.Inputs[0].RecordType.Fields.Select((x, i) => this.ResolveOutputType(i, NullableType.Required, requireFirst: true)));
                    break;
                }

            case SetOp.IntersectionMultiset:
            case SetOp.IntersectionMultisetAll:
                {
                    typesBuilder.AddRange(this.Inputs[0].RecordType.Fields.Select((x, i) => this.ResolveOutputType(i, NullableType.Required, requireFirst: false)));
                    break;
                }

            case SetOp.UnionDistinct:
            case SetOp.UnionAll:
                {
                    typesBuilder.AddRange(this.Inputs[0].RecordType.Fields.Select((x, i) => this.ResolveOutputType(i, NullableType.Nullable, requireFirst: false)));
                    break;
                }

            default:
                throw new NotImplementedException(this.SetOperation.ToString());
        }

        return TypeFactory.REQUIRED.Struct(typesBuilder.ToImmutable());
    }

    /// <summary>
    /// Resolves the output type of field at <paramref name="fieldIndex"/>.
    /// The nullability of output type is derived as if there is any "nullable|required", output type is "nullable|required".
    /// See <see href="https://substrait.io/relations/logical_relations/#set-operation-types">Substrait spec</see> for detail.
    /// </summary>
    /// <param name="fieldIndex">index of field to resolve type.</param>
    /// <param name="expectedNullable">desired nullability depending on set operation.</param>
    /// <param name="requireFirst">frist field must have expected nullable. This is to support non-SQL semantic that Substrait supports.</param>
    /// <returns>Resolved type.</returns>
    /// <exception cref="NotImplementedException">not impelmented.</exception>
    private IType ResolveOutputType(int fieldIndex, NullableType expectedNullable, bool requireFirst)
    {
        bool hasFirstExpectedNullable = this.Inputs[0].RecordType.Fields[fieldIndex].Nullable == expectedNullable;
        bool hasRestAnyExpectedNullable = this.Inputs.Skip(1).Any(x => x.RecordType.Fields[fieldIndex].Nullable == expectedNullable);
        bool hasExpectedNullable = requireFirst ? hasFirstExpectedNullable && hasRestAnyExpectedNullable : hasFirstExpectedNullable || hasRestAnyExpectedNullable;
        NullableType nullable = hasExpectedNullable ? expectedNullable : expectedNullable.Inverse();
        return TypeFactory.Of(nullable).ResolveTypeWithNullability(this.Inputs[0].RecordType.Fields[fieldIndex]);
    }
}
