// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Substrait.Core.Type;
using static Substrait.Core.Expression.Literal;
using static Substrait.Core.Type.IType;

namespace Substrait.Tests.Core;

/// <summary>
/// Tests substrait literals.
/// </summary>
public class LiteralTests
{
    /// <summary>
    /// Gets simple literal equals test cases.
    /// </summary>
    /// <returns>Literal and constructor argument types with two distinct values.</returns>
    public static IEnumerable<(System.Type LiteralType, System.Type ValueType, object Value1, object Value2)> SimpleLiteralEqualsTestCases()
    {
        return
        [
            (typeof(BoolLiteral), typeof(bool), true, false),
            (typeof(I8Literal), typeof(int), 123, 456),
            (typeof(I16Literal), typeof(int), 123, 456),
            (typeof(I32Literal), typeof(int), 123, 456),
            (typeof(I64Literal), typeof(long), 12345, 67890),
            (typeof(FP32Literal), typeof(int), 123, 456),
            (typeof(FP64Literal), typeof(int), 123, 456),
            (typeof(DateLiteral), typeof(int), 123, 456),
            (typeof(TimeLiteral), typeof(long), 12345, 67890),
            (typeof(StrLiteral), typeof(string), "abc", "def"),
            (typeof(BinaryLiteral), typeof(ByteString), ByteString.FromBase64("abcd"), ByteString.FromBase64("cdef")),
            (typeof(FixedCharLiteral), typeof(string), "abc", "def"),
            (typeof(FixedBinaryLiteral), typeof(ByteString), ByteString.FromBase64("abcd"), ByteString.FromBase64("cdef")),
        ];
    }

    /// <summary>
    /// Tests null literal equality.
    /// </summary>
    [Test]
    public async Task TestNullLiteralEquals()
    {
        var v1 = new NullLiteral(TypeFactory.NULLABLE.I64);
        var v2 = new NullLiteral(TypeFactory.NULLABLE.I64);
        var v3 = new NullLiteral(TypeFactory.NULLABLE.I32);

        await Assert.That(v2).IsEqualTo(v1);
        await Assert.That(v3).IsNotEqualTo(v1);

        await Assert.That(v2.GetHashCode()).IsEqualTo(v1.GetHashCode());
        await Assert.That(v3.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
    }

    /// <summary>
    /// Tests null literal equality.
    /// If the base type is not nullable, the literal type should entail corresponding nullable type.
    /// </summary>
    [Test]
    public async Task TestNullLiteralEqualsWithNonNullType()
    {
        var v1 = new NullLiteral(TypeFactory.NULLABLE.I64);
        var v2 = new NullLiteral(TypeFactory.REQUIRED.I64);

        await Assert.That(v2).IsEqualTo(v1);
        await Assert.That(v2.GetHashCode()).IsEqualTo(v1.GetHashCode());
    }

    /// <summary>
    /// Tests primivive literal equality.
    /// </summary>
    /// <param name="literalType">type of literal to test.</param>
    /// <param name="valueType">type of value to construct literal.</param>
    /// <param name="value1">first value for test.</param>
    /// <param name="value2">second value for test.</param>
    [Test]
    [MethodDataSource(nameof(SimpleLiteralEqualsTestCases))]
    public async Task TestSimpleLiteralEquals(System.Type literalType, System.Type valueType, object value1, object value2)
    {
        var ctor = await Assert.That(literalType.GetConstructor(new[] { valueType, typeof(NullableType) })).IsNotNull();

        var v1 = ctor.Invoke(new object[] { value1, NullableType.Required });
        var v2 = ctor.Invoke(new object[] { value1, NullableType.Required });
        var v3 = ctor.Invoke(new object[] { value2, NullableType.Required });
        var v4 = ctor.Invoke(new object[] { value1, NullableType.Nullable });

        await Assert.That(v2).IsEqualTo(v1);
        await Assert.That(v3).IsNotEqualTo(v1);
        await Assert.That(v4).IsNotEqualTo(v1);

        await Assert.That(v2.GetHashCode()).IsEqualTo(v1.GetHashCode());
        await Assert.That(v3.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v4.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
    }

    /// <summary>
    /// Tests precision timestamp literal equals.
    /// </summary>
    [Test]
    public async Task TestPrecisionTimestampLiteralEquals()
    {
        var v1 = new PrecisionTimestampLiteral(12345, 5, NullableType.Required);
        var v2 = new PrecisionTimestampLiteral(12345, 5, NullableType.Required);
        var v3 = new PrecisionTimestampLiteral(56789, 5, NullableType.Required);
        var v4 = new PrecisionTimestampLiteral(12345, 5, NullableType.Nullable);
        var v5 = new PrecisionTimestampLiteral(12345, 3, NullableType.Nullable);

        await Assert.That(v2).IsEqualTo(v1);
        await Assert.That(v3).IsNotEqualTo(v1);
        await Assert.That(v4).IsNotEqualTo(v1);
        await Assert.That(v5).IsNotEqualTo(v1);

        await Assert.That(v2.GetHashCode()).IsEqualTo(v1.GetHashCode());
        await Assert.That(v3.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v4.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v5.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
    }

    /// <summary>
    /// Tests precision timestamp tz literal equals.
    /// </summary>
    [Test]
    public async Task TestPrecisionTimestampLiteralTZEquals()
    {
        var v1 = new PrecisionTimestampTZLiteral(12345, 5, NullableType.Required);
        var v2 = new PrecisionTimestampTZLiteral(12345, 5, NullableType.Required);
        var v3 = new PrecisionTimestampTZLiteral(56789, 5, NullableType.Required);
        var v4 = new PrecisionTimestampTZLiteral(12345, 5, NullableType.Nullable);
        var v5 = new PrecisionTimestampTZLiteral(12345, 3, NullableType.Nullable);

        await Assert.That(v2).IsEqualTo(v1);
        await Assert.That(v3).IsNotEqualTo(v1);
        await Assert.That(v4).IsNotEqualTo(v1);
        await Assert.That(v5).IsNotEqualTo(v1);

        await Assert.That(v2.GetHashCode()).IsEqualTo(v1.GetHashCode());
        await Assert.That(v3.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v4.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v5.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
    }

    /// <summary>
    /// Tests varchar literal equals.
    /// </summary>
    [Test]
    public async Task TestVarCharLiteralEquals()
    {
        var v1 = new VarCharLiteral("abcd", 16, NullableType.Required);
        var v2 = new VarCharLiteral("abcd", 16, NullableType.Required);
        var v3 = new VarCharLiteral("defg", 16, NullableType.Required);
        var v4 = new VarCharLiteral("abcd", 16, NullableType.Nullable);
        var v5 = new VarCharLiteral("abcd", 9, NullableType.Nullable);

        await Assert.That(v2).IsEqualTo(v1);
        await Assert.That(v3).IsNotEqualTo(v1);
        await Assert.That(v4).IsNotEqualTo(v1);
        await Assert.That(v5).IsNotEqualTo(v1);

        await Assert.That(v2.GetHashCode()).IsEqualTo(v1.GetHashCode());
        await Assert.That(v3.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v4.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v5.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
    }

    /// <summary>
    /// Tests decimal literal equals.
    /// </summary>
    [Test]
    public async Task TestDecimalLiteralEquals()
    {
        var v1 = new DecimalLiteral(ByteString.FromBase64("abcd"), 10, 3, NullableType.Required);
        var v2 = new DecimalLiteral(ByteString.FromBase64("abcd"), 10, 3, NullableType.Required);
        var v3 = new DecimalLiteral(ByteString.FromBase64("dbca"), 10, 3, NullableType.Required);
        var v4 = new DecimalLiteral(ByteString.FromBase64("abcd"), 10, 3, NullableType.Nullable);
        var v5 = new DecimalLiteral(ByteString.FromBase64("abcd"), 12, 3, NullableType.Required);
        var v6 = new DecimalLiteral(ByteString.FromBase64("abcd"), 10, 4, NullableType.Required);

        await Assert.That(v2).IsEqualTo(v1);
        await Assert.That(v3).IsNotEqualTo(v1);
        await Assert.That(v4).IsNotEqualTo(v1);
        await Assert.That(v5).IsNotEqualTo(v1);
        await Assert.That(v6).IsNotEqualTo(v1);

        await Assert.That(v2.GetHashCode()).IsEqualTo(v1.GetHashCode());
        await Assert.That(v3.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v4.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v5.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v6.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
    }

    /// <summary>
    /// Tests struct literal equals.
    /// </summary>
    [Test]
    public async Task TestStructLiteralEquals()
    {
        var v1 = new StructLiteral([new I64Literal(1), new StrLiteral("abc")], NullableType.Required);
        var v2 = new StructLiteral([new I64Literal(1), new StrLiteral("abc")], NullableType.Required);
        var v3 = new StructLiteral([new I64Literal(2), new StrLiteral("def")], NullableType.Required);
        var v4 = new StructLiteral([new I64Literal(1), new StrLiteral("abc")], NullableType.Nullable);
        var v5 = new StructLiteral([new I32Literal(1), new StrLiteral("abc")], NullableType.Nullable);

        await Assert.That(v2).IsEqualTo(v1);
        await Assert.That(v3).IsNotEqualTo(v1);
        await Assert.That(v4).IsNotEqualTo(v1);
        await Assert.That(v5).IsNotEqualTo(v1);

        await Assert.That(v2.GetHashCode()).IsEqualTo(v1.GetHashCode());
        await Assert.That(v3.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v4.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
        await Assert.That(v5.GetHashCode()).IsNotEqualTo(v1.GetHashCode());
    }
}
