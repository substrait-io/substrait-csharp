# Type-alias references

`ParameterizedType.AliasReference` models `Type.alias`: a plan-local
reference to an alias anchor, plus the reference site's own required/nullable
state. It is a **lossless reference**, not a resolved type - it carries the
anchor value and nothing else, and never follows, expands, or validates the
alias it points to.

## Construction and conversion

```csharp
using Substrait.Tools;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;

ParameterizedType.AliasReference reference = TypeFactory.NULLABLE.AliasReference(5);

var export = new TypeToProtoConverter();
var import = new ProtoToTypeConverter();

Substrait.Protobuf.Type wireReference = export.From(reference);
IType roundTripReference = import.From(wireReference);
bool equivalent = reference.Equals(roundTripReference); // true
```

Anchor `0` is a valid plan-local anchor, not a stand-in for "unset" - it
round-trips and compares like any other anchor value. The anchor is the raw
unsigned wire value and is never conflated with extension type or
type-variation anchors, which occupy their own separate namespaces.

## Resolution and validation boundaries

`AliasReference` only carries the anchor and the reference site's own
nullability; it does not know the alias table, does not resolve the anchor to
a concrete type, and does not validate that the anchor exists. Standalone
conversion through `TypeToProtoConverter`/`ProtoToTypeConverter` must not be
read as proof that a reference is valid - resolving an anchor against an
alias table, detecting missing targets, and rejecting cycles belongs to
alias-table resolution
([substrait-io/substrait-csharp#78](https://github.com/substrait-io/substrait-csharp/issues/78)),
a separate step applied on top of this lossless model.

A proto `Alias` message with unspecified nullability is rejected, matching
the pinned schema's requirement that a reference site be explicitly
required or nullable.

## Type variation

The pinned `Type.alias` message has no `TypeVariationReference` field - an
alias reference cannot carry a type variation, the same constraint as
`ParameterizedType.Func`. `AliasReference`'s constructor still accepts the
normal `ITypeVariation?` parameter, for consistency with every other
`ParameterizedType`, but throws `ArgumentException` if given a non-null
value. `TypeFactory.ResolveTypeWithNullability` throws the same way if asked
to apply a non-null variation to an existing `AliasReference` value.

## Equality, traversal, and rewriting

`AliasReference` is a leaf: `InputNodes` is empty, matching `FixedChar`,
`VarChar`, and other simple parameterized types with no child types to
traverse. Top-down and bottom-up dispatchers, structural equality/hashing,
and `TypeFactory.ResolveTypeWithNullability` all handle it generically
through the existing `ParameterizedType` machinery, with no special-casing
beyond the new `TypeVisitor<,>.Visit(AliasReference, ...)` overload and the
variation rejection above. Direct `Equals`/`GetHashCode` compare only
`Anchor` and `Nullable` - there is never a variation to compare.

The new visitor overload is virtual and falls back to `Visit(IType, ...)`.
Existing `TypeVisitor<,>` subclasses do not need another abstract
implementation to keep compiling.

## Boundaries

- This adds a lossless reference **model** and conversion, not alias
  **resolution**: defining aliases, resolving references against an alias
  table, missing-target checks, and cycle detection belong to
  [substrait-io/substrait-csharp#78](https://github.com/substrait-io/substrait-csharp/issues/78).
- Storing or emitting `Plan.type_aliases` belongs to
  [substrait-io/substrait-csharp#27](https://github.com/substrait-io/substrait-csharp/issues/27).
  This slice supplies the reference model that work needs, independent of
  the full plan envelope.
