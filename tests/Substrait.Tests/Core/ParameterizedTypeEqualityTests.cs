// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Core.Type;
using NullableType = Substrait.Core.Type.IType.NullableType;

namespace Substrait.Tests.Core;

public sealed class ParameterizedTypeEqualityTests
{
    [Test]
    [Arguments("decimal")]
    [Arguments("fixedchar")]
    [Arguments("varchar")]
    [Arguments("fixedbinary")]
    [Arguments("pts")]
    [Arguments("ptstz")]
    [Arguments("struct")]
    [Arguments("list")]
    [Arguments("map")]
    public async Task EqualityIncludesTypeVariation(string kind)
    {
        IType original = Create(kind, NullableType.Required, "custom");
        IType equal = Create(kind, NullableType.Required, "custom");

        await Assert.That(original).IsEqualTo(equal);
        await Assert.That(original.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(original).IsNotEqualTo(Create(kind, NullableType.Nullable, "custom"));
        await Assert.That(original).IsNotEqualTo(Create(kind, NullableType.Required, "different"));
        await Assert.That(original).IsNotEqualTo(Create(kind, NullableType.Required, null));
    }

    [Test]
    public async Task ContainerEqualityIncludesNestedTypeVariations()
    {
        IType Nested(string variation) => TypeFactory.REQUIRED.Struct([
            TypeFactory.REQUIRED.List(Create("decimal", NullableType.Required, variation)),
            TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, Create("varchar", NullableType.Nullable, variation)),
        ]);

        await Assert.That(Nested("custom")).IsEqualTo(Nested("custom"));
        await Assert.That(Nested("custom").GetHashCode()).IsEqualTo(Nested("custom").GetHashCode());
        await Assert.That(Nested("custom")).IsNotEqualTo(Nested("different"));
    }

    private static IType Create(string kind, NullableType nullable, string? variation)
    {
        var factory = TypeFactory.Of(nullable);
        Func<ITypeVariation?, IType> create = kind switch
        {
            "decimal" => v => factory.Decimal(10, 2, v),
            "fixedchar" => v => factory.FixedChar(8, v),
            "varchar" => v => factory.VarChar(16, v),
            "fixedbinary" => v => factory.FixedBinary(4, v),
            "pts" => v => factory.PrecisionTimestamp(3, v),
            "ptstz" => v => factory.PrecisionTimestampTZ(6, v),
            "struct" => v => factory.Struct([TypeFactory.REQUIRED.I32], v),
            "list" => v => factory.List(TypeFactory.REQUIRED.I32, v),
            "map" => v => factory.Map(TypeFactory.REQUIRED.STR, TypeFactory.REQUIRED.I64, v),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return variation is null
            ? create(null)
            : create(new TypeVariationImpl("extension:example:types", create(null).TypeName, variation, string.Empty, FunctionBehavior.INHERITS));
    }
}
