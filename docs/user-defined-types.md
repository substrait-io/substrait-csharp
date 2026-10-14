# User-defined types

`UserDefinedType` represents a type identified by an extension namespace (URN)
and name. Its ordered `TypeParameter` values preserve the six protobuf oneof
cases, including defaults:

| Case | C# payload | Notes |
| --- | --- | --- |
| `TypeParameter.Null` | None | Explicitly selects an extension default, if any; not an unset parameter |
| `TypeParameter.DataType` | `IType` | Uses the shared type converters and preserves nested metadata |
| `TypeParameter.Boolean` | `bool` | `false` is present |
| `TypeParameter.Integer` | `long` | Includes the full signed 64-bit range |
| `TypeParameter.Enum` | `string` | Distinct from String, including empty text |
| `TypeParameter.String` | `string` | Empty text is present |

Null list entries and null payloads are invalid. Use `TypeParameter.Null` rather
than C# `null`. Parameter collections are copied into immutable storage.

## Construction and conversion

This example constructs a type and round-trips it with explicit extension
contexts. The protobuf plan below is only a container for the input extension
declarations, not an executable plan.

```csharp
using Substrait.Core.Extension;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;

var identity = new TypeAnchor("extension:example:types", "shape");
var definition = new TypeDefinition(identity);
var extensions = new ExtensionsCollection([definition], [], [], [], []);

UserDefinedType type = TypeFactory.NULLABLE.UserDefined(
    identity,
    [
        new TypeParameter.Null(),
        new TypeParameter.DataType(TypeFactory.REQUIRED.I64),
        new TypeParameter.Boolean(false),
        new TypeParameter.Integer(long.MinValue),
        new TypeParameter.Enum("cartesian"),
        new TypeParameter.String(string.Empty),
    ],
    declaration: definition);

var exportContext = new PlanToProtoConverter.ConverterContext();
Substrait.Protobuf.Type wire = new TypeToProtoConverter().From(type, exportContext);

var declarations = new Substrait.Protobuf.Plan
{
    ExtensionUrns =
    {
        new Substrait.Protobuf.SimpleExtensionURN
        {
            ExtensionUrnAnchor = 1,
            Urn = identity.Namespace,
        },
    },
    Extensions =
    {
        new Substrait.Protobuf.SimpleExtensionDeclaration
        {
            ExtensionType = new()
            {
                ExtensionUrnReference = 1,
                TypeAnchor = wire.UserDefined.TypeReference,
                Name = identity.Key,
            },
        },
    },
};

var lookup = new ExtensionsDictionary.Builder(declarations).Build();
IType roundTrip = new ProtoToTypeConverter(lookup, extensions).From(wire);
bool equivalent = type.Equals(roundTrip); // true
```

When converting a type extracted from a plan or extended expression, construct
`ExtensionsDictionary` from its actual extension declarations. The outbound context exposes
`ExtensionsCollector`, including dependencies of every nested type-valued
parameter. Reuse one context when converting related types.

Type references and type-variation references occupy separate namespaces.
Type anchor **0 is valid**; variation reference **0 means no variation**.
An unseeded export context deterministically assigns and deduplicates anchors.
Core types preserve semantic identity, not per-reference numeric provenance;
full-plan conversion separately retains imported declaration tables.
A standalone protobuf `Type` has no extension declarations: the overload that
creates its own context cannot provide a self-contained extension-aware result.

## Plan conversion

`PlanToProtoConverter` emits the URNs and type declarations required by the
plan's user-defined types, including nested DataType parameters. It shares one
extension context across all root and non-root entries, deduplicating identities
while keeping type, type-variation, and function anchors independent.
`ProtoToPlanConverter` reads these declarations automatically; registered type
definitions still come from the supplied `ExtensionsCollection`.

Using `type` and `extensions` from the construction example above:

```csharp
using Substrait.Core.Relation;
using CorePlan = Substrait.Core.Plan.Plan;
using PlanVersion = Substrait.Core.Plan.Version;

var read = new NamedTableRead(
    new NamedStruct(["value"], TypeFactory.REQUIRED.Struct([type])),
    ["shapes"],
    null);
var plan = new CorePlan([new CorePlan.Root(read, ["value"])], PlanVersion.Current);

Substrait.Protobuf.Plan wirePlan = new PlanToProtoConverter().From(plan);
var restoredPlan = new ProtoToPlanConverter(extensions).From(wirePlan);
bool relationsEquivalent = plan.Relations[0].Equals(restoredPlan.Relations[0]); // true
```

The exported plan contains its reference declarations; it does not embed the
external type definitions. The strict/permissive resolution rules below apply
to full-plan imports as well as standalone type imports.

New plans export only dependencies used by the core plan. Imported plans retain
their complete URN/declaration tables in `PlanMetadata`, including ordering,
aliases, unknown fields, and apparently unused entries. Opaque extension
payloads may reference any of those anchors, so export neither removes nor
renumbers them. Core references use the smallest existing anchor for their
identity; new dependencies receive free anchors in the appropriate namespace.
No per-node anchor provenance or byte-for-byte round-trip guarantee is added.

Retained tables participate in plan metadata equality because their bindings
can change the meaning of opaque payloads. A newly constructed plan and its
imported export can therefore have equal relations but different metadata.
See [plan extension metadata](metadata-facades.md#plan-extension-metadata).

## Resolution and errors

`ExtensionsDictionary.StrictMode.TYPE` controls definition lookup independently
of `TYPE_VARIATION` and `FUNCTION`.

| Situation | TYPE enabled | TYPE disabled |
| --- | --- | --- |
| Declared numeric reference and registered definition | Resolve and attach `Declaration` | Resolve and attach `Declaration` |
| Declared URN/name, definition not registered | `ArgumentException` | Preserve the identity and parameters; `Declaration` is null |
| Numeric reference not declared | `ArgumentException` | `ArgumentException` |

Malformed declaration namespaces/names, missing URNs, duplicate numeric type
anchors, and duplicate registered type identities cause `ArgumentException`.
Definition duplicate checks occur when the collection's lookup is materialized,
including after merging collections. Repeating the same definition instance
is rejected just like registering separate definitions with the same identity.

An unset parameter oneof, unset data type in a DataType parameter, or unspecified
UDT nullability causes `SerializationException`. Failures inside DataType
parameters are reported as `SerializationException` with the original conversion
exception retained as `InnerException`. The message includes the containing
UDT identities, zero-based parameter indices, and intervening struct field
indices, for example:

```text
Deserialization error at user-defined type extension:example:types#shape parameter 1 -> struct field 0 -> user-defined type extension:example:types#details parameter 2: Day interval types must specify precision.
```

Conversions outside DataType parameters retain their existing exception types.
No malformed reference is replaced with another type.

Variation resolution keeps the existing policy: nonzero unresolved variations
fail when `TYPE_VARIATION` is enabled and become absent when it is disabled.
For lossless variation conversion, supply the variation declarations and
definitions and leave strict variation resolution enabled.

## Equality, traversal, and rewriting

Type identity always includes both namespace and name. Definition availability
does not affect equality. Ordinary UDT equality and hashing include ordered
parameter cases and values, nested type metadata, nullability, and variation.

The shared `ITypeComparison` modes can ignore nullability, scalar parameter
values, or variations recursively. They do not erase extension identity,
parameter case/order, or nested type structure. The shared comparer hashes the
same selected information, including for a UDT nested in a struct.

Custom `IType` implementations, including subclasses of `ParameterizedType`,
also participate in recursive comparison and hashing through `InputNodes`.
Child count, order, and structure remain significant in every comparison mode;
the leaf shortcut is limited to known built-in leaf types.

`InputNodes` exposes only DataType payloads, in parameter order. Top-down and
bottom-up type dispatchers therefore visit nested types normally. A rewriting
visitor can reconstruct the UDT by replacing each DataType payload with its
context output, retaining all other parameters, its identity, and metadata.
`TypeFactory.ResolveTypeWithNullability` changes only the outer nullability and
requested variation, preserving nested metadata and the attached definition.

The new visitor overload is virtual and falls back to `Visit(IType, ...)`.
Existing visitor subclasses do not need another abstract implementation.
`DefaultTypeVisitor` routes UDTs to `DefaultVisit`.

## Boundaries

- `TypeDefinition` currently registers a known identity only. It does not load
  YAML, declare parameter contracts, select default values, or validate enum
  membership. Those features belong to
  [substrait-io/substrait-csharp#46](https://github.com/substrait-io/substrait-csharp/issues/46).
- Retaining extension declaration tables does not add the other plan-envelope
  features tracked in
  [substrait-io/substrait-csharp#27](https://github.com/substrait-io/substrait-csharp/issues/27),
  such as type aliases or dynamic parameter bindings.
- User-defined literal payloads belong to
  [substrait-io/substrait-csharp#40](https://github.com/substrait-io/substrait-csharp/issues/40).
- DataType parameters support the kinds already supported by the shared type
  converters. This feature does not add unrelated built-in types or expand the
  extension type-expression parser.
