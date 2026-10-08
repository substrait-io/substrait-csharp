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

        for (int mode = 0; mode <= (int)ITypeComparison.Strict; ++mode)
        {
            var comparer = TypeUtils.ITypeEqualityComparer.Of((ITypeComparison)mode);
            if (comparer.Equals(first, second))
            {
                await Assert.That(comparer.GetHashCode(first)).IsEqualTo(comparer.GetHashCode(second));
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(GetVariationCaseSensitivityCases))]
    public async Task TypeVariationCaseSensitivityMatchesEqualityAndHashing(IType first, IType second, bool sameVariation)
    {
        for (int mode = 0; mode <= (int)ITypeComparison.Strict; ++mode)
        {
            var comparison = (ITypeComparison)mode;
            var comparer = TypeUtils.ITypeEqualityComparer.Of(comparison);
            bool expected = sameVariation || (comparison & ITypeComparison.TypeVariation) == 0;
            await AssertTypeComparison(first, second, comparer, expected);
        }
    }

    [Test]
    [MethodDataSource(nameof(LeafHashCases))]
    public async Task HashesLeafTypesWithoutAllocations(IType type)
    {
        for (int mode = 0; mode <= (int)ITypeComparison.Strict; ++mode)
        {
            var comparer = TypeUtils.ITypeEqualityComparer.Of((ITypeComparison)mode);
            var (allocated, hash) = MeasureHashAllocations(type, comparer);
            await Assert.That(allocated).IsEqualTo(0L).Because($"Hashing {type.TypeName} in comparison mode {mode} should not allocate.");
            await Assert.That(hash).IsEqualTo(0);
        }
    }

    [Test]
    public async Task CustomCompositeTypesPreserveChildStructure()
    {
        TypeFactory factory = TypeFactory.REQUIRED;
        (IType First, IType Second, bool Equal)[] cases =
        [
            (new CustomContainer([]), new CustomContainer([]), true),
            (new CustomContainer([factory.I32]), new CustomContainer([factory.I32]), true),
            (new CustomContainer([]), new CustomContainer([factory.I32]), false),
            (new CustomContainer([factory.I32]), new CustomContainer([factory.I64]), false),
            (new CustomContainer([factory.I32, factory.I64]), new CustomContainer([factory.I64, factory.I32]), false),
            (
                new CustomContainer([new CustomContainer([factory.I32]), factory.I32]),
                new CustomContainer([new CustomContainer([factory.I32, factory.I32])]),
                false),
        ];

        for (int mode = 0; mode <= (int)ITypeComparison.Strict; ++mode)
        {
            var comparer = TypeUtils.ITypeEqualityComparer.Of((ITypeComparison)mode);
            foreach (var (first, second, equal) in cases)
            {
                await AssertTypeComparison(first, second, comparer, equal);
            }
        }
    }

    [Test]
    public async Task CustomCompositeTypesRespectNestedComparisonMode()
    {
        TypeFactory factory = TypeFactory.REQUIRED;
        var variation = new TypeVariationImpl("extension:test:types", "varchar", "custom", string.Empty, FunctionBehavior.INHERITS);
        IType first = new CustomContainer([factory.VarChar(8)]);
        (IType Child, ITypeComparison Difference)[] cases =
        [
            (factory.VarChar(8), 0),
            (factory.VarChar(16), ITypeComparison.TypeParameter),
            (TypeFactory.NULLABLE.VarChar(8), ITypeComparison.Nullability),
            (factory.VarChar(8, variation), ITypeComparison.TypeVariation),
            (TypeFactory.NULLABLE.VarChar(16, variation), ITypeComparison.Strict),
        ];

        for (int mode = 0; mode <= (int)ITypeComparison.Strict; ++mode)
        {
            var comparison = (ITypeComparison)mode;
            var comparer = TypeUtils.ITypeEqualityComparer.Of(comparison);
            foreach (var (child, difference) in cases)
            {
                IType second = new CustomContainer([child]);
                await AssertTypeComparison(first, second, comparer, (comparison & difference) == 0);
            }
        }
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(32, false)]
    [Arguments(32, true)]
    public async Task ComparesUserDefinedNodesWithoutAllocations(int parameterCount, bool withVariation)
    {
        UserDefinedType first = CreateType();
        UserDefinedType second = CreateType();
        for (int mode = 0; mode <= (int)ITypeComparison.Strict; ++mode)
        {
            var (allocated, equalCount) = MeasureNodeComparisonAllocations(first, second, (ITypeComparison)mode);
            await Assert.That(allocated).IsEqualTo(0L).Because($"Comparing {parameterCount} parameters in comparison mode {mode} should not allocate.");
            await Assert.That(equalCount).IsEqualTo(1000);
        }

        UserDefinedType CreateType() => (withVariation ? TypeFactory.NULLABLE : TypeFactory.REQUIRED).UserDefined(
            new TypeAnchor("extension:test:types", "shape"),
            Enumerable.Range(0, parameterCount).Select(index => new TypeParameter.Integer(index)),
            withVariation ? new TypeVariationImpl("extension:test:types", "shape", "custom", string.Empty, FunctionBehavior.INHERITS) : null);
    }

    private static async Task AssertTypeComparison(IType first, IType second, TypeUtils.ITypeEqualityComparer comparer, bool expected)
    {
        await Assert.That(comparer.Equals(first, second)).IsEqualTo(expected);
        await Assert.That(comparer.Equals(second, first)).IsEqualTo(expected);
        var set = new HashSet<IType>(comparer) { first };
        await Assert.That(set.Contains(second)).IsEqualTo(expected);
        await Assert.That(set.Add(second)).IsEqualTo(!expected);
        if (expected)
        {
            await Assert.That(comparer.GetHashCode(first)).IsEqualTo(comparer.GetHashCode(second));
        }
    }

    private static (long Allocated, int EqualCount) MeasureNodeComparisonAllocations(UserDefinedType first, UserDefinedType second, ITypeComparison comparison)
    {
        for (int index = 0; index < 1000; ++index)
        {
            first.NodeEquals(second, comparison);
        }

        int equalCount = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; ++index)
        {
            if (first.NodeEquals(second, comparison))
            {
                ++equalCount;
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return (allocated, equalCount);
    }

    public static IEnumerable<Func<IType>> LeafHashCases()
    {
        TypeFactory factory = TypeFactory.REQUIRED;
        var types = typeof(PrimitiveTypeFactory).GetProperties()
            .Where(property => typeof(PrimitiveType).IsAssignableFrom(property.PropertyType))
            .Select(property => (IType)property.GetValue(factory)!)
            .Concat(new IType[]
            {
                factory.PrecisionTime(9),
                factory.IntervalDay(6),
                factory.IntervalCompound(3),
                factory.PrecisionTimestamp(9),
                factory.PrecisionTimestampTZ(6),
                factory.FixedChar(8),
                factory.VarChar(16),
                factory.FixedBinary(4),
                factory.Decimal(10, 2),
            });
        foreach (IType type in types)
        {
            yield return () => type;
            yield return () => TypeFactory.NULLABLE.ResolveTypeWithNullability(
                type, new TypeVariationImpl("extension:test:types", type.TypeName, "custom", string.Empty, FunctionBehavior.INHERITS));
        }
    }

    private static (long Allocated, int Hash) MeasureHashAllocations(IType type, TypeUtils.ITypeEqualityComparer comparer)
    {
        int result = 0;
        for (int index = 0; index < 1000; ++index)
        {
            result ^= comparer.GetHashCode(type);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; ++index)
        {
            result ^= comparer.GetHashCode(type);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return (allocated, result);
    }

    public static IEnumerable<Func<(IType First, IType Second, bool SameVariation)>> GetVariationCaseSensitivityCases()
    {
        const string urn = "extension:test:types";
        const string name = "custom";
        TypeFactory factory = TypeFactory.REQUIRED;
        IType[] types =
        [
            factory.I32,
            factory.VarChar(8),
            factory.UserDefined(new TypeAnchor(urn, "shape"), [new TypeParameter.DataType(factory.I32)]),
        ];

        foreach (IType type in types)
        {
            var variation = new TypeVariationImpl(urn, type.TypeName, name, string.Empty, FunctionBehavior.INHERITS);
            IType first = factory.ResolveTypeWithNullability(type, variation);
            (TypeVariationImpl Variation, bool SameVariation)[] cases =
            [
                (new TypeVariationImpl(urn, type.TypeName.ToUpperInvariant(), name, string.Empty, FunctionBehavior.INHERITS), true),
                (new TypeVariationImpl(urn.ToUpperInvariant(), type.TypeName, name, string.Empty, FunctionBehavior.INHERITS), false),
                (new TypeVariationImpl(urn, type.TypeName, name.ToUpperInvariant(), string.Empty, FunctionBehavior.INHERITS), false),
            ];

            foreach (var (otherVariation, sameVariation) in cases)
            {
                IType second = factory.ResolveTypeWithNullability(type, otherVariation);
                yield return () => (first, second, sameVariation);
                yield return () => (
                    factory.Struct([factory.Struct([first])]),
                    factory.Struct([factory.Struct([second])]),
                    sameVariation);
            }
        }
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

    private sealed class CustomContainer(IReadOnlyList<IType> children) : ParameterizedType(IType.NullableType.Required)
    {
        public override IEnumerable<IType> InputNodes => children;

        public override string ShortTypeName => "custom";

        public override string TypeName => "custom";

        public override string ToTypeString() => "custom";

        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) =>
            visitor.Visit((IType)this, context);

        protected override bool NodeEqualTypeParameters(IType other) => true;
    }
}
