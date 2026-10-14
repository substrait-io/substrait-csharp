// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Metadata;
using Substrait.Protobuf;

namespace Substrait.Core.Extension;

/// <summary>
/// Collects extension declarations while translating the internal representation to protobuf.
/// </summary>
public class ExtensionsCollector
{
    private readonly IReadOnlyList<ReadOnlySimpleExtensionURN> urnDeclarations;
    private readonly IReadOnlyList<ReadOnlySimpleExtensionDeclaration> declarations;

    private ExtensionsCollector(
        IEnumerable<ReadOnlySimpleExtensionURN> urns,
        IEnumerable<ReadOnlySimpleExtensionDeclaration> declarations)
    {
        this.urnDeclarations = urns.ToImmutableList();
        this.declarations = declarations.ToImmutableList();
        this.ExtensionUris = this.urnDeclarations.Select(urn => urn.Urn).ToImmutableList();
        var positions = this.urnDeclarations.Select((urn, index) => (urn.ExtensionUrnAnchor, index))
            .ToDictionary(entry => entry.ExtensionUrnAnchor, entry => entry.index);
        this.Extensions = this.declarations.Select(declaration =>
        {
            var entry = Describe(declaration);
            return (entry.Type, entry.Name, positions[entry.Urn]);
        }).ToImmutableList();
    }

    /// <summary>
    /// The extension declaration type.
    /// </summary>
    public enum ExtensionType
    {
        /// <summary>
        /// A user-defined type.
        /// </summary>
        Type,

        /// <summary>
        /// A type variation.
        /// </summary>
        TypeVariation,

        /// <summary>
        /// A function.
        /// </summary>
        Function,
    }

    /// <summary>
    /// Gets extension URIs in export order. Positions need not equal imported wire anchors.
    /// </summary>
    public IReadOnlyList<string> ExtensionUris { get; init; }

    /// <summary>
    /// Gets declarations in export order, with zero-based indexes into <see cref="ExtensionUris"/>.
    /// </summary>
    public IReadOnlyList<(ExtensionType Type, string Name, int ExtensionUriReference)> Extensions { get; init; }

    internal void WriteTo(Protobuf.Plan plan)
    {
        plan.ExtensionUrns.AddRange(this.urnDeclarations.Select(urn => urn.ToProto()));
        plan.Extensions.AddRange(this.declarations.Select(declaration => declaration.ToProto()));
    }

    private static (ExtensionType Type, string Name, uint Anchor, uint Urn) Describe(ReadOnlySimpleExtensionDeclaration declaration) =>
        declaration switch
        {
            { ExtensionType: { } type } => (ExtensionType.Type, type.Name, type.TypeAnchor, type.ExtensionUrnReference),
            { ExtensionTypeVariation: { } variation } => (ExtensionType.TypeVariation, variation.Name, variation.TypeVariationAnchor, variation.ExtensionUrnReference),
            { ExtensionFunction: { } function } => (ExtensionType.Function, function.Name, function.FunctionAnchor, function.ExtensionUrnReference),
            _ => throw new ArgumentException("An extension declaration has an unset or unsupported mapping."),
        };

    /// <summary>
    /// Builds an extension collection and assigns stable anchors.
    /// </summary>
    public class Builder
    {
        private readonly List<ReadOnlySimpleExtensionURN> urns = new();
        private readonly List<ReadOnlySimpleExtensionDeclaration> retained = new();
        private readonly List<(ExtensionType Type, ReadOnlySimpleExtensionDeclaration Declaration)> added = new();
        private readonly Dictionary<string, uint> urnReferences = new(StringComparer.Ordinal);
        private readonly Dictionary<(ExtensionType Type, string Uri, string Name), uint> references = new();
        private readonly AnchorAllocator urnAnchors = new(1);
        private readonly Dictionary<ExtensionType, AnchorAllocator> anchors = new()
        {
            [ExtensionType.Type] = new(0),
            [ExtensionType.TypeVariation] = new(1),
            [ExtensionType.Function] = new(0),
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="Builder"/> class.
        /// </summary>
        public Builder()
            : this([], [])
        {
        }

        internal Builder(IEnumerable<ReadOnlySimpleExtensionURN> urns, IEnumerable<ReadOnlySimpleExtensionDeclaration> declarations)
        {
            var namespaces = new Dictionary<uint, string>();
            foreach (var urn in urns)
            {
                ValidateName(urn.Urn, "extension URN");
                this.urnAnchors.Reserve(urn.ExtensionUrnAnchor, "URN");
                namespaces.Add(urn.ExtensionUrnAnchor, urn.Urn);
                this.urns.Add(urn);
                if (!this.urnReferences.TryGetValue(urn.Urn, out uint existing) || urn.ExtensionUrnAnchor < existing)
                {
                    this.urnReferences[urn.Urn] = urn.ExtensionUrnAnchor;
                }
            }

            foreach (var declaration in declarations)
            {
                var entry = Describe(declaration);
                ValidateName(entry.Name, "extension name");
                if (!namespaces.TryGetValue(entry.Urn, out string? uri))
                {
                    throw new ArgumentException($"Could not find extension URN of {entry.Urn}.");
                }

                if (entry.Type == ExtensionType.TypeVariation && entry.Anchor == 0)
                {
                    throw new ArgumentException("Type variation anchor zero is reserved.");
                }

                this.anchors[entry.Type].Reserve(entry.Anchor, entry.Type.ToString());
                var key = (entry.Type, uri, entry.Name);
                if (!this.references.TryGetValue(key, out uint existing) || entry.Anchor < existing)
                {
                    this.references[key] = entry.Anchor;
                }

                this.retained.Add(declaration);
            }
        }

        /// <summary>
        /// Collects an extension and returns its plan anchor.
        /// </summary>
        /// <param name="type">The extension type.</param>
        /// <param name="uri">The extension URI.</param>
        /// <param name="name">The extension name.</param>
        /// <returns>The extension anchor.</returns>
        public int Collect(ExtensionType type, string uri, string name)
        {
            return checked((int)this.CollectReference(type, uri, name));
        }

        internal uint CollectReference(ExtensionType type, string uri, string name)
        {
            ValidateName(uri, nameof(uri));
            ValidateName(name, nameof(name));
            if (!this.anchors.TryGetValue(type, out AnchorAllocator? allocator))
            {
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported extension type.");
            }

            var key = (type, uri, name);
            if (this.references.TryGetValue(key, out uint existing))
            {
                return existing;
            }

            uint anchor = allocator.Allocate();
            if (!this.urnReferences.TryGetValue(uri, out uint urn))
            {
                urn = this.urnAnchors.Allocate();
                this.urnReferences.Add(uri, urn);
                this.urns.Add(ReadOnlySimpleExtensionURN.FromOwnedProto(new() { ExtensionUrnAnchor = urn, Urn = uri }));
            }

            var declaration = type switch
            {
                ExtensionType.Type => new SimpleExtensionDeclaration
                {
                    ExtensionType = new() { TypeAnchor = anchor, ExtensionUrnReference = urn, Name = name },
                },
                ExtensionType.TypeVariation => new SimpleExtensionDeclaration
                {
                    ExtensionTypeVariation = new() { TypeVariationAnchor = anchor, ExtensionUrnReference = urn, Name = name },
                },
                ExtensionType.Function => new SimpleExtensionDeclaration
                {
                    ExtensionFunction = new() { FunctionAnchor = anchor, ExtensionUrnReference = urn, Name = name },
                },
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported extension type."),
            };
            this.references.Add(key, anchor);
            this.added.Add((type, ReadOnlySimpleExtensionDeclaration.FromOwnedProto(declaration)));
            return anchor;
        }

        /// <summary>
        /// Builds the collected extension declarations.
        /// </summary>
        /// <returns>The collected extensions.</returns>
        public ExtensionsCollector Build()
        {
            return new ExtensionsCollector(
                this.urns,
                this.retained.Concat(this.added.OrderBy(entry => entry.Type).Select(entry => entry.Declaration)));
        }

        private static void ValidateName(string value, string description)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"The {description} must not be empty.");
            }
        }

        private sealed class AnchorAllocator(uint first)
        {
            private readonly HashSet<uint> used = new();
            private ulong next = first;

            public void Reserve(uint anchor, string kind)
            {
                if (!this.used.Add(anchor))
                {
                    throw new ArgumentException($"Duplicate {kind} anchor {anchor}.");
                }
            }

            public uint Allocate()
            {
                while (this.next <= uint.MaxValue && this.used.Contains((uint)this.next))
                {
                    ++this.next;
                }

                if (this.next > uint.MaxValue)
                {
                    throw new InvalidOperationException("The extension anchor namespace is exhausted.");
                }

                uint result = (uint)this.next;
                this.used.Add(result);
                ++this.next;
                return result;
            }
        }
    }
}
