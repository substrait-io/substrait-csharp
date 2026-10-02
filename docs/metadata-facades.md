# Read-only metadata facades

`Substrait.Core.Metadata` provides generated, immutable facades over protobuf
relation metadata. Core relations retain these facades through `RelationMetadata`,
and converters preserve them in both directions.

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

This entry point is not public. The plan converter uses it only for protobufs
parsed privately from bytes, streams, files, or JSON. Existing entry points
accepting caller-owned protobuf objects copy retained metadata instead. Neither
path retains the entire parsed plan merely to preserve metadata.

## Relation construction and conversion

`IRel.Metadata` exposes a `RelationMetadata` whose `Common` and
`AdvancedExtension` properties correspond to different protobuf locations.
For a named-table read, `TableAdvancedExtension` separately preserves
`ReadRel.NamedTable.advanced_extension`.

```csharp
using Substrait.Core.Metadata;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using Substrait.Protobuf;

var common = ReadOnlyRelCommon.FromProto(new RelCommon
{
    RelAnchor = 7,
    Hint = new RelCommon.Types.Hint { Alias = "orders" },
    Emit = new RelCommon.Types.Emit { OutputMapping = { 0, 0 } },
});
var metadata = new RelationMetadata(common);
var schema = new Substrait.Core.Type.NamedStruct(
    ["order_id"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]));
var read = new NamedTableRead(metadata, schema, ["orders"], filter: null);
var protobuf = new RelToProtoConverter().From(read);
```

Metadata-first constructors avoid ambiguous overloads with the existing
`Remap?` constructors. Old constructors remain available and emit explicit
direct or emit metadata. `Transmute` is derived from metadata and is not a second
independent setting. `RelationMetadata.Empty` represents absent common and
operator extensions; `RelationMetadata.Direct` represents explicit direct output.

Absent common, unset emit, and explicit direct all leave the output type
unchanged, but retain their distinct wire representations and structural
equality. An empty emit produces zero fields. Hints, including output names and
aliases, do not modify the core record type or a read's initial schema.

Relation and plan equality and hashing now include metadata. Named-table
extensions participate in the read's equality as well. The SDK preserves opaque
enhancements but does not interpret them: executing consumers must understand
required enhancements or reject them rather than silently ignore them.
`ReferenceRel` has no common or advanced-extension field; `Reference.Metadata`
is always empty and its target's metadata stays on the defining entry.

## Serialized input entry points

`ProtoToPlanConverter` supports:

- `FromBytes(byte[])`: binary protobuf; does not retain the caller's byte array.
- `FromStream(Stream)`: binary protobuf from the current stream position;
  leaves the caller's stream open on success and failure.
- `FromFile(string)`: binary protobuf; opens and closes its own file.
- `FromJson(string, strictMode, parser)`: explicit protobuf JSON, with an optional
  `JsonParser` configured with a payload type registry.

All use the same conversion, extension-resolution mode, and plan validation.
Formats are not guessed from file extensions. Use `FromJson(File.ReadAllText(path))`
for JSON files. Parsing uses the protobuf library's normal limits and reports its
parse errors; I/O errors propagate. Invalid plan anchors report
`SerializationException`. The caller-owned `From(Protobuf.Plan)` entry point and
standalone `ProtoToRelConverter` copy retained metadata, so modifying the source
after conversion cannot change the IR. Do not mutate source objects concurrently
with conversion.

## Plan-wide relation anchors

An absent anchor is valid; an explicitly set anchor must be in
`1..uint.MaxValue` and unique across every serialized relation occurrence in
all roots, non-root entries, and expression subqueries.

Reusing an anchored relation object inline serializes multiple occurrences and
is rejected, even when those occurrences share the same C# instance. Instead,
register it once with `PlanBuilder.RegisterSubplan` and reuse the returned
references. References do not recount the target's anchor, and their zero-based
ordinals are independent of relation anchors and extension-declaration anchors.

The builder checks registrations without consuming ordinals or reserving
anchors on failure. Full-plan construction/build and serialization validate
again, including custom `IPlan` implementations. Invalid composed plans report
`ArgumentException`; malformed wire plans report `SerializationException`.
Standalone relation converters reject zero anchors but cannot establish
uniqueness against relations outside that subtree.

Preserving relation anchors does not add support for anchor-based outer
references or lateral joins. Those remain separate features.

## Preview API migration

Custom `IRel` implementations must add `Metadata` and recompile. It must be
non-null, and its `Transmute` projection must equal the relation's output mapping.
Custom `Rel` subclasses inherit direct metadata by default; a subclass that
overrides `Transmute` must also provide matching metadata, preferably by using
the inherited `Transmute` getter and overriding `Metadata` instead.

Several concrete `Transmute` overrides are now inherited from `Rel`. Reflective
callers using `DeclaredOnly` must account for the inherited property. Existing
constructor signatures remain, but metadata-sensitive structural equality and
rejection of zero/duplicate anchors are intentional behavior changes.

For generation and schema-update instructions, see
[Contributing](../CONTRIBUTING.md#generated-metadata-facades).
