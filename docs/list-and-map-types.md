# List and map types

`ParameterizedType.List` wraps a single `ElementType`. `ParameterizedType.Map`
wraps a `KeyType` and a `ValueType`. Both follow the existing `Struct` pattern:
immutable, nullability and type variation apply only to the container itself,
and the element/key/value types are ordinary `IType` values that may be any
supported type, including another list, map, struct, or user-defined type.

## Construction and conversion

```csharp
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;

ParameterizedType.List list = TypeFactory.NULLABLE.List(TypeFactory.REQUIRED.I64);
ParameterizedType.Map map = TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, TypeFactory.NULLABLE.I64);

var export = new TypeToProtoConverter();
var import = new ProtoToTypeConverter();

Substrait.Protobuf.Type wireList = export.From(list);
IType roundTripList = import.From(wireList);
bool equivalent = list.Equals(roundTripList); // true
```

Conversion is non-recursive in both directions, so a list or map nested
arbitrarily deep inside other lists and maps does not risk a stack overflow;
only the CLR heap, not the call stack, bounds how deep a type can nest.

## Resolution and errors

A proto `List`/`Map` message with its element/key/value field entirely absent,
or present but with no `kind` set, causes a `SerializationException` rather
than a crash or a silently-dropped child. The message names the full
containing path, including struct field indices and `"list element"` /
`"map key"` / `"map value"` segments, even when no user-defined type is
involved, for example:

```text
Deserialization error at struct field 0 -> list element -> map key: The data type is unset.
```

## Equality, traversal, and rewriting

`InputNodes` exposes `[ElementType]` for a list and `[KeyType, ValueType]` for
a map - map keys always precede values, matching the order used when visiting,
converting, and comparing. Top-down and bottom-up dispatchers, structural
equality/hashing, and `TypeFactory.ResolveTypeWithNullability` all handle both
container kinds generically through `InputNodes`, with no special-casing
beyond the new `TypeVisitor<,>.Visit(List, ...)` / `Visit(Map, ...)` overloads.

Like `Struct`/`Decimal`/other existing parameterized types, direct
`Equals`/`GetHashCode` on `List`/`Map` ignore type variation; only
`IType.Equals(IType, ITypeComparison)` accounts for it.

The two new visitor overloads are virtual and fall back to
`Visit(IType, ...)`. Existing `TypeVisitor<,>` subclasses do not need another
abstract implementation to keep compiling.

## Named-struct field traversal

`TypeUtils.GetFieldNames(NamedStruct)` pairs each struct field's own declared
type with its name, in depth-first order. Only struct fields consume a name,
at any depth: a field reached transparently through a list element or a map
key/value is still paired with a name if it is itself a struct, and map keys
are paired before values. List and map containers contribute no pairs of
their own - matching the upstream rule that only struct fields are named.

```csharp
// struct<list<struct<i64, i64>>>: the one field (the list itself) is paired
// with its own name; the list contributes no further pair of its own, but the
// struct it transparently contains still pairs its 2 fields.
var schema = new NamedStruct(
    ["items", "a", "b"],
    TypeFactory.REQUIRED.Struct(
    [
        TypeFactory.REQUIRED.List(TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, TypeFactory.REQUIRED.I64])),
    ]));

foreach ((IType field, string name) in schema.GetFieldNames())
{
    // ("items", list<struct<i64,i64>>), ("a", i64), ("b", i64)
}
```

The traversal uses an explicit stack rather than recursion, so it is not
bounded by the CLR call stack at any nesting depth. A name count that does not
exactly match the struct's fields at every depth raises `ArgumentException`.

## Boundaries

- This adds collection **type** models and conversion, not collection
  **literals**: `Expression.Literal` support for list/map values belongs to
  [substrait-io/substrait-csharp#38](https://github.com/substrait-io/substrait-csharp/issues/38)
  and
  [substrait-io/substrait-csharp#40](https://github.com/substrait-io/substrait-csharp/issues/40).
- Extension YAML type-expression parsing for list/map type-class expressions
  belongs to
  [substrait-io/substrait-csharp#46](https://github.com/substrait-io/substrait-csharp/issues/46).
- `GetFieldNames` traverses a `NamedStruct`'s own field/name pairing; it is not
  wired into relation-level proto schema conversion
  (`RelToProtoConverter`/`ProtoToRelConverter`), which does not yet flatten
  names for nested structs at all - container or not. That wiring belongs to
  the relation/plan-envelope work in
  [substrait-io/substrait-csharp#27](https://github.com/substrait-io/substrait-csharp/issues/27) and
  [substrait-io/substrait-csharp#29](https://github.com/substrait-io/substrait-csharp/issues/29).
