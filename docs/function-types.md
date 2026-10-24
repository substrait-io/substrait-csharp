# Function types

`ParameterizedType.Func` models the `Type.func` proto message: an ordered list
of parameter types plus a return type. It represents a **function type** -
the signature a function value or reference has - not a function
**expression or declaration**; invoking a function, binding its arguments, or
declaring a user-defined function belongs elsewhere (see Boundaries).

## Construction and conversion

```csharp
using Substrait.Tools;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;

ParameterizedType.Func func = TypeFactory.REQUIRED.Func(
    [TypeFactory.REQUIRED.I64, TypeFactory.NULLABLE.STR],
    TypeFactory.REQUIRED.BOOL);

var export = new TypeToProtoConverter();
var import = new ProtoToTypeConverter();

Substrait.Protobuf.Type wireFunc = export.From(func);
IType roundTripFunc = import.From(wireFunc);
bool equivalent = func.Equals(roundTripFunc); // true
```

A proto `Func` message with its return type field entirely absent, or present
but with no `kind` set, causes a `SerializationException` naming the full
containing path, including a `"function return type"` or
`"function parameter N"` segment, rather than a crash or a silently-dropped
child.

## Type variation

Unlike every other parameterized type, the pinned `Type.func` proto message
has no `TypeVariationReference` field - a function type cannot carry a type
variation. `ParameterizedType.Func`'s constructor still accepts the normal
`ITypeVariation?` parameter, for consistency with every other
`ParameterizedType`, but throws `ArgumentException` if given a non-null
value. `TypeFactory.ResolveTypeWithNullability` throws the same way if asked
to apply a non-null variation to an existing `Func` value.

This is the one place the "shared variation helpers" - `IsCompatible`,
`ResolveTypeWithNullability`, and the `ITypeComparison`-aware equality/hashing
that every other type goes through generically - diverge for `Func`: they
still run, but any attempt to attach a variation fails fast instead of
silently succeeding or being accepted and then dropped on export.

## Equality, traversal, and rewriting

`InputNodes` exposes `[.. Parameters, ReturnType]` - parameters always precede
the return type, matching the order used when visiting, converting, and
comparing. Top-down and bottom-up dispatchers, structural equality/hashing,
and `TypeFactory.ResolveTypeWithNullability` all handle `Func` generically
through `InputNodes`, with no special-casing beyond the new
`TypeVisitor<,>.Visit(Func, ...)` overload and the variation rejection above.

Like `Struct`/`List`/`Map`/other existing parameterized types, direct
`Equals`/`GetHashCode` on `Func` ignore type variation (there is never one to
compare); only `IType.Equals(IType, ITypeComparison)` is variation-aware, and
for `Func` that check is always vacuously satisfied.

The new visitor overload is virtual and falls back to `Visit(IType, ...)`.
Existing `TypeVisitor<,>` subclasses do not need another abstract
implementation to keep compiling.

## Boundaries

- This adds a function **type** model and conversion, not function
  **expressions or declarations**: lambda/scalar-function invocation bodies
  belong to
  [substrait-io/substrait-csharp#41](https://github.com/substrait-io/substrait-csharp/issues/41).
- Extension YAML type-expression parsing for function type-class expressions
  belongs to
  [substrait-io/substrait-csharp#46](https://github.com/substrait-io/substrait-csharp/issues/46).
