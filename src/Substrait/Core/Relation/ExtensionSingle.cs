// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Tools;

namespace Substrait.Core.Relation;

/// <summary>An immutable custom relation with one input and an opaque detail payload.</summary>
public sealed class ExtensionSingle : SingleInput
{
    /// <summary>Initializes a custom single-input relation by snapshotting a protobuf message with direct output metadata.</summary>
    /// <param name="input">The input relation.</param>
    /// <param name="message">A non-null message to pack, or an already-packed Any to copy without repacking. Do not mutate concurrently with construction.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionSingle(IRel input, IMessage message, ParameterizedType.Struct? unmappedRecordType = null)
        : this(RelationMetadata.Direct, input, message, unmappedRecordType)
    {
    }

    /// <summary>Initializes a custom single-input relation by snapshotting a protobuf message with explicit common metadata.</summary>
    /// <param name="metadata">Common metadata. Operator-level advanced extensions are not supported by this wire variant.</param>
    /// <param name="input">The input relation.</param>
    /// <param name="message">A non-null message to pack, or an already-packed Any to copy without repacking. Do not mutate concurrently with construction.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionSingle(RelationMetadata metadata, IRel input, IMessage message, ParameterizedType.Struct? unmappedRecordType = null)
        : this(metadata, input, ProtoUtils.SnapshotAny(message), unmappedRecordType)
    {
    }

    /// <summary>Initializes a custom single-input relation with direct output metadata.</summary>
    /// <param name="input">The input relation.</param>
    /// <param name="detail">The opaque payload, or null when absent.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionSingle(IRel input, ReadOnlyAny? detail, ParameterizedType.Struct? unmappedRecordType = null)
        : this(RelationMetadata.Direct, input, detail, unmappedRecordType)
    {
    }

    /// <summary>Initializes a custom single-input relation with explicit common metadata.</summary>
    /// <param name="metadata">Common metadata. Operator-level advanced extensions are not supported by this wire variant.</param>
    /// <param name="input">The input relation.</param>
    /// <param name="detail">The opaque payload, or null when absent.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionSingle(RelationMetadata metadata, IRel input, ReadOnlyAny? detail, ParameterizedType.Struct? unmappedRecordType = null)
    {
        this.Metadata = RelationMetadata.ValidateCommonOnly(metadata);
        this.Input = input ?? throw new ArgumentNullException(nameof(input));
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
    public override IRel Input { get; }

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) =>
        visitor.Visit(this, context);

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other) => other is ExtensionSingle single
        && Equals(this.Detail, single.Detail) && Equals(this.UnmappedRecordType, single.UnmappedRecordType);

    /// <inheritdoc/>
    public override int GetNodeHashCode() => HashCode.Combine(nameof(ExtensionSingle), this.Detail, this.UnmappedRecordType);

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType() => this.UnmappedRecordType
        ?? throw new ExtensionSchemaUnavailableException(ExtensionRelationKind.Single, this.Detail?.TypeUrl);
}
