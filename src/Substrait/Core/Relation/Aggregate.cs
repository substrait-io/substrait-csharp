// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Tools;

namespace Substrait.Core.Relation;

/// <summary>
/// An immutable implementation of the AGGREGATE relational operator representing GROUP BY semantics, <see cref="Protobuf.AggregateRel"/>.
/// </summary>
public sealed class Aggregate : SingleInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Aggregate"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="groupingExpressions">Grouping expressions.</param>
    /// <param name="groupings">Groupings, i.e., each grouping is a list of references to grouping expressions.</param>
    /// <param name="measures">Aggregate measure expressions.</param>
    public Aggregate(IRel input, IEnumerable<IExpression> groupingExpressions, IEnumerable<Grouping> groupings, IEnumerable<Measure> measures)
    {
        this.Input = input;
        this.GroupingExpressions = groupingExpressions.ToImmutableList();
        this.Groupings = groupings.ToImmutableList();
        this.Measures = measures.ToImmutableList();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Aggregate"/> class.
    /// </summary>
    /// <param name="input">Input relation.</param>
    /// <param name="groupingExpressions">Grouping expressions.</param>
    /// <param name="groupings">Groupings, i.e., each grouping is a list of references to grouping expressions.</param>
    /// <param name="measures">Aggregate measure expressions.</param>
    /// <param name="transmute">The remap to apply to the output record.</param>
    public Aggregate(IRel input, IEnumerable<IExpression> groupingExpressions, IEnumerable<Grouping> groupings, IEnumerable<Measure> measures, Remap? transmute)
        : this(input, groupingExpressions, groupings, measures)
    {
        this.Metadata = RelationMetadata.FromRemap(transmute);
    }

    /// <summary>Initializes an aggregate with explicit metadata.</summary>
    /// <param name="metadata">Immutable metadata, including the output mapping.</param>
    /// <param name="input">Input relation.</param>
    /// <param name="groupingExpressions">Grouping expressions.</param>
    /// <param name="groupings">Grouping expression references.</param>
    /// <param name="measures">Aggregate measures.</param>
    public Aggregate(RelationMetadata metadata, IRel input, IEnumerable<IExpression> groupingExpressions, IEnumerable<Grouping> groupings, IEnumerable<Measure> measures)
        : this(input, groupingExpressions, groupings, measures)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <inheritdoc/>
    public override IRel Input { get; }

    /// <summary>
    /// Gets grouping expressions.
    /// </summary>
    public IReadOnlyList<IExpression> GroupingExpressions { get; }

    /// <summary>
    /// Gets groupings. Each grouping is a list of references to
    /// grouping expressions.
    /// </summary>
    public IReadOnlyList<Grouping> Groupings { get; }

    /// <summary>
    /// Gets measure expressions.
    /// </summary>
    public IReadOnlyList<Measure> Measures { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; } = RelationMetadata.Direct;

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public override int GetNodeHashCode()
    {
        return HashCode.Combine(
            nameof(Aggregate),
            this.Groupings.CombineHashCodes(),
            this.GroupingExpressions.CombineHashCodes(),
            this.Measures.CombineHashCodes());
    }

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other)
    {
        return other is Aggregate o
            && Enumerable.SequenceEqual(this.Groupings, o.Groupings)
            && Enumerable.SequenceEqual(this.GroupingExpressions, o.GroupingExpressions)
            && Enumerable.SequenceEqual(this.Measures, o.Measures);
    }

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType()
    {
        var typesBuilder = ImmutableList.CreateBuilder<IType>();
        typesBuilder.AddRange(this.GroupingExpressions.Select(g => g.Type));
        typesBuilder.AddRange(this.Measures.Select(m => m.Function.OutputType));
        return TypeFactory.REQUIRED.Struct(typesBuilder);
    }

    /// <summary>
    /// Grouping expression.
    /// </summary>
    public sealed class Grouping : IEquatable<Grouping>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Grouping"/> class.
        /// </summary>
        /// <param name="expressions">The list of references.</param>
        public Grouping(IEnumerable<int> expressions)
        {
            this.Expressions = expressions.ToImmutableList();
        }

        /// <summary>
        /// Gets grouping references.
        /// </summary>
        public IReadOnlyList<int> Expressions { get; }

        /// <inheritdoc/>
        public bool Equals(Grouping? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other is not null && Enumerable.SequenceEqual(this.Expressions, other.Expressions);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as Grouping);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return this.Expressions.CombineHashCodes();
        }
    }

    /// <summary>
    /// Measure expression.
    /// </summary>
    public sealed class Measure : IEquatable<Measure>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Measure"/> class.
        /// </summary>
        /// <param name="function">The measure function invocation.</param>
        /// <param name="preMeasureFilter">The filter to apply before the aggregate function is applied.</param>
        public Measure(AggregateFunctionInvocation function, IExpression? preMeasureFilter)
        {
            this.Function = function;
            this.PreMeasureFilter = preMeasureFilter;
        }

        /// <summary>
        /// Gets the pre-measure filter (optional).
        /// </summary>
        public IExpression? PreMeasureFilter { get; }

        /// <summary>
        /// Gets the measure function invocation.
        /// </summary>
        public AggregateFunctionInvocation Function { get; }

        /// <inheritdoc/>
        public bool Equals(Measure? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Function.Equals(other?.Function) && this.PreMeasureFilter.EqualsWithNull(other.PreMeasureFilter);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as Measure);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.Function.GetHashCode(), this.PreMeasureFilter);
        }
    }
}
