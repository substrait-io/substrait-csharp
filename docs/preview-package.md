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
  `Substrait.Net.Extensions` version 0.107.0.
- All targets also depend on `Antlr4.Runtime.Standard`, `Google.Protobuf`, and
  `YamlDotNet`. The `netstandard2.0` target also uses `IndexRange`,
  `Microsoft.Bcl.HashCode`, `System.Collections.Immutable`, and `System.Memory`
  compatibility packages.
- Applications must deploy patched .NET runtimes: .NET 8.0.33, 9.0.22,
  10.0.14, or later servicing releases of those versions include the
  [Brotli security fix (CVE-2026-111497)](https://github.com/dotnet/announcements/issues/494).
  Updating this library's NuGet dependencies does not update a consumer's shared
  runtime or a self-contained application's bundled runtime. .NET Framework is
  not affected by this advisory.
- Runtime dependency minimums include Google.Protobuf 3.36.2 and YamlDotNet
  18.1.0. The YAML collection adapter forwards the root deserializer required
  by YamlDotNet's updated interface while retaining nested read-only
  collections, polymorphic arguments, and aliases.
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

## UUID and temporal types

Core type conversion supports UUID, time, day intervals, and compound intervals
with nullability and type variations preserved through nested structs.
UUID remains distinct from binary and string types. Time and interval precision
accepts every integer from 0 through 12 (seconds through picoseconds), not just
multiples of three.

```csharp
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;

PrimitiveType.Uuid id = TypeFactory.REQUIRED.UUID;
PrimitiveType.Time time = TypeFactory.NULLABLE.PrecisionTime(9);
PrimitiveType.IntervalDay duration = TypeFactory.REQUIRED.IntervalDay(6);
ParameterizedType.IntervalCompound calendarDuration =
    TypeFactory.NULLABLE.IntervalCompound(3);

var schema = TypeFactory.REQUIRED.Struct([id, time, duration, calendarDuration]);
var protobuf = new TypeToProtoConverter().From(schema);
IType roundTrip = new ProtoToTypeConverter().From(protobuf);
```

The existing `PrimitiveType.Time` and `PrimitiveType.IntervalDay` public types,
visitor methods, and factory return types are retained. Their new `Precision`
properties and precision-aware `Of(int precision, ...)` overloads do not change
the defaults: `TIME`, `Time_(variation)`, and `Time.Of(nullable, variation)` still
mean precision 6; `INTERVAL_DAY`, `IntervalDay_(variation)`, and
`IntervalDay.Of(nullable, variation)` still mean explicit precision 0.
These classes remain under `PrimitiveType` for API compatibility even though
they now carry a precision parameter. Strict comparison and value equality
include precision, nullability, and type variation; nullability/variation
rewriting retains precision.

Legacy time names (`time`) and default-precision text representations remain
unchanged. Non-default time precision is rendered as `precision_time<P>` by
`ToTypeString()`, and non-default day intervals as `interval_day<P>`.
Time variations may name either the historical `time` parent or the current
`precision_time` parent. Custom variations must be supplied through an explicit
extension context on import; outbound conversion collects their references.
The default extension catalog does not supply custom variations.

New UUID and compound-interval visitor overloads are virtual fallbacks to
`Visit(IType, ...)`; existing custom visitors need not implement them.
`DefaultTypeVisitor` sends them to `DefaultVisit`.

Out-of-range precision throws `ArgumentOutOfRangeException`, consistent with
timestamp types. A protobuf day interval with absent precision throws
`SerializationException`; explicitly present zero is valid. This replaces the
earlier `NotSupportedException` for unsupported day-interval precision.
No specification package update is needed. This support concerns type metadata,
not UUID/temporal literal value encoding or additional YAML grammar support.

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

## Plan extension metadata

`IPlan.Metadata` exposes immutable `PlanMetadata` containing plan-level advanced
extensions, expected payload type URLs, and imported extension URN/declaration
tables. Metadata-first plan constructors,
`Plan.FromRelations`, and `PlanBuilder` preserve it through composition and
conversion. Builder editing methods let callers set, add, replace, remove, and
clear payloads and expected type URLs without constructing immutable metadata
upfront. `PlanBuilder.Metadata` exposes the current immutable snapshot; later
edits do not change previously built plans. Payload methods accept read-only
facades or protobuf messages, packing ordinary messages and copying an existing
`Any` without repacking. Whole-message replacement accepts a read-only facade or
a protobuf `AdvancedExtension`; public imports and exports are detached snapshots.
Absent/empty messages, unknown payloads and fields, and list order/duplicates
are preserved and participate in plan equality. No specification or dependency
update is required. Full-plan export emits user-defined type declarations and
retains imported declarations and their anchors, including aliases and entries
unused by the core plan that opaque extensions may reference. Core references
use one existing anchor per identity; new dependencies receive nonconflicting
anchors. Retained tables participate in metadata equality, without per-node
numeric-anchor provenance or a byte-for-byte round-trip guarantee.

Custom `IPlan` implementations must provide the new non-null `Metadata` property
and recompile, using `PlanMetadata.Empty` if appropriate. This is a breaking
preview interface change. Existing concrete plan/builder constructors remain
available and default to empty metadata. See
[plan extension metadata](metadata-facades.md#plan-extension-metadata) for examples
and binary/protobuf JSON limitations.

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

## Upgrading to Substrait 0.107.0

The three specification packages are upgraded together from 0.104.0 to 0.107.0.
Runtime dependency minimums are unchanged. `Version.Current` reports 0.107.0
from the protobuf assembly metadata, so newly constructed plans use that version.
The generated read-only metadata facades are unchanged.

The extension loader accepts the optional file-level `description` introduced
in 0.105.0 through `ExtensionDefinitions.Description`; unrecognized YAML fields
still fail rather than being ignored. Existing files without descriptions remain
valid. The same nine function families are loaded by default.

Consumers must account for the intervening upstream contract changes:

- Decimal `avg` now declares a nullable `DECIMAL?<38,S>` result, including its
  window form. Plans using this explicitly loaded extension should no longer
  declare a required result.
- The date and precision-time `round_calendar` overloads no longer accept a
  trailing origin value. Keep the origin enum argument and remove the duplicate
  trailing date/time argument, updating the compound signature accordingly.
- The deprecated `subtract:ptstz_iyear` overload is removed. Use
  `subtract:ptstz_iyear_str` with an explicit IANA timezone.
- Generated `LambdaInvocation.lambda` is now in a oneof. Existing inline-lambda
  wire encoding is unchanged, but callers using generated APIs should recompile
  and account for oneof presence.

The newer protobuf packages expose detached expression/relation subtrees and
named lambdas. Detached tables are supported as described in
[detached plan imports](#detached-plan-imports). All plan import entry points
still reject nonempty `Plan.named_lambdas` tables with `NotSupportedException`,
even when unreferenced or when extension resolution is non-strict.

The extension type-expression parser's existing limitations also remain:
parameterized precision-time/timestamp signatures are loaded as declarations
but are not available through function-key lookup. The contract changes above
do not add support for resolving those signatures.

See the upstream release notes for [0.105.0](https://github.com/substrait-io/substrait/releases/tag/v0.105.0),
[0.106.0](https://github.com/substrait-io/substrait/releases/tag/v0.106.0), and
[0.107.0](https://github.com/substrait-io/substrait/releases/tag/v0.107.0).

## Detached plan imports

`ProtoToPlanConverter.From`, `FromBytes`, `FromStream`, `FromFile`, and
`FromJson` resolve `detached_expression_ordinal` and `detached_rel_ordinal`
references using the tables of the containing `PlanRel`. Root and non-root
entries have separate namespaces, even when one refers to another through
`ReferenceRel`. Detachment is encoding-only, not computation sharing: core
relations and expressions contain the resolved logical trees.

Import validates the reachable portion of each entry. Detached ordinals must
be in range, each entry may be referenced at most once, and references must be
acyclic, including cycles through subqueries. Each referenced table entry must
have a concrete non-ordinal variant; its children may be detached ordinals.
Violations produce `SerializationException` identifying the plan entry.
Existing correlation, plan-reference, and relation-anchor checks apply after
resolution, in the original use-site scope. Unreachable table entries are not
execution roots and are not semantically validated.

Detached relation, expression, and mixed subquery chains are converted through
one shared work stack, with container-local resolution at frame entry. Binary
and JSON parsers retain their existing limits:
detachment does not make excessively nested individual chunks, types, literals,
or field-reference paths parseable. Standalone relation/expression converters
cannot resolve detached ordinals without their containing plan entry and
report `SerializationException`; convert the containing plan instead.

### Encoding metadata and opaque payloads

`IPlan.IRelation.Metadata` is a non-null `PlanRelationMetadata` snapshot of the
original detached tables, including unused entries. This is separate from
`IRel.Metadata` (operator metadata) and `IPlan.Metadata` (plan-level metadata).
The tables are retained in their original order because opaque `Any` payloads
may reference them. The library does not interpret or validate ordinals inside
opaque payloads; consumers that interpret them must apply the same
container-local validation rules.

`PlanRelationMetadata.FromProto(entry)` copies only the detached tables, not the
entry's root or relation. `ToProto()` returns an independent mutable copy of
those tables in a `PlanRel` with no relation variant set. Public object imports
copy retained messages; private binary/JSON imports can retain privately owned
messages. Exported tables can be mutated without changing the core plan.
Entry equality and hashing include the retained encoding metadata.

When copying an imported entry, pass its metadata to the metadata-first
`Plan.Root`/`Plan.Relation` constructors, or to the corresponding
`PlanBuilder.AddRoot`/`RegisterSubplan` overloads. `Plan.FromRelations` retains
each entry's metadata. Preserve plan-level extension declarations and relevant
plan entry ordinals as well: detached tables and opaque payloads may reference
them. Changing logical trees does not rewrite opaque payloads or retained
tables; applications making such edits are responsible for their consistency.

This adds a required `Metadata` property to custom `IPlan.IRelation`/`IRoot`
implementations. Return `PlanRelationMetadata.Empty` for entries without
retained encoding metadata. Existing concrete constructors and builder
overloads use this empty value automatically.

### Export scope

`PlanToProtoConverter.From(plan)` preserves retained detached tables and their
ordinals while emitting the resolved logical tree inline. This default output
is unchanged and may exceed parser limits for a deep import. Opt-in detachment
is described below. Standalone `ExtendedExpression` conversion is not supported.

## Bounded plan export

Use the depth overload to generate detached relation and expression subtrees:

```csharp
var protobuf = new PlanToProtoConverter().From(
    plan, maxInlineRelExpressionDepth: 8);
```

The positive integer bounds the number of concrete `Rel` and `Expression`
nodes along each inline path in the newly emitted logical query. Each chunk's
root counts as one. Relation-to-expression and expression-to-subquery edges
share one counter; subqueries do not reset depth unless detached. Sibling
counts do not affect depth. When adding a child would exceed the bound, that
child starts a detached chunk at depth one. Ordinal placeholders terminate the
inline path and do not count as concrete nodes. Zero and negative bounds throw
`ArgumentOutOfRangeException`.

For example, `Project -> Cast -> ScalarSubquery -> Read` has depth four.
At a bound of three, its final `Read` becomes a detached relation. Protobuf
wrapper messages do not count; neither do types, literal internals, metadata,
or embedded physical field references that are not `Expression` messages.
This is **not** a protobuf message-depth or parser recursion limit. Deep
excluded content or wrapper-heavy paths can still exceed binary or JSON parser
limits. The example value eight is not a universal parser-safety guarantee.

The bounded writer traverses relation/expression/subquery work iteratively and
does not use runtime protobuf descriptors. It emits each occurrence independently:
sharing a core node does not cause two uses of one detached ordinal. Output is
deterministic for a fixed plan and bound, but does not minimize table size.
Ordinary `ReferenceRel` links remain in the plan-wide namespace.

**Retained tables are exempt from the bound.** They are copied unchanged, in
their original order, to preserve opaque extension references. Newly generated
entries are appended within each root/non-root plan entry and obey the bound.
Repeated import/export can therefore grow the tables; this overload does not
compact, deduplicate, renumber, or reinterpret retained entries or opaque
payloads. Mutating returned protobufs does not modify the source plan.

Binary and JSON round-trips preserve the logical query, output names, schemas,
and metadata. Because encoding metadata participates in plan equality, an
import of newly detached output need not compare equal to the original plan
entry even when its logical relation tree is equal.

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
- The default `Time` model is serialized as `precision_time<6>` and default
  day intervals use explicit precision 0 (whole seconds). Precision-aware
  factories preserve all precisions from 0 through 12 without rescaling or
  truncation; absent day-interval precision remains invalid. See
  [UUID and temporal types](#uuid-and-temporal-types) for APIs and compatibility.
- Offset-based outer references remain supported using the deprecated
  `steps_out` representation. Relation-anchor outer references are explicitly
  unsupported by the internal model.

The package upgrade does not imply support for every newer specification
feature in the internal representation. Unsupported grammar constructs and
protobuf variants continue to report errors.