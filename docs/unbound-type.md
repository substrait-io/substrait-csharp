# Unbound placeholder type

`UnboundType` models `Type.unbound`: a placeholder for a type that has not
yet been bound to a concrete type, appearing wherever a concrete type would
normally appear in a **partially bound** plan or expression. It is not the
same as an unset `Type.kind` (which is a malformed message) or a nullable
concrete type (which is still a concrete type).

Unlike every other `IType`, the pinned `Type.unbound` message has no fields
at all - not even nullability. `UnboundType` is a singleton,
`UnboundType.Instance`, rather than something constructed through
`TypeFactory`: there is no nullability or type variation to choose at
construction time.

## Construction and conversion

```csharp
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;

IType unbound = UnboundType.Instance;

var export = new TypeToProtoConverter();
var import = new ProtoToTypeConverter();

Substrait.Protobuf.Type wireUnbound = export.From(unbound);
IType roundTripUnbound = import.From(wireUnbound);
bool same = ReferenceEquals(roundTripUnbound, UnboundType.Instance); // true
```

An explicit `Type.unbound` message round-trips to the same singleton and is
distinguishable from an entirely unset `Type.kind`, which is a malformed
message and still raises an error wherever any other unset/unsupported kind
would.

## Nullability and type variation

`UnboundType.Nullable` **throws** `NotSupportedException` rather than
returning a value - there is no nullability to report, and returning a
sentinel would let callers mistake it for a real answer.
`TypeFactory.ResolveTypeWithNullability` therefore also throws for an
`UnboundType` input, for the same reason: there is no nullability to
rewrite, so failing loudly is correct rather than silently binding the
placeholder into a concrete nullable/required type.

`UnboundType.TypeVariation` always returns `null`; the pinned message has no
variation field either, and there is no constructor argument through which a
caller could request one.

Because `Nullable` throws, generic code that reads an arbitrary `IType`'s
`Nullable` property unconditionally - including reflection-based tooling
that inspects every public property - will throw when given an
`UnboundType`. This is intentional, matching the placeholder's documented
semantics, but it means such code must either special-case `UnboundType` or
avoid touching `Nullable` without first checking the type.

## Equality, traversal, and rewriting

`UnboundType` has no child types (`InputNodes` is empty) and no parameters
to compare, so every instance is equal to every other and all instances
share the same hash code - in practice there is only ever one instance,
`UnboundType.Instance`. Top-down and bottom-up dispatchers, equality,
hashing, and `ToTypeString` (which returns the bare literal `"unbound"`,
with no parameters to print) all treat it as an ordinary leaf type, with no
special-casing beyond the new `TypeVisitor<,>.Visit(UnboundType, ...)`
overload and the throwing behavior above.

The new visitor overload is virtual and falls back to `Visit(IType, ...)`.
Existing `TypeVisitor<,>` subclasses do not need another abstract
implementation to keep compiling.

## Boundaries

- This adds a placeholder **type marker**, not a binder or execution engine:
  nothing in the core type API resolves, binds, or executes an unbound
  type. A plan containing one anywhere is partially bound, and consuming it
  further is the caller's responsibility.
- YAML type-variable binding and extension type-expression parsing remain in
  [substrait-io/substrait-csharp#46](https://github.com/substrait-io/substrait-csharp/issues/46).
