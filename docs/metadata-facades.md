# Read-only metadata facades

`Substrait.Core.Metadata` provides generated, immutable facades over protobuf
metadata. Core relations retain these facades through `RelationMetadata`, and
plans retain their advanced extensions through `PlanMetadata`. Converters
preserve them in both directions.

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

## Custom extension relations and schemas

`ExtensionLeaf`, `ExtensionSingle`, and `ExtensionMulti` model the three custom
relation variants. Their `Detail` is a nullable `ReadOnlyAny`: absent, empty,
and unknown payloads remain distinct and are never unpacked by the library.
All relation inputs are immutable and visited in order. A multi-input
extension's contract determines the allowed input count, including whether
zero or one input is meaningful.

These variants have `RelCommon` but no operator-level `advanced_extension`
field. Their metadata-first constructors therefore reject non-null
`RelationMetadata.AdvancedExtension`; extensions within `Common` are preserved.
Constructors without metadata use explicit direct output.

The specification leaves schema derivation to an agreement between producer and
consumer, documented alongside the extension's detail message. Supply a schema
directly when constructing a relation:

```csharp
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Metadata;
using Substrait.Core.Relation;
using Substrait.Core.Type;

var schema = new NamedStruct(
    ["order_id"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]));
var orders = new NamedTableRead(schema, ["orders"], filter: null);
var detail = ReadOnlyAny.FromProto(new Any
{
    TypeUrl = "type.acme.example/acme.KeepRowsV1",
});
var custom = new ExtensionSingle(
    orders, detail, unmappedRecordType: orders.RecordType);
```

Here the application explicitly declares that `KeepRowsV1` preserves its
input's schema; the library does not assume this for single-input extensions.

All three operators also accept a generated protobuf `IMessage`, with or without
explicit metadata. The `message` constructor packs it immediately using
`Any.Pack` and the standard `type.googleapis.com` prefix, retaining an immutable
snapshot rather than the mutable message. For example, using a well-known
protobuf message as a demonstration payload:

```csharp
var message = new StringValue { Value = "custom configuration" };
var fromMessage = new ExtensionSingle(
    orders, message: message, unmappedRecordType: orders.RecordType);
message.Value = "changed"; // Does not change fromMessage.Detail.
```

The stored `Detail` remains a `ReadOnlyAny`. Passing an already-packed `Any`
copies it without wrapping it in another `Any`, preserving custom type URLs,
payload bytes, unknown fields, and empty-message presence. To choose a custom
type URL prefix, pack the message yourself and pass the resulting `Any`.
Message construction does not infer the relation's output schema from the
protobuf message type. Do not mutate a message concurrently with construction.

The `message` overload rejects null. Use `detail: null` for absent payloads,
for example `new ExtensionLeaf(detail: null)`, since a positional null is
ambiguous between the `IMessage` and `ReadOnlyAny` overloads.

For imports, implement `IExtensionRelationSchemaResolver`:

```csharp
using System.Runtime.Serialization;
using Substrait.Core.Metadata;
using Substrait.Core.Relation;
using Struct = Substrait.Core.Type.ParameterizedType.Struct;

public sealed class AcmeSchemaResolver : IExtensionRelationSchemaResolver
{
    public Struct? Resolve(
        ExtensionRelationKind kind,
        ReadOnlyAny? detail,
        IReadOnlyList<IRel> inputs)
    {
        if (detail?.TypeUrl != "type.acme.example/acme.KeepRowsV1")
        {
            return null;
        }

        if (kind != ExtensionRelationKind.Single || inputs.Count != 1)
        {
            throw new SerializationException("KeepRowsV1 requires exactly one input.");
        }

        return inputs[0].RecordType;
    }
}
```

Pass the resolver to `new ProtoToPlanConverter(extensions, resolver)` or
`new ProtoToRelConverter(lookup, extensions, resolver, strictMode)`.
Here `extensions` is the existing function/type `ExtensionsCollection` and
`lookup` is the plan's `ExtensionsDictionary`. All plan input entry points and
nested subqueries use the same resolver. Existing constructor overloads use no
resolver. Function/type `StrictMode` does not change custom-relation resolution.

The resolver runs once per extension occurrence, bottom-up, and receives
immutable detail and ordered, already-converted inputs. Return `null` for an
unknown schema, or throw for a recognized but invalid extension. Implementations
should be deterministic, and safe for concurrent calls if the converter is
shared. Input schemas are not evaluated unless the resolver requests them:
a fixed output schema may be resolved even when an input's schema is unknown.
An application may decode a known detail message, but the library does not
require descriptors or retain the resolver in the resulting relation.

`UnmappedRecordType` stores the result as an immutable value. It is the schema
**before this relation's emit mapping**; input `RecordType` values already
include their respective mappings. For example, an unmapped schema
`[i64, string, bool]` with emit `[2, 0]` exposes `[bool, i64]` through
`RecordType`. Mapping is applied once by the relation base class.

A null schema means unresolved, not zero columns. An actual empty struct means
zero columns. Unresolved extensions can be traversed, compared, hashed, and
exported without a schema. Reading their `RecordType` throws
`ExtensionSchemaUnavailableException` identifying the variant and type URL.
Schema-dependent import, such as a filter above an unresolved extension, fails
with a contextual `SerializationException` retaining that cause. This is not
schema-free import of arbitrary plans; the existing expression model still
requires input types.

Equality and hashing include the supplied unmapped schema, detail, metadata,
and ordered inputs, without deriving schemas. Resolved and unresolved models
are distinct. A schema is not separately serialized or written into the detail;
reimport with the same contract to recover it. Opaque binary round trips need
no payload descriptors; standard protobuf JSON still requires a type registry.

## Plan extension metadata

`IPlan.Metadata` exposes an immutable `PlanMetadata` with:

- `AdvancedExtensions`: a nullable `ReadOnlyAdvancedExtension` corresponding
  to `Plan.advanced_extensions`, separate from relation-level extensions.
- `ExpectedTypeUrls`: an immutable, ordered list corresponding to
  `Plan.expected_type_urls`. Duplicates, case, and empty strings are preserved;
  null list elements are rejected because protobuf strings cannot be null.

Use the existing `PlanBuilder` to edit extensions while composing a plan; no
immutable metadata object is required upfront. Callers explicitly choose whether
a payload is an enhancement or an optimization; the library does not infer its
meaning or unpack it:

```csharp
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;

var schema = new Substrait.Core.Type.NamedStruct(
    ["order_id"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]));
var orders = new NamedTableRead(schema, ["orders"], filter: null);
var builder = new PlanBuilder();
builder.AddRoot(orders, ["order_id"]);
var configuration = new StringValue { Value = "required configuration" };
builder.SetEnhancement(configuration);
builder.AddOptimization(new StringValue { Value = "optional optimization" });
builder.AddExpectedTypeUrl("type.googleapis.com/google.protobuf.StringValue");
IPlan plan = builder.Build();

configuration.Value = "changed";
builder.ClearEnhancement();
// Neither the source mutation nor the builder edit changes the earlier plan.
string typeUrl = plan.Metadata.AdvancedExtensions!.Enhancement!.TypeUrl;
Substrait.Protobuf.Plan protobuf = new PlanToProtoConverter().From(plan);
IPlan roundTrip = new ProtoToPlanConverter().From(protobuf);
```

Builder editing methods are:

| Purpose | Methods |
| --- | --- |
| Entire advanced-extension message | `SetAdvancedExtensions`, `ClearAdvancedExtensions` |
| Enhancement payload | `SetEnhancement`, `ClearEnhancement` |
| Ordered optimization payloads | `AddOptimization`, `ReplaceOptimization`, `RemoveOptimizationAt`, `ClearOptimizations` |
| Ordered expected type URLs | `SetExpectedTypeUrls`, `AddExpectedTypeUrl`, `ReplaceExpectedTypeUrl`, `RemoveExpectedTypeUrlAt`, `ClearExpectedTypeUrls` |

Payload setters accept either `ReadOnlyAny` or `IMessage`. Ordinary messages are
packed using the standard `type.googleapis.com` prefix; an existing `Any` is
copied without repacking, preserving custom type URLs and unknown fields.
`SetAdvancedExtensions` accepts either `ReadOnlyAdvancedExtension` or the
protobuf `AdvancedExtension` message. Public message inputs and URL collections
are snapshotted when accepted. Setters reject null; use the explicit clear
methods instead. Replacement and removal indexes are zero-based and must exist.
Failed edits do not change the builder's metadata.

`builder.Metadata` exposes the current immutable snapshot for inspection.
Each edit replaces that snapshot, leaving previous snapshots and built plans
unchanged. An unchanged `Build()` reuses the current metadata rather than
recopying it. Editing an advanced-extension field preserves the other fields
and unknown protobuf fields. Expected type URLs are edited independently and
are never inferred from payloads. Builders are not thread-safe.

Setting or adding a payload creates the advanced-extension message if absent.
Removing the last payload leaves a present empty message; only
`ClearAdvancedExtensions()` removes the whole message, without changing expected
type URLs. Clearing an enhancement or optimization list when the message is
already absent leaves it absent.

Callers with existing immutable metadata can still construct `PlanMetadata` from
a `ReadOnlyAdvancedExtension` or a protobuf `AdvancedExtension`. The protobuf
constructor snapshots the entire message, including nested payloads and unknown
fields. That metadata can be supplied to
`new Plan(metadata, roots, version)` or
`Plan.FromRelations(metadata, relations, version)`, or used as the starting point
for `new PlanBuilder(metadata, version)`. The builder's metadata-first overload
requires an explicit `version` argument (null selects the current version), so
the existing `new PlanBuilder(null)` call remains unambiguous.

`PlanMetadata.Empty` has absent advanced extensions and no expected type URLs.
Existing plan constructors, factories, and builders use it by default.
`new PlanMetadata(new AdvancedExtension())` instead preserves a present empty
message. To explicitly pass an absent facade, use
`new PlanMetadata(advancedExtensions: null)`; the protobuf `message` overload
rejects null. A null expected-URL collection means an empty list.

Metadata and plan equality/hashing include advanced-extension presence and
contents, optimization order, and the exact ordered expected type URLs.
Adding metadata can therefore make two otherwise identical plans unequal.
Neither construction nor conversion sorts, deduplicates, validates descriptor
availability, or infers the expected type URLs from payloads. The list may
contain unused or ignorable message types, and does not require a matching
payload to appear in the plan.

All plan import entry points preserve these fields. `From(Protobuf.Plan)` copies
caller-owned messages; binary and JSON entry points privately own their parsed
messages and can retain the facades without extra cloning. Exports always return
detached protobuf messages. Do not mutate input messages or collections
concurrently with construction/import.

Unknown payloads survive binary round trips without descriptors. Protobuf JSON
still needs the payload descriptors in a type registry; `expected_type_urls`
does not provide descriptors or populate that registry. Opaque enhancements are
preserved, not accepted as executable: consumers must understand required
enhancements or reject them. Function/type extension `StrictMode` does not
change advanced-extension preservation. Simple extension declarations and URNs
remain a separate mechanism with unchanged behavior.

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

Custom `IPlan` implementations must add a non-null `PlanMetadata Metadata`
property and recompile. Use `PlanMetadata.Empty` when no plan extensions are
present. This is a source/binary compatibility change for custom interface
implementations in the preview API; existing `Plan` and `PlanBuilder` constructor
signatures remain available. Export explicitly rejects null plan metadata.

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
