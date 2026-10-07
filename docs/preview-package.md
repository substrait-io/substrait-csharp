# Preview package status

`Substrait.Net` 0.1 previews are evaluation packages. Continuous
integration produces `.nupkg` and `.snupkg` workflow artifacts, but does not
publish them to a package feed.

## API baseline

The Roslyn Public API analyzer records the current surface in
`src/Substrait/PublicAPI.Unshipped.txt`. `PublicAPI.Shipped.txt` remains empty for
the first preview, so no API is declared stable yet. Moving APIs into the shipped
baseline requires a separate review after package-consumer feedback. Until then,
preview releases may contain breaking API changes.

## Compatibility scope

- The package targets `net10.0`, `net8.0`, and `netstandard2.0`. .NET 9
  consumers use the compatible `net8.0` package asset.
- Generated protobuf bindings, ANTLR parsers, and embedded extension definitions
  come from `Substrait.Net.Protobuf`, `Substrait.Net.Antlr`, and
  `Substrait.Net.Extensions` version 0.104.0.
- All targets also depend on `Antlr4.Runtime.Standard`, `Google.Protobuf`, and
  `YamlDotNet`. The `netstandard2.0` target also uses `IndexRange`,
  `Microsoft.Bcl.HashCode`, `System.Collections.Immutable`, and `System.Memory`
  compatibility packages.
- Runtime dependency minimums include Google.Protobuf 3.36.2 and YamlDotNet
  18.1.0. The YAML collection adapter forwards the root deserializer required
  by YamlDotNet's updated interface while retaining nested read-only
  collections, polymorphic arguments, and aliases. The Substrait specification
  packages remain at 0.104.0.
- Product-specific integrations, schemas, and test assets remain outside the
  public package.

Package validation covers metadata, dependencies, license expression, README,
symbols, Source Link, an SPDX 2.2 SBOM, and standalone package consumers on
.NET 8, .NET 9, and .NET 10 across Windows, Linux, and macOS. Windows also
executes the `netstandard2.0` package asset from a .NET Framework 4.6.2
consumer. When organization policy blocks public package downloads on developer
machines, the hosted CI jobs are the authoritative clean-restore result.

## Remaining gate

Behavioral comparison with downstream consumers using an approved compatibility
corpus is still required before package adoption. Existing non-public fixture
collections cannot be copied into this repository without the provenance review
described in `tests/README.md`.

## Read-only metadata facades

`Substrait.Core.Metadata` adds generated facades for common relation metadata
and advanced extensions. Public imports and exports deep-copy mutable protobuf
messages; nested access exposes read-only facades and collections. Unknown fields,
opaque `Any` payloads, and field presence are retained by supported relation
converters. `IRel.Metadata` contains common and operator-level metadata;
named-table extensions have a separate property. New metadata-first constructors
coexist with the old remap constructors. `Transmute` is derived from metadata.

Custom `IRel` implementations must provide non-null metadata with a matching
output mapping and recompile. Concrete relation `Transmute` properties are now
inherited from the base implementation. Structural equality and hashing include
metadata and preserve distinctions such as absent versus explicit direct output.
Plans reject explicit zero and duplicate relation anchors, counting inline
occurrences rather than distinct object identities. Explicit reference sharing
does not duplicate a target anchor.

New bytes, stream, file, and JSON plan entry points privately parse protobufs and
retain metadata without extra cloning. Existing protobuf-object entry points
copy retained metadata. See [metadata facades](metadata-facades.md) for ownership,
equality, migration, validation, and protobuf JSON limitations. No specification
package update is required, and anchor-based outer-reference resolution remains
unsupported.

## Custom extension relations

The core model and converters support all three extension relation variants,
including opaque detail preservation, common metadata, input traversal, and
plan references/anchors beneath their inputs. The new visitor overloads are
virtual fallbacks, so existing visitor subclasses need not implement them.
No specification package update is needed.

Schemas are caller-supplied immutable values or results of an explicit
`IExtensionRelationSchemaResolver` during import. Missing schemas do not prevent
opaque export but fail explicitly when needed by a typed operation. Supplied
schemas participate in model equality and are not serialized separately.
Existing converter overloads remain available without a schema resolver.
Operator constructors also accept protobuf `IMessage` payloads, packing ordinary
messages and copying an already-packed `Any` without nesting. The corresponding
`ReadOnlyAny` overloads remain available; use `detail: null` rather than a
positional null for absent payloads to avoid overload ambiguity.
See [extension schema contracts](metadata-facades.md#custom-extension-relations-and-schemas).

## Multi-relation plans and references

Plan conversion supports any ordered mixture of root and non-root entries,
including multiple independent outputs, shared subplans, and empty root name
lists. Binary and protobuf JSON round trips preserve entry order, root names,
version, and reference ordinals. References can point forward or backward,
including to a root's input, and form acyclic chains or shared DAGs.

### API migration and ordering

- `IPlan.Relations : IReadOnlyList<IPlan.IRelation>` is the authoritative list
  of **all** entries. `IPlan.IRelation.Input` is the entry's relation tree.
  `IPlan.IRoot` extends `IRelation` and adds `Names`.
- Custom `IPlan` implementations must implement the new `Relations` property
  and recompile. `IRoot.Input` is now inherited from `IRelation` rather than
  declared directly on `IRoot`; this is an approved preview API change.
- Existing `new Plan(IEnumerable<IPlan.IRoot>, IVersion)` and `Plan.Root`
  construction remain available. `IPlan.Roots` remains an ordered root-only
  projection; its indexes are **not** subtree ordinals.
- Use `Plan.Relation(IRel input)` for non-root entries and
  `Plan.FromRelations(IEnumerable<IPlan.IRelation>, IVersion)` for mixed
  advanced composition. The factory snapshots the supplied entry list.
- `new PlanBuilder(IVersion? version = null)` defaults to `Version.Current`.
  `RegisterSubplan(IRel)` appends a non-root entry and returns a `Reference`;
  `AddRoot(IRel, IEnumerable<string>)` appends a root and also returns a
  `Reference`, allowing later entries to reference that root's input.
  Both operations consume an ordinal in the same zero-based sequence.
- Each registration adds an entry, even for the same or an equal relation
  instance. Merely reusing an inline relation object does not introduce a
  reference or a top-level entry. Register once and reuse the returned reference
  when sharing is intended.
- `Build()` produces an immutable `Plan` snapshot. Appending registrations
  afterward neither changes earlier plans nor renumbers existing entries.

`Reference : ZeroInput` exposes `SubtreeOrdinal` and `Target`. Its target is
the referenced entry's **input instance**, not a copy or a root wrapper.
The reference's schema comes from that input, including its remapping; root
output names never alter the schema. Relation visitors treat references as
leaves: `Target` is not an `Inputs` child. Traverse `Relations` explicitly when
processing the complete plan.

Reference equality and hashing use the ordinal rather than recursively following
the target. An ordinal identifies an edge within a particular plan, not a globally
unique relation. Plan equality and hashing include every entry's kind, position,
input, root names, and the plan version, so different target entry contents remain
significant.

### Validation and advanced composition

The builder validates dependencies and correlation during registration, and
`Build()` validates the complete snapshot. A builder accepts only references
issued by itself, including those nested in expression subqueries; references
from another builder or the public constructor are rejected.

Advanced callers may construct `new Reference(int subtreeOrdinal, IRel target)`
and use `Plan.FromRelations` to assemble forward references. A negative ordinal
is rejected by the constructor. Full plan validation rejects out-of-range
ordinals, targets that are not the exact input instance bound to their ordinal,
and cyclic dependencies, including dependencies inside expression subqueries.
Structural equality of two target trees is not enough to satisfy the identity
binding. Invalid composed plans report `ArgumentException`; malformed wire
references, cycles, and unset relation variants report `SerializationException`.
Standalone relation converters reject references when no plan context is
available; use the plan converters for reference-bearing graphs.

Wire conversion resolves dependencies iteratively without reordering entries,
and references to an entry share its single converted input instance. Extension
anchors are collected across all entries in one plan. Conversion adds no
nondeterministic metadata. Empty plans remain allowed at construction and
deserialization, but serialization still requires at least one entry.

### Correlation boundaries

Each top-level entry is converted independently, with no enclosing schemas
borrowed from its callers. Registering a subtree does not capture the outer
schema of a later reference site. A `FieldReference.SubqueryLevels` value larger
than the subquery nesting depth within the entry is unsupported caller-dependent
correlation and is rejected, even if the entry is only referenced from a subquery.

Nested subqueries can still correlate to enclosing queries **within the same
entry**. For example, a field one subquery level outward inside an entry's scalar
subquery is supported; a one-level outer field at the entry's top level is not.
When extracting a shared subplan, keep its correlated enclosing query inside the
same entry or remove the caller-dependent correlation before registration.

## Migration to specification packages

Replacing the v0.73.0 submodule with the 0.104.0 packages changes the preview API
and wire compatibility:

- Generated protobuf types retain the `Substrait.Protobuf` namespace but move
  from `Substrait.Net.dll` to `Substrait.Net.Protobuf.dll`. Consumers must
  recompile. The packages flow transitively from `Substrait.Net`.
- ANTLR types now use `Substrait.Antlr.SubstraitType` in
  `Substrait.Net.Antlr.dll`, replacing `Substrait.Antlr.Type`.
- Plans use `ExtensionUrns`, `SimpleExtensionURN`, and
  `ExtensionUrnReference`. URI-only plans must be migrated before conversion;
  the new protobuf package no longer exposes the removed URI fields. Internal
  API names such as `Uri` and `ExtensionUris` are retained, but their values
  must now be extension URNs when serializing plans.
- Default extensions use their declared namespaces, such as
  `extension:io.substrait:functions_arithmetic`, rather than
  `/functions_arithmetic.yaml`. The same nine function families are loaded.
  The old `type_variations.yaml` catalog is no longer published upstream, so
  default type variations are empty. Custom type variations can still be loaded
  explicitly. Function implementation deprecation metadata is preserved.
- The existing microsecond `Time` model is serialized as `precision_time<6>`;
  other time precisions are rejected rather than rescaled or truncated.
  Day intervals use explicit precision 0 (whole seconds); fractional or
  unspecified-precision interval types are rejected.
- Offset-based outer references remain supported using the deprecated
  `steps_out` representation. Relation-anchor outer references are explicitly
  unsupported by the internal model.

The package upgrade does not imply support for every newer specification
feature in the internal representation. Unsupported grammar constructs and
protobuf variants continue to report errors.