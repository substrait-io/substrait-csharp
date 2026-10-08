// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Tools;
using static Substrait.Core.Extension.ExtensionsDictionary;

#if NET5_0_OR_GREATER
using StringReadOnlySet = System.Collections.Generic.IReadOnlySet<string>;
#else
using StringReadOnlySet = System.Collections.Immutable.ImmutableHashSet<string>;
#endif

namespace Substrait.Core.Extension;

/// <summary>
/// Immutable collection of definitions present in one or more extensions.
/// </summary>
public sealed class ExtensionsCollection
{
    private readonly IReadOnlyList<TypeDefinition> types;
    private readonly Lazy<IReadOnlyDictionary<TypeAnchor, TypeDefinition>> typesDictSupplier;
    private readonly IReadOnlyList<TypeVariationImpl> typeVariationImpls;
    private readonly IReadOnlyList<ScalarFunctionImpl> scalarFunctionImpls;
    private readonly IReadOnlyList<AggregateFunctionImpl> aggregateFunctionImpls;
    private readonly IReadOnlyList<WindowFunctionImpl> windowFunctionImpls;
    private readonly Lazy<StringReadOnlySet> namespaceSupplier;
    private readonly Lazy<IReadOnlyDictionary<TypeVariationImplAnchor, TypeVariationImpl>> typeVariationImplsDictSupplier;
    private readonly Lazy<IReadOnlyDictionary<FunctionImplAnchor, ScalarFunctionImpl>> scalarFunctionImplsDictSupplier;
    private readonly Lazy<IReadOnlyDictionary<FunctionImplAnchor, AggregateFunctionImpl>> aggregateFunctionImplsDictSupplier;
    private readonly Lazy<IReadOnlyDictionary<FunctionImplAnchor, WindowFunctionImpl>> windowFunctionImplsDictSupplier;

    /// <summary>
    /// Initializes an empty extension collection.
    /// </summary>
    public ExtensionsCollection()
        : this([], [], [], [])
    {
    }

    /// <summary>
    /// Initializes an extension collection.
    /// </summary>
    public ExtensionsCollection(
        IEnumerable<TypeVariationImpl> typeVariationImpls,
        IEnumerable<ScalarFunctionImpl> scalarFunctionImpls,
        IEnumerable<AggregateFunctionImpl> aggregateFunctionImpls,
        IEnumerable<WindowFunctionImpl> windowFunctionImpls)
        : this([], typeVariationImpls, scalarFunctionImpls, aggregateFunctionImpls, windowFunctionImpls)
    {
    }

    /// <summary>Initializes an extension collection including programmatically registered types.</summary>
    public ExtensionsCollection(
        IEnumerable<TypeDefinition> types,
        IEnumerable<TypeVariationImpl> typeVariationImpls,
        IEnumerable<ScalarFunctionImpl> scalarFunctionImpls,
        IEnumerable<AggregateFunctionImpl> aggregateFunctionImpls,
        IEnumerable<WindowFunctionImpl> windowFunctionImpls)
    {
        this.types = types.ToImmutableList();
        this.typesDictSupplier = new Lazy<IReadOnlyDictionary<TypeAnchor, TypeDefinition>>(
            () => this.types.ToDictionary(definition => definition.Anchor));
        this.typeVariationImpls = typeVariationImpls.ToImmutableList();
        this.scalarFunctionImpls = scalarFunctionImpls.ToImmutableList();
        this.aggregateFunctionImpls = aggregateFunctionImpls.ToImmutableList();
        this.windowFunctionImpls = windowFunctionImpls.ToImmutableList();
        this.namespaceSupplier = new Lazy<StringReadOnlySet>(() =>
            this.typeVariationImpls.Select(implementation => implementation.Uri)
                .Concat(this.types.Select(definition => definition.Anchor.Namespace))
                .Concat(this.scalarFunctionImpls.Select(implementation => implementation.Uri))
                .Concat(this.aggregateFunctionImpls.Select(implementation => implementation.Uri))
                .Concat(this.windowFunctionImpls.Select(implementation => implementation.Uri))
                .ToImmutableHashSet());
        this.typeVariationImplsDictSupplier = new Lazy<IReadOnlyDictionary<TypeVariationImplAnchor, TypeVariationImpl>>(
            () => CreateDictionary(this.typeVariationImpls, implementation => implementation.Anchor));
        this.scalarFunctionImplsDictSupplier = new Lazy<IReadOnlyDictionary<FunctionImplAnchor, ScalarFunctionImpl>>(
            () => CreateDictionary(this.scalarFunctionImpls, implementation => implementation.Anchor));
        this.aggregateFunctionImplsDictSupplier = new Lazy<IReadOnlyDictionary<FunctionImplAnchor, AggregateFunctionImpl>>(
            () => CreateDictionary(this.aggregateFunctionImpls, implementation => implementation.Anchor));
        this.windowFunctionImplsDictSupplier = new Lazy<IReadOnlyDictionary<FunctionImplAnchor, WindowFunctionImpl>>(
            () => CreateDictionary(this.windowFunctionImpls, implementation => implementation.Anchor));
    }

    /// <summary>Gets known extension type definitions.</summary>
    public IReadOnlyList<TypeDefinition> Types => this.types;

    /// <summary>Gets known type variation declarations.</summary>
    public IReadOnlyList<TypeVariationImpl> TypeVariationImpls => this.typeVariationImpls;

    /// <summary>Gets known scalar function declarations.</summary>
    public IReadOnlyList<ScalarFunctionImpl> ScalarFunctionImpls => this.scalarFunctionImpls;

    /// <summary>Gets known aggregate function declarations.</summary>
    public IReadOnlyList<AggregateFunctionImpl> AggregateFunctionImpls => this.aggregateFunctionImpls;

    /// <summary>Gets known window function declarations.</summary>
    public IReadOnlyList<WindowFunctionImpl> WindowFunctionImpls => this.windowFunctionImpls;

    /// <summary>Gets the total number of extension declarations.</summary>
    public int Count => this.types.Count + this.typeVariationImpls.Count + this.scalarFunctionImpls.Count + this.aggregateFunctionImpls.Count + this.windowFunctionImpls.Count;

    /// <summary>Resolves a type definition, throwing on unresolved definitions when TYPE checks are enabled.</summary>
    public bool TryGetType(TypeAnchor anchor, StrictMode strictMode, out TypeDefinition? type)
    {
        if (this.typesDictSupplier.Value.TryGetValue(anchor, out type))
        {
            return true;
        }

        if (strictMode.IsOn(StrictMode.TYPE))
        {
            this.CheckNamespace(anchor.Namespace);
            throw new ArgumentException($"Unexpected type with key {anchor.Key}. The namespace {anchor.Namespace} is loaded but no type with this key was found.");
        }

        return false;
    }

    /// <summary>Tries to resolve a type variation.</summary>
    public bool TryGetTypeVariation(TypeVariationImplAnchor anchor, StrictMode strictMode, out TypeVariationImpl? typeVariation)
    {
        if (this.typeVariationImplsDictSupplier.Value.TryGetValue(anchor, out typeVariation))
        {
            return true;
        }

        if (strictMode.IsOn(StrictMode.TYPE_VARIATION))
        {
            this.CheckNamespace(anchor.Namespace);
            throw new ArgumentException($"Unexpected type variation with key {anchor.Key}. The namespace {anchor.Namespace} is loaded but no type variation with this key was found.");
        }

        return false;
    }

    /// <summary>Tries to resolve a scalar function.</summary>
    public bool TryGetScalarFunction(FunctionImplAnchor anchor, StrictMode strictMode, out ScalarFunctionImpl? function)
    {
        return this.TryGetFunctionImpl(anchor, this.scalarFunctionImplsDictSupplier.Value, strictMode, out function);
    }

    /// <summary>Tries to resolve an aggregate function.</summary>
    public bool TryGetAggregateFunction(FunctionImplAnchor anchor, StrictMode strictMode, out AggregateFunctionImpl? function)
    {
        return this.TryGetFunctionImpl(anchor, this.aggregateFunctionImplsDictSupplier.Value, strictMode, out function);
    }

    /// <summary>Tries to resolve a window function.</summary>
    public bool TryGetWindowFunction(FunctionImplAnchor anchor, StrictMode strictMode, out WindowFunctionImpl? function)
    {
        return this.TryGetFunctionImpl(anchor, this.windowFunctionImplsDictSupplier.Value, strictMode, out function);
    }

    /// <summary>Merges another collection into this collection.</summary>
    /// <param name="extensionCollection">The collection to merge.</param>
    /// <returns>The merged collection.</returns>
    public ExtensionsCollection Merge(ExtensionsCollection extensionCollection)
    {
        return new ExtensionsCollection(
            this.types.Concat(extensionCollection.types),
            this.typeVariationImpls.Concat(extensionCollection.typeVariationImpls),
            this.scalarFunctionImpls.Concat(extensionCollection.scalarFunctionImpls),
            this.aggregateFunctionImpls.Concat(extensionCollection.aggregateFunctionImpls),
            this.windowFunctionImpls.Concat(extensionCollection.windowFunctionImpls));
    }

    private static IReadOnlyDictionary<TAnchor, TImplementation> CreateDictionary<TAnchor, TImplementation>(
        IEnumerable<TImplementation> implementations,
        Func<TImplementation, TAnchor> getAnchor)
        where TAnchor : notnull
    {
        ImmutableDictionary<TAnchor, TImplementation>.Builder dictionary = ImmutableDictionary.CreateBuilder<TAnchor, TImplementation>();
        foreach (TImplementation implementation in implementations)
        {
            try
            {
                dictionary.Add(getAnchor(implementation), implementation);
            }
            catch (NotSupportedException)
            {
                // Some extension type expressions are not supported by the parser yet.
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("Input extension file is malformed: a duplicate extension key was found.", ex);
            }
        }

        return dictionary.ToImmutable();
    }

    private bool TryGetFunctionImpl<TImplementation>(
        FunctionImplAnchor anchor,
        IReadOnlyDictionary<FunctionImplAnchor, TImplementation> dictionary,
        StrictMode strictMode,
        out TImplementation? implementation)
        where TImplementation : FunctionImpl
    {
        if (dictionary.TryGetValue(anchor, out implementation))
        {
            return true;
        }

        if (strictMode.IsOn(StrictMode.FUNCTION))
        {
            this.CheckNamespace(anchor.Namespace);
            throw new ArgumentException($"Unexpected {typeof(TImplementation).Name} with key {anchor.Key}. The namespace {anchor.Namespace} is loaded but no {typeof(TImplementation).Name} with this key found.");
        }

        return false;
    }

    private void CheckNamespace(string namespaceStr)
    {
        if (!this.namespaceSupplier.Value.Contains(namespaceStr))
        {
            throw new ArgumentException($"Received a reference for extension {namespaceStr} but that extension is not currently loaded.");
        }
    }
}
