// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Google.Protobuf;
using Substrait.Core.Metadata;
using Substrait.Core.Relation;
using Substrait.Protobuf;
using Substrait.Tools;

namespace Substrait.Core.Plan;

/// <summary>
/// Composes a plan with append-only relations and editable metadata.
/// Every registration creates a distinct entry; sharing occurs only by reusing returned references.
/// Builder edits do not change previously built plans. Builders are not thread-safe.
/// </summary>
public sealed class PlanBuilder
{
    private readonly List<IPlan.IRelation> relations = new();
    private readonly Dictionary<uint, string> anchors = new();

    // Shared by this builder's references without retaining the builder itself.
    private readonly object owner = new();
    private readonly IVersion version;
    private PlanMetadata metadata;

    /// <summary>Initializes a builder.</summary>
    /// <param name="version">The plan version, or the current specification version.</param>
    public PlanBuilder(IVersion? version = null)
        : this(PlanMetadata.Empty, version)
    {
    }

    /// <summary>Initializes a builder with explicit immutable plan-level metadata.</summary>
    /// <param name="metadata">The initial metadata. Later edits do not change this snapshot.</param>
    /// <param name="version">The plan version, or null for the current specification version.</param>
    public PlanBuilder(PlanMetadata metadata, IVersion? version)
    {
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.version = version ?? Version.Current;
    }

    /// <summary>Gets the current immutable metadata snapshot, unaffected by later edits.</summary>
    public PlanMetadata Metadata => this.metadata;

    /// <summary>Replaces all advanced extensions with an immutable snapshot, retaining expected type URLs.</summary>
    /// <param name="extensions">The non-null advanced extensions.</param>
    public void SetAdvancedExtensions(ReadOnlyAdvancedExtension extensions) =>
        this.metadata = new(extensions ?? throw new ArgumentNullException(nameof(extensions)), this.metadata.ExpectedTypeUrls);

    /// <summary>Copies a protobuf advanced-extension message, retaining expected type URLs.</summary>
    /// <param name="message">The non-null message to copy. Do not mutate concurrently with this call.</param>
    public void SetAdvancedExtensions(AdvancedExtension message) =>
        this.SetAdvancedExtensions(ReadOnlyAdvancedExtension.FromProto(message ?? throw new ArgumentNullException(nameof(message))));

    /// <summary>Removes the entire advanced-extension message, retaining expected type URLs.</summary>
    public void ClearAdvancedExtensions()
    {
        if (this.metadata.AdvancedExtensions is not null)
        {
            this.metadata = new(expectedTypeUrls: this.metadata.ExpectedTypeUrls);
        }
    }

    /// <summary>Sets the enhancement, retaining optimizations and unknown fields.</summary>
    /// <param name="detail">The non-null immutable payload.</param>
    public void SetEnhancement(ReadOnlyAny detail) =>
        this.UpdateAdvancedExtensions(extensions => extensions.Enhancement =
            (detail ?? throw new ArgumentNullException(nameof(detail))).ToProto());

    /// <summary>Snapshots a message as the enhancement. An existing Any is copied without repacking.</summary>
    /// <param name="message">The non-null payload message. Do not mutate concurrently with this call.</param>
    public void SetEnhancement(IMessage message) => this.SetEnhancement(ProtoUtils.SnapshotAny(message));

    /// <summary>Removes the enhancement without removing an existing advanced-extension message.</summary>
    public void ClearEnhancement()
    {
        if (this.metadata.AdvancedExtensions?.Enhancement is not null)
        {
            this.UpdateAdvancedExtensions(extensions => extensions.Enhancement = null);
        }
    }

    /// <summary>Appends an optimization, preserving order and duplicates.</summary>
    /// <param name="detail">The non-null immutable payload.</param>
    public void AddOptimization(ReadOnlyAny detail) =>
        this.UpdateAdvancedExtensions(extensions => extensions.Optimization.Add(
            (detail ?? throw new ArgumentNullException(nameof(detail))).ToProto()));

    /// <summary>Snapshots and appends an optimization message. An existing Any is copied without repacking.</summary>
    /// <param name="message">The non-null payload message. Do not mutate concurrently with this call.</param>
    public void AddOptimization(IMessage message) => this.AddOptimization(ProtoUtils.SnapshotAny(message));

    /// <summary>Replaces the optimization at a zero-based index.</summary>
    /// <param name="index">The index of an existing optimization.</param>
    /// <param name="detail">The non-null immutable replacement payload.</param>
    public void ReplaceOptimization(int index, ReadOnlyAny detail) =>
        this.UpdateAdvancedExtensions(extensions => extensions.Optimization[index] =
            (detail ?? throw new ArgumentNullException(nameof(detail))).ToProto());

    /// <summary>Snapshots a message and replaces an optimization. An existing Any is copied without repacking.</summary>
    /// <param name="index">The index of an existing optimization.</param>
    /// <param name="message">The non-null replacement message. Do not mutate concurrently with this call.</param>
    public void ReplaceOptimization(int index, IMessage message) => this.ReplaceOptimization(index, ProtoUtils.SnapshotAny(message));

    /// <summary>Removes an optimization without removing an existing advanced-extension message.</summary>
    /// <param name="index">The zero-based index of an existing optimization.</param>
    public void RemoveOptimizationAt(int index) =>
        this.UpdateAdvancedExtensions(extensions => extensions.Optimization.RemoveAt(index));

    /// <summary>Removes all optimizations without removing an existing advanced-extension message.</summary>
    public void ClearOptimizations()
    {
        if (this.metadata.AdvancedExtensions is { } extensions && extensions.Optimization.Count != 0)
        {
            this.UpdateAdvancedExtensions(value => value.Optimization.Clear());
        }
    }

    /// <summary>Snapshots and replaces the ordered expected type URLs without inferring or deduplicating them.</summary>
    /// <param name="expectedTypeUrls">The non-null sequence, containing no null elements.</param>
    public void SetExpectedTypeUrls(IEnumerable<string> expectedTypeUrls) =>
        this.metadata = new(this.metadata.AdvancedExtensions, expectedTypeUrls ?? throw new ArgumentNullException(nameof(expectedTypeUrls)));

    /// <summary>Appends an expected type URL, preserving duplicates and empty strings.</summary>
    /// <param name="typeUrl">The non-null type URL.</param>
    public void AddExpectedTypeUrl(string typeUrl) =>
        this.SetExpectedTypeUrls(this.metadata.ExpectedTypeUrls.ToImmutableList().Add(typeUrl ?? throw new ArgumentNullException(nameof(typeUrl))));

    /// <summary>Replaces an expected type URL at a zero-based index.</summary>
    /// <param name="index">The index of an existing expected type URL.</param>
    /// <param name="typeUrl">The non-null replacement type URL.</param>
    public void ReplaceExpectedTypeUrl(int index, string typeUrl) =>
        this.SetExpectedTypeUrls(this.metadata.ExpectedTypeUrls.ToImmutableList().SetItem(index, typeUrl ?? throw new ArgumentNullException(nameof(typeUrl))));

    /// <summary>Removes an expected type URL at a zero-based index.</summary>
    /// <param name="index">The index of an existing expected type URL.</param>
    public void RemoveExpectedTypeUrlAt(int index) =>
        this.SetExpectedTypeUrls(this.metadata.ExpectedTypeUrls.ToImmutableList().RemoveAt(index));

    /// <summary>Removes all expected type URLs without changing advanced extensions.</summary>
    public void ClearExpectedTypeUrls() => this.SetExpectedTypeUrls(ImmutableList<string>.Empty);

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
    /// <returns>A plan unaffected by subsequent registrations or metadata edits.</returns>
    public Plan Build()
    {
        for (int ordinal = 0; ordinal < this.relations.Count; ++ordinal)
        {
            _ = PlanValidation.ValidateEntry(this.relations[ordinal].Input, ordinal, this.relations, this.owner);
        }

        return Plan.FromRelations(this.metadata, this.relations, this.version);
    }

    private void UpdateAdvancedExtensions(Action<AdvancedExtension> edit)
    {
        AdvancedExtension extensions = this.metadata.AdvancedExtensions?.ToProto() ?? new();
        edit(extensions);
        this.SetAdvancedExtensions(ReadOnlyAdvancedExtension.FromOwnedProto(extensions));
    }

    private Reference Add(IPlan.IRelation entry)
    {
        int ordinal = this.relations.Count;
        var entryAnchors = new Dictionary<uint, string>();
        _ = PlanValidation.ValidateEntry(entry.Input, ordinal, this.relations, this.owner, entryAnchors);
        var addedAnchors = new List<uint>(entryAnchors.Count);
        try
        {
            foreach (KeyValuePair<uint, string> anchor in entryAnchors)
            {
                PlanValidation.RegisterAnchor(anchor.Key, this.anchors, anchor.Value, message => new ArgumentException(message));
                addedAnchors.Add(anchor.Key);
            }
        }
        catch (ArgumentException)
        {
            foreach (uint anchor in addedAnchors)
            {
                this.anchors.Remove(anchor);
            }

            throw;
        }

        this.relations.Add(entry);
        return new Reference(ordinal, entry.Input, this.owner);
    }
}
