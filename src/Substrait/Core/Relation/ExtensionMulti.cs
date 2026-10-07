// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Google.Protobuf;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Tools;

namespace Substrait.Core.Relation;

/// <summary>An immutable custom relation with ordered inputs and an opaque detail payload.</summary>
public sealed class ExtensionMulti : NInput
{
    /// <summary>Initializes a custom multi-input relation by snapshotting a protobuf message with direct output metadata.</summary>
    /// <param name="inputs">The ordered input relations. The extension contract determines the required count.</param>
    /// <param name="message">A non-null message to pack, or an already-packed Any to copy without repacking. Do not mutate concurrently with construction.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionMulti(IEnumerable<IRel> inputs, IMessage message, ParameterizedType.Struct? unmappedRecordType = null)
        : this(RelationMetadata.Direct, inputs, message, unmappedRecordType)
    {
    }

    /// <summary>Initializes a custom multi-input relation by snapshotting a protobuf message with explicit common metadata.</summary>
    /// <param name="metadata">Common metadata. Operator-level advanced extensions are not supported by this wire variant.</param>
    /// <param name="inputs">The ordered input relations. The extension contract determines the required count.</param>
    /// <param name="message">A non-null message to pack, or an already-packed Any to copy without repacking. Do not mutate concurrently with construction.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionMulti(RelationMetadata metadata, IEnumerable<IRel> inputs, IMessage message, ParameterizedType.Struct? unmappedRecordType = null)
        : this(metadata, inputs, ProtoUtils.SnapshotAny(message), unmappedRecordType)
    {
    }

    /// <summary>Initializes a custom multi-input relation with direct output metadata.</summary>
    /// <param name="inputs">The ordered input relations. The extension contract determines the required count.</param>
    /// <param name="detail">The opaque payload, or null when absent.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionMulti(IEnumerable<IRel> inputs, ReadOnlyAny? detail, ParameterizedType.Struct? unmappedRecordType = null)
        : this(RelationMetadata.Direct, inputs, detail, unmappedRecordType)
    {
    }

    /// <summary>Initializes a custom multi-input relation with explicit common metadata.</summary>
    /// <param name="metadata">Common metadata. Operator-level advanced extensions are not supported by this wire variant.</param>
    /// <param name="inputs">The ordered input relations. The extension contract determines the required count.</param>
    /// <param name="detail">The opaque payload, or null when absent.</param>
    /// <param name="unmappedRecordType">The output schema before emit, or null when unknown.</param>
    public ExtensionMulti(RelationMetadata metadata, IEnumerable<IRel> inputs, ReadOnlyAny? detail, ParameterizedType.Struct? unmappedRecordType = null)
    {
        this.Metadata = RelationMetadata.ValidateCommonOnly(metadata);
        this.Inputs = (inputs ?? throw new ArgumentNullException(nameof(inputs))).ToImmutableList();
        if (this.Inputs.Any(input => input is null))
        {
            throw new ArgumentException("Extension inputs must not contain null relations.", nameof(inputs));
        }

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
    public override IReadOnlyList<IRel> Inputs { get; }

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) =>
        visitor.Visit(this, context);

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other) => other is ExtensionMulti multi
        && Equals(this.Detail, multi.Detail) && Equals(this.UnmappedRecordType, multi.UnmappedRecordType);

    /// <inheritdoc/>
    public override int GetNodeHashCode() => HashCode.Combine(nameof(ExtensionMulti), this.Detail, this.UnmappedRecordType);

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType() => this.UnmappedRecordType
        ?? throw new ExtensionSchemaUnavailableException(ExtensionRelationKind.Multi, this.Detail?.TypeUrl);
}
