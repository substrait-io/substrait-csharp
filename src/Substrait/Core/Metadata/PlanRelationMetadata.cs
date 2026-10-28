// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Protobuf;

namespace Substrait.Core.Metadata;

/// <summary>
/// Immutable encoding metadata for one plan entry. Retained detached tables keep
/// references in opaque extension payloads valid, including references to unused entries.
/// </summary>
public sealed class PlanRelationMetadata : IEquatable<PlanRelationMetadata>
{
    private readonly ImmutableList<Protobuf.Expression> expressions;
    private readonly ImmutableList<Rel> relations;

    private PlanRelationMetadata(PlanRel entry, bool owned)
    {
        this.expressions = entry.DetachedExpressions.Select(value => owned ? value : value.Clone()).ToImmutableList();
        this.relations = entry.DetachedRels.Select(value => owned ? value : value.Clone()).ToImmutableList();
    }

    /// <summary>Gets metadata with no detached subtrees.</summary>
    public static PlanRelationMetadata Empty { get; } = new(new PlanRel(), owned: true);

    /// <summary>Gets the number of retained detached expressions.</summary>
    public int DetachedExpressionCount => this.expressions.Count;

    /// <summary>Gets the number of retained detached relations.</summary>
    public int DetachedRelCount => this.relations.Count;

    /// <summary>Snapshots the detached tables of a protobuf plan entry, without its root or relation.</summary>
    /// <param name="entry">The entry to copy. Do not mutate it concurrently with this call.</param>
    /// <returns>Immutable container-local encoding metadata.</returns>
    public static PlanRelationMetadata FromProto(PlanRel entry) =>
        Capture(entry ?? throw new ArgumentNullException(nameof(entry)), owned: false);

    /// <summary>Returns a mutable copy containing only the detached tables, with no root or relation set.</summary>
    /// <returns>A new protobuf plan entry with the original table order and contents.</returns>
    public PlanRel ToProto()
    {
        var result = new PlanRel();
        result.DetachedExpressions.AddRange(this.expressions.Select(value => value.Clone()));
        result.DetachedRels.AddRange(this.relations.Select(value => value.Clone()));
        return result;
    }

    /// <inheritdoc/>
    public bool Equals(PlanRelationMetadata? other) => other is not null
        && this.expressions.SequenceEqual(other.expressions)
        && this.relations.SequenceEqual(other.relations);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PlanRelationMetadata other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var expression in this.expressions)
        {
            hash.Add(expression);
        }

        foreach (Rel relation in this.relations)
        {
            hash.Add(relation);
        }

        return hash.ToHashCode();
    }

    internal static PlanRelationMetadata Capture(PlanRel entry, bool owned) =>
        entry.DetachedExpressions.Count == 0 && entry.DetachedRels.Count == 0 ? Empty : new(entry, owned);
}
