// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
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
    {
        this.extensions = extensions;
    }

    /// <summary>
    /// Converts a protobuf plan.
    /// </summary>
    /// <param name="plan">The protobuf plan.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    /// <returns>The internal plan.</returns>
    public IPlan From(
        Protobuf.Plan plan,
        ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT)
    {
        var inputs = new Protobuf.Rel[plan.Relations.Count];
        var dependencies = new List<IReadOnlyList<int>>(plan.Relations.Count);
        for (int ordinal = 0; ordinal < plan.Relations.Count; ++ordinal)
        {
            Protobuf.PlanRel entry = plan.Relations[ordinal];
            inputs[ordinal] = entry.RelTypeCase switch
            {
                Protobuf.PlanRel.RelTypeOneofCase.Root when entry.Root.Input is not null => entry.Root.Input,
                Protobuf.PlanRel.RelTypeOneofCase.Rel => entry.Rel,
                _ => throw new SerializationException($"Plan relation ordinal {ordinal} must have a root input or a non-root relation; its variant or input is unset."),
            };
            dependencies.Add(ProtoPlanDependencies.Find(inputs[ordinal], ordinal, plan.Relations.Count));
        }

        IReadOnlyList<int> order = PlanValidation.GetDependencyOrder(dependencies, message => new SerializationException(message));
        var converted = new IRel?[plan.Relations.Count];
        var relationConverter = this.GetProtoRelConverter(new ExtensionsDictionary.Builder(plan).Build(), strictMode);
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

        Protobuf.Version version = plan.Version;
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
        new(lookup, this.extensions, strictMode);
}
