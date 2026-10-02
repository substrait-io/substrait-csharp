// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Extension.Types;
using Substrait.Core.Type;
using Substrait.Tools;

namespace Substrait.Tests.Tools;

public sealed class TypeUtilsTests
{
    [Test]
    public async Task ConcatCombinesNamedStructs()
    {
        var first = new NamedStruct(
            ["a", "b", "c"],
            new ParameterizedType.Struct(
                [TypeFactory.REQUIRED.BOOL, TypeFactory.REQUIRED.STR, TypeFactory.REQUIRED.BINARY],
                IType.NullableType.Nullable));
        var second = new NamedStruct(
            ["x", "y"],
            new ParameterizedType.Struct(
                [TypeFactory.REQUIRED.FP64, TypeFactory.REQUIRED.FP32],
                IType.NullableType.Required));

        NamedStruct result = first.Concat(second);

        await AssertSequenceEqual(["a", "b", "c", "x", "y"], result.Names);
        await AssertSequenceEqual(
            [TypeFactory.REQUIRED.BOOL, TypeFactory.REQUIRED.STR, TypeFactory.REQUIRED.BINARY, TypeFactory.REQUIRED.FP64, TypeFactory.REQUIRED.FP32],
            result.Struct.Fields);
        await Assert.That(result.Struct.Nullable).IsEqualTo(IType.NullableType.Required);
    }

    [Test]
    public async Task ConcatCombinesNamedStructSequence()
    {
        var first = new NamedStruct(["i"], TypeFactory.NULLABLE.Struct([TypeFactory.REQUIRED.BOOL]));
        var second = new NamedStruct(["j"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.FP64]));
        var third = new NamedStruct(["k"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.STR]));

        NamedStruct result = TypeUtils.Concat([first, second, third]);

        await AssertSequenceEqual(["i", "j", "k"], result.Names);
        await AssertSequenceEqual(
            [TypeFactory.REQUIRED.BOOL, TypeFactory.REQUIRED.FP64, TypeFactory.REQUIRED.STR],
            result.Struct.Fields);
        await Assert.That(result.Struct.Nullable).IsEqualTo(IType.NullableType.Required);
    }

    [Test]
    public async Task RenameReplacesNamesAndPreservesStruct()
    {
        var namedStruct = new NamedStruct(
            ["i", "j"],
            TypeFactory.NULLABLE.Struct([TypeFactory.REQUIRED.BINARY, TypeFactory.REQUIRED.STR]));

        NamedStruct result = namedStruct.Rename(["x", "y"]);

        await AssertSequenceEqual(["x", "y"], result.Names);
        await Assert.That(result.Struct).IsSameReferenceAs(namedStruct.Struct);
    }

    [Test]
    [MethodDataSource(nameof(GetTypeComparisonCases))]
    public async Task TypeEqualityComparerHonorsComparisonMode(IType first, IType second, bool sameTypeParameters)
    {
        bool sameBaseType = first.GetType() == second.GetType()
            && first.InputNodes.Select(type => type.GetType()).SequenceEqual(second.InputNodes.Select(type => type.GetType()));
        bool sameNullability = first.Nullable == second.Nullable
            && first.InputNodes.Select(type => type.Nullable).SequenceEqual(second.InputNodes.Select(type => type.Nullable));
        bool sameTypeVariation = first.TypeVariation.EqualsWithNull(second.TypeVariation)
            && first.InputNodes.Select(type => type.TypeVariation).SequenceEqual(second.InputNodes.Select(type => type.TypeVariation));

        await Assert.That(first.Equals(second, ITypeComparison.IgnoreNullability)).IsEqualTo(sameBaseType);
        await Assert.That(first.Equals(second, ITypeComparison.IgnoreTypeParameters)).IsEqualTo(sameBaseType && sameNullability);
        await Assert.That(first.Equals(second, ITypeComparison.IgnoreTypeVariation)).IsEqualTo(sameBaseType && sameNullability && sameTypeParameters);
        await Assert.That(first.Equals(second, ITypeComparison.Strict)).IsEqualTo(sameBaseType && sameNullability && sameTypeParameters && sameTypeVariation);
    }
    public static IEnumerable<Func<(IType First, IType Second, bool SameTypeParameters)>> GetTypeComparisonCases()
    {
        TypeFactory required = TypeFactory.REQUIRED;
        TypeFactory nullable = TypeFactory.NULLABLE;

        foreach (IType type in typeof(PrimitiveTypeFactory)
            .GetProperties()
            .Where(property => typeof(PrimitiveType).IsAssignableFrom(property.PropertyType))
            .Select(property => property.GetValue(required))
            .Cast<IType>())
        {
            var firstVariation = new TypeVariationImpl("/test", type.TypeName, "var1", string.Empty, FunctionBehavior.INHERITS);
            var secondVariation = new TypeVariationImpl("/test", type.TypeName, "var2", string.Empty, FunctionBehavior.INHERITS);
            IType[] types =
            [
                required.ResolveTypeWithNullability(type),
                required.ResolveTypeWithNullability(type, firstVariation),
                required.ResolveTypeWithNullability(type, secondVariation),
                nullable.ResolveTypeWithNullability(type),
                nullable.ResolveTypeWithNullability(type, firstVariation),
                nullable.ResolveTypeWithNullability(type, secondVariation),
            ];

            foreach (IType first in types)
            {
                foreach (IType second in types)
                {
                    yield return () => (first, second, true);
                }
            }
        }

        (IType First, IType Second)[] parameterizedTypes =
        [
            (required.PrecisionTimestamp(1), required.PrecisionTimestamp(2)),
            (required.PrecisionTimestampTZ(1), required.PrecisionTimestampTZ(2)),
            (required.FixedChar(1), required.FixedChar(2)),
            (required.VarChar(1), required.VarChar(2)),
            (required.FixedBinary(1), required.FixedBinary(2)),
            (required.Decimal(2, 1), required.Decimal(2, 2)),
        ];

        foreach ((IType first, IType second) in parameterizedTypes)
        {
            var firstVariation = new TypeVariationImpl("/test", first.TypeName, "var1", string.Empty, FunctionBehavior.INHERITS);
            var secondVariation = new TypeVariationImpl("/test", first.TypeName, "var2", string.Empty, FunctionBehavior.INHERITS);
            var types = new List<(IType Type, int ParameterGroup)>();
            AddParameterizedTypes(types, first, 0, required, nullable, firstVariation, secondVariation);
            AddParameterizedTypes(types, second, 1, required, nullable, firstVariation, secondVariation);

            foreach ((IType firstType, int firstGroup) in types)
            {
                foreach ((IType secondType, int secondGroup) in types)
                {
                    yield return () => (firstType, secondType, firstGroup == secondGroup);
                }
            }
        }

        var structFirstVariation = new TypeVariationImpl("/test", required.VarChar(1).TypeName, "var1", string.Empty, FunctionBehavior.INHERITS);
        var structSecondVariation = new TypeVariationImpl("/test", required.VarChar(1).TypeName, "var2", string.Empty, FunctionBehavior.INHERITS);
        IType firstVarchar = required.VarChar(1);
        IType secondVarchar = required.VarChar(2);
        (IType Type, int ParameterGroup)[] structTypes =
        [
            (required.Struct([required.I64, required.ResolveTypeWithNullability(firstVarchar)]), 0),
            (required.Struct([required.I64, required.ResolveTypeWithNullability(firstVarchar, structFirstVariation)]), 0),
            (required.Struct([required.I64, required.ResolveTypeWithNullability(firstVarchar, structSecondVariation)]), 0),
            (required.Struct([required.I64, required.ResolveTypeWithNullability(secondVarchar)]), 1),
            (required.Struct([required.I64, required.ResolveTypeWithNullability(secondVarchar, structFirstVariation)]), 1),
            (required.Struct([required.I64, required.ResolveTypeWithNullability(secondVarchar, structSecondVariation)]), 1),
            (required.Struct([required.I64, nullable.ResolveTypeWithNullability(firstVarchar)]), 0),
            (required.Struct([required.I64, nullable.ResolveTypeWithNullability(firstVarchar, structFirstVariation)]), 0),
            (required.Struct([required.I64, nullable.ResolveTypeWithNullability(firstVarchar, structSecondVariation)]), 0),
        ];

        foreach ((IType first, int firstGroup) in structTypes)
        {
            foreach ((IType second, int secondGroup) in structTypes)
            {
                yield return () => (first, second, firstGroup == secondGroup);
            }
        }
    }

    private static void AddParameterizedTypes(
        List<(IType Type, int ParameterGroup)> types,
        IType type,
        int parameterGroup,
        TypeFactory required,
        TypeFactory nullable,
        TypeVariationImpl firstVariation,
        TypeVariationImpl secondVariation)
    {
        types.Add((type, parameterGroup));
        types.Add((required.ResolveTypeWithNullability(type, firstVariation), parameterGroup));
        types.Add((required.ResolveTypeWithNullability(type, secondVariation), parameterGroup));
        types.Add((nullable.ResolveTypeWithNullability(type), parameterGroup));
        types.Add((nullable.ResolveTypeWithNullability(type, firstVariation), parameterGroup));
        types.Add((nullable.ResolveTypeWithNullability(type, secondVariation), parameterGroup));
    }

    private static async Task AssertSequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        await Assert.That(expected.SequenceEqual(actual)).IsTrue().Because($"Expected [{string.Join(", ", expected)}], but found [{string.Join(", ", actual)}].");
    }
}
