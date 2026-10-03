// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Core.Extension;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Tools;

namespace Substrait.Core.Plan.Converters;

/// <summary>
/// Converts protobuf plans to the internal representation.
/// </summary>
public class ProtoToPlanConverter
{
    private readonly ExtensionsCollection extensions;
    private readonly IExtensionRelationSchemaResolver? extensionSchemaResolver;

    /// <summary>
    /// Initializes a converter using standard extensions.
    /// </summary>
    public ProtoToPlanConverter()
        : this(ExtensionUtils.LoadDefaults())
    {
    }

    /// <summary>
    /// Initializes a converter.
    /// </summary>
    /// <param name="extensions">The available extensions.</param>
    public ProtoToPlanConverter(ExtensionsCollection extensions)
        : this(extensions, extensionSchemaResolver: null)
    {
    }

    /// <summary>Initializes a converter with an explicit custom-relation schema contract.</summary>
    /// <param name="extensions">The available function and type extensions.</param>
    /// <param name="extensionSchemaResolver">The custom-relation schema resolver, or null to retain unresolved schemas.</param>
    public ProtoToPlanConverter(ExtensionsCollection extensions, IExtensionRelationSchemaResolver? extensionSchemaResolver)
    {
        this.extensions = extensions;
        this.extensionSchemaResolver = extensionSchemaResolver;
    }

    /// <summary>
    /// Converts a protobuf plan.
    /// </summary>
    /// <param name="plan">The protobuf plan.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    /// <returns>The internal plan.</returns>
    public IPlan From(
        Protobuf.Plan plan,
        ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT) =>
        this.FromCore(plan, strictMode, ownsMetadata: false);

    /// <summary>Parses binary protobuf bytes privately and converts without copying retained metadata.</summary>
    /// <param name="data">Binary protobuf plan bytes.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    /// <returns>The validated immutable plan.</returns>
    public IPlan FromBytes(byte[] data, ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT) =>
        this.FromCore(Protobuf.Plan.Parser.ParseFrom(data), strictMode, ownsMetadata: true);

    /// <summary>Parses a binary protobuf stream privately. The caller's stream remains open.</summary>
    /// <param name="stream">The readable binary stream at its current position.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    /// <returns>The validated immutable plan.</returns>
    public IPlan FromStream(Stream stream, ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT) =>
        this.FromCore(Protobuf.Plan.Parser.ParseFrom(stream), strictMode, ownsMetadata: true);

    /// <summary>Reads a binary protobuf file, closing the file on success or failure.</summary>
    /// <param name="path">The binary plan file.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    /// <returns>The validated immutable plan.</returns>
    public IPlan FromFile(string path, ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT)
    {
        using FileStream stream = File.OpenRead(path);
        return this.FromStream(stream, strictMode);
    }

    /// <summary>Parses protobuf JSON privately. Any payloads require descriptors in the supplied parser's type registry.</summary>
    /// <param name="json">The protobuf JSON plan.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    /// <param name="parser">Optional protobuf JSON parser with payload descriptors.</param>
    /// <returns>The validated immutable plan.</returns>
    public IPlan FromJson(string json, ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT, JsonParser? parser = null) =>
        this.FromCore((parser ?? JsonParser.Default).Parse<Protobuf.Plan>(json), strictMode, ownsMetadata: true);

    private IPlan FromCore(Protobuf.Plan plan, ExtensionsDictionary.StrictMode strictMode, bool ownsMetadata)
    {
        _ = plan ?? throw new ArgumentNullException(nameof(plan));
        var inputs = new Protobuf.Rel[plan.Relations.Count];
        var dependencies = new List<IReadOnlyList<int>>(plan.Relations.Count);
        var anchors = new Dictionary<uint, string>();
        for (int ordinal = 0; ordinal < plan.Relations.Count; ++ordinal)
        {
            Protobuf.PlanRel entry = plan.Relations[ordinal];
            inputs[ordinal] = entry.RelTypeCase switch
            {
                Protobuf.PlanRel.RelTypeOneofCase.Root when entry.Root.Input is not null => entry.Root.Input,
                Protobuf.PlanRel.RelTypeOneofCase.Rel => entry.Rel,
                _ => throw new SerializationException($"Plan relation ordinal {ordinal} must have a root input or a non-root relation; its variant or input is unset."),
            };
            dependencies.Add(ProtoPlanDependencies.Find(inputs[ordinal], ordinal, plan.Relations.Count, anchors));
        }

        IReadOnlyList<int> order = PlanValidation.GetDependencyOrder(dependencies, message => new SerializationException(message));
        var converted = new IRel?[plan.Relations.Count];
        var relationConverter = new ProtoToRelConverter(new ExtensionsDictionary.Builder(plan).Build(), this.extensions, strictMode, ownsMetadata, this.extensionSchemaResolver);
        relationConverter.ReferenceResolver = ordinal =>
        {
            PlanValidation.ValidateOrdinal(ordinal, converted.Length, "Plan reference resolution", message => new SerializationException(message));
            return converted[ordinal] ?? throw new SerializationException($"Reference ordinal {ordinal} has not been resolved in dependency order.");
        };
        foreach (int ordinal in order)
        {
            try
            {
                // Entries are independent correlation scopes, even when referenced
                // from a subquery. Never borrow a referring caller's schemas.
                converted[ordinal] = relationConverter.ToRel(inputs[ordinal]);
            }
            catch (SerializationException error)
            {
                throw new SerializationException($"Plan relation ordinal {ordinal}: {error.Message}", error);
            }
        }

        var entries = plan.Relations.Select((entry, ordinal) =>
        {
            IRel input = converted[ordinal] ?? throw new SerializationException($"Plan relation ordinal {ordinal} was not converted.");
            return entry.RelTypeCase == Protobuf.PlanRel.RelTypeOneofCase.Root
                ? (IPlan.IRelation)new Plan.Root(input, entry.Root.Names)
                : new Plan.Relation(input);
        });

        Protobuf.Version version = plan.Version ?? throw new SerializationException("Plan version is required by this converter.");
        return Plan.FromRelations(
            entries,
            new Version(
                version.MajorNumber,
                version.MinorNumber,
                version.PatchNumber,
                version.GitHash,
                version.Producer));
    }

    /// <summary>
    /// Creates the relation converter used by this plan converter.
    /// </summary>
    /// <param name="lookup">The plan extension lookup.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    /// <returns>The relation converter.</returns>
    protected ProtoToRelConverter GetProtoRelConverter(
        ExtensionsDictionary lookup,
        ExtensionsDictionary.StrictMode strictMode) =>
        new(lookup, this.extensions, this.extensionSchemaResolver, strictMode);
}
