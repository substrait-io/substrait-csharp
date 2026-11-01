# Type-alias resolution

`TypeAliasTable` models `Plan.type_aliases`: a plan-scoped table of alias
anchor &rarr; concrete-type definitions. It validates the whole table once, at
construction, then resolves any `ParameterizedType.AliasReference` - at any
depth inside a type tree - against it. This is **resolution**, a distinct
step from the lossless reference model in
[type-alias references](alias-references.md): an `AliasReference` on its own
never knows whether its anchor exists or what it points to.

## Building a table

```csharp
using Substrait.Core.Type;
using Substrait.Tools;

var table = new TypeAliasTable.Builder()
    .Add(5, TypeFactory.REQUIRED.I64)
    .Add(7, TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.AliasReference(5)]))
    .Build();

IType resolved = table.Resolve(TypeFactory.NULLABLE.AliasReference(7));
// struct<i64>?, with anchor 5's definition resolved into anchor 7's.
```

Anchor `0` is a valid anchor, not a stand-in for "unset". The order entries
are added in does not affect resolution: `Build()` computes its own
dependency order, so a definition may reference an anchor added to the
builder later (a forward reference).

## Resolving references

`Resolve(type)` walks `type` - struct fields, list elements, map keys and
values, user-defined data-type parameters, and function parameters/return
types, at any nesting depth - and replaces every `AliasReference` it finds
with that anchor's definition. Two things happen at each replaced reference:

- The **reference site's own nullability** replaces the definition's root
  nullability. The definition's own type variation and every nested child
  are otherwise preserved untouched; resolving two references to the same
  anchor with different nullability (one required, one nullable) never
  mutates the shared definition, and the two results share identical nested
  structure under their differing roots.
- Nothing is resolved recursively "live" at each call: `Build()` already
  expanded every definition's own nested alias references, in dependency
  order, once. `Resolve(type)` therefore only ever substitutes against that
  already-flattened table, regardless of how many aliases a definition
  transitively depended on.

A type with no alias reference anywhere resolves to an equivalent type
(container nodes are still reconstructed, matching how every other
rewriting visitor in this codebase behaves; the content is unchanged).

`TypeAliasTable.Definitions` exposes each anchor's definition exactly as
declared - including any nested, unexpanded alias reference - for callers
that need the as-declared form rather than `Resolve`'s flattened one (for
example, re-exporting the table losslessly).

## Validation and errors

Every check below is a C# `ArgumentException` from the in-memory
`TypeAliasTable.Builder`/`TypeAliasTable` API, and a `SerializationException`
from `ProtoToTypeAliasTableConverter` (the proto boundary wraps the former
into the latter, matching how `ProtoToTypeConverter` wraps construction
failures elsewhere). None of these ever produce a partial table or a
silently-wrong resolved type.

Checked once, eagerly, for the whole table at `Build()`:

- A **duplicate anchor** (the same anchor added twice).
- A **missing target**: a definition whose own nested alias reference names
  an anchor absent from the table.
- A **direct alias-to-alias definition**: a definition that is itself an
  `AliasReference` at its root. (A definition may still reference another
  alias *nested inside* a container - only the bare, unwrapped case is
  rejected, matching the pinned `TypeAlias` contract.)
- A **non-concrete definition**: a definition that is `UnboundType` at its
  root.
- A **circular alias dependency**, self-referential or indirect through any
  number of other anchors, detected via the same topological-sort idiom
  `PlanValidation.GetDependencyOrder` already uses for the plan's
  cross-relation dependency graph: anchors are processed in dependency order,
  and any anchor still unprocessed once nothing more can be scheduled is
  part of a cycle. A **shared, acyclic dependency** (two unrelated anchors
  both depending on a third) is ordinary bookkeeping, not a cycle - nothing
  about the algorithm treats "depended on more than once" as meaningful.

Checked per reference site, at `Resolve()`:

- A reference whose own nullability is not explicitly required or nullable.
  (`AliasReference`'s constructor does not itself enforce this, so
  resolution guards it explicitly rather than surfacing whatever unrelated
  exception `TypeFactory.Of` would throw for an unsupported nullability.)
- A reference naming an anchor absent from the table. This is distinct from
  the table-build-time missing-target check above: it fires for a reference
  appearing in an arbitrary type tree handed to `Resolve`, not for a
  dangling reference between two of the table's own definitions.

Checked only by `ProtoToTypeAliasTableConverter`, at the proto boundary:

- A **missing definition type**: an entry whose `TypeAlias.type` field is
  unset. (Every other validation above operates on already-converted `IType`
  values, where this distinction cannot be expressed - the aliased type is
  always already a *some* concrete type or it would have failed sooner.)
  "Every type parameter must be specified", the pinned contract's other
  requirement on a definition, is enforced for free: there is no
  "unspecified parameter" representable in any `IType` constructor, so
  reusing `ProtoToTypeConverter` to convert each entry's type already rules
  it out.

Resolve-time errors name the offending anchor and the path to it (for
example, `"...at struct field 1 -> function parameter 0"`), mirroring
`ProtoToTypeConverter`'s own path-reporting convention. Table-build-time
errors name the anchor(s) involved; a `TypeAlias` entry has no deeper
"path" of its own to report.

## Recursion safety

Every traversal here - the table's own dependency-graph walk, the
topological sort, and the reference-substituting rewrite - is iterative,
using an explicit stack or queue, the same discipline `ProtoPlanDependencies`
and `ProtoToTypeConverter` already use elsewhere in this codebase. A
dependency chain thousands of anchors deep resolves without unbounded CLR
recursion, and a cycle is detected deterministically rather than overflowing
the stack or hanging.

## Proto conversion and the conversion-context contract

```csharp
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type.Converters;

var table = new ProtoToTypeAliasTableConverter().From(plan.TypeAliases);

var context = new PlanToProtoConverter.ConverterContext();
IReadOnlyList<Substrait.Protobuf.TypeAlias> exported =
    new TypeAliasTableToProtoConverter().From(table, context);
```

`ProtoToTypeAliasTableConverter`/`TypeAliasTableToProtoConverter` convert
between `IEnumerable<Protobuf.TypeAlias>` (the shape of `Plan.type_aliases`)
and `TypeAliasTable`, independent of the rest of the plan envelope. Export
optionally takes a shared `PlanToProtoConverter.ConverterContext` - the same
context `TypeToProtoConverter`/`RelToProtoConverter` already thread through
a whole plan export - so that extension and type-variation anchors
referenced inside an alias definition are collected into the *same*
plan-wide declaration table as everything else, rather than a separate one
local to the alias table. A caller that owns the full plan envelope (see
[boundaries](#boundaries) below) passes its own context in; passing none
creates a fresh one, for standalone use.

## Boundaries

- This slice owns alias-definition models and table validation/resolution,
  independent of the full plan envelope. Retaining `Plan.type_aliases` on
  the internal `Plan`/`PlanMetadata` model, and invoking this table from
  `ProtoToPlanConverter`/`PlanToProtoConverter`, belongs to
  [substrait-io/substrait-csharp#27](https://github.com/substrait-io/substrait-csharp/issues/27).
  Until that lands, `ProtoToPlanConverter` explicitly rejects any plan
  declaring `Plan.type_aliases` rather than silently dropping the table.
- Extension YAML type variables/type classes remain out of scope here; see
  [substrait-io/substrait-csharp#46](https://github.com/substrait-io/substrait-csharp/issues/46).
  Alias anchors are a separate namespace from extension type anchors.
