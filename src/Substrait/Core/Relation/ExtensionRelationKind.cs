// SPDX-License-Identifier: Apache-2.0

namespace Substrait.Core.Relation;

/// <summary>The wire variant of a custom relation.</summary>
public enum ExtensionRelationKind
{
    /// <summary>A relation with no inputs.</summary>
    Leaf,

    /// <summary>A relation with exactly one input.</summary>
    Single,

    /// <summary>A relation with an ordered list of inputs.</summary>
    Multi,
}
