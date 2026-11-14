// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using Substrait.Tools.Visitor;
using Empty = Google.Protobuf.WellKnownTypes.Empty;
using NullableType = Substrait.Core.Type.IType.NullableType;
using ProtoType = Substrait.Protobuf.Type;
using TypeKind = Substrait.Protobuf.Type.KindOneofCase;

namespace Substrait.Tests.Core;

public sealed class TypeKindCoverageTests
{
    private const string Urn = "extension:example:type-kind-coverage";
    private static readonly TypeAnchor Shape = new(Urn, "shape");

    private static readonly IReadOnlyDictionary<TypeKind, Func<TypeFactory, bool, IType>> Kinds = new Dictionary<TypeKind, Func<TypeFactory, bool, IType>>
    {
        [TypeKind.Bool] = (f, v) => f.Boolean_(Variation(f.BOOL, v)),
        [TypeKind.I8] = (f, v) => f.I8_(Variation(f.I8, v)),
        [TypeKind.I16] = (f, v) => f.I16_(Variation(f.I16, v)),
        [TypeKind.I32] = (f, v) => f.I32_(Variation(f.I32, v)),
        [TypeKind.I64] = (f, v) => f.I64_(Variation(f.I64, v)),
        [TypeKind.Fp32] = (f, v) => f.FP32_(Variation(f.FP32, v)),
        [TypeKind.Fp64] = (f, v) => f.FP64_(Variation(f.FP64, v)),
        [TypeKind.String] = (f, v) => f.String_(Variation(f.STR, v)),
        [TypeKind.Binary] = (f, v) => f.Binary_(Variation(f.BINARY, v)),
        [TypeKind.Date] = (f, v) => f.Date_(Variation(f.DATE, v)),
        [TypeKind.IntervalYear] = (f, v) => f.IntervalYear_(Variation(f.INTERVAL_YEAR, v)),
        [TypeKind.IntervalDay] = (f, v) => f.IntervalDay(3, Variation(f.IntervalDay(3), v)),
        [TypeKind.IntervalCompound] = (f, v) => f.IntervalCompound(6, Variation(f.IntervalCompound(6), v)),
        [TypeKind.Uuid] = (f, v) => f.Uuid_(Variation(f.UUID, v)),
        [TypeKind.FixedChar] = (f, v) => f.FixedChar(8, Variation(f.FixedChar(8), v)),
        [TypeKind.Varchar] = (f, v) => f.VarChar(16, Variation(f.VarChar(16), v)),
        [TypeKind.FixedBinary] = (f, v) => f.FixedBinary(4, Variation(f.FixedBinary(4), v)),
        [TypeKind.Decimal] = (f, v) => f.Decimal(38, 10, Variation(f.Decimal(38, 10), v)),
        [TypeKind.PrecisionTime] = (f, v) => f.PrecisionTime(9, Variation(f.PrecisionTime(9), v)),
        [TypeKind.PrecisionTimestamp] = (f, v) => f.PrecisionTimestamp(3, Variation(f.PrecisionTimestamp(3), v)),
        [TypeKind.PrecisionTimestampTz] = (f, v) => f.PrecisionTimestampTZ(6, Variation(f.PrecisionTimestampTZ(6), v)),
        [TypeKind.Struct] = (f, v) => f.Struct(StructFields(), Variation(f.Struct(StructFields()), v)),
        [TypeKind.List] = (f, v) => f.List(TypeFactory.NULLABLE.I32, Variation(f.List(TypeFactory.NULLABLE.I32), v)),
        [TypeKind.Map] = (f, v) => f.Map(TypeFactory.REQUIRED.STR, TypeFactory.NULLABLE.I64, Variation(f.Map(TypeFactory.REQUIRED.STR, TypeFactory.NULLABLE.I64), v)),
        [TypeKind.Func] = (f, _) => f.Func([TypeFactory.REQUIRED.I32, TypeFactory.NULLABLE.STR], TypeFactory.NULLABLE.BOOL),
        [TypeKind.Unbound] = (_, _) => UnboundType.Instance,
        [TypeKind.UserDefined] = (f, v) => f.UserDefined(Shape, UserDefinedParameters(), Variation(f.UserDefined(Shape, UserDefinedParameters()), v)),
        [TypeKind.Alias] = (f, _) => f.AliasReference(7),
    };

    public enum Position
    {
        StructField,
        ListElement,
        MapKey,
        MapValue,
        UserDefinedParameter,
        FunctionParameter,
        FunctionReturn,
    }

    [Test]
    public async Task InventoryCoversEveryTypeKindInThePinnedSchema()
    {
        TypeKind[] schemaKinds = Enum.GetValues<TypeKind>().Where(kind => kind != TypeKind.None).Order().ToArray();

        await Assert.That(Kinds.Keys.Order().ToArray()).IsEquivalentTo(schemaKinds);
        foreach (var (kind, create) in Kinds)
        {
            await Assert.That(new TypeToProtoConverter().From(create(TypeFactory.REQUIRED, false), new PlanToProtoConverter.ConverterContext()).KindCase).IsEqualTo(kind);
        }
    }

    [Test]
    [MethodDataSource(nameof(RootCases))]
    public async Task RoundTripsEveryKindAtTheRoot(TypeKind kind, NullableType nullable, bool withVariation)
    {
        await AssertRoundTrips(Create(kind, nullable, withVariation));
    }

    [Test]
    [MethodDataSource(nameof(RootCases))]
    public async Task MatchesTheHandWrittenWireFormOfEveryKind(TypeKind kind, NullableType nullable, bool withVariation)
    {
        IType type = Create(kind, nullable, withVariation);
        ProtoType expected = WireForm(kind, (ProtoType.Types.Nullability)nullable, withVariation ? 1u : 0u);

        await Assert.That(new TypeToProtoConverter().From(type, new PlanToProtoConverter.ConverterContext())).IsEqualTo(expected);
        foreach (ProtoType wire in Serialized(expected))
        {
            IType imported = HandWrittenImport(type).From(wire);
            await Assert.That(imported).IsEqualTo(type);
            await Assert.That(new TypeToProtoConverter().From(imported, new PlanToProtoConverter.ConverterContext())).IsEqualTo(expected);
        }
    }

    [Test]
    [MethodDataSource(nameof(PositionCases))]
    public async Task RoundTripsEveryKindInEveryContainerPosition(TypeKind kind, Position position)
    {
        await AssertRoundTrips(Wrap(position, Create(kind, NullableType.Nullable, withVariation: true)));
    }

    [Test]
    [MethodDataSource(nameof(DeepCompositeCases))]
    public async Task RoundTripsDeepMixedCompositesPreservingEveryNode(IType composite)
    {
        await AssertRoundTrips(composite);
    }

    [Test]
    [MethodDataSource(nameof(ParameterBoundaryCases))]
    public async Task RoundTripsParameterBoundaryValues(IType type)
    {
        await AssertRoundTrips(type);
    }

    [Test]
    public async Task PreservesEveryUserDefinedParameterCaseIncludingDefaultValuedPayloads()
    {
        IType type = TypeFactory.REQUIRED.UserDefined(Shape, UserDefinedParameters());
        ProtoType proto = new TypeToProtoConverter().From(type, new PlanToProtoConverter.ConverterContext());

        foreach (ProtoType wire in Serialized(proto))
        {
            await Assert.That(wire.UserDefined.TypeParameters.Select(parameter => parameter.ParameterCase).SequenceEqual(
                [
                    ProtoType.Types.Parameter.ParameterOneofCase.Null,
                    ProtoType.Types.Parameter.ParameterOneofCase.DataType,
                    ProtoType.Types.Parameter.ParameterOneofCase.Boolean,
                    ProtoType.Types.Parameter.ParameterOneofCase.Integer,
                    ProtoType.Types.Parameter.ParameterOneofCase.Enum,
                    ProtoType.Types.Parameter.ParameterOneofCase.String,
                ])).IsTrue();
        }

        await AssertRoundTrips(type);
    }

    [Test]
    public async Task PreservesMeaningWhenTheWireUsesNonCanonicalExtensionAnchors()
    {
        var variation = new TypeVariationImpl(Urn, Shape.Key, "shape_variation", string.Empty, FunctionBehavior.INHERITS);
        var plan = new Substrait.Protobuf.Plan
        {
            ExtensionUrns = { new Substrait.Protobuf.SimpleExtensionURN { ExtensionUrnAnchor = 5, Urn = Urn } },
            Extensions =
            {
                new Substrait.Protobuf.SimpleExtensionDeclaration { ExtensionType = new() { ExtensionUrnReference = 5, TypeAnchor = 29, Name = Shape.Key } },
                new Substrait.Protobuf.SimpleExtensionDeclaration { ExtensionTypeVariation = new() { ExtensionUrnReference = 5, TypeVariationAnchor = 7, Name = variation.Name } },
            },
        };
        var original = new ProtoType
        {
            List = new ProtoType.Types.List
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Type = new ProtoType { UserDefined = new ProtoType.Types.UserDefined { TypeReference = 29, TypeVariationReference = 7, Nullability = ProtoType.Types.Nullability.Nullable } },
            },
        };
        IType imported = new ProtoToTypeConverter(new ExtensionsDictionary.Builder(plan).Build(), new ExtensionsCollection([new TypeDefinition(Shape)], [variation], [], [], [])).From(original);

        var context = new PlanToProtoConverter.ConverterContext();
        ProtoType exported = new TypeToProtoConverter().From(imported, context);

        await Assert.That(exported.List.Type.UserDefined.TypeReference).IsNotEqualTo(29u);
        await Assert.That(Import(context, imported).From(exported)).IsEqualTo(imported);
        await Assert.That(imported).IsEqualTo(TypeFactory.REQUIRED.List(TypeFactory.NULLABLE.UserDefined(Shape, [], variation)));
    }

    [Test]
    [MethodDataSource(nameof(Positions))]
    public async Task RejectsAnUnsetKindInEveryContainerPosition(Position position)
    {
        var context = new PlanToProtoConverter.ConverterContext();
        IType placeholder = TypeFactory.REQUIRED.I8;
        ProtoType proto = new TypeToProtoConverter().From(Wrap(position, placeholder), context);
        ProtoType child = position switch
        {
            Position.StructField => proto.Struct.Types_[1],
            Position.ListElement => proto.List.Type,
            Position.MapKey => proto.Map.Key,
            Position.MapValue => proto.Map.Value,
            Position.UserDefinedParameter => proto.UserDefined.TypeParameters[1].DataType,
            Position.FunctionParameter => proto.Func.ParameterTypes[1],
            Position.FunctionReturn => proto.Func.ReturnType,
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };
        child.ClearKind();

        await Assert.That(() => Import(context, Wrap(position, placeholder)).From(proto)).ThrowsExactly<SerializationException>();
    }

    public static IEnumerable<(TypeKind Kind, NullableType Nullable, bool WithVariation)> RootCases()
    {
        foreach (TypeKind kind in Kinds.Keys)
        {
            NullableType[] nullabilities = kind == TypeKind.Unbound ? [NullableType.Required] : [NullableType.Required, NullableType.Nullable];
            foreach (NullableType nullable in nullabilities)
            {
                yield return (kind, nullable, false);
                if (SupportsVariation(kind))
                {
                    yield return (kind, nullable, true);
                }
            }
        }
    }

    public static IEnumerable<(TypeKind Kind, Position Position)> PositionCases() =>
        Kinds.Keys.SelectMany(kind => Enum.GetValues<Position>().Select(position => (kind, position)));

    public static IEnumerable<Position> Positions() => Enum.GetValues<Position>();

    public static IEnumerable<Func<IType>> DeepCompositeCases()
    {
        yield return () => TypeFactory.NULLABLE.Struct(Kinds.Keys.Select(kind => Create(kind, NullableType.Nullable, SupportsVariation(kind))));
        yield return () => TypeFactory.REQUIRED.Map(
            Create(TypeKind.Decimal, NullableType.Required, withVariation: true),
            TypeFactory.NULLABLE.List(
                TypeFactory.REQUIRED.UserDefined(
                    Shape,
                    [new TypeParameter.Integer(0), new TypeParameter.DataType(TypeFactory.NULLABLE.Struct([
                        Create(TypeKind.PrecisionTimestampTz, NullableType.Nullable, withVariation: true),
                        TypeFactory.REQUIRED.Func([TypeFactory.NULLABLE.AliasReference(3), UnboundType.Instance], TypeFactory.REQUIRED.List(TypeFactory.NULLABLE.AliasReference(4))),
                    ]))],
                    Variation(TypeFactory.REQUIRED.UserDefined(Shape, []), true)),
                Variation(TypeFactory.NULLABLE.List(TypeFactory.REQUIRED.I8), true)));
        yield return () =>
        {
            IType type = Create(TypeKind.Varchar, NullableType.Nullable, withVariation: true);
            for (int level = 0; level < 20; ++level)
            {
                type = (level % 4) switch
                {
                    0 => TypeFactory.NULLABLE.List(type),
                    1 => TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.I32, type),
                    2 => TypeFactory.NULLABLE.Struct([UnboundType.Instance, type]),
                    _ => TypeFactory.REQUIRED.Func([type], TypeFactory.NULLABLE.AliasReference((uint)level)),
                };
            }

            return type;
        };
    }

    public static IEnumerable<Func<IType>> ParameterBoundaryCases()
    {
        foreach (TypeFactory f in new[] { TypeFactory.REQUIRED, TypeFactory.NULLABLE })
        {
            foreach (int precision in new[] { 0, 12 })
            {
                yield return () => f.PrecisionTime(precision);
                yield return () => f.PrecisionTimestamp(precision);
                yield return () => f.PrecisionTimestampTZ(precision);
                yield return () => f.IntervalDay(precision);
                yield return () => f.IntervalCompound(precision);
            }

            yield return () => f.Decimal(1, 0);
            yield return () => f.Decimal(38, 0);
            yield return () => f.Decimal(38, 38);
            yield return () => f.FixedChar(1);
            yield return () => f.VarChar(1);
            yield return () => f.FixedBinary(1);
            yield return () => f.FixedChar(int.MaxValue);
            yield return () => f.VarChar(int.MaxValue);
            yield return () => f.FixedBinary(int.MaxValue);
        }
    }

    private static async Task AssertRoundTrips(IType type)
    {
        var context = new PlanToProtoConverter.ConverterContext();
        ProtoType proto = new TypeToProtoConverter().From(type, context);
        ProtoToTypeConverter import = Import(context, type);

        foreach (ProtoType wire in Serialized(proto))
        {
            IType imported = import.From(wire);
            await Assert.That(imported.Equals(type, ITypeComparison.Strict)).IsTrue();
            await Assert.That(imported).IsEqualTo(type);

            var reexportContext = new PlanToProtoConverter.ConverterContext();
            ProtoType reexported = new TypeToProtoConverter().From(imported, reexportContext);
            await Assert.That(reexported).IsEqualTo(proto);
            await Assert.That(Import(reexportContext, imported).From(reexported)).IsEqualTo(imported);
        }
    }

    private static ProtoType WireForm(TypeKind kind, ProtoType.Types.Nullability n, uint v)
    {
        const ProtoType.Types.Nullability required = ProtoType.Types.Nullability.Required;
        const ProtoType.Types.Nullability nullable = ProtoType.Types.Nullability.Nullable;
        return kind switch
        {
            TypeKind.Bool => new() { Bool = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.I8 => new() { I8 = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.I16 => new() { I16 = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.I32 => new() { I32 = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.I64 => new() { I64 = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.Fp32 => new() { Fp32 = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.Fp64 => new() { Fp64 = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.String => new() { String = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.Binary => new() { Binary = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.Date => new() { Date = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.IntervalYear => new() { IntervalYear = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.IntervalDay => new() { IntervalDay = new() { Precision = 3, Nullability = n, TypeVariationReference = v } },
            TypeKind.IntervalCompound => new() { IntervalCompound = new() { Precision = 6, Nullability = n, TypeVariationReference = v } },
            TypeKind.Uuid => new() { Uuid = new() { Nullability = n, TypeVariationReference = v } },
            TypeKind.FixedChar => new() { FixedChar = new() { Length = 8, Nullability = n, TypeVariationReference = v } },
            TypeKind.Varchar => new() { Varchar = new() { Length = 16, Nullability = n, TypeVariationReference = v } },
            TypeKind.FixedBinary => new() { FixedBinary = new() { Length = 4, Nullability = n, TypeVariationReference = v } },
            TypeKind.Decimal => new() { Decimal = new() { Precision = 38, Scale = 10, Nullability = n, TypeVariationReference = v } },
            TypeKind.PrecisionTime => new() { PrecisionTime = new() { Precision = 9, Nullability = n, TypeVariationReference = v } },
            TypeKind.PrecisionTimestamp => new() { PrecisionTimestamp = new() { Precision = 3, Nullability = n, TypeVariationReference = v } },
            TypeKind.PrecisionTimestampTz => new() { PrecisionTimestampTz = new() { Precision = 6, Nullability = n, TypeVariationReference = v } },
            TypeKind.Struct => new()
            {
                Struct = new()
                {
                    Types_ = { new ProtoType { I64 = new() { Nullability = required } }, new ProtoType { String = new() { Nullability = nullable } } },
                    Nullability = n,
                    TypeVariationReference = v,
                },
            },
            TypeKind.List => new() { List = new() { Type = new() { I32 = new() { Nullability = nullable } }, Nullability = n, TypeVariationReference = v } },
            TypeKind.Map => new()
            {
                Map = new()
                {
                    Key = new() { String = new() { Nullability = required } },
                    Value = new() { I64 = new() { Nullability = nullable } },
                    Nullability = n,
                    TypeVariationReference = v,
                },
            },
            TypeKind.Func => new()
            {
                Func = new()
                {
                    ParameterTypes = { new ProtoType { I32 = new() { Nullability = required } }, new ProtoType { String = new() { Nullability = nullable } } },
                    ReturnType = new() { Bool = new() { Nullability = nullable } },
                    Nullability = n,
                },
            },
            TypeKind.Unbound => new() { Unbound = new() },
            TypeKind.UserDefined => new()
            {
                UserDefined = new()
                {
                    TypeReference = 0,
                    TypeParameters =
                    {
                        new ProtoType.Types.Parameter { Null = new Empty() },
                        new ProtoType.Types.Parameter { DataType = new() { I32 = new() { Nullability = nullable } } },
                        new ProtoType.Types.Parameter { Boolean = false },
                        new ProtoType.Types.Parameter { Integer = 0 },
                        new ProtoType.Types.Parameter { Enum = string.Empty },
                        new ProtoType.Types.Parameter { String = string.Empty },
                    },
                    Nullability = n,
                    TypeVariationReference = v,
                },
            },
            TypeKind.Alias => new() { Alias = new() { TypeAliasReference_ = 7, Nullability = n } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static ProtoToTypeConverter HandWrittenImport(IType type)
    {
        var plan = new Substrait.Protobuf.Plan
        {
            ExtensionUrns = { new Substrait.Protobuf.SimpleExtensionURN { ExtensionUrnAnchor = 1, Urn = Urn } },
            Extensions = { new Substrait.Protobuf.SimpleExtensionDeclaration { ExtensionType = new() { ExtensionUrnReference = 1, TypeAnchor = 0, Name = Shape.Key } } },
        };
        TypeVariationImpl[] variations = type.TypeVariation is TypeVariationImpl variation ? [variation] : [];
        foreach (TypeVariationImpl declared in variations)
        {
            plan.Extensions.Add(new Substrait.Protobuf.SimpleExtensionDeclaration { ExtensionTypeVariation = new() { ExtensionUrnReference = 1, TypeVariationAnchor = 1, Name = declared.Name } });
        }

        return new ProtoToTypeConverter(
            new ExtensionsDictionary.Builder(plan).Build(),
            new ExtensionsCollection([new TypeDefinition(Shape)], variations, [], [], []));
    }

    private static IEnumerable<ProtoType> Serialized(ProtoType proto) =>
        [proto, ProtoType.Parser.ParseFrom(proto.ToByteArray()), ProtoType.Parser.ParseJson(proto.ToString())];

    private static IType Create(TypeKind kind, NullableType nullable, bool withVariation) => Kinds[kind](TypeFactory.Of(nullable), withVariation);

    private static bool SupportsVariation(TypeKind kind) => kind is not (TypeKind.Func or TypeKind.Alias or TypeKind.Unbound);

    private static IType Wrap(Position position, IType inner) => position switch
    {
        Position.StructField => TypeFactory.NULLABLE.Struct([TypeFactory.REQUIRED.I8, inner]),
        Position.ListElement => TypeFactory.REQUIRED.List(inner),
        Position.MapKey => TypeFactory.NULLABLE.Map(inner, TypeFactory.REQUIRED.STR),
        Position.MapValue => TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.I32, inner),
        Position.UserDefinedParameter => TypeFactory.REQUIRED.UserDefined(Shape, [new TypeParameter.Integer(1), new TypeParameter.DataType(inner)]),
        Position.FunctionParameter => TypeFactory.REQUIRED.Func([TypeFactory.REQUIRED.I64, inner], TypeFactory.REQUIRED.BOOL),
        Position.FunctionReturn => TypeFactory.NULLABLE.Func([TypeFactory.REQUIRED.I64], inner),
        _ => throw new ArgumentOutOfRangeException(nameof(position)),
    };

    private static IEnumerable<IType> StructFields() => [TypeFactory.REQUIRED.I64, TypeFactory.NULLABLE.STR];

    private static IEnumerable<TypeParameter> UserDefinedParameters() =>
    [
        new TypeParameter.Null(),
        new TypeParameter.DataType(TypeFactory.NULLABLE.I32),
        new TypeParameter.Boolean(false),
        new TypeParameter.Integer(0),
        new TypeParameter.Enum(string.Empty),
        new TypeParameter.String(string.Empty),
    ];

    private static TypeVariationImpl? Variation(IType plain, bool withVariation) =>
        withVariation ? new TypeVariationImpl(Urn, plain.TypeName, $"{plain.TypeName}_variation", string.Empty, FunctionBehavior.INHERITS) : null;

    private static ProtoToTypeConverter Import(PlanToProtoConverter.ConverterContext context, IType type)
    {
        var plan = new Substrait.Protobuf.Plan();
        ExtensionsCollector collected = context.ExtensionsCollector;
        plan.ExtensionUrns.AddRange(collected.ExtensionUris.Select((urn, index) =>
            new Substrait.Protobuf.SimpleExtensionURN { Urn = urn, ExtensionUrnAnchor = (uint)index + 1 }));
        int typeAnchor = -1;
        int variationAnchor = 0;
        foreach (var extension in collected.Extensions)
        {
            plan.Extensions.Add(extension.Type switch
            {
                ExtensionsCollector.ExtensionType.Type => new Substrait.Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionType = new() { TypeAnchor = (uint)++typeAnchor, Name = extension.Name, ExtensionUrnReference = (uint)extension.ExtensionUriReference + 1 },
                },
                ExtensionsCollector.ExtensionType.TypeVariation => new Substrait.Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionTypeVariation = new() { TypeVariationAnchor = (uint)++variationAnchor, Name = extension.Name, ExtensionUrnReference = (uint)extension.ExtensionUriReference + 1 },
                },
                _ => throw new InvalidOperationException("Unexpected extension kind in type conversion."),
            });
        }

        TypeVariationImpl[] variations = new TopDownTraversal<IType>().Traverse(type)
            .Select(node => node.TypeVariation)
            .OfType<TypeVariationImpl>()
            .Distinct()
            .ToArray();
        return new ProtoToTypeConverter(
            new ExtensionsDictionary.Builder(plan).Build(),
            new ExtensionsCollection([new TypeDefinition(Shape)], variations, [], [], []));
    }
}
