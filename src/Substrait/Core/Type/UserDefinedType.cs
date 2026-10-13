// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Extension.Types;
using Substrait.Tools;

namespace Substrait.Core.Type;

/// <summary>An immutable extension-defined type with ordered, case-tagged parameters.</summary>
public sealed class UserDefinedType : IType, IEquatable<UserDefinedType>
{
    /// <summary>Initializes an extension-defined type, optionally attaching a registered definition.</summary>
    public UserDefinedType(
        TypeAnchor anchor,
        IEnumerable<TypeParameter> parameters,
        IType.NullableType nullable,
        ITypeVariation? typeVariation = null,
        TypeDefinition? declaration = null)
    {
        this.Anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
        this.Parameters = (parameters ?? throw new ArgumentNullException(nameof(parameters))).ToImmutableList();
        if (this.Parameters.Any(parameter => parameter is null))
        {
            throw new ArgumentException("Use TypeParameter.Null for an explicit null parameter.", nameof(parameters));
        }

        if (nullable is not (IType.NullableType.Required or IType.NullableType.Nullable))
        {
            throw new ArgumentOutOfRangeException(nameof(nullable), nullable, "User-defined types must specify nullability.");
        }

        if (declaration is not null && declaration.Anchor != anchor)
        {
            throw new ArgumentException("The definition must identify the same extension type.", nameof(declaration));
        }

        this.Nullable = nullable;
        this.TypeVariation = typeVariation;
        this.Declaration = declaration;
        if (typeVariation is not null && !typeVariation.IsCompatible(this))
        {
            throw new ArgumentException($"Type variation {typeVariation.Namespace}.{typeVariation.Name} requires {typeVariation.BaseTypeName} but got {this.TypeName}.", nameof(typeVariation));
        }
    }

    /// <summary>Gets the namespace and type name, independent of any plan-local anchor.</summary>
    public TypeAnchor Anchor { get; }

    /// <summary>Gets the ordered parameters, including explicit nulls.</summary>
    public IReadOnlyList<TypeParameter> Parameters { get; }

    /// <summary>Gets the registered definition, or null when constructed or resolved without one.</summary>
    public TypeDefinition? Declaration { get; }

    /// <inheritdoc/>
    public IType.NullableType Nullable { get; }

    /// <inheritdoc/>
    public ITypeVariation? TypeVariation { get; }

    /// <inheritdoc/>
    public IEnumerable<IType> InputNodes => this.Parameters.OfType<TypeParameter.DataType>().Select(parameter => parameter.Value);

    /// <inheritdoc/>
    public string ShortTypeName => $"u!{this.Anchor.Key}";

    /// <inheritdoc/>
    public string TypeName => this.Anchor.Key;

    /// <inheritdoc/>
    public string ToTypeString() => $"{this.Anchor.Namespace}#{this.ShortTypeName}<{string.Join(", ", this.Parameters)}>";

    /// <inheritdoc/>
    public TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

    /// <inheritdoc/>
    public bool NodeEquals(IType other, ITypeComparison comparison)
    {
        if (other is not UserDefinedType type || this.Anchor != type.Anchor
            || this.Parameters.Count != type.Parameters.Count
            || ((comparison & ITypeComparison.Nullability) != 0 && this.Nullable != type.Nullable)
            || ((comparison & ITypeComparison.TypeVariation) != 0 && !this.TypeVariation.EqualsWithNull(type.TypeVariation)))
        {
            return false;
        }

        bool compareParameterValues = (comparison & ITypeComparison.TypeParameter) != 0;
        for (int index = 0; index < this.Parameters.Count; ++index)
        {
            TypeParameter parameter = this.Parameters[index];
            TypeParameter compared = type.Parameters[index];
            if (parameter.GetType() != compared.GetType()
                || (compareParameterValues && parameter is not TypeParameter.DataType && !parameter.Equals(compared)))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public bool Equals(UserDefinedType? other) => TypeUtils.ITypeEqualityComparer.Of(ITypeComparison.Strict).Equals(this, other);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UserDefinedType other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => TypeUtils.ITypeEqualityComparer.Of(ITypeComparison.Strict).GetHashCode(this);
}
