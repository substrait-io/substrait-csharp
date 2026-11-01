// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using NullableType = Substrait.Core.Type.IType.NullableType;
using ProtoType = Substrait.Protobuf.Type;
using ProtoTypeAlias = Substrait.Protobuf.TypeAlias;

namespace Substrait.Tests.Core;

public sealed class TypeAliasTableTests
{
    private const string Urn = "extension:example:alias-table-types";

    [Test]
    public async Task EmptyTableHasNoDefinitions()
    {
        await Assert.That(TypeAliasTable.Empty.Count).IsEqualTo(0);
        await Assert.That(new TypeAliasTable.Builder().Build()).IsSameReferenceAs(TypeAliasTable.Empty);
    }

    [Test]
    public async Task ResolvesASimpleReferenceApplyingSiteNullabilityAndPreservingDefinitionTypeVariation()
    {
        var variation = new TypeVariationImpl(Urn, "i64", "packed", string.Empty, FunctionBehavior.INHERITS);
        var definition = TypeFactory.REQUIRED.I64_(variation);
        TypeAliasTable table = new TypeAliasTable.Builder().Add(5, definition).Build();

        IType requiredResolved = table.Resolve(TypeFactory.REQUIRED.AliasReference(5));
        IType nullableResolved = table.Resolve(TypeFactory.NULLABLE.AliasReference(5));

        await Assert.That(requiredResolved.Nullable).IsEqualTo(NullableType.Required);
        await Assert.That(requiredResolved.TypeVariation).IsEqualTo(variation);
        await Assert.That(nullableResolved.Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(nullableResolved.TypeVariation).IsEqualTo(variation);

        // Neither resolution mutated the shared definition.
        await Assert.That(definition.Nullable).IsEqualTo(NullableType.Required);
        await Assert.That(definition.TypeVariation).IsEqualTo(variation);
    }

    [Test]
    public async Task ResolvesAnchorZero()
    {
        TypeAliasTable table = new TypeAliasTable.Builder().Add(0, TypeFactory.REQUIRED.I64).Build();

        IType resolved = table.Resolve(TypeFactory.REQUIRED.AliasReference(0));

        await Assert.That(resolved).IsEqualTo(TypeFactory.REQUIRED.I64);
    }

    [Test]
    public async Task ResolutionDoesNotDependOnTheOrderDefinitionsWereAdded()
    {
        // Anchor 1 is added before anchor 2, even though 1's definition references 2 - a forward
        // reference the topological order must still handle.
        IType inner = TypeFactory.REQUIRED.I64;
        var forward = new TypeAliasTable.Builder()
            .Add(1, TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.AliasReference(2)]))
            .Add(2, inner)
            .Build();
        var backward = new TypeAliasTable.Builder()
            .Add(2, inner)
            .Add(1, TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.AliasReference(2)]))
            .Build();

        IType expected = TypeFactory.REQUIRED.Struct([inner]);
        await Assert.That(forward.Resolve(TypeFactory.REQUIRED.AliasReference(1))).IsEqualTo(expected);
        await Assert.That(backward.Resolve(TypeFactory.REQUIRED.AliasReference(1))).IsEqualTo(expected);
    }

    [Test]
    public async Task ResolvesDiamondSharedDependencyWithoutFalseCycle()
    {
        // 1 and 2 both depend on 3: a shared, acyclic dependency must not be mistaken for a cycle.
        IType shared = TypeFactory.REQUIRED.I64;
        TypeAliasTable table = new TypeAliasTable.Builder()
            .Add(1, TypeFactory.REQUIRED.List(TypeFactory.REQUIRED.AliasReference(3)))
            .Add(2, TypeFactory.REQUIRED.List(TypeFactory.REQUIRED.AliasReference(3)))
            .Add(3, shared)
            .Build();

        await Assert.That(table.Resolve(TypeFactory.REQUIRED.AliasReference(1))).IsEqualTo(TypeFactory.REQUIRED.List(shared));
        await Assert.That(table.Resolve(TypeFactory.REQUIRED.AliasReference(2))).IsEqualTo(TypeFactory.REQUIRED.List(shared));
    }

    [Test]
    public async Task ResolvesNestedReferenceInsideStructListMapUserDefinedAndFunc()
    {
        IType target = TypeFactory.REQUIRED.I32;
        TypeAliasTable table = new TypeAliasTable.Builder().Add(9, target).Build();
        IType reference = TypeFactory.REQUIRED.AliasReference(9);
        var anchor = new TypeAnchor(Urn, "shape");

        IType struct_ = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I8, reference]);
        await Assert.That(table.Resolve(struct_)).IsEqualTo(TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I8, target]));

        IType list = TypeFactory.REQUIRED.List(reference);
        await Assert.That(table.Resolve(list)).IsEqualTo(TypeFactory.REQUIRED.List(target));

        IType mapKey = TypeFactory.REQUIRED.Map(reference, TypeFactory.REQUIRED.STR);
        await Assert.That(table.Resolve(mapKey)).IsEqualTo(TypeFactory.REQUIRED.Map(target, TypeFactory.REQUIRED.STR));
        IType mapValue = TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, reference);
        await Assert.That(table.Resolve(mapValue)).IsEqualTo(TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, target));

        IType userDefined = TypeFactory.REQUIRED.UserDefined(anchor, [new TypeParameter.DataType(reference)]);
        var resolvedUserDefined = (UserDefinedType)table.Resolve(userDefined);
        await Assert.That(((TypeParameter.DataType)resolvedUserDefined.Parameters[0]).Value).IsEqualTo(target);

        IType funcParameter = TypeFactory.REQUIRED.Func([reference], TypeFactory.REQUIRED.BOOL);
        await Assert.That(table.Resolve(funcParameter)).IsEqualTo(TypeFactory.REQUIRED.Func([target], TypeFactory.REQUIRED.BOOL));
        IType funcReturn = TypeFactory.REQUIRED.Func([TypeFactory.REQUIRED.BOOL], reference);
        await Assert.That(table.Resolve(funcReturn)).IsEqualTo(TypeFactory.REQUIRED.Func([TypeFactory.REQUIRED.BOOL], target));
    }

    [Test]
    public async Task RejectsDuplicateAnchor()
    {
        var builder = new TypeAliasTable.Builder().Add(5, TypeFactory.REQUIRED.I64);

        await Assert.That(() => builder.Add(5, TypeFactory.REQUIRED.I32)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsDefinitionThatIsDirectlyAnotherAlias()
    {
        var builder = new TypeAliasTable.Builder();

        await Assert.That(() => builder.Add(5, TypeFactory.REQUIRED.AliasReference(6))).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsNonConcreteUnboundDefinition()
    {
        var builder = new TypeAliasTable.Builder();

        await Assert.That(() => builder.Add(5, UnboundType.Instance)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsDefinitionReferencingAMissingAnchor()
    {
        var builder = new TypeAliasTable.Builder().Add(5, TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.AliasReference(404)]));

        await Assert.That(() => builder.Build()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsResolveTimeReferenceToAMissingAnchor()
    {
        TypeAliasTable table = new TypeAliasTable.Builder().Add(1, TypeFactory.REQUIRED.I64).Build();

        await Assert.That(() => table.Resolve(TypeFactory.REQUIRED.AliasReference(404))).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsSelfCycle()
    {
        var builder = new TypeAliasTable.Builder().Add(1, TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.AliasReference(1)]));

        await Assert.That(() => builder.Build()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsMultiNodeCycle()
    {
        var builder = new TypeAliasTable.Builder()
            .Add(1, TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.AliasReference(2)]))
            .Add(2, TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.AliasReference(3)]))
            .Add(3, TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.AliasReference(1)]));

        await Assert.That(() => builder.Build()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsCycleNestedInsideAFunctionReturnType()
    {
        // The same cycle check must see through containers other than struct/list/map.
        var builder = new TypeAliasTable.Builder()
            .Add(1, TypeFactory.REQUIRED.Func([], TypeFactory.REQUIRED.AliasReference(2)))
            .Add(2, TypeFactory.REQUIRED.Func([], TypeFactory.REQUIRED.AliasReference(1)));

        await Assert.That(() => builder.Build()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsCycleNestedInsideAListElement()
    {
        var builder = new TypeAliasTable.Builder()
            .Add(1, TypeFactory.REQUIRED.List(TypeFactory.REQUIRED.AliasReference(2)))
            .Add(2, TypeFactory.REQUIRED.List(TypeFactory.REQUIRED.AliasReference(1)));

        await Assert.That(() => builder.Build()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsCycleNestedInsideMapKeyOrValue()
    {
        var keyCycle = new TypeAliasTable.Builder()
            .Add(1, TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.AliasReference(2), TypeFactory.REQUIRED.STR))
            .Add(2, TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.AliasReference(1), TypeFactory.REQUIRED.STR));
        await Assert.That(() => keyCycle.Build()).ThrowsExactly<ArgumentException>();

        var valueCycle = new TypeAliasTable.Builder()
            .Add(1, TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, TypeFactory.REQUIRED.AliasReference(2)))
            .Add(2, TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, TypeFactory.REQUIRED.AliasReference(1)));
        await Assert.That(() => valueCycle.Build()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RejectsCycleNestedInsideAUserDefinedDataTypeParameter()
    {
        var anchor = new TypeAnchor(Urn, "shape");
        var builder = new TypeAliasTable.Builder()
            .Add(1, TypeFactory.REQUIRED.UserDefined(anchor, [new TypeParameter.DataType(TypeFactory.REQUIRED.AliasReference(2))]))
            .Add(2, TypeFactory.REQUIRED.UserDefined(anchor, [new TypeParameter.DataType(TypeFactory.REQUIRED.AliasReference(1))]));

        await Assert.That(() => builder.Build()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ResolvesDeepChainWithoutStackOverflow()
    {
        const int depth = 2000;
        var builder = new TypeAliasTable.Builder();
        for (uint anchor = 0; anchor < depth; ++anchor)
        {
            builder.Add(anchor, anchor == 0 ? TypeFactory.REQUIRED.I64 : TypeFactory.REQUIRED.List(TypeFactory.REQUIRED.AliasReference(anchor - 1)));
        }

        TypeAliasTable table = builder.Build();

        IType resolved = table.Resolve(TypeFactory.REQUIRED.AliasReference((uint)depth - 1));
        for (int level = 0; level < depth - 1; ++level)
        {
            resolved = ((ParameterizedType.List)resolved).ElementType;
        }

        await Assert.That(resolved).IsEqualTo(TypeFactory.REQUIRED.I64);
    }

    [Test]
    public async Task PreservesNestedMetadataDeepInsideAContainerDefinition()
    {
        var variation = new TypeVariationImpl(Urn, "fixedchar", "custom", string.Empty, FunctionBehavior.INHERITS);
        IType nestedWithVariation = TypeFactory.REQUIRED.FixedChar(4, variation);
        TypeAliasTable table = new TypeAliasTable.Builder()
            .Add(1, TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, nestedWithVariation]))
            .Build();

        var resolved = (ParameterizedType.Struct)table.Resolve(TypeFactory.NULLABLE.AliasReference(1));

        await Assert.That(resolved.Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(resolved.Fields[1]).IsSameReferenceAs(nestedWithVariation);
        await Assert.That(resolved.Fields[1].TypeVariation).IsEqualTo(variation);
    }

    [Test]
    public async Task DefinitionsExposesEachAnchorsAsDeclaredDefinitionPreservingNestedAliasReferences()
    {
        IType asDeclared = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.AliasReference(2)]);
        TypeAliasTable table = new TypeAliasTable.Builder()
            .Add(1, asDeclared)
            .Add(2, TypeFactory.REQUIRED.I64)
            .Build();

        // Definitions is the as-declared form, not Resolve's flattened form: anchor 1's nested
        // reference to anchor 2 survives, rather than being inlined to I64 ahead of time.
        await Assert.That(table.Definitions[1]).IsSameReferenceAs(asDeclared);
        var declaredStruct = (ParameterizedType.Struct)table.Definitions[1];
        await Assert.That(declaredStruct.Fields[0]).IsAssignableTo<ParameterizedType.AliasReference>();
    }

    [Test]
    public async Task RoundTripsThroughProtoTypeAliasDeclarations()
    {
        var protoImport = new ProtoToTypeAliasTableConverter();
        var protoExport = new TypeAliasTableToProtoConverter();
        var aliases = new[]
        {
            new ProtoTypeAlias { TypeAliasAnchor = 2, Type = RequiredI64() },
            new ProtoTypeAlias
            {
                TypeAliasAnchor = 1,
                Type = new ProtoType
                {
                    Struct = new ProtoType.Types.Struct
                    {
                        Nullability = ProtoType.Types.Nullability.Required,
                        Types_ = { new ProtoType { Alias = new ProtoType.Types.TypeAliasReference { TypeAliasReference_ = 2, Nullability = ProtoType.Types.Nullability.Required } } },
                    },
                },
            },
        };

        TypeAliasTable table = protoImport.From(aliases);
        await Assert.That(table.Count).IsEqualTo(2);
        await Assert.That(table.Resolve(TypeFactory.REQUIRED.AliasReference(1))).IsEqualTo(TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]));

        var exported = protoExport.From(table);
        await Assert.That(exported.Count).IsEqualTo(2);
        TypeAliasTable reimported = protoImport.From(exported);
        await Assert.That(reimported.Resolve(TypeFactory.REQUIRED.AliasReference(1))).IsEqualTo(table.Resolve(TypeFactory.REQUIRED.AliasReference(1)));
    }

    [Test]
    public async Task RejectsAnUnsetAliasedTypeDuringProtoConversion()
    {
        var protoImport = new ProtoToTypeAliasTableConverter();
        var aliases = new[] { new ProtoTypeAlias { TypeAliasAnchor = 1 } };

        await Assert.That(() => protoImport.From(aliases)).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task RejectsAMalformedAliasGraphDuringProtoConversion()
    {
        // The proto converter wraps the same in-memory table validation (here, a direct
        // alias-to-alias definition) into the repository's wire-deserialization exception type.
        var protoImport = new ProtoToTypeAliasTableConverter();
        var aliases = new[]
        {
            new ProtoTypeAlias
            {
                TypeAliasAnchor = 1,
                Type = new ProtoType { Alias = new ProtoType.Types.TypeAliasReference { TypeAliasReference_ = 2, Nullability = ProtoType.Types.Nullability.Required } },
            },
        };

        await Assert.That(() => protoImport.From(aliases)).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task RepeatedIndependentProtoConversionsDoNotLeakState()
    {
        var protoImport = new ProtoToTypeAliasTableConverter();
        var aliases = new[] { new ProtoTypeAlias { TypeAliasAnchor = 1, Type = RequiredI64() } };

        TypeAliasTable first = protoImport.From(aliases);
        TypeAliasTable second = protoImport.From(aliases);

        await Assert.That(first.Resolve(TypeFactory.REQUIRED.AliasReference(1))).IsEqualTo(second.Resolve(TypeFactory.REQUIRED.AliasReference(1)));
        await Assert.That(first).IsNotSameReferenceAs(second);
    }

    [Test]
    public async Task ExportSharesExtensionAnchorsWithTheGivenConverterContext()
    {
        // The "conversion-context contract": extension anchors referenced inside an alias
        // definition are collected into the same plan-wide table as the rest of the plan, not a
        // separate one local to the alias-table converter.
        var variation = new TypeVariationImpl(Urn, "i64", "packed", string.Empty, FunctionBehavior.INHERITS);
        TypeAliasTable table = new TypeAliasTable.Builder().Add(1, TypeFactory.REQUIRED.I64_(variation)).Build();
        var context = new PlanToProtoConverter.ConverterContext();

        IReadOnlyList<ProtoTypeAlias> exported = new TypeAliasTableToProtoConverter().From(table, context);

        await Assert.That(exported[0].Type.I64.TypeVariationReference).IsNotEqualTo(0u);
        await Assert.That(context.ExtensionsCollector.ExtensionUris).Contains(Urn);
    }

    [Test]
    public async Task RejectsUnspecifiedNullabilityAtResolveTime()
    {
        // The reference constructor itself does not validate nullability; resolution must still
        // reject it rather than let it reach TypeFactory.Of with an unsupported value.
        TypeAliasTable table = new TypeAliasTable.Builder().Add(1, TypeFactory.REQUIRED.I64).Build();
        IType unspecified = new ParameterizedType.AliasReference(1, NullableType.Unspecified);

        await Assert.That(() => table.Resolve(unspecified)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RepeatedIndependentResolutionsDoNotLeakState()
    {
        TypeAliasTable table = new TypeAliasTable.Builder().Add(1, TypeFactory.REQUIRED.I64).Build();
        IType reference = TypeFactory.REQUIRED.AliasReference(1);

        IType first = table.Resolve(reference);
        IType second = table.Resolve(reference);

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(table.Resolve(reference)).IsEqualTo(TypeFactory.REQUIRED.I64);
    }

    [Test]
    public async Task TypesWithNoAliasReferenceResolveToAnEquivalentType()
    {
        TypeAliasTable table = new TypeAliasTable.Builder().Add(1, TypeFactory.REQUIRED.I64).Build();
        IType type = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.BOOL, TypeFactory.NULLABLE.STR]);

        await Assert.That(table.Resolve(type)).IsEqualTo(type);
    }

    private static ProtoType RequiredI64() => new() { I64 = new ProtoType.Types.I64 { Nullability = ProtoType.Types.Nullability.Required } };
}
