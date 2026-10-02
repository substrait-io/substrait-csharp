// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Type;
using Substrait.Tools;

namespace Substrait.Core.Relation;

/// <summary>
/// A leaf referencing an entry in a plan's ordered relation list.
/// The target is a cross-tree link, not an input. Equality compares ordinals;
/// the containing plan compares the contents of all target entries.
/// </summary>
public sealed class Reference : ZeroInput
{
    /// <summary>
    /// Initializes an explicitly bound reference. Prefer <see cref="Plan.PlanBuilder"/>
    /// for composition; the containing plan validates bounds and target identity.
    /// </summary>
    /// <remarks>
    /// This creates an unowned reference for explicit plan assembly, not for registration with a builder.
    /// </remarks>
    /// <param name="subtreeOrdinal">The zero-based ordinal among all plan entries.</param>
    /// <param name="target">The input of the entry at that ordinal.</param>
    public Reference(int subtreeOrdinal, IRel target)
        : this(subtreeOrdinal, target, null)
    {
    }

    /// <summary>
    /// Initializes a reference for <see cref="Plan.PlanBuilder"/> with its ownership token.
    /// The public constructor delegates here with no owner for explicit plan assembly.
    /// </summary>
    /// <param name="subtreeOrdinal">The zero-based ordinal among all plan entries.</param>
    /// <param name="target">The input of the entry at that ordinal.</param>
    /// <param name="owner">The builder's identity token, or null for an unowned reference.</param>
    internal Reference(int subtreeOrdinal, IRel target, object? owner)
    {
        if (subtreeOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subtreeOrdinal), subtreeOrdinal, "Reference ordinal must be non-negative.");
        }

        this.SubtreeOrdinal = subtreeOrdinal;
        this.Target = target ?? throw new ArgumentNullException(nameof(target));
        this.Owner = owner;
    }

    /// <summary>Gets the target entry's ordinal among all plan entries.</summary>
    public int SubtreeOrdinal { get; }

    /// <summary>Gets the referenced input, without any root output-name overrides.</summary>
    public IRel Target { get; }

    /// <inheritdoc/>
    public override Remap? Transmute => null;

    /// <summary>
    /// Gets the originating builder's opaque identity token, or null for explicitly
    /// constructed and deserialized references.
    /// </summary>
    /// <remarks>
    /// A builder compares this token by reference identity to reject foreign or unowned references.
    /// It is not the builder itself, so a reference does not keep the builder and later entries alive.
    /// Ownership is composition-only metadata: it is neither serialized nor included in equality
    /// or hashing. Completed plans validate the ordinal and target binding independently of ownership.
    /// </remarks>
    internal object? Owner { get; }

    /// <inheritdoc/>
    public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) =>
        visitor.Visit(this, context);

    /// <inheritdoc/>
    public override bool NodeEquals(IRel other) => other is Reference reference && this.SubtreeOrdinal == reference.SubtreeOrdinal;

    /// <inheritdoc/>
    public override int GetNodeHashCode() => HashCode.Combine(nameof(Reference), this.SubtreeOrdinal);

    /// <inheritdoc/>
    protected override ParameterizedType.Struct DeriveRecordType()
    {
        // Warm dependency schemas bottom-up, including cross-tree links, without
        // recursively evaluating lazy schemas along a long reference chain.
        var stack = new Stack<(IRel Node, bool Complete)>();
        var active = new HashSet<IRel>(ReferenceEqualityComparer.Instance) { this };
        stack.Push((this.Target, false));
        while (stack.Count > 0)
        {
            var (node, complete) = stack.Pop();
            if (node is Rel { HasRecordType: true })
            {
                continue;
            }

            if (complete)
            {
                _ = node.RecordType;
                active.Remove(node);
                continue;
            }

            if (!active.Add(node))
            {
                throw new InvalidOperationException($"Cycle while deriving the schema of reference ordinal {this.SubtreeOrdinal}.");
            }

            stack.Push((node, true));
            if (node is Reference reference)
            {
                stack.Push((reference.Target, false));
            }
            else
            {
                foreach (IRel input in node.Inputs)
                {
                    stack.Push((input, false));
                }
            }
        }

        return this.Target.RecordType;
    }
}
