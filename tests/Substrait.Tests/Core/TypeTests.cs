// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Type;
using static Substrait.Tools.TypeUtils;

namespace Substrait.Tests.Core;

/// <summary>
/// Type related tests.
/// </summary>
public class TypeTests
{
    /// <summary>
    /// Gets all primitive types from the type factory.
    /// </summary>
    /// <param name="typeFactory">type factory.</param>
    /// <returns>All primitive types.</returns>
    public static IEnumerable<PrimitiveType> GetAllPrimitiveTypes(TypeFactory typeFactory)
    {
        return typeFactory.GetType().GetProperties().Where(x => typeof(PrimitiveType).IsAssignableFrom(x.PropertyType)).Select(x => x.GetValue(typeFactory)).Cast<PrimitiveType>();
    }

    /// <summary>
    /// Tests equality of primitive types.
    /// </summary>
    [Test]
    public async Task TestPrimitiveTypeEquals()
    {
        var allPrimitiveTypes = GetAllPrimitiveTypes(TypeFactory.REQUIRED).Concat(GetAllPrimitiveTypes(TypeFactory.NULLABLE)).ToImmutableList();

        for (var i = 0; i < allPrimitiveTypes.Count; ++i)
        {
            for (var j = 0; j < allPrimitiveTypes.Count; ++j)
            {
                await Assert.That(i == j).IsEqualTo(allPrimitiveTypes[i].Equals(allPrimitiveTypes[j])).Because($"({allPrimitiveTypes[i]},{allPrimitiveTypes[j]})");
                await Assert.That(i == j).IsEqualTo(allPrimitiveTypes[i].GetHashCode() == allPrimitiveTypes[j].GetHashCode()).Because($"({allPrimitiveTypes[i]},{allPrimitiveTypes[j]}).GetHashCode()");
            }
        }
    }

    /// <summary>
    /// Tests precision timestamp equals.
    /// </summary>
    [Test]
    public async Task TestPrecisionTimestampEquals()
    {
        var t1 = TypeFactory.REQUIRED.PrecisionTimestamp(1);
        var t2 = TypeFactory.REQUIRED.PrecisionTimestamp(1);
        var t3 = TypeFactory.REQUIRED.PrecisionTimestamp(2);
        var t4 = TypeFactory.NULLABLE.PrecisionTimestamp(1);

        await Assert.That(t2).IsEqualTo(t1);
        await Assert.That(t3).IsNotEqualTo(t1);
        await Assert.That(t4).IsNotEqualTo(t1);

        await Assert.That(t2.GetHashCode()).IsEqualTo(t1.GetHashCode());
        await Assert.That(t3.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
        await Assert.That(t4.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
    }

    /// <summary>
    /// Tests that PrecisionTimestamp and PrecisionTimestampTZ, despite having an identical shape
    /// (a single Precision field), are never equal to each other.
    /// </summary>
    [Test]
    public async Task TestPrecisionTimestampAndPrecisionTimestampTZAreNotEqual()
    {
        IType withoutTz = TypeFactory.REQUIRED.PrecisionTimestamp(6);
        IType withTz = TypeFactory.REQUIRED.PrecisionTimestampTZ(6);

        await Assert.That(withTz).IsNotEqualTo(withoutTz);
        await Assert.That(withTz.GetHashCode()).IsNotEqualTo(withoutTz.GetHashCode());
    }

    /// <summary>
    /// Tests precision timestamp with timezone equals.
    /// </summary>
    [Test]
    public async Task TestPrecisionTimestampTZEquals()
    {
        var t1 = TypeFactory.REQUIRED.PrecisionTimestampTZ(1);
        var t2 = TypeFactory.REQUIRED.PrecisionTimestampTZ(1);
        var t3 = TypeFactory.REQUIRED.PrecisionTimestampTZ(2);
        var t4 = TypeFactory.NULLABLE.PrecisionTimestampTZ(1);

        await Assert.That(t2).IsEqualTo(t1);
        await Assert.That(t3).IsNotEqualTo(t1);
        await Assert.That(t4).IsNotEqualTo(t1);

        await Assert.That(t2.GetHashCode()).IsEqualTo(t1.GetHashCode());
        await Assert.That(t3.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
        await Assert.That(t4.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
    }

    /// <summary>
    /// Tests fixed char equals.
    /// </summary>
    [Test]
    public async Task TestFixedCharEquals()
    {
        var t1 = TypeFactory.REQUIRED.FixedChar(1);
        var t2 = TypeFactory.REQUIRED.FixedChar(1);
        var t3 = TypeFactory.REQUIRED.FixedChar(2);
        var t4 = TypeFactory.NULLABLE.FixedChar(1);

        await Assert.That(t2).IsEqualTo(t1);
        await Assert.That(t3).IsNotEqualTo(t1);
        await Assert.That(t4).IsNotEqualTo(t1);

        await Assert.That(t2.GetHashCode()).IsEqualTo(t1.GetHashCode());
        await Assert.That(t3.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
        await Assert.That(t4.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
    }

    /// <summary>
    /// Tests fixed char equals.
    /// </summary>
    [Test]
    public async Task TestVarCharEquals()
    {
        var t1 = TypeFactory.REQUIRED.VarChar(1);
        var t2 = TypeFactory.REQUIRED.VarChar(1);
        var t3 = TypeFactory.REQUIRED.VarChar(2);
        var t4 = TypeFactory.NULLABLE.VarChar(1);

        await Assert.That(t2).IsEqualTo(t1);
        await Assert.That(t3).IsNotEqualTo(t1);
        await Assert.That(t4).IsNotEqualTo(t1);

        await Assert.That(t2.GetHashCode()).IsEqualTo(t1.GetHashCode());
        await Assert.That(t3.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
        await Assert.That(t4.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
    }

    /// <summary>
    /// Tests fixed binary equals.
    /// </summary>
    [Test]
    public async Task TestFixedBinaryEquals()
    {
        var t1 = TypeFactory.REQUIRED.FixedBinary(1);
        var t2 = TypeFactory.REQUIRED.FixedBinary(1);
        var t3 = TypeFactory.REQUIRED.FixedBinary(2);
        var t4 = TypeFactory.NULLABLE.FixedBinary(1);

        await Assert.That(t2).IsEqualTo(t1);
        await Assert.That(t3).IsNotEqualTo(t1);
        await Assert.That(t4).IsNotEqualTo(t1);

        await Assert.That(t2.GetHashCode()).IsEqualTo(t1.GetHashCode());
        await Assert.That(t3.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
        await Assert.That(t4.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
    }

    /// <summary>
    /// Tests decimal equals.
    /// </summary>
    [Test]
    public async Task TestDecimalEquals()
    {
        var t1 = TypeFactory.REQUIRED.Decimal(2, 1);
        var t2 = TypeFactory.REQUIRED.Decimal(2, 1);
        var t3 = TypeFactory.REQUIRED.Decimal(2, 2);
        var t4 = TypeFactory.REQUIRED.Decimal(3, 1);
        var t5 = TypeFactory.NULLABLE.Decimal(2, 1);

        await Assert.That(t2).IsEqualTo(t1);
        await Assert.That(t3).IsNotEqualTo(t1);
        await Assert.That(t4).IsNotEqualTo(t1);
        await Assert.That(t5).IsNotEqualTo(t1);

        await Assert.That(t2.GetHashCode()).IsEqualTo(t1.GetHashCode());
        await Assert.That(t3.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
        await Assert.That(t4.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
        await Assert.That(t5.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
    }

    /// <summary>
    /// Tests struct equals.
    /// </summary>
    [Test]
    public async Task TestStructEquals()
    {
        var fields1 = new List<IType> { TypeFactory.REQUIRED.I64, TypeFactory.NULLABLE.STR };
        var fields2 = new List<IType> { TypeFactory.NULLABLE.STR, TypeFactory.REQUIRED.I64 };

        var t1 = TypeFactory.REQUIRED.Struct(fields1);
        var t2 = TypeFactory.REQUIRED.Struct(fields1);
        var t3 = TypeFactory.REQUIRED.Struct(fields2);
        var t4 = TypeFactory.NULLABLE.Struct(fields1);

        await Assert.That(t2).IsEqualTo(t1);
        await Assert.That(t3).IsNotEqualTo(t1);
        await Assert.That(t4).IsNotEqualTo(t1);

        await Assert.That(t2.GetHashCode()).IsEqualTo(t1.GetHashCode());
        await Assert.That(t3.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
        await Assert.That(t4.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
    }

    /// <summary>
    /// Tests nested struct equals.
    /// </summary>
    [Test]
    public async Task TestNestedStructEquals()
    {
        var fields1 = new List<IType> { TypeFactory.REQUIRED.I64, TypeFactory.NULLABLE.STR };
        var struct1 = TypeFactory.REQUIRED.Struct(fields1);
        var struct11 = TypeFactory.REQUIRED.Struct(fields1);
        var struct2 = TypeFactory.NULLABLE.Struct(fields1);

        var fields2 = new List<IType> { TypeFactory.NULLABLE.STR, struct1, TypeFactory.REQUIRED.I64 };
        var fields3 = new List<IType> { TypeFactory.NULLABLE.STR, struct11, TypeFactory.REQUIRED.I64 };
        var fields4 = new List<IType> { TypeFactory.NULLABLE.STR, struct2, TypeFactory.REQUIRED.I64 };

        var t1 = TypeFactory.REQUIRED.Struct(fields2);
        var t2 = TypeFactory.REQUIRED.Struct(fields3);
        var t3 = TypeFactory.REQUIRED.Struct(fields4);

        await Assert.That(t2).IsEqualTo(t1);
        await Assert.That(t3).IsNotEqualTo(t1);

        await Assert.That(t2.GetHashCode()).IsEqualTo(t1.GetHashCode());
        await Assert.That(t3.GetHashCode()).IsNotEqualTo(t1.GetHashCode());
    }

    /// <summary>
    /// Tests whether inverse nullable throws with unspecified nullable type.
    /// </summary>
    [Test]
    public async Task TestInverseNullableThrowsWithUnspecified()
    {
        await Assert.That(() => IType.NullableType.Unspecified.Inverse()).ThrowsExactly<NotImplementedException>();
    }

    /// <summary>
    /// Tests that Decimal rejects out-of-range precision and scale.
    /// </summary>
    [Test]
    public async Task TestDecimalRejectsOutOfRangePrecisionAndScale()
    {
        await Assert.That(() => TypeFactory.REQUIRED.Decimal(0, 0)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.Decimal(39, 0)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.Decimal(5, -1)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.Decimal(5, 6)).ThrowsExactly<ArgumentOutOfRangeException>();

        // Boundary values are accepted.
        TypeFactory.REQUIRED.Decimal(1, 0);
        TypeFactory.REQUIRED.Decimal(38, 38);
    }

    /// <summary>
    /// Tests that FixedChar, VarChar, and FixedBinary reject a non-positive length.
    /// </summary>
    [Test]
    public async Task TestFixedLengthTypesRejectNonPositiveLength()
    {
        await Assert.That(() => TypeFactory.REQUIRED.FixedChar(0)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.FixedChar(-1)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.VarChar(0)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.FixedBinary(0)).ThrowsExactly<ArgumentOutOfRangeException>();

        // A positive length is accepted.
        TypeFactory.REQUIRED.FixedChar(1);
        TypeFactory.REQUIRED.VarChar(1);
        TypeFactory.REQUIRED.FixedBinary(1);
    }

    /// <summary>
    /// Tests that PrecisionTimestamp and PrecisionTimestampTZ reject a precision outside 0-12.
    /// </summary>
    [Test]
    public async Task TestPrecisionTimestampRejectsOutOfRangePrecision()
    {
        await Assert.That(() => TypeFactory.REQUIRED.PrecisionTimestamp(-1)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.PrecisionTimestamp(13)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.PrecisionTimestampTZ(-1)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.PrecisionTimestampTZ(13)).ThrowsExactly<ArgumentOutOfRangeException>();

        // Boundary values (seconds through picoseconds) are accepted.
        TypeFactory.REQUIRED.PrecisionTimestamp(0);
        TypeFactory.REQUIRED.PrecisionTimestamp(12);
        TypeFactory.REQUIRED.PrecisionTimestampTZ(0);
        TypeFactory.REQUIRED.PrecisionTimestampTZ(12);
    }
}
