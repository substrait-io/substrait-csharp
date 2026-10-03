// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Protobuf;
using Substrait.Tools;

namespace Substrait.Core.Metadata;

/// <summary>Immutable plan-level advanced extensions and expected payload type URLs.</summary>
public sealed class PlanMetadata : IEquatable<PlanMetadata>
{
    /// <summary>Initializes metadata from an immutable facade and snapshots the expected type URLs.</summary>
    /// <param name="advancedExtensions">Plan-level extensions, or null to preserve absence.</param>
    /// <param name="expectedTypeUrls">Expected payload type URLs, or null for an empty list. Order and duplicates are preserved.</param>
    public PlanMetadata(ReadOnlyAdvancedExtension? advancedExtensions = null, IEnumerable<string>? expectedTypeUrls = null)
    {
        this.AdvancedExtensions = advancedExtensions;
        this.ExpectedTypeUrls = expectedTypeUrls?.ToImmutableList() ?? ImmutableList<string>.Empty;
        if (this.ExpectedTypeUrls.Any(value => value is null))
        {
            throw new ArgumentException("Expected type URLs must not contain null values.", nameof(expectedTypeUrls));
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

    /// <inheritdoc/>
    public bool Equals(PlanMetadata? other) => other is not null
        && Equals(this.AdvancedExtensions, other.AdvancedExtensions)
        && this.ExpectedTypeUrls.SequenceEqual(other.ExpectedTypeUrls, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PlanMetadata other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.AdvancedExtensions, this.ExpectedTypeUrls.CombineHashCodes());

    internal static PlanMetadata Capture(AdvancedExtension? advancedExtensions, IEnumerable<string> expectedTypeUrls, bool owned) =>
        new(RelationMetadata.CaptureExtension(advancedExtensions, owned), expectedTypeUrls);
}
