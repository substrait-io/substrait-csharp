// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Relation;

namespace Substrait.Core.Plan;

/// <summary>
/// Composes an append-only plan. Every registration creates a distinct entry;
/// sharing occurs only by reusing returned references.
/// </summary>
public sealed class PlanBuilder
{
    private readonly List<IPlan.IRelation> relations = new();

    // Shared by this builder's references without retaining the builder itself.
    private readonly object owner = new();
    private readonly IVersion version;

    /// <summary>Initializes a builder.</summary>
    /// <param name="version">The plan version, or the current specification version.</param>
    public PlanBuilder(IVersion? version = null)
    {
        this.version = version ?? Version.Current;
    }

    /// <summary>Registers a non-root subtree and returns its builder-owned reference.</summary>
    /// <param name="subtree">A subtree using only references already registered by this builder.</param>
    /// <returns>A reusable reference to the new entry.</returns>
    public Reference RegisterSubplan(IRel subtree) => this.Add(new Plan.Relation(subtree));

    /// <summary>Registers a separate output root and returns a reference to its input.</summary>
    /// <param name="input">The root input.</param>
    /// <param name="names">The root's output names.</param>
    /// <returns>A reusable reference to the root entry.</returns>
    public Reference AddRoot(IRel input, IEnumerable<string> names) => this.Add(new Plan.Root(input, names));

    /// <summary>Validates the graph and creates an immutable snapshot.</summary>
    /// <returns>A plan unaffected by subsequent registrations.</returns>
    public Plan Build()
    {
        for (int ordinal = 0; ordinal < this.relations.Count; ++ordinal)
        {
            _ = PlanValidation.ValidateEntry(this.relations[ordinal].Input, ordinal, this.relations, this.owner);
        }

        return Plan.FromRelations(this.relations, this.version);
    }

    private Reference Add(IPlan.IRelation entry)
    {
        int ordinal = this.relations.Count;
        _ = PlanValidation.ValidateEntry(entry.Input, ordinal, this.relations, this.owner);
        this.relations.Add(entry);
        return new Reference(ordinal, entry.Input, this.owner);
    }
}
