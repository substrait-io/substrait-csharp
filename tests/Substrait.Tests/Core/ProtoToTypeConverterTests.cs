// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class ProtoToTypeConverterTests
{
    [Test]
    public async Task ConvertsPrimitiveAndParameterizedTypes()
    {
        ProtoToTypeConverter converter = new();

        IType boolean = converter.From(new ProtoType
        {
            Bool = new ProtoType.Types.Boolean { Nullability = ProtoType.Types.Nullability.Nullable },
        });
        IType decimalType = converter.From(new ProtoType
        {
            Decimal = new ProtoType.Types.Decimal
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Precision = 10,
                Scale = 2,
            },
        });

        await Assert.That(boolean).IsEqualTo(TypeFactory.NULLABLE.Boolean_(null));
        await Assert.That(decimalType).IsEqualTo(TypeFactory.REQUIRED.Decimal(10, 2));
    }

    [Test]
    [MethodDataSource(nameof(GetLeafTypeCases))]
    public async Task ConvertsLeafTypes(ProtoType protoType, IType expected)
    {
        IType actual = new ProtoToTypeConverter().From(protoType);

        await Assert.That(expected.Equals(actual, ITypeComparison.Strict)).IsTrue().Because($"Expected {expected}, but found {actual}.");
        await Assert.That(new TypeToProtoConverter().From(actual)).IsEqualTo(protoType);
    }

    [Test]
    [Arguments(0)]
    [Arguments(3)]
    [Arguments(9)]
    public async Task RejectsUnsupportedTimePrecision(int precision)
    {
        ProtoType type = new()
        {
            PrecisionTime = new() { Precision = precision, Nullability = ProtoType.Types.Nullability.Required },
        };

        await Assert.That(() => new ProtoToTypeConverter().From(type)).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task RejectsMissingOrFractionalIntervalPrecision()
    {
        ProtoType type = new()
        {
            IntervalDay = new() { Nullability = ProtoType.Types.Nullability.Required },
        };
        ProtoToTypeConverter converter = new();

        await Assert.That(() => converter.From(type)).ThrowsExactly<NotSupportedException>();
        type.IntervalDay.Precision = 6;
        await Assert.That(() => converter.From(type)).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task RejectsUnspecifiedNullability()
    {
        ProtoType protoType = new()
        {
            I16 = new ProtoType.Types.I16 { Nullability = ProtoType.Types.Nullability.Unspecified },
        };

        await Assert.That(() => new ProtoToTypeConverter().From(protoType)).ThrowsExactly<NotImplementedException>();
    }

    [Test]
    public async Task ConvertsNestedStructWithoutRecursion()
    {
        ProtoType inner = new()
        {
            Struct = new ProtoType.Types.Struct
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Types_ =
                {
                    new ProtoType { I64 = new ProtoType.Types.I64 { Nullability = ProtoType.Types.Nullability.Required } },
                },
            },
        };
        ProtoType outer = new()
        {
            Struct = new ProtoType.Types.Struct
            {
                Nullability = ProtoType.Types.Nullability.Nullable,
                Types_ = { inner },
            },
        };

        IType result = new ProtoToTypeConverter().From(outer);

        await Assert.That(result).IsEqualTo(TypeFactory.NULLABLE.Struct([TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])]));
    }

    [Test]
    public async Task ConvertsDeeplyNestedStructAndPreservesFieldOrder()
    {
        ProtoType deepest = new()
        {
            Struct = new ProtoType.Types.Struct
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Types_ =
                {
                    new ProtoType { Decimal = new ProtoType.Types.Decimal { Nullability = ProtoType.Types.Nullability.Required, Precision = 10, Scale = 2 } },
                    new ProtoType { I64 = new ProtoType.Types.I64 { Nullability = ProtoType.Types.Nullability.Nullable } },
                },
            },
        };
        ProtoType middle = new()
        {
            Struct = new ProtoType.Types.Struct
            {
                Nullability = ProtoType.Types.Nullability.Nullable,
                Types_ =
                {
                    new ProtoType { Bool = new ProtoType.Types.Boolean { Nullability = ProtoType.Types.Nullability.Required } },
                    deepest,
                },
            },
        };
        ProtoType outer = new()
        {
            Struct = new ProtoType.Types.Struct
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Types_ =
                {
                    new ProtoType { I32 = new ProtoType.Types.I32 { Nullability = ProtoType.Types.Nullability.Required } },
                    middle,
                    new ProtoType { String = new ProtoType.Types.String { Nullability = ProtoType.Types.Nullability.Nullable } },
                },
            },
        };

        IType result = new ProtoToTypeConverter().From(outer);

        await Assert.That(result).IsEqualTo(TypeFactory.REQUIRED.Struct(
            [
                TypeFactory.REQUIRED.I32,
                TypeFactory.NULLABLE.Struct(
                [
                    TypeFactory.REQUIRED.Boolean_(null),
                    TypeFactory.REQUIRED.Struct(
                    [
                        TypeFactory.REQUIRED.Decimal(10, 2),
                        TypeFactory.NULLABLE.I64,
                    ]),
                ]),
                TypeFactory.NULLABLE.String_(null),
            ]));
    }

    [Test]
    public async Task NonStrictModeIgnoresUnknownTypeVariation()
    {
        ProtoType type = new()
        {
            I64 = new ProtoType.Types.I64
            {
                Nullability = ProtoType.Types.Nullability.Required,
                TypeVariationReference = 42,
            },
        };
        ProtoToTypeConverter converter = new(
            new ExtensionsDictionary.Builder().Build(),
            new ExtensionsCollection(),
            ExtensionsDictionary.StrictMode.OFF);

        await Assert.That(converter.From(type)).IsEqualTo(TypeFactory.REQUIRED.I64);
    }

    [Test]
    public async Task StrictModeReportsUnknownTypeVariationClearly()
    {
        var knownVariation = new TypeVariationImpl("/types.yaml", "i64", "known", string.Empty, FunctionBehavior.INHERITS);
        var extensions = new ExtensionsCollection([knownVariation], [], [], []);

        ArgumentException exception = await Assert.That(() => extensions.TryGetTypeVariation(
            new TypeVariationImplAnchor("/types.yaml", "unknown"),
            ExtensionsDictionary.StrictMode.TYPE_VARIATION,
            out _)).ThrowsExactly<ArgumentException>().And.IsNotNull();

        await Assert.That(exception.Message).Contains("Unexpected type variation with key unknown");
        await Assert.That(exception.Message).Contains("no type variation with this key was found");
    }

    [Test]
    public async Task ConvertsNestedInternalStructToProto()
    {
        IType type = TypeFactory.NULLABLE.Struct(
        [
            TypeFactory.REQUIRED.I32,
            TypeFactory.REQUIRED.Struct([TypeFactory.NULLABLE.String_(null), TypeFactory.REQUIRED.Decimal(12, 3)]),
        ]);

        ProtoType result = new TypeToProtoConverter().From(type);

        await Assert.That(result.Struct.Nullability).IsEqualTo(ProtoType.Types.Nullability.Nullable);
        await Assert.That(result.Struct.Types_[0].I32).IsNotNull();
        await Assert.That(result.Struct.Types_[1].Struct.Types_.Count).IsEqualTo(2);
        await Assert.That(result.Struct.Types_[1].Struct.Types_[0].String).IsNotNull();
        await Assert.That(result.Struct.Types_[1].Struct.Types_[1].Decimal.Precision).IsEqualTo(12);
    }

    [Test]
    public async Task CollectsTypeVariationWithReservedZeroAnchor()
    {
        var variation = new TypeVariationImpl("/types.yaml", "i64", "custom", string.Empty, FunctionBehavior.INHERITS);
        IType type = TypeFactory.REQUIRED.I64_(variation);
        var context = new PlanToProtoConverter.ConverterContext();

        ProtoType result = new TypeToProtoConverter().From(type, context);

        await Assert.That(result.I64.TypeVariationReference).IsEqualTo(1U);
        await Assert.That(context.ExtensionsCollector.ExtensionUris.Count).IsEqualTo(1);
        await Assert.That(context.ExtensionsCollector.ExtensionUris[0]).IsEqualTo("/types.yaml");
        await Assert.That(context.ExtensionsCollector.Extensions[0].Type).IsEqualTo(ExtensionsCollector.ExtensionType.TypeVariation);
    }
    public static IEnumerable<Func<(ProtoType ProtoType, IType Expected)>> GetLeafTypeCases()
    {
        yield return () => (new ProtoType { I8 = new ProtoType.Types.I8 { Nullability = ProtoType.Types.Nullability.Required } }, TypeFactory.REQUIRED.I8);
        yield return () => (new ProtoType { I16 = new ProtoType.Types.I16 { Nullability = ProtoType.Types.Nullability.Required } }, TypeFactory.REQUIRED.I16);
        yield return () => (new ProtoType { I32 = new ProtoType.Types.I32 { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.I32);
        yield return () => (new ProtoType { I64 = new ProtoType.Types.I64 { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.I64);
        yield return () => (new ProtoType { Fp32 = new ProtoType.Types.FP32 { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.FP32);
        yield return () => (new ProtoType { Fp64 = new ProtoType.Types.FP64 { Nullability = ProtoType.Types.Nullability.Required } }, TypeFactory.REQUIRED.FP64);
        yield return () => (new ProtoType { String = new ProtoType.Types.String { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.STR);
        yield return () => (new ProtoType { Binary = new ProtoType.Types.Binary { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.BINARY);
        yield return () => (new ProtoType { Date = new ProtoType.Types.Date { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.DATE);
        yield return () => (new ProtoType { PrecisionTime = new ProtoType.Types.PrecisionTime { Precision = 6, Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.TIME);
        yield return () => (new ProtoType { IntervalYear = new ProtoType.Types.IntervalYear { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.INTERVAL_YEAR);
        yield return () => (new ProtoType { IntervalDay = new ProtoType.Types.IntervalDay { Precision = 0, Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.INTERVAL_DAY);
        yield return
        () => (new ProtoType
        {
            PrecisionTimestamp = new ProtoType.Types.PrecisionTimestamp
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Precision = 9,
            },
        }, TypeFactory.REQUIRED.PrecisionTimestamp(9));
        yield return
        () => (new ProtoType
        {
            PrecisionTimestampTz = new ProtoType.Types.PrecisionTimestampTZ
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Precision = 6,
            },
        }, TypeFactory.REQUIRED.PrecisionTimestampTZ(6));
        yield return
        () => (new ProtoType
        {
            FixedChar = new ProtoType.Types.FixedChar
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Length = 1,
            },
        }, TypeFactory.REQUIRED.FixedChar(1));
        yield return
        () => (new ProtoType
        {
            Varchar = new ProtoType.Types.VarChar
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Length = 50,
            },
        }, TypeFactory.REQUIRED.VarChar(50));
        yield return
        () => (new ProtoType
        {
            FixedBinary = new ProtoType.Types.FixedBinary
            {
                Nullability = ProtoType.Types.Nullability.Nullable,
                Length = 16,
            },
        }, TypeFactory.NULLABLE.FixedBinary(16));
    }
}
