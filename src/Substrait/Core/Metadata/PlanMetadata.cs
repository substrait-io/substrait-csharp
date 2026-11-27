// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Type;
using Substrait.Protobuf;
using Substrait.Tools;

namespace Substrait.Core.Metadata;

/// <summary>Immutable plan-level extensions, declaration tables, type aliases, dynamic parameter bindings, and expected payload type URLs.</summary>
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
        : this(advancedExtensions, expectedTypeUrls, extensionUrns, extensions, null, null)
    {
    }

    /// <summary>Initializes metadata with retained declaration tables, type aliases, and dynamic parameter bindings.</summary>
    /// <param name="advancedExtensions">Plan-level extensions, or null to preserve absence.</param>
    /// <param name="expectedTypeUrls">Expected payload type URLs, or null for an empty list.</param>
    /// <param name="extensionUrns">Immutable URN declarations, or null for an empty table.</param>
    /// <param name="extensions">Immutable extension declarations, or null for an empty table.</param>
    /// <param name="typeAliases">Type alias definitions, or null for an empty table.</param>
    /// <param name="parameterBindings">Dynamic parameter bindings, or null for none. Order is preserved; anchors must be unique.</param>
    public PlanMetadata(
        ReadOnlyAdvancedExtension? advancedExtensions,
        IEnumerable<string>? expectedTypeUrls,
        IEnumerable<ReadOnlySimpleExtensionURN>? extensionUrns,
        IEnumerable<ReadOnlySimpleExtensionDeclaration>? extensions,
        TypeAliasTable? typeAliases,
        IEnumerable<DynamicParameterBinding>? parameterBindings)
    {
        this.AdvancedExtensions = advancedExtensions;
        this.ExpectedTypeUrls = expectedTypeUrls?.ToImmutableList() ?? ImmutableList<string>.Empty;
        this.ExtensionUrns = extensionUrns?.ToImmutableList() ?? ImmutableList<ReadOnlySimpleExtensionURN>.Empty;
        this.Extensions = extensions?.ToImmutableList() ?? ImmutableList<ReadOnlySimpleExtensionDeclaration>.Empty;
        this.TypeAliases = typeAliases ?? TypeAliasTable.Empty;
        this.ParameterBindings = parameterBindings?.ToImmutableList() ?? ImmutableList<DynamicParameterBinding>.Empty;
        if (this.ExpectedTypeUrls.Any(value => value is null))
        {
            throw new ArgumentException("Expected type URLs must not contain null values.", nameof(expectedTypeUrls));
        }

        if (this.ExtensionUrns.Any(value => value is null) || this.Extensions.Any(value => value is null))
        {
            throw new ArgumentException("Extension declaration tables must not contain null values.");
        }

        if (this.ParameterBindings.Any(value => value is null))
        {
            throw new ArgumentException("Dynamic parameter bindings must not contain null values.", nameof(parameterBindings));
        }

        var anchors = new HashSet<uint>();
        foreach (DynamicParameterBinding binding in this.ParameterBindings)
        {
            if (!anchors.Add(binding.ParameterAnchor))
            {
                throw new ArgumentException($"Duplicate dynamic parameter anchor {binding.ParameterAnchor}.", nameof(parameterBindings));
            }
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

    /// <summary>Gets the type alias definitions that alias references in the plan resolve against.</summary>
    public TypeAliasTable TypeAliases { get; }

    /// <summary>Gets the dynamic parameter bindings in their original order.</summary>
    public IReadOnlyList<DynamicParameterBinding> ParameterBindings { get; }

    /// <inheritdoc/>
    public bool Equals(PlanMetadata? other) => other is not null
        && Equals(this.AdvancedExtensions, other.AdvancedExtensions)
        && this.ExpectedTypeUrls.SequenceEqual(other.ExpectedTypeUrls, StringComparer.Ordinal)
        && this.ExtensionUrns.SequenceEqual(other.ExtensionUrns)
        && this.Extensions.SequenceEqual(other.Extensions)
        && this.TypeAliases.Equals(other.TypeAliases)
        && this.ParameterBindings.SequenceEqual(other.ParameterBindings);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PlanMetadata other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        this.AdvancedExtensions, this.ExpectedTypeUrls.CombineHashCodes(),
        this.ExtensionUrns.CombineHashCodes(), this.Extensions.CombineHashCodes(),
        this.TypeAliases, this.ParameterBindings.CombineHashCodes());

    internal static PlanMetadata Capture(Protobuf.Plan plan, bool owned, TypeAliasTable typeAliases, IEnumerable<DynamicParameterBinding> parameterBindings) =>
        new(
            RelationMetadata.CaptureExtension(plan.AdvancedExtensions, owned),
            plan.ExpectedTypeUrls,
            plan.ExtensionUrns.Select(value => owned ? ReadOnlySimpleExtensionURN.FromOwnedProto(value) : ReadOnlySimpleExtensionURN.FromProto(value)),
            plan.Extensions.Select(value => owned ? ReadOnlySimpleExtensionDeclaration.FromOwnedProto(value) : ReadOnlySimpleExtensionDeclaration.FromProto(value)),
            typeAliases,
            parameterBindings);
}
