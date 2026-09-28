// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

[TestClass]
public sealed class ProtoToTypeConverterTests
{
    [TestMethod]
    public void ConvertsPrimitiveAndParameterizedTypes()
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

        Assert.AreEqual(TypeFactory.NULLABLE.Boolean_(null), boolean);
        Assert.AreEqual(TypeFactory.REQUIRED.Decimal(10, 2), decimalType);
    }

    [DataTestMethod]
    [DynamicData(nameof(GetLeafTypeCases), DynamicDataSourceType.Method)]
    public void ConvertsLeafTypes(ProtoType protoType, IType expected)
    {
        IType actual = new ProtoToTypeConverter().From(protoType);

        Assert.IsTrue(expected.Equals(actual, ITypeComparison.Strict), $"Expected {expected}, but found {actual}.");
        Assert.AreEqual(protoType, new TypeToProtoConverter().From(actual));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(9)]
    public void RejectsUnsupportedTimePrecision(int precision)
    {
        ProtoType type = new()
        {
            PrecisionTime = new() { Precision = precision, Nullability = ProtoType.Types.Nullability.Required },
        };

        Assert.ThrowsException<NotSupportedException>(() => new ProtoToTypeConverter().From(type));
    }

    [TestMethod]
    public void RejectsMissingOrFractionalIntervalPrecision()
    {
        ProtoType type = new()
        {
            IntervalDay = new() { Nullability = ProtoType.Types.Nullability.Required },
        };
        ProtoToTypeConverter converter = new();

        Assert.ThrowsException<NotSupportedException>(() => converter.From(type));
        type.IntervalDay.Precision = 6;
        Assert.ThrowsException<NotSupportedException>(() => converter.From(type));
    }

    [TestMethod]
    public void RejectsUnspecifiedNullability()
    {
        ProtoType protoType = new()
        {
            I16 = new ProtoType.Types.I16 { Nullability = ProtoType.Types.Nullability.Unspecified },
        };

        Assert.ThrowsException<NotImplementedException>(() => new ProtoToTypeConverter().From(protoType));
    }

    [TestMethod]
    public void ConvertsNestedStructWithoutRecursion()
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

        Assert.AreEqual(TypeFactory.NULLABLE.Struct([TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])]), result);
    }

    [TestMethod]
    public void ConvertsDeeplyNestedStructAndPreservesFieldOrder()
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

        Assert.AreEqual(
            TypeFactory.REQUIRED.Struct(
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
            ]),
            result);
    }

    [TestMethod]
    public void NonStrictModeIgnoresUnknownTypeVariation()
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

        Assert.AreEqual(TypeFactory.REQUIRED.I64, converter.From(type));
    }

    [TestMethod]
    public void StrictModeReportsUnknownTypeVariationClearly()
    {
        var knownVariation = new TypeVariationImpl("/types.yaml", "i64", "known", string.Empty, FunctionBehavior.INHERITS);
        var extensions = new ExtensionsCollection([knownVariation], [], [], []);

        ArgumentException exception = Assert.ThrowsException<ArgumentException>(() => extensions.TryGetTypeVariation(
            new TypeVariationImplAnchor("/types.yaml", "unknown"),
            ExtensionsDictionary.StrictMode.TYPE_VARIATION,
            out _));

        StringAssert.Contains(exception.Message, "Unexpected type variation with key unknown");
        StringAssert.Contains(exception.Message, "no type variation with this key was found");
    }

    [TestMethod]
    public void ConvertsNestedInternalStructToProto()
    {
        IType type = TypeFactory.NULLABLE.Struct(
        [
            TypeFactory.REQUIRED.I32,
            TypeFactory.REQUIRED.Struct([TypeFactory.NULLABLE.String_(null), TypeFactory.REQUIRED.Decimal(12, 3)]),
        ]);

        ProtoType result = new TypeToProtoConverter().From(type);

        Assert.AreEqual(ProtoType.Types.Nullability.Nullable, result.Struct.Nullability);
        Assert.IsNotNull(result.Struct.Types_[0].I32);
        Assert.AreEqual(2, result.Struct.Types_[1].Struct.Types_.Count);
        Assert.IsNotNull(result.Struct.Types_[1].Struct.Types_[0].String);
        Assert.AreEqual(12, result.Struct.Types_[1].Struct.Types_[1].Decimal.Precision);
    }

    [TestMethod]
    public void CollectsTypeVariationWithReservedZeroAnchor()
    {
        var variation = new TypeVariationImpl("/types.yaml", "i64", "custom", string.Empty, FunctionBehavior.INHERITS);
        IType type = TypeFactory.REQUIRED.I64_(variation);
        var context = new PlanToProtoConverter.ConverterContext();

        ProtoType result = new TypeToProtoConverter().From(type, context);

        Assert.AreEqual(1U, result.I64.TypeVariationReference);
        Assert.AreEqual(1, context.ExtensionsCollector.ExtensionUris.Count);
        Assert.AreEqual("/types.yaml", context.ExtensionsCollector.ExtensionUris[0]);
        Assert.AreEqual(ExtensionsCollector.ExtensionType.TypeVariation, context.ExtensionsCollector.Extensions[0].Type);
    }

    private static IEnumerable<object?[]> GetLeafTypeCases()
    {
        yield return [new ProtoType { I8 = new ProtoType.Types.I8 { Nullability = ProtoType.Types.Nullability.Required } }, TypeFactory.REQUIRED.I8];
        yield return [new ProtoType { I16 = new ProtoType.Types.I16 { Nullability = ProtoType.Types.Nullability.Required } }, TypeFactory.REQUIRED.I16];
        yield return [new ProtoType { I32 = new ProtoType.Types.I32 { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.I32];
        yield return [new ProtoType { I64 = new ProtoType.Types.I64 { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.I64];
        yield return [new ProtoType { Fp32 = new ProtoType.Types.FP32 { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.FP32];
        yield return [new ProtoType { Fp64 = new ProtoType.Types.FP64 { Nullability = ProtoType.Types.Nullability.Required } }, TypeFactory.REQUIRED.FP64];
        yield return [new ProtoType { String = new ProtoType.Types.String { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.STR];
        yield return [new ProtoType { Binary = new ProtoType.Types.Binary { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.BINARY];
        yield return [new ProtoType { Date = new ProtoType.Types.Date { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.DATE];
        yield return [new ProtoType { PrecisionTime = new ProtoType.Types.PrecisionTime { Precision = 6, Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.TIME];
        yield return [new ProtoType { IntervalYear = new ProtoType.Types.IntervalYear { Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.INTERVAL_YEAR];
        yield return [new ProtoType { IntervalDay = new ProtoType.Types.IntervalDay { Precision = 0, Nullability = ProtoType.Types.Nullability.Nullable } }, TypeFactory.NULLABLE.INTERVAL_DAY];
        yield return
        [
            new ProtoType
            {
                PrecisionTimestamp = new ProtoType.Types.PrecisionTimestamp
                {
                    Nullability = ProtoType.Types.Nullability.Required,
                    Precision = 9,
                },
            },
            TypeFactory.REQUIRED.PrecisionTimestamp(9),
        ];
        yield return
        [
            new ProtoType
            {
                PrecisionTimestampTz = new ProtoType.Types.PrecisionTimestampTZ
                {
                    Nullability = ProtoType.Types.Nullability.Required,
                    Precision = 6,
                },
            },
            TypeFactory.REQUIRED.PrecisionTimestampTZ(6),
        ];
        yield return
        [
            new ProtoType
            {
                FixedChar = new ProtoType.Types.FixedChar
                {
                    Nullability = ProtoType.Types.Nullability.Required,
                    Length = 1,
                },
            },
            TypeFactory.REQUIRED.FixedChar(1),
        ];
        yield return
        [
            new ProtoType
            {
                Varchar = new ProtoType.Types.VarChar
                {
                    Nullability = ProtoType.Types.Nullability.Required,
                    Length = 50,
                },
            },
            TypeFactory.REQUIRED.VarChar(50),
        ];
        yield return
        [
            new ProtoType
            {
                FixedBinary = new ProtoType.Types.FixedBinary
                {
                    Nullability = ProtoType.Types.Nullability.Nullable,
                    Length = 16,
                },
            },
            TypeFactory.NULLABLE.FixedBinary(16),
        ];
    }
}
