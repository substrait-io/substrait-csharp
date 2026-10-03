// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Metadata;
using Substrait.Core.Type;

namespace Substrait.Core.Relation;

/// <summary>Supplies the producer/consumer schema contract for custom relations.</summary>
/// <remarks>
/// Called once per imported extension occurrence, after converting its inputs.
/// The relation stores the result, not the resolver. Implementations should be
/// deterministic and must throw for recognized but invalid extensions.
/// </remarks>
public interface IExtensionRelationSchemaResolver
{
    /// <summary>Resolves the output schema before this relation's emit mapping.</summary>
    /// <param name="kind">The extension's wire variant.</param>
    /// <param name="detail">The immutable opaque payload, or null when absent.</param>
    /// <param name="inputs">
    /// Immutable, ordered input relations. Access their RecordType only when needed;
    /// an input's schema may be unresolved. Input schemas include their own emit mappings.
    /// </param>
    /// <returns>The unmapped schema, or null when it is unknown. An empty struct means zero columns.</returns>
    ParameterizedType.Struct? Resolve(
        ExtensionRelationKind kind,
        ReadOnlyAny? detail,
        IReadOnlyList<IRel> inputs);
}
