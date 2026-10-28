// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Extension.Types;

namespace Substrait.Core.Type;

/// <summary>The unbound placeholder type (<c>Type.unbound</c>); carries no nullability or type variation.</summary>
public sealed class UnboundType : IType, IEquatable<UnboundType>
{
    /// <summary>Gets the single instance of the unbound placeholder type.</summary>
    public static readonly UnboundType Instance = new();

    private UnboundType()
    {
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">The unbound type has no nullability.</exception>
    public IType.NullableType Nullable => throw new NotSupportedException("The unbound type has no nullability.");

    /// <inheritdoc/>
    public ITypeVariation? TypeVariation => null;

    /// <inheritdoc/>
    public IEnumerable<IType> InputNodes => [];

    /// <inheritdoc/>
    public string ShortTypeName => "unbound";

    /// <inheritdoc/>
    public string TypeName => "unbound";

    /// <inheritdoc/>
    public string ToTypeString() => "unbound";

    /// <inheritdoc/>
    public TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public bool NodeEquals(IType other, ITypeComparison comparison) => other is UnboundType;

    /// <inheritdoc/>
    public bool Equals(UnboundType? other) => other is not null;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UnboundType;

    /// <inheritdoc/>
    public override int GetHashCode() => this.ShortTypeName.GetHashCode();
}
