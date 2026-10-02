// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System;
using Substrait.Core.Type;
using Substrait.Tools;
using Substrait.Tools.Visitor;

namespace Substrait.Core.Relation;

/// <summary>
/// Base class for all relational operators, <see cref="Protobuf.Rel"/>.
/// </summary>
public abstract class Rel : IRel, IEquatable<Rel>, INodeEquatable<IRel>
{
    private readonly Lazy<ParameterizedType.Struct> recordType;
    private readonly Lazy<int> hashCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="Rel"/> class.
    /// </summary>
    protected Rel()
    {
        this.recordType = new Lazy<ParameterizedType.Struct>(() =>
        {
            ParameterizedType.Struct s = this.DeriveRecordType();
            return this.Transmute is null ? s : this.Transmute.Transmute(s);
        });

        this.hashCode = new Lazy<int>(() =>
        {
            return new GetNodeHashCodeDispatcher().Dispatch(this, NoOpContext<IRel, int>.DEFAULT);
        });
    }

    /// <inheritdoc/>
    public abstract Remap? Transmute { get; }

    /// <inheritdoc/>
    public abstract IReadOnlyList<IRel> Inputs { get; }

    /// <inheritdoc/>
    public IEnumerable<IRel> InputNodes => this.Inputs;

    /// <inheritdoc/>
    public ParameterizedType.Struct RecordType { get => this.recordType.Value; }

    /// <inheritdoc/>
    public bool HasHashCode => this.hashCode.IsValueCreated;

    internal bool HasRecordType => this.recordType.IsValueCreated;

    /// <inheritdoc/>
    public abstract TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context);

    /// <inheritdoc/>
    public bool Equals(Rel? other)
    {
        return this.NodeEqualsImpl<IRel, NodeEqualsDispatcher>(other);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as Rel);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return this.hashCode.Value;
    }

    /// <inheritdoc/>
    public abstract int GetNodeHashCode();

    /// <inheritdoc/>
    public abstract bool NodeEquals(IRel other);

    /// <summary>
    /// Derived record type.
    /// </summary>
    /// <returns>The derived record type.</returns>
    protected abstract ParameterizedType.Struct DeriveRecordType();

    /// <summary>
    /// Dispatch NodeEquals for equality comparison.
    /// </summary>
    public sealed class NodeEqualsDispatcher : RelTopDownDispatcher<IEnumerator<IRel>, bool>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeEqualsDispatcher"/> class.
        /// </summary>
        public NodeEqualsDispatcher()
            : base(NodeEqualsVisitor.DEFAULT)
        {
        }

        /// <inheritdoc/>
        protected override bool ShouldBailOut(bool result, IEnumerator<IRel> context)
        {
            return !result;
        }

        /// <summary>
        /// NodeEquality visitor.
        /// </summary>
        private sealed class NodeEqualsVisitor : DefaultRelVisitor<IEnumerator<IRel>, bool>
        {
            /// <summary>
            /// Default instance.
            /// </summary>
            internal static readonly NodeEqualsVisitor DEFAULT = new();

            /// <inheritdoc/>
            protected override bool DefaultVisit(IRel rel, IEnumerator<IRel> context)
            {
                return context.MoveNext()
                    && context.Current is not null
                    && rel.Transmute.EqualsWithNull(context.Current.Transmute)
                    && rel switch
                    {
                        INodeEquatable<IRel> r => r.NodeEquals(context.Current),
                        _ => rel.Equals(context.Current),
                    };
            }
        }
    }

    /// <summary>
    /// Dispatch NodeHashes for equality comparison.
    /// </summary>
    public sealed class GetNodeHashCodeDispatcher : RelBottomUpDispatcher<NoOpContext<IRel, int>, int>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GetNodeHashCodeDispatcher"/> class.
        /// </summary>
        public GetNodeHashCodeDispatcher()
            : base(GetNodeHashCodeVisitor.DEFAULT, new GetNodeHashCodeTraversal())
        {
        }

        private sealed class GetNodeHashCodeTraversal : BottomUpTraversal<IRel>
        {
            protected override bool ShouldVisit(IRel node)
            {
                return node is INodeEquatable<IRel> n && !n.HasHashCode;
            }
        }

        /// <summary>
        /// GetNodeHashCode visitor.
        /// </summary>
        private sealed class GetNodeHashCodeVisitor : DefaultRelVisitor<NoOpContext<IRel, int>, int>
        {
            /// <summary>
            /// Default instance.
            /// </summary>
            internal static readonly GetNodeHashCodeVisitor DEFAULT = new();

            /// <inheritdoc/>
            protected override int DefaultVisit(IRel rel, NoOpContext<IRel, int> noContext)
            {
                return rel switch
                {
                    // The hash codes of input nodes have been generated (or ensured the hash code is already generated)
                    // by the bottom up traversal thus it won't trigger a deep recursive call.
                    INodeEquatable<IRel> r => HashCode.Combine(r.GetNodeHashCode(), rel.InputNodes.CombineHashCodes(), rel.Transmute),
                    _ => rel.GetHashCode(),
                };
            }
        }
    }
}
