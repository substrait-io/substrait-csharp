// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Relation;
using Substrait.Protobuf;

namespace Substrait.Core.Metadata;

/// <summary>Immutable common and operator-level metadata, preserving their distinct wire locations.</summary>
public sealed class RelationMetadata : IEquatable<RelationMetadata>
{
    /// <summary>Initializes metadata from immutable facades. A null common preserves absence.</summary>
    /// <param name="common">Common fields, including output mapping and relation anchor.</param>
    /// <param name="advancedExtension">The operator-level advanced extension, separate from common extensions.</param>
    public RelationMetadata(ReadOnlyRelCommon? common = null, ReadOnlyAdvancedExtension? advancedExtension = null)
    {
        this.Common = common;
        this.AdvancedExtension = advancedExtension;
        this.Transmute = common?.Emit is { } emit ? new Remap(emit.OutputMapping) : null;
    }

    /// <summary>Gets metadata with no common or operator-level fields.</summary>
    public static RelationMetadata Empty { get; } = new();

    /// <summary>Gets explicit direct output metadata, used by legacy relation constructors.</summary>
    public static RelationMetadata Direct { get; } = new(
        ReadOnlyRelCommon.FromOwnedProto(new RelCommon { Direct = new() }));

    /// <summary>Gets the common fields, or null when the common message is absent.</summary>
    public ReadOnlyRelCommon? Common { get; }

    /// <summary>Gets the operator-level advanced extension.</summary>
    public ReadOnlyAdvancedExtension? AdvancedExtension { get; }

    /// <summary>Gets the immutable mapping derived from the common emit field.</summary>
    public Remap? Transmute { get; }

    /// <summary>Creates explicit direct or emit metadata from a legacy output mapping.</summary>
    /// <param name="transmute">The mapping, or null for direct output.</param>
    /// <returns>Metadata containing the corresponding emit kind.</returns>
    public static RelationMetadata FromRemap(Remap? transmute) => transmute is null
        ? Direct
        : new(ReadOnlyRelCommon.FromOwnedProto(new RelCommon { Emit = new() { OutputMapping = { transmute.Indices } } }));

    /// <inheritdoc/>
    public bool Equals(RelationMetadata? other) =>
        other is not null && Equals(this.Common, other.Common) && Equals(this.AdvancedExtension, other.AdvancedExtension);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RelationMetadata other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Common, this.AdvancedExtension);

    internal static RelationMetadata ValidateCommonOnly(RelationMetadata metadata)
    {
        _ = metadata ?? throw new ArgumentNullException(nameof(metadata));
        if (metadata.AdvancedExtension is not null)
        {
            throw new ArgumentException("Extension relations support common metadata but have no operator-level advanced_extension field.", nameof(metadata));
        }

        return metadata;
    }

    internal static RelationMetadata Capture(RelCommon? common, AdvancedExtension? advancedExtension, bool owned) => new(
        common is null ? null : owned ? ReadOnlyRelCommon.FromOwnedProto(common) : ReadOnlyRelCommon.FromProto(common),
        CaptureExtension(advancedExtension, owned));

    internal static ReadOnlyAdvancedExtension? CaptureExtension(AdvancedExtension? value, bool owned) =>
        value is null ? null : owned ? ReadOnlyAdvancedExtension.FromOwnedProto(value) : ReadOnlyAdvancedExtension.FromProto(value);
}
