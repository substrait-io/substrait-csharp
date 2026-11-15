// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Core.Type;
using NullableType = Substrait.Core.Type.IType.NullableType;

namespace Substrait.Tests.Core;

public sealed class PrimitiveTypeEqualityTests
{
    [Test]
    [Arguments("bool")]
    [Arguments("i8")]
    [Arguments("i16")]
    [Arguments("i32")]
    [Arguments("i64")]
    [Arguments("fp32")]
    [Arguments("fp64")]
    [Arguments("str")]
    [Arguments("binary")]
    [Arguments("date")]
    [Arguments("iyear")]
    public async Task VariedInstancesCompareByValue(string kind)
    {
        IType original = Create(kind, NullableType.Required, "custom");
        IType equal = Create(kind, NullableType.Required, "custom");

        await Assert.That(ReferenceEquals(original, equal)).IsFalse();
        await Assert.That(original).IsEqualTo(equal);
        await Assert.That(original.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(TypeFactory.REQUIRED.List(original)).IsEqualTo(TypeFactory.REQUIRED.List(equal));
        await Assert.That(original).IsNotEqualTo(Create(kind, NullableType.Nullable, "custom"));
        await Assert.That(original).IsNotEqualTo(Create(kind, NullableType.Required, "different"));
        await Assert.That(original).IsNotEqualTo(Create(kind, NullableType.Required, null));
    }

    private static IType Create(string kind, NullableType nullable, string? variation)
    {
        var factory = TypeFactory.Of(nullable);
        Func<ITypeVariation?, IType> create = kind switch
        {
            "bool" => factory.Boolean_,
            "i8" => factory.I8_,
            "i16" => factory.I16_,
            "i32" => factory.I32_,
            "i64" => factory.I64_,
            "fp32" => factory.FP32_,
            "fp64" => factory.FP64_,
            "str" => factory.String_,
            "binary" => factory.Binary_,
            "date" => factory.Date_,
            "iyear" => factory.IntervalYear_,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return variation is null
            ? create(null)
            : create(new TypeVariationImpl("extension:example:types", create(null).TypeName, variation, string.Empty, FunctionBehavior.INHERITS));
    }
}
