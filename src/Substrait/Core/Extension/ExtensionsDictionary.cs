// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Protobuf;

namespace Substrait.Core.Extension;

/// <summary>
/// An immutable dictionary that resolves references to extension definitions within a plan.
/// </summary>
public sealed class ExtensionsDictionary
{
    private readonly IReadOnlyDictionary<int, TypeVariationImplAnchor> typeVariationAnchorMap;
    private readonly IReadOnlyDictionary<int, FunctionImplAnchor> functionAnchorMap;

    private ExtensionsDictionary(IEnumerable<KeyValuePair<int, TypeVariationImplAnchor>> typeVariationAnchorMap, IEnumerable<KeyValuePair<int, FunctionImplAnchor>> functionAnchorMap)
    {
        this.typeVariationAnchorMap = typeVariationAnchorMap.ToImmutableDictionary();
        this.functionAnchorMap = functionAnchorMap.ToImmutableDictionary();
    }

    /// <summary>
    /// Controls whether unresolved extension references cause an exception.
    /// </summary>
    [Flags]
    public enum StrictMode
    {
        /// <summary>Fully non-strict.</summary>
        OFF = 0,

        /// <summary>Strict function checks.</summary>
        FUNCTION = 1,

        /// <summary>Strict type checks.</summary>
        TYPE = 2,

        /// <summary>Strict type variation checks.</summary>
        TYPE_VARIATION = 4,

        /// <summary>All strict checks.</summary>
        STRICT = FUNCTION | TYPE | TYPE_VARIATION,
    }

    /// <summary>
    /// Gets a type variation anchor.
    /// </summary>
    /// <param name="reference">Type variation reference.</param>
    /// <returns>The type variation anchor.</returns>
    public TypeVariationImplAnchor GetTypeVariationAnchor(int reference)
    {
        return this.typeVariationAnchorMap.TryGetValue(reference, out TypeVariationImplAnchor? anchor)
            ? anchor
            : throw new ArgumentException($"Invalid type variation ID: {reference}. Verify that the ID is included in the plan's extensions section.");
    }

    /// <summary>
    /// Tries to get a type variation anchor.
    /// </summary>
    /// <param name="reference">Type variation reference.</param>
    /// <param name="anchor">Type variation anchor.</param>
    /// <returns>True when the anchor is known.</returns>
    public bool TryGetTypeVariationAnchor(int reference, out TypeVariationImplAnchor? anchor)
    {
        return this.typeVariationAnchorMap.TryGetValue(reference, out anchor);
    }

    /// <summary>
    /// Resolves a type variation declaration.
    /// </summary>
    public bool TryGetTypeVariation(TypeVariationImplAnchor anchor, ExtensionsCollection extensions, StrictMode strictMode, out TypeVariationImpl? typeVariation)
    {
        return extensions.TryGetTypeVariation(anchor, strictMode, out typeVariation);
    }

    /// <summary>
    /// Gets a function anchor.
    /// </summary>
    /// <param name="reference">Function reference.</param>
    /// <returns>The function anchor.</returns>
    public FunctionImplAnchor GetFunctionAnchor(int reference)
    {
        return this.functionAnchorMap.TryGetValue(reference, out FunctionImplAnchor? anchor)
            ? anchor
            : throw new ArgumentException($"Invalid function ID: {reference}. Verify that the ID is included in the plan's extensions section.");
    }

    /// <summary>Resolves a scalar function declaration.</summary>
    public bool TryGetScalarFunction(FunctionImplAnchor anchor, ExtensionsCollection extensions, StrictMode strictMode, out ScalarFunctionImpl? function)
    {
        return extensions.TryGetScalarFunction(anchor, strictMode, out function);
    }

    /// <summary>Resolves an aggregate function declaration.</summary>
    public bool TryGetAggregateFunction(FunctionImplAnchor anchor, ExtensionsCollection extensions, StrictMode strictMode, out AggregateFunctionImpl? function)
    {
        return extensions.TryGetAggregateFunction(anchor, strictMode, out function);
    }

    /// <summary>Resolves a window function declaration.</summary>
    public bool TryGetWindowFunction(FunctionImplAnchor anchor, ExtensionsCollection extensions, StrictMode strictMode, out WindowFunctionImpl? function)
    {
        return extensions.TryGetWindowFunction(anchor, strictMode, out function);
    }

    /// <summary>
    /// Builds an extension dictionary from protobuf declarations.
    /// </summary>
    public sealed class Builder
    {
        private readonly IDictionary<int, TypeVariationImplAnchor> typeVariationMap = new Dictionary<int, TypeVariationImplAnchor>();
        private readonly IDictionary<int, FunctionImplAnchor> functionMap = new Dictionary<int, FunctionImplAnchor>();

        /// <summary>Initializes an empty builder.</summary>
        public Builder()
            : this([], [])
        {
        }

        /// <summary>Initializes a builder from a protobuf plan.</summary>
        public Builder(Protobuf.Plan plan)
            : this(plan.ExtensionUrns, plan.Extensions)
        {
        }

        /// <summary>Initializes a builder from a protobuf extended expression.</summary>
        public Builder(ExtendedExpression extendedExpression)
            : this(extendedExpression.ExtensionUrns, extendedExpression.Extensions)
        {
        }

        private Builder(IEnumerable<SimpleExtensionURN> extensionUrns, IEnumerable<SimpleExtensionDeclaration> extensions)
        {
            IReadOnlyDictionary<int, string> namespaceMap = extensionUrns.ToImmutableDictionary(
                extension => (int)extension.ExtensionUrnAnchor,
                extension => extension.Urn);

            foreach (SimpleExtensionDeclaration extension in extensions)
            {
                if (extension.ExtensionTypeVariation is not null)
                {
                    SimpleExtensionDeclaration.Types.ExtensionTypeVariation typeVariation = extension.ExtensionTypeVariation;
                    string namespaceUri = GetNamespace(namespaceMap, (int)typeVariation.ExtensionUrnReference);
                    this.typeVariationMap.Add((int)typeVariation.TypeVariationAnchor, new TypeVariationImplAnchor(namespaceUri, typeVariation.Name));
                }
                else if (extension.ExtensionFunction is not null)
                {
                    SimpleExtensionDeclaration.Types.ExtensionFunction function = extension.ExtensionFunction;
                    string namespaceUri = GetNamespace(namespaceMap, (int)function.ExtensionUrnReference);
                    this.functionMap.Add((int)function.FunctionAnchor, new FunctionImplAnchor(namespaceUri, function.Name));
                }
            }
        }

        /// <summary>Builds the extension dictionary.</summary>
        /// <returns>The extension dictionary.</returns>
        public ExtensionsDictionary Build()
        {
            return new ExtensionsDictionary(this.typeVariationMap, this.functionMap);
        }

        private static string GetNamespace(IReadOnlyDictionary<int, string> namespaceMap, int reference)
        {
            return namespaceMap.TryGetValue(reference, out string? namespaceUri)
                ? namespaceUri
                : throw new ArgumentException($"Could not find extension URN of {reference}");
        }
    }
}
