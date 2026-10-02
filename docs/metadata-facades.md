# Read-only metadata facades

`Substrait.Core.Metadata` provides generated, immutable facades over protobuf
relation metadata. These are a foundation for preserving metadata during core
relation conversion; the existing relation converters do not use them yet.

The supported roots are `ReadOnlyRelCommon` and `ReadOnlyAdvancedExtension`.
Their nested message fields use corresponding facades, including `ReadOnlyAny`
for opaque payloads. Facades are not protobuf messages and cannot be mutated
through protobuf reflection or merge APIs.

## Import, inspect, and export

```csharp
using Substrait.Core.Metadata;
using Substrait.Protobuf;

var source = new RelCommon
{
    RelAnchor = 42,
    Hint = new RelCommon.Types.Hint { Alias = "orders" },
};
var metadata = ReadOnlyRelCommon.FromProto(source);

source.Hint.Alias = "changed"; // Does not change the snapshot.
uint? anchor = metadata.RelAnchor;
string? alias = metadata.Hint?.Alias; // Still "orders".

var editable = metadata.ToProto();
editable.Hint.Alias = "customers";
var updated = ReadOnlyRelCommon.FromProto(editable);
```

`FromProto` deep-copies its argument. The caller retains ownership and may
subsequently modify it, but must not mutate it concurrently with the import.
`ToProto` returns a fresh, detached deep copy. Editing an exported message never
modifies its facade.

Nested message properties are nullable facades, scalar properties are
getter-only, and repeated fields expose read-only collections. Child facades
and collections are created once and reused on subsequent access. `ByteString`
values are immutable and can be shared. Public factories copy the message graph
once; child facades do not independently clone every nested message.

## Preservation and equality

- Message absence is represented by `null`, distinct from a present empty
  message. Optional scalars use nullable values: an absent relation anchor is
  `null`, while an explicitly set zero is `0`.
- `EmitKindCase` distinguishes unset, direct, and emit. An empty emit mapping
  remains different from an absent mapping.
- Enum properties use the protobuf enum types and retain unknown numeric values.
- Advanced extensions retain optimization order and enhancement presence.
  `Any` type URLs and payload bytes are preserved without unpacking.
- Unknown protobuf fields remain in the backing messages and survive export.
- Equality and hashing delegate to the generated protobuf implementations.
  They are structural protobuf comparisons, not query-semantic equivalence.
  Facades with different hints can compare unequal even when they describe the
  same computation. Do not use hash codes as persisted or cross-process IDs.

These wrappers preserve data, not validate its semantics: for example, they
allow an explicit zero anchor. Plan-wide anchor validation belongs in plan
construction and conversion, not in generated accessors.

Binary protobuf can preserve unknown `Any` payloads without descriptors.
Standard protobuf JSON still requires the corresponding payload descriptors
in a type registry. The facades neither add a JSON encoding nor remove that
restriction.

## Internal ownership boundary

The generated internal `FromOwnedProto` factory avoids import-time copying
when the library already owns a message. Its contract covers the entire
mutable message graph: no retained alias may mutate it or escape through a
mutable public API. Nested facades wrap submessages of the same owned graph.
Exports still clone.

This entry point is not public. It supports future library-controlled parsing
and metadata conversion without imposing an ownership-transfer contract on
callers. This change adds neither new parsing entry points nor changes to
existing relation serialization.

For generation and schema-update instructions, see
[Contributing](../CONTRIBUTING.md#generated-metadata-facades).
