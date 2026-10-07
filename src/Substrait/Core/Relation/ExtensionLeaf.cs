// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Tools;

namespace Substrait.Core.Relation;

/// <summary>An immutable custom relation with no inputs and an opaque detail payload.</summary>
public sealed class ExtensionLeaf : ZeroInput
{
    /// <summary>Initializes a custom leaf by snapshotting a protobuf message with direct output metadata.</summary>
    /// <param name="message">A non-null message to pack, or an already-packed Any to copy without repacking. Do not mutate concurrently with construction.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionLeaf(IMessage message, ParameterizedType.Struct? unmappedRecordType = null)
        : this(RelationMetadata.Direct, message, unmappedRecordType)
    {
    }

    /// <summary>Initializes a custom leaf by snapshotting a protobuf message with explicit common metadata.</summary>
    /// <param name="metadata">Common metadata. Operator-level advanced extensions are not supported by this wire variant.</param>
    /// <param name="message">A non-null message to pack, or an already-packed Any to copy without repacking. Do not mutate concurrently with construction.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionLeaf(RelationMetadata metadata, IMessage message, ParameterizedType.Struct? unmappedRecordType = null)
        : this(metadata, ProtoUtils.SnapshotAny(message), unmappedRecordType)
    {
    }

    /// <summary>Initializes a custom leaf with direct output metadata.</summary>
    /// <param name="detail">The opaque payload, or null when absent.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionLeaf(ReadOnlyAny? detail, ParameterizedType.Struct? unmappedRecordType = null)
        : this(RelationMetadata.Direct, detail, unmappedRecordType)
    {
    }

    /// <summary>Initializes a custom leaf with explicit common metadata.</summary>
    /// <param name="metadata">Common metadata. Operator-level advanced extensions are not supported by this wire variant.</param>
    /// <param name="detail">The opaque payload, or null when absent.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionLeaf(RelationMetadata metadata, ReadOnlyAny? detail, ParameterizedType.Struct? unmappedRecordType = null)
    {
        this.Metadata = RelationMetadata.ValidateCommonOnly(metadata);
        this.Detail = detail;
        this.UnmappedRecordType = unmappedRecordType;
    }

    /// <summary>Gets the immutable opaque payload, preserving absence.</summary>
    public ReadOnlyAny? Detail { get; }

    /// <summary>Gets the supplied output schema before emit, or null when unresolved.</summary>
    public ParameterizedType.Struct? UnmappedRecordType { get; }

    /// <inheritdoc/>
    public override RelationMetadata Metadata { get; }

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) =>
        visitor.Visit(this, context);

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other) => other is ExtensionLeaf leaf
        && Equals(this.Detail, leaf.Detail) && Equals(this.UnmappedRecordType, leaf.UnmappedRecordType);

    /// <inheritdoc/>
    public override int GetNodeHashCode() => HashCode.Combine(nameof(ExtensionLeaf), this.Detail, this.UnmappedRecordType);

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType() => this.UnmappedRecordType
        ?? throw new ExtensionSchemaUnavailableException(ExtensionRelationKind.Leaf, this.Detail?.TypeUrl);
}
