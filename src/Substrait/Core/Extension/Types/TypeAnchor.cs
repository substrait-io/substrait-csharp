// SPDX-License-Identifier: Apache-2.0

namespace Substrait.Core.Extension.Types;

/// <summary>Identifies an extension type independently of its plan-local numeric anchor.</summary>
public sealed record TypeAnchor : IAnchor
{
    /// <summary>Initializes a type identity from its extension namespace and name.</summary>
    public TypeAnchor(string namespaceStr, string key)
    {
        if (string.IsNullOrWhiteSpace(namespaceStr))
        {
            throw new ArgumentException("A type namespace must not be empty.", nameof(namespaceStr));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A type name must not be empty.", nameof(key));
        }

        this.Namespace = namespaceStr;
        this.Key = key;
    }

    /// <inheritdoc/>
    public string Namespace { get; }

    /// <inheritdoc/>
    public string Key { get; }
}
