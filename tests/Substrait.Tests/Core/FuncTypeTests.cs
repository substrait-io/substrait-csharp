// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using TUnit.Assertions.Enums;
using NullableType = Substrait.Core.Type.IType.NullableType;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class FuncTypeTests
{
    private const string Urn = "extension:example:func-types";

    [Test]
    public async Task RoundTripsThroughProto()
    {
        ParameterizedType.Func type = TypeFactory.NULLABLE.Func([TypeFactory.REQUIRED.I64, TypeFactory.NULLABLE.STR], TypeFactory.REQUIRED.I32);
        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(type);

        await Assert.That(proto.Func.Nullability).IsEqualTo(ProtoType.Types.Nullability.Nullable);
        await Assert.That(proto.Func.ParameterTypes.Count).IsEqualTo(2);
        await Assert.That(proto.Func.ParameterTypes[0].I64).IsNotNull();
        await Assert.That(proto.Func.ParameterTypes[1].String.Nullability).IsEqualTo(ProtoType.Types.Nullability.Nullable);
        await Assert.That(proto.Func.ReturnType.I32).IsNotNull();

        foreach (var roundTripped in new[] { proto, ProtoType.Parser.ParseFrom(proto.ToByteArray()), ProtoType.Parser.ParseJson(proto.ToString()) })
        {
            IType actual = import.From(roundTripped);
            await Assert.That(actual).IsEqualTo(type);
            await Assert.That(actual.Equals(type, ITypeComparison.Strict)).IsTrue();
            await Assert.That(actual.GetHashCode()).IsEqualTo(type.GetHashCode());
            await Assert.That(export.From(actual)).IsEqualTo(proto);
        }
    }

    [Test]
    public async Task RoundTripsZeroParameterFunction()
    {
        ParameterizedType.Func type = TypeFactory.REQUIRED.Func([], TypeFactory.REQUIRED.I64);
        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(type);
        await Assert.That(proto.Func.ParameterTypes.Count).IsEqualTo(0);

        IType actual = import.From(proto);
        await Assert.That(actual).IsEqualTo(type);
        await Assert.That(((ParameterizedType.Func)actual).Parameters.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RoundTripsFunctionTypeAsParameterAndAsReturnType()
    {
        // func(func(i64)->bool, str) -> func(fp32)->i32: a function type appears both as a parameter
        // and as the return type.
        ParameterizedType.Func parameterFunc = TypeFactory.REQUIRED.Func([TypeFactory.REQUIRED.I64], TypeFactory.REQUIRED.BOOL);
        ParameterizedType.Func returnFunc = TypeFactory.NULLABLE.Func([TypeFactory.REQUIRED.FP32], TypeFactory.REQUIRED.I32);
        ParameterizedType.Func type = TypeFactory.REQUIRED.Func([parameterFunc, TypeFactory.REQUIRED.STR], returnFunc);

        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();
        ProtoType proto = export.From(type);
        IType actual = import.From(proto);

        await Assert.That(actual).IsEqualTo(type);
        await Assert.That(export.From(actual)).IsEqualTo(proto);

        var actualFunc = (ParameterizedType.Func)actual;
        await Assert.That(actualFunc.Parameters[0]).IsEqualTo(parameterFunc);
        await Assert.That(actualFunc.ReturnType).IsEqualTo(returnFunc);
    }

    [Test]
    public async Task PreservesChildMetadataAcrossRoundTrip()
    {
        // Func itself cannot carry a variation, but its parameter and return types still carry
        // their own variations and nullability across the round trip.
        var paramVariation = new TypeVariationImpl(Urn, "i64", "packed", string.Empty, FunctionBehavior.INHERITS);
        var returnVariation = new TypeVariationImpl(Urn, "string", "ci", string.Empty, FunctionBehavior.INHERITS);
        IType parameterType = TypeFactory.NULLABLE.I64_(paramVariation);
        IType returnType = TypeFactory.REQUIRED.String_(returnVariation);
        ParameterizedType.Func type = TypeFactory.REQUIRED.Func([parameterType], returnType);

        var context = new PlanToProtoConverter.ConverterContext();
        var export = new TypeToProtoConverter();
        ProtoType proto = export.From(type, context);

        var extensions = new ExtensionsCollection([], [paramVariation, returnVariation], [], [], []);
        var import = new ProtoToTypeConverter(LookupFrom(context), extensions);
        var roundTrip = (ParameterizedType.Func)import.From(proto);

        await Assert.That(roundTrip.Equals(type, ITypeComparison.Strict)).IsTrue();
        await Assert.That(roundTrip.Parameters[0].Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(roundTrip.Parameters[0].TypeVariation).IsEqualTo(paramVariation);
        await Assert.That(roundTrip.ReturnType.Nullable).IsEqualTo(NullableType.Required);
        await Assert.That(roundTrip.ReturnType.TypeVariation).IsEqualTo(returnVariation);
        await Assert.That(roundTrip.TypeVariation).IsNull();
    }

    [Test]
    public async Task NullabilityIsIndependentOfParameterAndReturnNullability()
    {
        // The function value's own nullability, each parameter's nullability, and the return type's
        // nullability are three independent dimensions.
        ParameterizedType.Func type = TypeFactory.NULLABLE.Func(
            [TypeFactory.REQUIRED.I64, TypeFactory.NULLABLE.I32], TypeFactory.REQUIRED.STR);

        await Assert.That(type.Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(type.Parameters[0].Nullable).IsEqualTo(NullableType.Required);
        await Assert.That(type.Parameters[1].Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(type.ReturnType.Nullable).IsEqualTo(NullableType.Required);

        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();
        var roundTrip = (ParameterizedType.Func)import.From(export.From(type));

        await Assert.That(roundTrip.Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(roundTrip.Parameters[0].Nullable).IsEqualTo(NullableType.Required);
        await Assert.That(roundTrip.Parameters[1].Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(roundTrip.ReturnType.Nullable).IsEqualTo(NullableType.Required);
    }

    [Test]
    public async Task RejectsNonNullTypeVariationAtConstruction()
    {
        var variation = new TypeVariationImpl(Urn, "func", "custom", string.Empty, FunctionBehavior.INHERITS);

        ArgumentException error = await Assert.That(() =>
            new ParameterizedType.Func([TypeFactory.REQUIRED.I64], TypeFactory.REQUIRED.I32, NullableType.Required, variation))
            .ThrowsExactly<ArgumentException>().And.IsNotNull();
        await Assert.That(error.ParamName).IsEqualTo("typeVariation");

        await Assert.That(() => TypeFactory.REQUIRED.Func([], TypeFactory.REQUIRED.I64, variation)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ResolveTypeWithNullabilityRejectsNonNullVariationButPreservesParametersAndReturnType()
    {
        var variation = new TypeVariationImpl(Urn, "func", "custom", string.Empty, FunctionBehavior.INHERITS);
        IType parameterType = TypeFactory.REQUIRED.I64;
        IType returnType = TypeFactory.NULLABLE.STR;
        ParameterizedType.Func type = TypeFactory.REQUIRED.Func([parameterType], returnType);

        await Assert.That(() => TypeFactory.NULLABLE.ResolveTypeWithNullability(type, variation)).ThrowsExactly<ArgumentException>();

        // No-op: same nullability requested, no variation requested -> same instance.
        await Assert.That(TypeFactory.REQUIRED.ResolveTypeWithNullability(type)).IsSameReferenceAs(type);

        // Changing only nullability leaves parameters and return type untouched by reference.
        var nullableFunc = (ParameterizedType.Func)TypeFactory.NULLABLE.ResolveTypeWithNullability(type);
        await Assert.That(nullableFunc.Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(nullableFunc.Parameters[0]).IsSameReferenceAs(parameterType);
        await Assert.That(nullableFunc.ReturnType).IsSameReferenceAs(returnType);
        await Assert.That(nullableFunc.TypeVariation).IsNull();
    }

    [Test]
    public async Task EqualityAndHashingDistinguishFuncByParametersReturnTypeAndNullability()
    {
        ParameterizedType.Func type = TypeFactory.REQUIRED.Func([TypeFactory.REQUIRED.I64], TypeFactory.REQUIRED.I32);
        ParameterizedType.Func same = TypeFactory.REQUIRED.Func([TypeFactory.REQUIRED.I64], TypeFactory.REQUIRED.I32);
        ParameterizedType.Func differentParameter = TypeFactory.REQUIRED.Func([TypeFactory.NULLABLE.I64], TypeFactory.REQUIRED.I32);
        ParameterizedType.Func differentReturn = TypeFactory.REQUIRED.Func([TypeFactory.REQUIRED.I64], TypeFactory.NULLABLE.I32);
        ParameterizedType.Func differentArity = TypeFactory.REQUIRED.Func([TypeFactory.REQUIRED.I64, TypeFactory.REQUIRED.I64], TypeFactory.REQUIRED.I32);
        ParameterizedType.Func differentNullability = TypeFactory.NULLABLE.Func([TypeFactory.REQUIRED.I64], TypeFactory.REQUIRED.I32);

        await Assert.That(same).IsEqualTo(type);
        await Assert.That(same.GetHashCode()).IsEqualTo(type.GetHashCode());
        await Assert.That(type).IsNotEqualTo(differentParameter);
        await Assert.That(type).IsNotEqualTo(differentReturn);
        await Assert.That(type).IsNotEqualTo(differentArity);
        await Assert.That(type).IsNotEqualTo(differentNullability);
    }

    [Test]
    public async Task DefaultVisitorFallsBackForFunc()
    {
        var visitor = new RecordingDefaultVisitor();
        ParameterizedType.Func type = TypeFactory.REQUIRED.Func([TypeFactory.REQUIRED.I64], TypeFactory.REQUIRED.I32);

        type.Accept(visitor, 0);

        await Assert.That(visitor.VisitedTypes).IsEquivalentTo(new IType[] { type });
    }

    [Test]
    public async Task VisitorOverloadIsBackwardCompatible()
    {
        var overload = typeof(TypeVisitor<object, object>).GetMethod("Visit", [typeof(ParameterizedType.Func), typeof(object)])!;
        await Assert.That(overload.IsVirtual).IsTrue();
        await Assert.That(overload.IsAbstract).IsFalse();
    }

    [Test]
    public async Task RejectsFuncWithUnsetOrEmptyReturnType()
    {
        ProtoType missingReturnType = new() { Func = new ProtoType.Types.Func { Nullability = ProtoType.Types.Nullability.Required } };
        ProtoType emptyReturnType = new()
        {
            Func = new ProtoType.Types.Func { Nullability = ProtoType.Types.Nullability.Required, ReturnType = new ProtoType() },
        };
        ProtoType emptyParameter = new()
        {
            Func = new ProtoType.Types.Func
            {
                Nullability = ProtoType.Types.Nullability.Required,
                ParameterTypes = { new ProtoType() },
                ReturnType = new ProtoType { I64 = new ProtoType.Types.I64 { Nullability = ProtoType.Types.Nullability.Required } },
            },
        };
        ProtoToTypeConverter converter = new();

        await Assert.That(() => converter.From(missingReturnType)).ThrowsExactly<SerializationException>();
        await Assert.That(() => converter.From(emptyReturnType)).ThrowsExactly<SerializationException>();
        await Assert.That(() => converter.From(emptyParameter)).ThrowsExactly<SerializationException>();

        // Nested under a struct, the error names the full path, including which parameter failed.
        ProtoType nested = new()
        {
            Struct = new ProtoType.Types.Struct { Nullability = ProtoType.Types.Nullability.Required, Types_ = { emptyParameter } },
        };
        SerializationException error = await Assert.That(() => converter.From(nested)).ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("struct field 0");
        await Assert.That(error.Message).Contains("function parameter 0");
    }

    [Test]
    public async Task DeeplyNestedFunctionReturnTypeRoundTripsWithoutRecursion()
    {
        IType deep = TypeFactory.REQUIRED.I64;
        for (int i = 0; i < 1500; ++i)
        {
            deep = TypeFactory.REQUIRED.Func([], deep);
        }

        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(deep);
        IType actual = import.From(proto);

        await Assert.That(actual).IsEqualTo(deep);
    }

    [Test]
    public async Task GetFieldNamesDescendsThroughParametersThenReturnTypeButPairsNoNameForEitherSlot()
    {
        // struct<func(struct<i64> -> struct<bool>)>: the one outer field (the function itself) is
        // paired with its own name ("a"); the function contributes no pair of its own, but the
        // structs it transparently contains - the parameter first, then the return type - still pair
        // their fields with names. Parameter and return type use distinct leaf types (i64 vs bool) so
        // a traversal-order bug would show up as the wrong type at the wrong name, not just the wrong
        // count.
        IType parameterField = TypeFactory.REQUIRED.I64;
        IType returnField = TypeFactory.REQUIRED.BOOL;
        IType parameterStruct = TypeFactory.REQUIRED.Struct([parameterField]);
        IType returnStruct = TypeFactory.REQUIRED.Struct([returnField]);
        IType function = TypeFactory.REQUIRED.Func([parameterStruct], returnStruct);
        var schema = new NamedStruct(["a", "b", "c"], TypeFactory.REQUIRED.Struct([function]));

        await Assert.That(schema.GetFieldNames().ToList()).IsEquivalentTo(
            [(function, "a"), (parameterField, "b"), (returnField, "c")], CollectionOrdering.Matching);
    }

    private static ExtensionsDictionary LookupFrom(PlanToProtoConverter.ConverterContext context)
    {
        var plan = new Protobuf.Plan();
        ExtensionsCollector collected = context.ExtensionsCollector;
        plan.ExtensionUrns.AddRange(collected.ExtensionUris.Select((urn, index) =>
            new Protobuf.SimpleExtensionURN { Urn = urn, ExtensionUrnAnchor = (uint)index + 1 }));
        int variationAnchor = 0;
        foreach (var extension in collected.Extensions)
        {
            plan.Extensions.Add(extension.Type switch
            {
                ExtensionsCollector.ExtensionType.TypeVariation => new Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionTypeVariation = new() { TypeVariationAnchor = (uint)++variationAnchor, Name = extension.Name, ExtensionUrnReference = (uint)extension.ExtensionUriReference + 1 },
                },
                _ => throw new InvalidOperationException("Unexpected extension kind in function type conversion."),
            });
        }

        return new ExtensionsDictionary.Builder(plan).Build();
    }

    private sealed class RecordingDefaultVisitor : DefaultTypeVisitor<int, int>
    {
        public List<IType> VisitedTypes { get; } = [];

        protected override int DefaultVisit(IType type, int context)
        {
            this.VisitedTypes.Add(type);
            return context;
        }
    }
}
