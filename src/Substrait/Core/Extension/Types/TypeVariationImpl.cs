// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Extension.Functions;
using Substrait.Core.Type;
using static Substrait.Tools.TypeUtils;

namespace Substrait.Core.Extension.Types;

/// <summary>
/// Type variation.
/// </summary>
/// <see href="https://substrait.io/types/type_variations/">Substrait type Variation documentation.</see>
/// <remarks>
/// Initializes a new instance of the <see cref="TypeVariationImpl"/> class.
/// </remarks>
public sealed class TypeVariationImpl : IEquatable<TypeVariationImpl>, ITypeVariation
{
    private readonly string uri;
    private readonly string parentType;
    private readonly string name;
    private readonly string description;
    private readonly FunctionBehavior behavior;

    private readonly Lazy<TypeVariationImplAnchor> anchor;

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeVariationImpl"/> class.
    /// This is only used for deserialization purpose from YAML files.
    /// </summary>
    public TypeVariationImpl()
        : this(string.Empty, string.Empty, string.Empty, string.Empty, FunctionBehavior.INHERITS)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeVariationImpl"/> class.
    /// </summary>
    /// <param name="uri">Uri.</param>
    /// <param name="parent">parent type name.</param>
    /// <param name="name">name.</param>
    /// <param name="description">description.</param>
    /// <param name="behavior">behavior of function.</param>
    public TypeVariationImpl(string uri, string parent, string name, string description, FunctionBehavior behavior)
    {
        this.uri = uri;
        this.parentType = parent;
        this.name = name;
        this.description = description;
        this.behavior = behavior;

        this.anchor = new Lazy<TypeVariationImplAnchor>(() =>
        {
            return new TypeVariationImplAnchor(this.uri, this.name);
        });
    }

    /// <summary>
    /// Gets the URI of the namespace.
    /// </summary>
    public string Uri { get => this.uri; init => this.uri = value; }

    /// <summary>
    /// Gets the parent type name.
    /// </summary>
    public string Parent { get => this.parentType; init => this.parentType = value; }

    /// <summary>
    /// Gets the base type name.
    /// </summary>
    public string BaseTypeName => this.parentType;

    /// <summary>
    /// Gets the name of type variation.
    /// </summary>
    public string Name { get => this.name; init => this.name = value; }

    /// <summary>
    /// Gets the description of the type variation.
    /// </summary>
    public string Description { get => this.description; init => this.description = value; }

    /// <summary>
    /// Gets the function behavior of this type variation.
    /// </summary>
    public FunctionBehavior FunctionBehavior { get => this.behavior; init => this.behavior = value; }

    /// <summary>
    /// Gets the anchor of this type variation.
    /// </summary>
    public TypeVariationImplAnchor Anchor => this.anchor.Value;

    /// <inheritdoc/>
    public string Namespace => this.uri;

    /// <inheritdoc/>
    public bool Equals(TypeVariationImpl? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null && this.Namespace.Equals(other.Namespace)
            && this.Name.Equals(other.Name)
            && this.BaseTypeName.Equals(other.BaseTypeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as TypeVariationImpl);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Namespace, this.BaseTypeName, this.Name);
    }

    /// <inheritdoc/>
    public bool IsCompatible(IType type)
    {
        return this.BaseTypeName.Equals(type.TypeName, StringComparison.OrdinalIgnoreCase)
            || (type is PrimitiveType.Time && this.BaseTypeName.Equals("precision_time", StringComparison.OrdinalIgnoreCase));
    }
}
