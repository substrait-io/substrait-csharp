// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Protobuf;
using Substrait.Tools;

namespace Substrait.Core.Metadata;

/// <summary>Immutable plan-level extensions, declaration tables, and expected payload type URLs.</summary>
public sealed class PlanMetadata : IEquatable<PlanMetadata>
{
    /// <summary>Initializes metadata from an immutable facade and snapshots the expected type URLs.</summary>
    /// <param name="advancedExtensions">Plan-level extensions, or null to preserve absence.</param>
    /// <param name="expectedTypeUrls">Expected payload type URLs, or null for an empty list. Order and duplicates are preserved.</param>
    public PlanMetadata(ReadOnlyAdvancedExtension? advancedExtensions = null, IEnumerable<string>? expectedTypeUrls = null)
        : this(advancedExtensions, expectedTypeUrls, null, null)
    {
    }

    /// <summary>Initializes metadata with retained declaration tables, preserving their order and anchors.</summary>
    /// <param name="advancedExtensions">Plan-level extensions, or null to preserve absence.</param>
    /// <param name="expectedTypeUrls">Expected payload type URLs, or null for an empty list.</param>
    /// <param name="extensionUrns">Immutable URN declarations, or null for an empty table.</param>
    /// <param name="extensions">Immutable extension declarations, or null for an empty table.</param>
    public PlanMetadata(
        ReadOnlyAdvancedExtension? advancedExtensions,
        IEnumerable<string>? expectedTypeUrls,
        IEnumerable<ReadOnlySimpleExtensionURN>? extensionUrns,
        IEnumerable<ReadOnlySimpleExtensionDeclaration>? extensions)
    {
        this.AdvancedExtensions = advancedExtensions;
        this.ExpectedTypeUrls = expectedTypeUrls?.ToImmutableList() ?? ImmutableList<string>.Empty;
        this.ExtensionUrns = extensionUrns?.ToImmutableList() ?? ImmutableList<ReadOnlySimpleExtensionURN>.Empty;
        this.Extensions = extensions?.ToImmutableList() ?? ImmutableList<ReadOnlySimpleExtensionDeclaration>.Empty;
        if (this.ExpectedTypeUrls.Any(value => value is null))
        {
            throw new ArgumentException("Expected type URLs must not contain null values.", nameof(expectedTypeUrls));
        }

        if (this.ExtensionUrns.Any(value => value is null) || this.Extensions.Any(value => value is null))
        {
            throw new ArgumentException("Extension declaration tables must not contain null values.");
        }
    }

    /// <summary>Initializes metadata by snapshotting a protobuf advanced-extension message.</summary>
    /// <param name="message">The non-null message to copy. Do not mutate it concurrently with construction.</param>
    /// <param name="expectedTypeUrls">Expected payload type URLs, or null for an empty list. Order and duplicates are preserved.</param>
    public PlanMetadata(AdvancedExtension message, IEnumerable<string>? expectedTypeUrls = null)
        : this(ReadOnlyAdvancedExtension.FromProto(message ?? throw new ArgumentNullException(nameof(message))), expectedTypeUrls)
    {
    }

    /// <summary>Gets metadata with absent advanced extensions and no expected type URLs.</summary>
    public static PlanMetadata Empty { get; } = new();

    /// <summary>Gets the opaque plan-level extensions, or null when the message is absent.</summary>
    public ReadOnlyAdvancedExtension? AdvancedExtensions { get; }

    /// <summary>Gets the ordered expected type URLs without deduplication, inference, or validation of payload availability.</summary>
    public IReadOnlyList<string> ExpectedTypeUrls { get; }

    /// <summary>Gets retained URN declarations, including unused entries and aliases.</summary>
    public IReadOnlyList<ReadOnlySimpleExtensionURN> ExtensionUrns { get; }

    /// <summary>Gets retained extension declarations. Opaque payloads may reference any of their anchors.</summary>
    public IReadOnlyList<ReadOnlySimpleExtensionDeclaration> Extensions { get; }

    /// <inheritdoc/>
    public bool Equals(PlanMetadata? other) => other is not null
        && Equals(this.AdvancedExtensions, other.AdvancedExtensions)
        && this.ExpectedTypeUrls.SequenceEqual(other.ExpectedTypeUrls, StringComparer.Ordinal)
        && this.ExtensionUrns.SequenceEqual(other.ExtensionUrns)
        && this.Extensions.SequenceEqual(other.Extensions);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PlanMetadata other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        this.AdvancedExtensions, this.ExpectedTypeUrls.CombineHashCodes(),
        this.ExtensionUrns.CombineHashCodes(), this.Extensions.CombineHashCodes());

    internal static PlanMetadata Capture(Protobuf.Plan plan, bool owned) =>
        new(
            RelationMetadata.CaptureExtension(plan.AdvancedExtensions, owned),
            plan.ExpectedTypeUrls,
            plan.ExtensionUrns.Select(value => owned ? ReadOnlySimpleExtensionURN.FromOwnedProto(value) : ReadOnlySimpleExtensionURN.FromProto(value)),
            plan.Extensions.Select(value => owned ? ReadOnlySimpleExtensionDeclaration.FromOwnedProto(value) : ReadOnlySimpleExtensionDeclaration.FromProto(value)));
}
