// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Relation;

namespace Substrait.Core.Plan;

/// <summary>
/// Interface for plan.
/// </summary>
public interface IPlan
{
    /// <summary>
    /// Interface for an entry in the ordered plan relation list.
    /// </summary>
    public interface IRelation
    {
        /// <summary>
        /// Gets the input relation.
        /// </summary>
        public IRel Input { get; }
    }

    /// <summary>
    /// Interface for a plan output root.
    /// </summary>
    public interface IRoot : IRelation
    {
        /// <summary>
        /// Gets the names.
        /// </summary>
        public IReadOnlyList<string> Names { get; }
    }

    /// <summary>
    /// Gets all entries in ordinal order, including roots and non-root relations.
    /// </summary>
    public IReadOnlyList<IRelation> Relations { get; }

    /// <summary>
    /// Gets the roots.
    /// </summary>
    public IReadOnlyList<IRoot> Roots { get; }

    /// <summary>
    /// Gets the version associated with the plan.
    /// </summary>
    public IVersion Version { get; }
}
