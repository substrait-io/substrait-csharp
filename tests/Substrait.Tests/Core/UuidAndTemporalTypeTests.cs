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
using NullableType = Substrait.Core.Type.IType.NullableType;
using ProtoType = Substrait.Protobuf.Type;
using TypeKind = Substrait.Protobuf.Type.KindOneofCase;

namespace Substrait.Tests.Core;

public sealed class UuidAndTemporalTypeTests
{
    [Test]
    [MethodDataSource(nameof(TypeCases))]
    public async Task RoundTripsTypesAndNestedMetadata(TypeKind kind, int precision, NullableType nullable, bool hasVariation)
    {
        var variation = hasVariation ? CreateVariation(kind) : null;
        IType type = CreateType(kind, precision, nullable, variation);
        ProtoType expected = CreateProto(kind, precision, nullable, hasVariation ? 1U : 0U);
        var import = CreateImportConverter(variation);
        var export = new TypeToProtoConverter();
        var context = new PlanToProtoConverter.ConverterContext();

        await Assert.That(export.From(type, context)).IsEqualTo(expected);
        await Assert.That(context.ExtensionsCollector.Extensions.Count).IsEqualTo(hasVariation ? 1 : 0);
        if (hasVariation)
        {
            await Assert.That(context.ExtensionsCollector.Extensions[0].Type).IsEqualTo(ExtensionsCollector.ExtensionType.TypeVariation);
            await Assert.That(context.ExtensionsCollector.ExtensionUris[0]).IsEqualTo(variation!.Namespace);
        }

        foreach (var proto in new[] { expected, ProtoType.Parser.ParseFrom(expected.ToByteArray()), ProtoType.Parser.ParseJson(expected.ToString()) })
        {
            IType actual = import.From(proto);
            await Assert.That(actual.Equals(type, ITypeComparison.Strict)).IsTrue();
            await Assert.That(actual).IsEqualTo(type);
            await Assert.That(actual.GetHashCode()).IsEqualTo(type.GetHashCode());
            await Assert.That(export.From(actual)).IsEqualTo(expected);
        }

        var nested = TypeFactory.NULLABLE.Struct([TypeFactory.REQUIRED.Struct([type]), TypeFactory.REQUIRED.I32]);
        ProtoType nestedProto = export.From(nested);
        IType nestedRoundTrip = import.From(nestedProto);
        await Assert.That(nestedRoundTrip.Equals(nested, ITypeComparison.Strict)).IsTrue();
        await Assert.That(export.From(nestedRoundTrip)).IsEqualTo(nestedProto);
        await Assert.That(nestedProto.Struct.Types_[0].Struct.Types_[0]).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(TypeCases))]
    public async Task RewritesNullabilityAndVariationsWithoutLosingPrecision(TypeKind kind, int precision, NullableType nullable, bool hasVariation)
    {
        var variation = hasVariation ? CreateVariation(kind) : null;
        IType type = CreateType(kind, precision, nullable, variation);
        var factory = TypeFactory.Of(nullable.Inverse());
        IType rewritten = factory.ResolveTypeWithNullability(type);
        await Assert.That(rewritten).IsEqualTo(CreateType(kind, precision, nullable.Inverse(), variation));

        var replacement = CreateVariation(kind, "replacement");
        IType replaced = factory.ResolveTypeWithNullability(type, replacement);
        await Assert.That(replaced).IsEqualTo(CreateType(kind, precision, nullable.Inverse(), replacement));
        await Assert.That(TypeFactory.Of(nullable).ResolveTypeWithNullability(type, replacement))
            .IsEqualTo(CreateType(kind, precision, nullable, replacement));
        await Assert.That(factory.ResolveTypeWithNullability(type, null))
            .IsEqualTo(CreateType(kind, precision, nullable.Inverse(), null));
        await Assert.That(type.TypeVariation).IsEqualTo(variation);
        await Assert.That(type.Nullable).IsEqualTo(nullable);
    }

    [Test]
    [Arguments(TypeKind.PrecisionTime)]
    [Arguments(TypeKind.IntervalDay)]
    [Arguments(TypeKind.IntervalCompound)]
    public async Task TemporalComparisonIncludesPrecisionNullabilityAndVariation(TypeKind kind)
    {
        IType original = CreateType(kind, 3, NullableType.Required, CreateVariation(kind));
        IType equal = CreateType(kind, 3, NullableType.Required, CreateVariation(kind));
        IType differentPrecision = CreateType(kind, 6, NullableType.Required, CreateVariation(kind));
        IType differentNullability = CreateType(kind, 3, NullableType.Nullable, CreateVariation(kind));
        IType differentVariation = CreateType(kind, 3, NullableType.Required, CreateVariation(kind, "different"));

        await Assert.That(original).IsEqualTo(equal);
        await Assert.That(original.GetHashCode()).IsEqualTo(equal.GetHashCode());
        foreach (IType different in new[] { differentPrecision, differentNullability, differentVariation })
        {
            await Assert.That(original).IsNotEqualTo(different);
            await Assert.That(original.Equals(different, ITypeComparison.Strict)).IsFalse();
        }

        await Assert.That(original.Equals(differentPrecision, ITypeComparison.IgnoreTypeParameters)).IsTrue();
        await Assert.That(original.Equals(differentNullability, ITypeComparison.TypeParameter | ITypeComparison.TypeVariation)).IsTrue();
        await Assert.That(original.Equals(differentVariation, ITypeComparison.IgnoreTypeVariation)).IsTrue();
        await Assert.That(original.ToTypeString()).Contains("<3>");
    }

    [Test]
    [Arguments(-1)]
    [Arguments(13)]
    [Arguments(int.MinValue)]
    [Arguments(int.MaxValue)]
    public async Task RejectsInvalidTemporalPrecision(int precision)
    {
        foreach (var kind in new[] { TypeKind.PrecisionTime, TypeKind.IntervalDay, TypeKind.IntervalCompound })
        {
            ArgumentOutOfRangeException error = await Assert.That(() => CreateType(kind, precision, NullableType.Required, null))
                .ThrowsExactly<ArgumentOutOfRangeException>().And.IsNotNull();
            await Assert.That(error.ParamName).IsEqualTo("precision");
            await Assert.That(() => CreateImportConverter(null).From(CreateProto(kind, precision, NullableType.Required, 0)))
                .ThrowsExactly<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task PreservesLegacyTemporalApisAndDefaults()
    {
        foreach (var factory in new[] { TypeFactory.REQUIRED, TypeFactory.NULLABLE })
        {
            await Assert.That(ReferenceEquals(factory.TIME, factory.PrecisionTime(6))).IsTrue();
            await Assert.That(ReferenceEquals(factory.TIME, factory.Time_(null))).IsTrue();
            await Assert.That(ReferenceEquals(factory.TIME, PrimitiveType.Time.Of(factory.TIME.Nullable))).IsTrue();
            await Assert.That(ReferenceEquals(factory.INTERVAL_DAY, factory.IntervalDay(0))).IsTrue();
            await Assert.That(ReferenceEquals(factory.INTERVAL_DAY, factory.IntervalDay_(null))).IsTrue();
            await Assert.That(ReferenceEquals(factory.INTERVAL_DAY, PrimitiveType.IntervalDay.Of(factory.INTERVAL_DAY.Nullable))).IsTrue();
            await Assert.That(factory.TIME.Precision).IsEqualTo(6);
            await Assert.That(factory.INTERVAL_DAY.Precision).IsEqualTo(0);
            await Assert.That(factory.TIME.ToTypeString()).IsEqualTo("time");
            await Assert.That(factory.INTERVAL_DAY.ToTypeString()).IsEqualTo("interval_day");
            await Assert.That(factory.TIME.TypeName).IsEqualTo("time");
            await Assert.That(factory.INTERVAL_DAY.TypeName).IsEqualTo("interval_day");
            await Assert.That(new TypeToProtoConverter().From(factory.INTERVAL_DAY).IntervalDay.HasPrecision).IsTrue();
        }

        var legacyVariation = new TypeVariationImpl("extension:example:types", "time", "legacy", string.Empty, FunctionBehavior.INHERITS);
        await Assert.That(TypeFactory.REQUIRED.Time_(legacyVariation).Precision).IsEqualTo(6);
        await Assert.That(TypeFactory.REQUIRED.PrecisionTime(9, legacyVariation).TypeVariation).IsEqualTo(legacyVariation);
        await Assert.That(TypeFactory.REQUIRED.PrecisionTime(9).ToString()).IsEqualTo("pt<9>");
        await Assert.That(TypeFactory.NULLABLE.IntervalDay(3).ToString()).IsEqualTo("iday?<3>");
    }

    [Test]
    public async Task UuidIsDistinctFromBinaryAndStringAndHonorsVariations()
    {
        var type = TypeFactory.REQUIRED.UUID;
        foreach (IType other in new IType[] { TypeFactory.REQUIRED.BINARY, TypeFactory.REQUIRED.FixedBinary(16), TypeFactory.REQUIRED.STR })
        {
            await Assert.That(type.Equals(other, ITypeComparison.Strict)).IsFalse();
            await Assert.That(type.Equals(other, ITypeComparison.IgnoreNullability)).IsFalse();
        }

        await Assert.That(type.TypeName).IsEqualTo("uuid");
        await Assert.That(type.ToTypeString()).IsEqualTo("uuid");
        await Assert.That(TypeFactory.NULLABLE.UUID.ToString()).IsEqualTo("uuid?");
        var varied = TypeFactory.REQUIRED.Uuid_(CreateVariation(TypeKind.Uuid));
        await Assert.That(varied).IsNotEqualTo(type);
        await Assert.That(varied.Equals(type, ITypeComparison.Strict)).IsFalse();
        await Assert.That(varied.Equals(type, ITypeComparison.IgnoreTypeVariation)).IsTrue();
        await Assert.That(TypeFactory.NULLABLE.ResolvePrimitiveTypeWithNullability(varied))
            .IsEqualTo(TypeFactory.NULLABLE.Uuid_(CreateVariation(TypeKind.Uuid)));
        await Assert.That(() => TypeFactory.REQUIRED.Uuid_(CreateVariation(TypeKind.IntervalDay)))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments(TypeKind.Uuid)]
    [Arguments(TypeKind.PrecisionTime)]
    [Arguments(TypeKind.IntervalDay)]
    [Arguments(TypeKind.IntervalCompound)]
    public async Task RejectsUnspecifiedNullabilityAndUnresolvedVariations(TypeKind kind)
    {
        var converter = CreateImportConverter(null);
        await Assert.That(() => converter.From(CreateProto(kind, 3, NullableType.Unspecified, 0)))
            .ThrowsExactly<NotImplementedException>();
        await Assert.That(() => converter.From(CreateProto(kind, 3, NullableType.Required, 42)))
            .ThrowsExactly<SerializationException>();
    }

    public static IEnumerable<(TypeKind Kind, int Precision, NullableType Nullable, bool HasVariation)> TypeCases()
    {
        foreach (var kind in new[] { TypeKind.Uuid, TypeKind.PrecisionTime, TypeKind.IntervalDay, TypeKind.IntervalCompound })
        {
            foreach (int precision in kind == TypeKind.Uuid ? [0] : Enumerable.Range(0, 13))
            {
                foreach (var nullable in new[] { NullableType.Required, NullableType.Nullable })
                {
                    yield return (kind, precision, nullable, false);
                    yield return (kind, precision, nullable, true);
                }
            }
        }
    }

    private static IType CreateType(TypeKind kind, int precision, NullableType nullable, ITypeVariation? variation)
    {
        var factory = TypeFactory.Of(nullable);
        return kind switch
        {
            TypeKind.Uuid => factory.Uuid_(variation),
            TypeKind.PrecisionTime => factory.PrecisionTime(precision, variation),
            TypeKind.IntervalDay => factory.IntervalDay(precision, variation),
            TypeKind.IntervalCompound => factory.IntervalCompound(precision, variation),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static ProtoType CreateProto(TypeKind kind, int precision, NullableType nullable, uint variation)
    {
        var protoNullable = (ProtoType.Types.Nullability)nullable;
        return kind switch
        {
            TypeKind.Uuid => new() { Uuid = new() { Nullability = protoNullable, TypeVariationReference = variation } },
            TypeKind.PrecisionTime => new() { PrecisionTime = new() { Precision = precision, Nullability = protoNullable, TypeVariationReference = variation } },
            TypeKind.IntervalDay => new() { IntervalDay = new() { Precision = precision, Nullability = protoNullable, TypeVariationReference = variation } },
            TypeKind.IntervalCompound => new() { IntervalCompound = new() { Precision = precision, Nullability = protoNullable, TypeVariationReference = variation } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static TypeVariationImpl CreateVariation(TypeKind kind, string name = "custom")
    {
        string parent = kind switch
        {
            TypeKind.Uuid => "uuid",
            TypeKind.PrecisionTime => "precision_time",
            TypeKind.IntervalDay => "interval_day",
            TypeKind.IntervalCompound => "interval_compound",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new TypeVariationImpl("extension:example:types", parent, name, string.Empty, FunctionBehavior.INHERITS);
    }

    private static ProtoToTypeConverter CreateImportConverter(TypeVariationImpl? variation)
    {
        var plan = new Substrait.Protobuf.Plan();
        if (variation is not null)
        {
            plan.ExtensionUrns.Add(new Substrait.Protobuf.SimpleExtensionURN { ExtensionUrnAnchor = 1, Urn = variation.Namespace });
            plan.Extensions.Add(new Substrait.Protobuf.SimpleExtensionDeclaration
            {
                ExtensionTypeVariation = new()
                {
                    ExtensionUrnReference = 1,
                    TypeVariationAnchor = 1,
                    Name = variation.Name,
                },
            });
        }

        return new ProtoToTypeConverter(
            new ExtensionsDictionary.Builder(plan).Build(),
            new ExtensionsCollection(variation is null ? [] : [variation], [], [], []));
    }
}
