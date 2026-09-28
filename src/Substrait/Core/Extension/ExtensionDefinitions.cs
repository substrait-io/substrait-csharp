// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;

namespace Substrait.Core.Extension;

/// <summary>
/// Immutable implementation of a collection of definitions present in an extension file.
/// </summary>
public sealed class ExtensionDefinitions
{
    private readonly IReadOnlyList<TypeVariation> typeVariations;
    private readonly IReadOnlyList<ScalarFunction> scalarFunctions;
    private readonly IReadOnlyList<AggregateFunction> aggregateFunctions;
    private readonly IReadOnlyList<WindowFunction> windowFunctions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionDefinitions"/> class.
    /// </summary>
    /// <param name="typeVariations">Type variations.</param>
    /// <param name="scalarFunctions">Scalar functions.</param>
    /// <param name="aggregateFunctions">Aggregate functions.</param>
    /// <param name="windowFunctions">Window functions.</param>
    public ExtensionDefinitions(IEnumerable<TypeVariation> typeVariations, IEnumerable<ScalarFunction> scalarFunctions, IEnumerable<AggregateFunction> aggregateFunctions, IEnumerable<WindowFunction> windowFunctions)
    {
        this.typeVariations = typeVariations.ToImmutableList();
        this.scalarFunctions = scalarFunctions.ToImmutableList();
        this.aggregateFunctions = aggregateFunctions.ToImmutableList();
        this.windowFunctions = windowFunctions.ToImmutableList();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionDefinitions"/> class.
    /// This is only used for deserialization purposes from YAML files.
    /// </summary>
    public ExtensionDefinitions()
        : this([], [], [], [])
    {
    }

    /// <summary>Gets the extension's declared URN.</summary>
    public string Urn { get; init; } = string.Empty;

    /// <summary>
    /// Gets list of type variations.
    /// </summary>
    public IReadOnlyList<TypeVariation> TypeVariations
    {
        get => this.typeVariations;
        init => this.typeVariations = value.ToImmutableList();
    }

    /// <summary>
    /// Gets list of scalar functions.
    /// </summary>
    public IReadOnlyList<ScalarFunction> ScalarFunctions
    {
        get => this.scalarFunctions;
        init => this.scalarFunctions = value.ToImmutableList();
    }

    /// <summary>
    /// Gets list of aggregate functions.
    /// </summary>
    public IReadOnlyList<AggregateFunction> AggregateFunctions
    {
        get => this.aggregateFunctions;
        init => this.aggregateFunctions = value.ToImmutableList();
    }

    /// <summary>
    /// Gets list of window functions.
    /// </summary>
    public IReadOnlyList<WindowFunction> WindowFunctions
    {
        get => this.windowFunctions;
        init => this.windowFunctions = value.ToImmutableList();
    }

    /// <summary>
    /// Gets the aggregated count of all functions and types in this extension.
    /// </summary>
    /// <returns>The aggregated count of all functions and types.</returns>
    public int Size()
    {
        return this.TypeVariations.Count
            + this.ScalarFunctions.Count
            + this.AggregateFunctions.Count
            + this.WindowFunctions.Count;
    }

    /// <summary>
    /// Resolves the functions.
    /// </summary>
    /// <param name="uri">The URI on which functions should be resolved.</param>
    /// <returns>Returns the list of functions.</returns>
    public IEnumerable<FunctionImpl> Resolve(string uri)
    {
        return this.ScalarFunctions.SelectMany(function => function.Resolve(uri))
            .Concat<FunctionImpl>(this.AggregateFunctions.SelectMany(function => function.Resolve(uri)))
            .Concat<FunctionImpl>(this.WindowFunctions.SelectMany(function => function.Resolve(uri)));
    }
}
