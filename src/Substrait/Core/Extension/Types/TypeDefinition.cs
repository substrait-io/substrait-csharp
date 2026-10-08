// SPDX-License-Identifier: Apache-2.0

namespace Substrait.Core.Extension.Types;

/// <summary>
/// Registers a known extension type. Parameter contracts and YAML loading are not yet supported.
/// </summary>
public sealed class TypeDefinition
{
    /// <summary>Initializes a type definition with its extension identity.</summary>
    public TypeDefinition(TypeAnchor anchor)
    {
        this.Anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
    }

    /// <summary>Gets the extension identity.</summary>
    public TypeAnchor Anchor { get; }
}
