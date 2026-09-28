// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Reflection;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Extensions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeDeserializers;
using static Substrait.Core.Extension.Functions.WindowFunctionImpl;

namespace Substrait.Tools;

/// <summary>
/// Utilities for loading simple extensions.
/// </summary>
public static class ExtensionUtils
{
    private static readonly string[] Namespaces =
    [
        "aggregate_approx",
        "aggregate_generic",
        "arithmetic",
        "boolean",
        "comparison",
        "datetime",
        "logarithmic",
        "rounding",
        "string",
    ];

    private static readonly Lazy<ExtensionsCollection> Defaults = new(LoadDefaultsInternal);

    /// <summary>
    /// Resolves an extension file path to a stream.
    /// </summary>
    public interface IExtensionFileResolver
    {
        /// <summary>Resolves an extension file.</summary>
        /// <param name="path">The path to resolve.</param>
        /// <returns>The extension file stream.</returns>
        Stream Resolve(string path);
    }

    /// <summary>Loads the standard extension collection.</summary>
    /// <returns>The standard extensions.</returns>
    public static ExtensionsCollection LoadDefaults()
    {
        return Defaults.Value;
    }

    /// <summary>Loads and merges extension files.</summary>
    /// <param name="extensionFiles">Extension files to load.</param>
    /// <param name="resolver">Resolver used to open each file.</param>
    /// <returns>The merged extension collection.</returns>
    public static ExtensionsCollection Load(IEnumerable<ExtensionFile> extensionFiles, IExtensionFileResolver resolver)
    {
        ExtensionsCollection[] collections = extensionFiles.Select(file =>
        {
            using Stream stream = resolver.Resolve(file.Path);
            return Load(file.Namespace, stream);
        }).ToArray();

        if (collections.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(extensionFiles));
        }

        return collections.Skip(1).Aggregate(collections[0], (current, collection) => current.Merge(collection));
    }

    /// <summary>Loads an extension collection from YAML.</summary>
    /// <param name="namespaceStr">The extension namespace, or an empty string to use the declared URN.</param>
    /// <param name="stream">The YAML stream.</param>
    /// <returns>The loaded extension collection.</returns>
    public static ExtensionsCollection Load(string namespaceStr, Stream stream)
    {
        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeDiscriminatingNodeDeserializer(options =>
            {
                options.AddUniqueKeyTypeDiscriminator<IArgument>(new Dictionary<string, Type>
                {
                    { "value", typeof(ValueArgument) },
                    { "type", typeof(TypeArgument) },
                    { "options", typeof(EnumArgument) },
                });
            })
            .WithTypeMapping<IVariadicBehavior, VariadicBehavior>()
            .WithTypeMapping<IOption, Option>()
            .WithNodeDeserializer(inner => new ReadOnlyCollectionDeserializer(inner), registration => registration.InsteadOf<ObjectNodeDeserializer>())
            .Build();

        using StreamReader reader = new(stream);
        ExtensionDefinitions? definitions = deserializer.Deserialize<ExtensionDefinitions>(reader);
        if (definitions is null)
        {
            throw new ArgumentException("Failed to load extension signatures from " + namespaceStr);
        }

        string resolvedNamespace = string.IsNullOrEmpty(namespaceStr) ? definitions.Urn : namespaceStr;
        if (string.IsNullOrEmpty(resolvedNamespace))
        {
            throw new ArgumentException("An extension must declare a URN or be loaded with an explicit namespace.");
        }

        return BuildExtensionCollection(resolvedNamespace, definitions);
    }

    /// <summary>Builds a resolved extension collection.</summary>
    /// <param name="namespaceStr">The extension namespace.</param>
    /// <param name="extensionSignatures">The parsed extension signatures.</param>
    /// <returns>The resolved extension collection.</returns>
    public static ExtensionsCollection BuildExtensionCollection(string namespaceStr, ExtensionDefinitions extensionSignatures)
    {
        IReadOnlyList<TypeVariationImpl> typeVariations = extensionSignatures.TypeVariations.Select(type => type.Resolve(namespaceStr)).ToImmutableList();
        IReadOnlyList<ScalarFunctionImpl> scalarFunctions = extensionSignatures.ScalarFunctions.SelectMany(function => function.Resolve(namespaceStr)).ToImmutableList();
        IReadOnlyList<AggregateFunctionImpl> aggregateFunctions = extensionSignatures.AggregateFunctions.SelectMany(function => function.Resolve(namespaceStr)).ToImmutableList();
        IEnumerable<WindowFunctionImpl> windowFunctions = extensionSignatures.WindowFunctions.SelectMany(function => function.Resolve(namespaceStr));
        IEnumerable<WindowFunctionImpl> aggregateWindowFunctions = aggregateFunctions.Select(function =>
            new WindowFunctionImpl(function, function.Decomposable, function.Intermediate, WindowMode.Streaming));

        return new ExtensionsCollection(typeVariations, scalarFunctions, aggregateFunctions, windowFunctions.Concat(aggregateWindowFunctions));
    }

    private static ExtensionsCollection LoadDefaultsInternal()
    {
        IEnumerable<ExtensionFile> files = Namespaces
            .Select(name => new ExtensionFile(string.Empty, $"substrait/extensions/functions_{name}.yaml"));
        return Load(files, new EmbeddedResourceResolver(typeof(SubstraitExtensions).Assembly));
    }

    /// <summary>Identifies an extension namespace and its resolvable path.</summary>
    /// <param name="namespaceStr">The extension namespace.</param>
    /// <param name="path">The resolvable path.</param>
    public readonly struct ExtensionFile(string namespaceStr, string path)
    {
        /// <summary>Gets the extension namespace.</summary>
        public string Namespace => namespaceStr;

        /// <summary>Gets the resolvable path.</summary>
        public string Path => path;
    }

    /// <summary>Resolves files from the local filesystem.</summary>
    public sealed class FileSystemResolver : IExtensionFileResolver
    {
        /// <inheritdoc/>
        public Stream Resolve(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read);
        }
    }

    /// <summary>Resolves embedded assembly resources.</summary>
    public sealed class EmbeddedResourceResolver(Assembly assembly) : IExtensionFileResolver
    {
        /// <inheritdoc/>
        public Stream Resolve(string path)
        {
            return assembly.GetManifestResourceStream(path) ?? throw new FileNotFoundException(path);
        }
    }

    private sealed class ReadOnlyCollectionDeserializer(INodeDeserializer inner) : INodeDeserializer
    {
        public bool Deserialize(IParser parser, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value)
        {
            if (expectedType.IsGenericType && expectedType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                value = nestedObjectDeserializer(parser, typeof(List<>).MakeGenericType(expectedType.GetGenericArguments()[0]));
                return true;
            }

            if (expectedType.IsGenericType && expectedType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            {
                value = nestedObjectDeserializer(parser, typeof(Dictionary<,>).MakeGenericType(expectedType.GetGenericArguments()));
                return true;
            }

            return inner.Deserialize(parser, expectedType, nestedObjectDeserializer, out value);
        }
    }
}
