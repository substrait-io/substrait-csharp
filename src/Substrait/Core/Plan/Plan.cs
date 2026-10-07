// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Metadata;
using Substrait.Core.Relation;
using Substrait.Tools;
using static Substrait.Core.Plan.IPlan;

namespace Substrait.Core.Plan;

/// <summary>
/// An immutable implementation of plan defined in <see cref="IPlan"/>.
/// </summary>
public sealed class Plan : IPlan, IEquatable<Plan>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plan"/> class.
    /// </summary>
    /// <param name="roots">The roots of the plan.</param>
    /// <param name="version">The version of the plan.</param>
    public Plan(IEnumerable<IRoot> roots, IVersion version)
        : this(PlanMetadata.Empty, roots, version)
    {
    }

    /// <summary>Initializes a plan with explicit immutable metadata and output roots.</summary>
    /// <param name="metadata">The plan-level metadata.</param>
    /// <param name="roots">The roots of the plan.</param>
    /// <param name="version">The version of the plan.</param>
    public Plan(PlanMetadata metadata, IEnumerable<IRoot> roots, IVersion version)
        : this(metadata, roots.Cast<IRelation>().ToImmutableList(), version)
    {
    }

    private Plan(PlanMetadata metadata, ImmutableList<IRelation> relations, IVersion version)
    {
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        PlanValidation.Validate(relations);
        this.Relations = relations.Select(entry => entry is IRoot root
            ? (IRelation)new Root(root.Input, root.Names)
            : new Relation(entry.Input)).ToImmutableList();
        this.Roots = this.Relations.OfType<IRoot>().ToImmutableList();
        this.Version = version ?? throw new ArgumentNullException(nameof(version));
    }

    /// <summary>
    /// Creates a plan from ordered root and non-root entries. Reference ordinals index this list.
    /// </summary>
    /// <param name="relations">All entries in ordinal order.</param>
    /// <param name="version">The plan version.</param>
    /// <returns>An immutable, validated plan.</returns>
    public static Plan FromRelations(IEnumerable<IRelation> relations, IVersion version) =>
        FromRelations(PlanMetadata.Empty, relations, version);

    /// <summary>Creates a plan with explicit metadata and ordered root and non-root entries.</summary>
    /// <param name="metadata">The plan-level metadata.</param>
    /// <param name="relations">All entries in ordinal order.</param>
    /// <param name="version">The plan version.</param>
    /// <returns>An immutable, validated plan.</returns>
    public static Plan FromRelations(PlanMetadata metadata, IEnumerable<IRelation> relations, IVersion version) =>
        new(metadata, relations.ToImmutableList(), version);

    /// <inheritdoc/>
    public IReadOnlyList<IRelation> Relations { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IRoot> Roots { get; }

    /// <inheritdoc/>
    public IVersion Version { get; }

    /// <inheritdoc/>
    public PlanMetadata Metadata { get; }

    /// <inheritdoc/>
    public bool Equals(Plan? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null && Enumerable.SequenceEqual(this.Relations, other.Relations)
            && this.Version.Equals(other.Version) && this.Metadata.Equals(other.Metadata);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as Plan);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Relations.CombineHashCodes(), this.Version, this.Metadata);
    }

    /// <summary>
    /// A non-root plan entry, distinct from a root even when its output names are empty.
    /// </summary>
    public sealed class Relation : IRelation, IEquatable<Relation>
    {
        /// <summary>Initializes a non-root entry.</summary>
        /// <param name="input">The relation tree.</param>
        public Relation(IRel input)
        {
            this.Input = input ?? throw new ArgumentNullException(nameof(input));
        }

        /// <inheritdoc/>
        public IRel Input { get; }

        /// <inheritdoc/>
        public bool Equals(Relation? other) => other is not null && this.Input.Equals(other.Input);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => this.Equals(obj as Relation);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(nameof(Relation), this.Input);
    }

    /// <summary>
    /// An immutable implementation of root defined in <see cref="IRoot"/>.
    /// </summary>
    public sealed class Root : IRoot, IEquatable<Root>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Root"/> class.
        /// </summary>
        /// <param name="input">The input relation.</param>
        /// <param name="names">The output names for the relation.</param>
        public Root(IRel input, IEnumerable<string> names)
        {
            this.Input = input ?? throw new ArgumentNullException(nameof(input));
            this.Names = names.ToImmutableList();
        }

        /// <inheritdoc />
        public IRel Input { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> Names { get; }

        /// <inheritdoc />
        public bool Equals(Root? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other is not null && Enumerable.SequenceEqual(this.Names, other.Names) && this.Input.Equals(other.Input);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as Root);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(nameof(Root), this.Names.CombineHashCodes(), this.Input);
        }
    }
}
