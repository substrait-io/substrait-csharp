// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Tools.Visitor;

namespace Substrait.Core.Relation;

/// <summary>
/// Interface for all relational operators, <see cref="Protobuf.Rel"/>.
/// </summary>
public interface IRel : INavigableNode<IRel>
{
    /// <summary>Gets the immutable relation metadata. References have empty metadata.</summary>
    public RelationMetadata Metadata { get; }

    /// <summary>
    /// Gets transmute.
    /// </summary>
    public Remap? Transmute { get; }

    /// <summary>
    /// Gets record type.
    /// </summary>
    public ParameterizedType.Struct RecordType { get; }

    /// <summary>
    /// Gets input relations to this relation.
    /// </summary>
    public IReadOnlyList<IRel> Inputs { get; }

    /// <summary>
    /// Accepts the node visitor.
    /// </summary>
    /// <typeparam name="TContext">Context for the visitor implementation.</typeparam>
    /// <typeparam name="TOutput">Output for the visitor implementation.</typeparam>
    /// <param name="visitor">Node visitor.</param>
    /// <param name="context">Context object.</param>
    /// <returns>Returns the output object.</returns>
    public TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context);
}
