# Type support

Core type conversion covers every `Type.kind` alternative in the pinned
Substrait **0.107.0** specification, in both directions: protobuf to core to
protobuf, and core to protobuf to core, including protobuf binary and JSON
serialization. Nullability, type variations, precision, lengths, alias
anchors, and user-defined parameter cases are preserved at every nesting
level. A coverage test checks the supported kinds against the generated
`Type.KindOneofCase` enum, so a kind added by a future specification update
fails the build until it is handled.

## Support matrix

Factory members are on `TypeFactory.REQUIRED` and `TypeFactory.NULLABLE`.
Upper-case properties have no type variation; the methods accept one for kinds
that support it.

| `Type.kind` | Core type | Factory | Parameters | Variation |
| --- | --- | --- | --- | --- |
| `bool` | `PrimitiveType.Bool` | `BOOL`, `Boolean_` | | Yes |
| `i8` | `PrimitiveType.I8` | `I8`, `I8_` | | Yes |
| `i16` | `PrimitiveType.I16` | `I16`, `I16_` | | Yes |
| `i32` | `PrimitiveType.I32` | `I32`, `I32_` | | Yes |
| `i64` | `PrimitiveType.I64` | `I64`, `I64_` | | Yes |
| `fp32` | `PrimitiveType.FP32` | `FP32`, `FP32_` | | Yes |
| `fp64` | `PrimitiveType.FP64` | `FP64`, `FP64_` | | Yes |
| `string` | `PrimitiveType.Str` | `STR`, `String_` | | Yes |
| `binary` | `PrimitiveType.Binary` | `BINARY`, `Binary_` | | Yes |
| `date` | `PrimitiveType.Date` | `DATE`, `Date_` | | Yes |
| `interval_year` | `PrimitiveType.IntervalYear` | `INTERVAL_YEAR`, `IntervalYear_` | | Yes |
| `interval_day` | `PrimitiveType.IntervalDay` | `IntervalDay` | Precision 0-12 | Yes |
| `interval_compound` | `ParameterizedType.IntervalCompound` | `IntervalCompound` | Precision 0-12 | Yes |
| `uuid` | `PrimitiveType.Uuid` | `UUID`, `Uuid_` | | Yes |
| `fixed_char` | `ParameterizedType.FixedChar` | `FixedChar` | Length 1 or more | Yes |
| `varchar` | `ParameterizedType.VarChar` | `VarChar` | Length 1 or more | Yes |
| `fixed_binary` | `ParameterizedType.FixedBinary` | `FixedBinary` | Length 1 or more | Yes |
| `decimal` | `ParameterizedType.Decimal` | `Decimal` | Precision 1-38, scale 0 to precision | Yes |
| `precision_time` | `PrimitiveType.Time` | `PrecisionTime` | Precision 0-12 | Yes |
| `precision_timestamp` | `ParameterizedType.PrecisionTimestamp` | `PrecisionTimestamp` | Precision 0-12 | Yes |
| `precision_timestamp_tz` | `ParameterizedType.PrecisionTimestampTZ` | `PrecisionTimestampTZ` | Precision 0-12 | Yes |
| `struct` | `ParameterizedType.Struct` | `Struct` | Field types | Yes |
| `list` | `ParameterizedType.List` | `List` | Element type | Yes |
| `map` | `ParameterizedType.Map` | `Map` | Key and value types | Yes |
| `func` | `ParameterizedType.Func` | `Func` | Parameter and return types | No |
| `unbound` | `UnboundType` | `UnboundType.Instance` | | No |
| `user_defined` | `UserDefinedType` | `UserDefined` | Six parameter cases | Yes |
| `alias` | `ParameterizedType.AliasReference` | `AliasReference` | Alias anchor | No |

Out-of-range parameters throw `ArgumentOutOfRangeException`. `Func` and
`AliasReference` reject a non-null variation because their protobuf messages
have no variation field. `UnboundType` has no nullability; reading it throws.
See the per-type documents for details:
[list and map types](list-and-map-types.md),
[function types](function-types.md),
[the unbound placeholder type](unbound-type.md),
[user-defined types](user-defined-types.md),
[type-alias references](alias-references.md),
[type-alias resolution](alias-resolution.md), and
[UUID and temporal types](preview-package.md#uuid-and-temporal-types).

## Construction and conversion

Types without extension references convert with the default converters:

```csharp
using Google.Protobuf;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;

IType type = TypeFactory.REQUIRED.Struct([
    TypeFactory.REQUIRED.Decimal(18, 2),
    TypeFactory.NULLABLE.List(TypeFactory.NULLABLE.PrecisionTimestampTZ(6)),
    TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, TypeFactory.NULLABLE.UUID),
]);

Substrait.Protobuf.Type wire = new TypeToProtoConverter().From(type);
string json = JsonFormatter.Default.Format(wire);
IType roundTrip = new ProtoToTypeConverter().From(Substrait.Protobuf.Type.Parser.ParseJson(json));
bool identical = type.Equals(roundTrip); // true
```

## Extension and alias contexts

Type variations and user-defined types are extension references. Export
collects them into the `ExtensionsCollector` of the
`PlanToProtoConverter.ConverterContext` passed to `TypeToProtoConverter.From`;
reuse one context for related types. Import needs an `ExtensionsDictionary`
built from the declarations that accompany the protobuf type, plus an
`ExtensionsCollection` with the variation and type definitions. A standalone
protobuf `Type` carries no declarations. See
[user-defined types](user-defined-types.md#construction-and-conversion) for a
complete example; variations follow the same pattern.

Standalone conversion keeps an alias reference as an unresolved
`ParameterizedType.AliasReference`. Resolve references against a plan's
`type_aliases` with `TypeAliasTable`. `ProtoToTypeAliasTableConverter` accepts
the configured `ProtoToTypeConverter` for alias definitions that use
extensions, and `TypeAliasTableToProtoConverter` accepts the shared export
context. See [type-alias resolution](alias-resolution.md).

## Equality

Plain `Equals` and `GetHashCode` are strict for every type: they compare
nullability, parameters, and type variation at every node, the same as
`IType.Equals(IType, ITypeComparison.Strict)`. Use another `ITypeComparison`,
such as `IgnoreTypeVariation`, for a relaxed comparison;
`TypeUtils.ITypeEqualityComparer` provides the same comparisons for
collections.

## Errors

| Input | Root | Nested |
| --- | --- | --- |
| Unset `Type.kind` | `NotImplementedException` | `SerializationException` naming the path |
| Unspecified nullability | `NotImplementedException` | `SerializationException` naming the path |
| Undeclared variation anchor | `SerializationException` | `SerializationException` naming the path |
| Out-of-range parameter | `ArgumentOutOfRangeException` | `SerializationException` naming the path |

## Boundaries

- `ProtoToPlanConverter` rejects plans that declare `Plan.type_aliases`; full
  plan-envelope alias retention belongs to
  [substrait-io/substrait-csharp#27](https://github.com/substrait-io/substrait-csharp/issues/27).
- Extension YAML type expressions and type classes belong to
  [substrait-io/substrait-csharp#46](https://github.com/substrait-io/substrait-csharp/issues/46).
- Coverage uses small synthetic fixtures. An upstream conformance corpus and
  capability matrix belong to
  [substrait-io/substrait-csharp#48](https://github.com/substrait-io/substrait-csharp/issues/48).
- This covers type metadata, not literal values of these types.
