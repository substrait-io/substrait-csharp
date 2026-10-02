// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Tools;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the PROJECT relational operator representing calculated expressions of fields, <see cref="Protobuf.ProjectRel"/>.
/// </summary>
public sealed class Project : SingleInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Project"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="expressions">The list of expressions that the project will compute.</param>
    public Project(IRel input, IEnumerable<IExpression> expressions)
    {
        this.Input = input;
        this.Expressions = expressions.ToImmutableList();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Project"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="expressions">The list of expressions that the project will compute.</param>
    /// <param name="transmute">Remap to apply on the output.</param>
    public Project(IRel input, IEnumerable<IExpression> expressions, Remap? transmute)
      : this(input, expressions)
    {
        this.Metadata = RelationMetadata.FromRemap(transmute);
    }

    /// <summary>Initializes a project with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="input">Input relation.</param>
    /// <param name="expressions">Computed expressions.</param>
    public Project(RelationMetadata metadata, IRel input, IEnumerable<IExpression> expressions)
        : this(input, expressions)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <inheritdoc/>
    public override IRel Input { get; }

    /// <summary>
    /// Gets expressions.
    /// </summary>
    public IReadOnlyList<IExpression> Expressions { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; } = RelationMetadata.Direct;

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(nameof(Project), this.Expressions.CombineHashCodes());
    }

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return other is Project o && Enumerable.SequenceEqual(this.Expressions, o.Expressions);
    }

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType()
    {
        ParameterizedType.Struct initial = this.Input.RecordType;
        var typesBuilder = ImmutableList.CreateBuilder<IType>();
        typesBuilder.AddRange(initial.Fields);
        typesBuilder.AddRange(this.Expressions.Select(expr => expr.Type));
        return TypeFactory.Of(initial.Nullable).Struct(typesBuilder);
    }
}
