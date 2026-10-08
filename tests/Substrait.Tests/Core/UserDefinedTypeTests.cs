// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using Substrait.Tools.Visitor;
using NullableType = Substrait.Core.Type.IType.NullableType;
using ProtoParameter = Substrait.Protobuf.Type.Types.Parameter;
using ProtoType = Substrait.Protobuf.Type;
using StrictMode = Substrait.Core.Extension.ExtensionsDictionary.StrictMode;

namespace Substrait.Tests.Core;

public sealed class UserDefinedTypeTests
{
    private const string Urn = "extension:example:types";
    private static readonly TypeAnchor Anchor = new(Urn, "shape");

    [Test]
    [MethodDataSource(nameof(ParameterCases))]
    public async Task RoundTripsEveryParameterCase(TypeParameter parameter, ProtoParameter wireParameter)
    {
        var proto = CreateProto(0, wireParameter);
        var import = CreateImport();
        var export = new TypeToProtoConverter();
        foreach (ProtoType input in new[] { proto, ProtoType.Parser.ParseFrom(proto.ToByteArray()), ProtoType.Parser.ParseJson(proto.ToString()) })
        {
            var type = (UserDefinedType)import.From(input);
            await Assert.That(type.Anchor).IsEqualTo(Anchor);
            await Assert.That(type.Parameters.Single()).IsEqualTo(parameter);
            await Assert.That(type.Declaration).IsNotNull();
            var context = new PlanToProtoConverter.ConverterContext();
            ProtoType output = export.From(type, context);
            await Assert.That(output).IsEqualTo(proto);
            await Assert.That(output.UserDefined.TypeParameters[0].ParameterCase).IsEqualTo(wireParameter.ParameterCase);
            await Assert.That(context.ExtensionsCollector.Extensions.Single().Type).IsEqualTo(ExtensionsCollector.ExtensionType.Type);
            await Assert.That(context.ExtensionsCollector.ExtensionUris.Single()).IsEqualTo(Urn);

            var constructed = TypeFactory.REQUIRED.UserDefined(Anchor, [parameter]);
            await Assert.That(import.From(export.From(constructed))).IsEqualTo(constructed);
            await Assert.That(type.GetHashCode()).IsEqualTo(constructed.GetHashCode());
        }
    }

    public static IEnumerable<Func<(TypeParameter Parameter, ProtoParameter Wire)>> ParameterCases()
    {
        yield return () => (new TypeParameter.Null(), new ProtoParameter { Null = new() });
        yield return () => (new TypeParameter.Boolean(false), new ProtoParameter { Boolean = false });
        yield return () => (new TypeParameter.Boolean(true), new ProtoParameter { Boolean = true });
        foreach (long value in new[] { long.MinValue, -1L, 0L, 1L, long.MaxValue })
        {
            yield return () => (new TypeParameter.Integer(value), new ProtoParameter { Integer = value });
        }

        foreach (string text in new[] { string.Empty, "same", "quotes\"\\\n" })
        {
            yield return () => (new TypeParameter.Enum(text), new ProtoParameter { Enum = text });
            yield return () => (new TypeParameter.String(text), new ProtoParameter { String = text });
        }

        yield return () => (new TypeParameter.DataType(TypeFactory.NULLABLE.I64), new ProtoParameter
        {
            DataType = new ProtoType { I64 = new() { Nullability = ProtoType.Types.Nullability.Nullable } },
        });
    }

    [Test]
    [Arguments(NullableType.Required)]
    [Arguments(NullableType.Nullable)]
    public async Task PreservesMixedOrderNestedMetadataAndDependencies(NullableType nullable)
    {
        var variation = Variation();
        var nestedVariation = new TypeVariationImpl(Urn, "i64", "nested", string.Empty, FunctionBehavior.INHERITS);
        var structVariation = new TypeVariationImpl(Urn, "struct", "record", string.Empty, FunctionBehavior.INHERITS);
        var child = TypeFactory.NULLABLE.UserDefined(
            Anchor, [new TypeParameter.Integer(42), new TypeParameter.DataType(TypeFactory.NULLABLE.I64_(nestedVariation))], variation);
        var type = TypeFactory.Of(nullable).UserDefined(
            Anchor,
            [
                new TypeParameter.Null(),
                new TypeParameter.String(string.Empty),
                new TypeParameter.DataType(TypeFactory.REQUIRED.Struct([child, child], structVariation)),
                new TypeParameter.Boolean(false),
                new TypeParameter.Enum(string.Empty),
                new TypeParameter.DataType(child),
                new TypeParameter.Integer(0),
            ],
            variation);
        var context = new PlanToProtoConverter.ConverterContext();
        var export = new TypeToProtoConverter();
        ProtoType proto = export.From(type, context);
        var import = new ProtoToTypeConverter(
            LookupFrom(context),
            new ExtensionsCollection([new TypeDefinition(Anchor)], [variation, nestedVariation, structVariation], [], [], []));
        var roundTrip = (UserDefinedType)import.From(proto);

        await Assert.That(roundTrip).IsEqualTo(type);
        await Assert.That(roundTrip.GetHashCode()).IsEqualTo(type.GetHashCode());
        await Assert.That(export.From(roundTrip, new PlanToProtoConverter.ConverterContext())).IsEqualTo(proto);
        await Assert.That(roundTrip.Parameters.Select(parameter => parameter.GetType()).SequenceEqual(type.Parameters.Select(parameter => parameter.GetType()))).IsTrue();
        await Assert.That(context.ExtensionsCollector.Extensions.Count).IsEqualTo(4);
        await Assert.That(context.ExtensionsCollector.Extensions.Count(extension => extension.Type == ExtensionsCollector.ExtensionType.Type)).IsEqualTo(1);
        await Assert.That(context.ExtensionsCollector.Extensions.Count(extension => extension.Type == ExtensionsCollector.ExtensionType.TypeVariation)).IsEqualTo(3);
        await Assert.That(proto.UserDefined.TypeReference).IsEqualTo(0U);
        await Assert.That(proto.UserDefined.TypeVariationReference).IsGreaterThan(0U);
        await Assert.That(proto.UserDefined.Nullability).IsEqualTo((ProtoType.Types.Nullability)nullable);
    }

    [Test]
    public async Task CollectsDistinctNestedTypeIdentities()
    {
        var nestedAnchor = new TypeAnchor("extension:other:types", "nested");
        var nested = TypeFactory.REQUIRED.UserDefined(nestedAnchor, []);
        var type = TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.DataType(nested)]);
        var context = new PlanToProtoConverter.ConverterContext();
        ProtoType proto = new TypeToProtoConverter().From(type, context);
        await Assert.That(proto.UserDefined.TypeReference).IsEqualTo(1U);
        await Assert.That(proto.UserDefined.TypeParameters[0].DataType.UserDefined.TypeReference).IsEqualTo(0U);
        var import = new ProtoToTypeConverter(
            LookupFrom(context),
            new ExtensionsCollection([new TypeDefinition(Anchor), new TypeDefinition(nestedAnchor)], [], [], [], []));
        await Assert.That(import.From(proto)).IsEqualTo(type);
        await Assert.That(context.ExtensionsCollector.Extensions.Count).IsEqualTo(2);
        await Assert.That(context.ExtensionsCollector.ExtensionUris.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(0U)]
    [Arguments(1U)]
    [Arguments(uint.MaxValue)]
    public async Task SeparatesTypeAndVariationAnchors(uint reference)
    {
        var plan = DeclarationPlan(reference);
        var variation = Variation();
        plan.Extensions.Add(new Protobuf.SimpleExtensionDeclaration
        {
            ExtensionTypeVariation = new() { ExtensionUrnReference = 1, TypeVariationAnchor = 1, Name = variation.Name },
        });
        var extendedExpression = new Protobuf.ExtendedExpression
        {
            ExtensionUrns = { plan.ExtensionUrns },
            Extensions = { plan.Extensions },
        };
        foreach (ExtensionsDictionary lookup in new[] { new ExtensionsDictionary.Builder(plan).Build(), new ExtensionsDictionary.Builder(extendedExpression).Build() })
        {
            var proto = CreateProto(reference);
            proto.UserDefined.TypeVariationReference = 1;
            var import = new ProtoToTypeConverter(lookup, Definitions(variation));
            var type = (UserDefinedType)import.From(proto);
            await Assert.That(type.Anchor).IsEqualTo(Anchor);
            await Assert.That(type.TypeVariation).IsEqualTo(variation);
            await Assert.That(lookup.GetTypeVariationAnchor(1).Key).IsEqualTo(variation.Name);
            await Assert.That(lookup.TryGetTypeAnchor(reference, out TypeAnchor? anchor)).IsTrue();
            await Assert.That(anchor).IsEqualTo(Anchor);
        }
    }

    [Test]
    [Arguments(StrictMode.OFF)]
    [Arguments(StrictMode.FUNCTION)]
    [Arguments(StrictMode.TYPE_VARIATION)]
    public async Task PermissiveResolutionPreservesDeclaredIdentity(StrictMode mode)
    {
        var proto = CreateProto(0, new ProtoParameter { Boolean = false });
        var import = CreateImport(new ExtensionsCollection(), mode);
        var type = (UserDefinedType)import.From(proto);
        await Assert.That(type.Declaration).IsNull();
        await Assert.That(type.Anchor).IsEqualTo(Anchor);
        await Assert.That(new TypeToProtoConverter().From(type)).IsEqualTo(proto);
    }

    [Test]
    [Arguments(StrictMode.TYPE)]
    [Arguments(StrictMode.STRICT)]
    public async Task StrictTypeResolutionRejectsUnknownDefinitions(StrictMode mode)
    {
        await Assert.That(() => CreateImport(new ExtensionsCollection(), mode).From(CreateProto(0))).ThrowsExactly<ArgumentException>();
        var knownNamespace = new ExtensionsCollection([new TypeDefinition(new TypeAnchor(Urn, "different"))], [], [], [], []);
        var error = await Assert.That(() => CreateImport(knownNamespace, mode).From(CreateProto(0)))
            .ThrowsExactly<ArgumentException>().And.IsNotNull();
        await Assert.That(error.Message).Contains(Anchor.Key);
    }

    [Test]
    [Arguments(StrictMode.OFF)]
    [Arguments(StrictMode.STRICT)]
    public async Task RejectsUndeclaredAnchorsInEveryMode(StrictMode mode)
    {
        var import = new ProtoToTypeConverter(new ExtensionsDictionary.Builder().Build(), Definitions(), mode);
        var error = await Assert.That(() => import.From(CreateProto(17))).ThrowsExactly<ArgumentException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("17");
    }

    [Test]
    public async Task TypeAndVariationStrictFlagsAreIndependent()
    {
        var proto = CreateProto(0);
        proto.UserDefined.TypeVariationReference = 12;
        var permissiveVariation = (UserDefinedType)CreateImport(Definitions(), StrictMode.TYPE).From(proto);
        await Assert.That(permissiveVariation.Declaration).IsNotNull();
        await Assert.That(permissiveVariation.TypeVariation).IsNull();
        await Assert.That(() => CreateImport(Definitions(), StrictMode.TYPE_VARIATION).From(proto)).ThrowsExactly<SerializationException>();
    }

    [Test]
    [Arguments(StrictMode.OFF)]
    [Arguments(StrictMode.STRICT)]
    public async Task RejectsUnsetParametersAndNestedTypes(StrictMode mode)
    {
        foreach (ProtoParameter invalid in new[] { new ProtoParameter(), new ProtoParameter { DataType = new ProtoType() } })
        {
            var proto = CreateProto(0, new ProtoParameter { Null = new() }, invalid);
            var error = await Assert.That(() => CreateImport(mode: mode).From(proto))
                .ThrowsExactly<SerializationException>().And.IsNotNull();
            await Assert.That(error.Message).Contains("parameter 1");
        }

        var unspecified = CreateProto(0);
        unspecified.UserDefined.Nullability = ProtoType.Types.Nullability.Unspecified;
        await Assert.That(() => CreateImport(mode: mode).From(unspecified)).ThrowsExactly<SerializationException>();
    }

    [Test]
    [Arguments(StrictMode.OFF)]
    [Arguments(StrictMode.STRICT)]
    public async Task ReportsNestedTypeErrorsWithIdentityAndPath(StrictMode mode)
    {
        var invalid = new ProtoType
        {
            IntervalDay = new() { Nullability = ProtoType.Types.Nullability.Required },
        };
        var nestedAnchor = new TypeAnchor(Urn, "details");
        var nested = CreateProto(
            1, new ProtoParameter { Null = new() }, new ProtoParameter { Integer = 0 }, new ProtoParameter { DataType = invalid });
        var record = new ProtoType
        {
            Struct = new()
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Types_ = { new ProtoType { I64 = new() { Nullability = ProtoType.Types.Nullability.Required } }, nested },
            },
        };
        var proto = CreateProto(0, new ProtoParameter { Boolean = false }, new ProtoParameter { DataType = record });
        var declarations = DeclarationPlan(0);
        declarations.Extensions.Add(new Protobuf.SimpleExtensionDeclaration
        {
            ExtensionType = new() { TypeAnchor = 1, ExtensionUrnReference = 1, Name = nestedAnchor.Key },
        });
        var import = new ProtoToTypeConverter(
            new ExtensionsDictionary.Builder(declarations).Build(),
            new ExtensionsCollection([new TypeDefinition(Anchor), new TypeDefinition(nestedAnchor)], [], [], [], []),
            mode);

        var original = await Assert.That(() => import.From(invalid)).ThrowsExactly<SerializationException>().And.IsNotNull();
        var error = await Assert.That(() => import.From(proto)).ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(error.Message).IsEqualTo(
            $"Deserialization error at user-defined type {Urn}#shape parameter 1 -> struct field 1 -> user-defined type {Urn}#details parameter 2: {original.Message}");
        await Assert.That(error.InnerException).IsAssignableTo<SerializationException>();
        await Assert.That(error.InnerException!.Message).IsEqualTo(original.Message);
        await Assert.That(error.InnerException.InnerException).IsNull();
        await Assert.That(original.InnerException).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(NestedErrorCases))]
    public async Task ContextualizesOnlyNestedConversionErrors(Func<ProtoType> createInvalid, System.Type exceptionType)
    {
        foreach (StrictMode mode in new[] { StrictMode.OFF, StrictMode.STRICT })
        {
            var import = CreateImport(mode: mode);
            var invalid = createInvalid();
            var original = await Assert.That(() => import.From(invalid)).Throws<Exception>().And.IsNotNull();
            await Assert.That(original.GetType()).IsEqualTo(exceptionType);

            var nested = CreateProto(0, new ProtoParameter { Null = new() }, new ProtoParameter { DataType = invalid });
            var error = await Assert.That(() => import.From(nested)).ThrowsExactly<SerializationException>().And.IsNotNull();
            await Assert.That(error.Message).IsEqualTo(
                $"Deserialization error at user-defined type {Urn}#shape parameter 1: {original.Message}");
            await Assert.That(error.InnerException!.GetType()).IsEqualTo(exceptionType);
            await Assert.That(error.InnerException.Message).IsEqualTo(original.Message);
        }
    }

    public static IEnumerable<(Func<ProtoType> CreateInvalid, System.Type ExceptionType)> NestedErrorCases()
    {
        yield return (() => new ProtoType
        {
            PrecisionTime = new() { Precision = 13, Nullability = ProtoType.Types.Nullability.Required },
        }, typeof(ArgumentOutOfRangeException));
        yield return (() => new ProtoType { List = new() }, typeof(NotImplementedException));
        yield return (() => CreateProto(17), typeof(ArgumentException));
    }

    [Test]
    public async Task FormatsDeepErrorPathsWithoutRecursion()
    {
        ProtoType proto = new();
        for (int depth = 0; depth < 1500; ++depth)
        {
            proto = CreateProto(0, new ProtoParameter { DataType = proto });
        }

        var error = await Assert.That(() => CreateImport().From(proto)).ThrowsExactly<SerializationException>().And.IsNotNull();
        string segment = $"user-defined type {Urn}#shape parameter 0";
        await Assert.That(error.Message).IsEqualTo(
            $"Deserialization error at {string.Join(" -> ", Enumerable.Repeat(segment, 1500))}: The data type is unset.");
        await Assert.That(error.InnerException).IsAssignableTo<SerializationException>();
        await Assert.That(error.InnerException!.InnerException).IsNull();
    }

    [Test]
    public async Task RejectsMalformedDeclarationsAndDuplicateDefinitions()
    {
        var missingUrn = DeclarationPlan(0);
        missingUrn.ExtensionUrns.Clear();
        await Assert.That(() => new ExtensionsDictionary.Builder(missingUrn)).ThrowsExactly<ArgumentException>();

        var duplicate = DeclarationPlan(0);
        duplicate.Extensions.Add(duplicate.Extensions[0].Clone());
        await Assert.That(() => new ExtensionsDictionary.Builder(duplicate)).ThrowsExactly<ArgumentException>();

        var emptyName = DeclarationPlan(0);
        emptyName.Extensions[0].ExtensionType.Name = string.Empty;
        await Assert.That(() => new ExtensionsDictionary.Builder(emptyName)).ThrowsExactly<ArgumentException>();
        var emptyUrn = DeclarationPlan(0);
        emptyUrn.ExtensionUrns[0].Urn = string.Empty;
        await Assert.That(() => new ExtensionsDictionary.Builder(emptyUrn)).ThrowsExactly<ArgumentException>();

        var merged = Definitions().Merge(Definitions());
        await Assert.That(() => merged.TryGetType(Anchor, StrictMode.OFF, out _)).ThrowsExactly<ArgumentException>();
        await Assert.That(new ExtensionsCollection().Merge(Definitions()).Count).IsEqualTo(1);
        await Assert.That(Definitions().Types.Single().Anchor).IsEqualTo(Anchor);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RejectsDuplicateTypeIdentitiesRegardlessOfInstance(bool reuseInstance)
    {
        var first = new TypeDefinition(Anchor);
        var second = reuseInstance ? first : new TypeDefinition(new TypeAnchor(Urn, Anchor.Key));
        foreach (var collection in new[]
        {
            new ExtensionsCollection([first, second], [], [], [], []),
            new ExtensionsCollection([first], [], [], [], []).Merge(new ExtensionsCollection([second], [], [], [], [])),
        })
        {
            foreach (StrictMode mode in new[] { StrictMode.OFF, StrictMode.STRICT })
            {
                var error = await Assert.That(() => collection.TryGetType(Anchor, mode, out _))
                    .ThrowsExactly<ArgumentException>().And.IsNotNull();
                await Assert.That(error.Message).Contains(Anchor.Namespace);
                await Assert.That(error.Message).Contains(Anchor.Key);
            }
        }
    }

    [Test]
    public async Task ValidatesAndSnapshotsConstruction()
    {
        var parameters = new List<TypeParameter> { new TypeParameter.Null() };
        var type = TypeFactory.REQUIRED.UserDefined(Anchor, parameters);
        parameters.Add(new TypeParameter.Integer(1));
        await Assert.That(type.Parameters.Count).IsEqualTo(1);
        await Assert.That(() => TypeFactory.REQUIRED.UserDefined(Anchor, [null!])).ThrowsExactly<ArgumentException>();
        await Assert.That(() => TypeFactory.REQUIRED.UserDefined(Anchor, null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => TypeFactory.REQUIRED.UserDefined(null!, [])).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new TypeParameter.DataType(null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new TypeParameter.Enum(null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new TypeParameter.String(null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new UserDefinedType(Anchor, [], NullableType.Unspecified)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => TypeFactory.REQUIRED.UserDefined(Anchor, [], declaration: new TypeDefinition(new TypeAnchor(Urn, "different"))))
            .ThrowsExactly<ArgumentException>();
        await Assert.That(() => TypeFactory.REQUIRED.UserDefined(Anchor, [], new TypeVariationImpl(Urn, "i64", "bad", string.Empty, FunctionBehavior.INHERITS)))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task EqualityPreservesIdentityParameterCasesAndStructure()
    {
        var type = TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.Enum(string.Empty), new TypeParameter.Integer(0)]);
        var equal = TypeFactory.REQUIRED.UserDefined(new TypeAnchor(Urn, "shape"), [new TypeParameter.Enum(string.Empty), new TypeParameter.Integer(0)], declaration: new TypeDefinition(Anchor));
        await Assert.That(type).IsEqualTo(equal);
        await Assert.That(type.GetHashCode()).IsEqualTo(equal.GetHashCode());
        foreach (var different in new[]
        {
            TypeFactory.REQUIRED.UserDefined(new TypeAnchor(Urn, "other"), type.Parameters),
            TypeFactory.REQUIRED.UserDefined(new TypeAnchor("extension:other:types", "shape"), type.Parameters),
            TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.String(string.Empty), new TypeParameter.Integer(0)]),
            TypeFactory.REQUIRED.UserDefined(Anchor, type.Parameters.Reverse()),
            TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.Null(), new TypeParameter.Integer(0)]),
            TypeFactory.REQUIRED.UserDefined(Anchor, []),
        })
        {
            await Assert.That(type.Equals(different, ITypeComparison.IgnoreNullability)).IsFalse();
            await Assert.That(type).IsNotEqualTo(different);
        }

        IType first = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]), TypeFactory.REQUIRED.I32]);
        IType second = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32, TypeFactory.REQUIRED.I32])]);
        var left = TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.DataType(first)]);
        var right = TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.DataType(second)]);
        await Assert.That(left).IsNotEqualTo(right);
        await Assert.That(left.Equals(right, ITypeComparison.IgnoreNullability)).IsFalse();
    }

    [Test]
    public async Task ComparisonAndHashingRespectModesRecursively()
    {
        var variation = Variation();
        var nestedVariation = new TypeVariationImpl(Urn, "decimal", "nested", string.Empty, FunctionBehavior.INHERITS);
        var type = TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.Integer(10), new TypeParameter.DataType(TypeFactory.REQUIRED.Decimal(10, 2, nestedVariation))], variation);
        var differentParameters = TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.Integer(20), new TypeParameter.DataType(TypeFactory.REQUIRED.Decimal(20, 3, nestedVariation))], variation);
        var differentNullability = TypeFactory.NULLABLE.UserDefined(Anchor, [new TypeParameter.Integer(10), new TypeParameter.DataType(TypeFactory.NULLABLE.Decimal(10, 2, nestedVariation))], variation);
        var differentVariation = TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.Integer(10), new TypeParameter.DataType(TypeFactory.REQUIRED.Decimal(10, 2))]);
        foreach ((UserDefinedType other, ITypeComparison mode) in new[]
        {
            (differentParameters, ITypeComparison.Nullability | ITypeComparison.TypeVariation),
            (differentNullability, ITypeComparison.TypeParameter | ITypeComparison.TypeVariation),
            (differentVariation, ITypeComparison.IgnoreTypeVariation),
        })
        {
            await Assert.That(type).IsNotEqualTo(other);
            var comparer = TypeUtils.ITypeEqualityComparer.Of(mode);
            await Assert.That(comparer.Equals(type, other)).IsTrue();
            await Assert.That(comparer.GetHashCode(type)).IsEqualTo(comparer.GetHashCode(other));
            IType first = TypeFactory.REQUIRED.Struct([type]);
            IType second = TypeFactory.REQUIRED.Struct([other]);
            await Assert.That(comparer.Equals(first, second)).IsTrue();
            await Assert.That(comparer.GetHashCode(first)).IsEqualTo(comparer.GetHashCode(second));
        }

        await Assert.That(new TypeParameter.DataType(type)).IsNotEqualTo(new TypeParameter.DataType(differentVariation));
    }

    [Test]
    public async Task TraversesAndRewritesTypeParametersWithoutLosingMetadata()
    {
        var variation = Variation();
        var definition = new TypeDefinition(Anchor);
        var child = TypeFactory.NULLABLE.Struct([TypeFactory.REQUIRED.I32]);
        var type = TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.Integer(0), new TypeParameter.DataType(child), new TypeParameter.Null()], variation, definition);
        var visitor = new RecordingVisitor();
        var topDown = new List<IType>();
        new TypeTopDownDispatcher<List<IType>, VoidOutput>(visitor).Dispatch(type, topDown);
        await Assert.That(topDown.SequenceEqual(new IType[] { type, child, TypeFactory.REQUIRED.I32 })).IsTrue();
        var bottomUp = new RecordingContext();
        new TypeBottomUpDispatcher<RecordingContext, IType>(new RewritingVisitor()).Dispatch(type, bottomUp);
        await Assert.That(bottomUp.Visited.SequenceEqual(new IType[] { TypeFactory.REQUIRED.I32, child, type })).IsTrue();
        var rewritten = (UserDefinedType)bottomUp.Result!;
        await Assert.That(rewritten.Anchor).IsEqualTo(type.Anchor);
        await Assert.That(rewritten.TypeVariation).IsEqualTo(variation);
        await Assert.That(rewritten.Declaration).IsSameReferenceAs(definition);
        var rewrittenChild = (ParameterizedType.Struct)((TypeParameter.DataType)rewritten.Parameters[1]).Value;
        await Assert.That(rewrittenChild.Nullable).IsEqualTo(child.Nullable);
        await Assert.That(rewrittenChild.Fields.Single()).IsEqualTo(TypeFactory.REQUIRED.I64);
        await Assert.That(rewritten.Parameters[0]).IsEqualTo(type.Parameters[0]);
        await Assert.That(rewritten.Parameters[2]).IsEqualTo(type.Parameters[2]);

        var nullable = (UserDefinedType)TypeFactory.NULLABLE.ResolveTypeWithNullability(type);
        await Assert.That(nullable.Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(nullable.TypeVariation).IsSameReferenceAs(variation);
        await Assert.That(((TypeParameter.DataType)nullable.Parameters[1]).Value).IsSameReferenceAs(child);
        await Assert.That(nullable.Declaration).IsSameReferenceAs(definition);
        await Assert.That(TypeFactory.NULLABLE.ResolveTypeWithNullability(type, null).TypeVariation).IsNull();
        await Assert.That(TypeFactory.REQUIRED.ResolveTypeWithNullability(type)).IsSameReferenceAs(type);
    }

    [Test]
    public async Task VisitorOverloadIsBackwardCompatible()
    {
        var overload = typeof(TypeVisitor<object, object>).GetMethod("Visit", [typeof(UserDefinedType), typeof(object)])!;
        await Assert.That(overload.IsVirtual).IsTrue();
        await Assert.That(overload.IsAbstract).IsFalse();
        var type = TypeFactory.REQUIRED.UserDefined(Anchor, []);
        await Assert.That(type.Accept(new FallbackVisitor(), new object())).IsSameReferenceAs(type);
    }

    [Test]
    public async Task ConvertsDeeplyNestedParametersWithoutRecursiveCalls()
    {
        IType type = TypeFactory.REQUIRED.I64;
        for (int depth = 0; depth < 1500; ++depth)
        {
            type = TypeFactory.REQUIRED.UserDefined(Anchor, [new TypeParameter.DataType(type)]);
        }

        var context = new PlanToProtoConverter.ConverterContext();
        ProtoType proto = new TypeToProtoConverter().From(type, context);
        IType roundTrip = CreateImport().From(proto);
        await Assert.That(roundTrip.Equals(type, ITypeComparison.Strict)).IsTrue();
        await Assert.That(roundTrip.GetHashCode()).IsEqualTo(type.GetHashCode());
        await Assert.That(context.ExtensionsCollector.Extensions.Count).IsEqualTo(1);
    }

    private static TypeVariationImpl Variation() => new(Urn, Anchor.Key, "packed", string.Empty, FunctionBehavior.INHERITS);

    private static ExtensionsCollection Definitions(params TypeVariationImpl[] variations) =>
        new([new TypeDefinition(Anchor)], variations, [], [], []);

    private static ProtoToTypeConverter CreateImport(ExtensionsCollection? extensions = null, StrictMode mode = StrictMode.STRICT) =>
        new(new ExtensionsDictionary.Builder(DeclarationPlan(0)).Build(), extensions ?? Definitions(), mode);

    private static ProtoType CreateProto(uint reference, params ProtoParameter[] parameters) => new()
    {
        UserDefined = new()
        {
            TypeReference = reference,
            Nullability = ProtoType.Types.Nullability.Required,
            TypeParameters = { parameters },
        },
    };

    private static Protobuf.Plan DeclarationPlan(uint reference) => new()
    {
        ExtensionUrns = { new Protobuf.SimpleExtensionURN { ExtensionUrnAnchor = 1, Urn = Urn } },
        Extensions =
        {
            new Protobuf.SimpleExtensionDeclaration
            {
                ExtensionType = new() { ExtensionUrnReference = 1, TypeAnchor = reference, Name = Anchor.Key },
            },
        },
    };

    private static ExtensionsDictionary LookupFrom(PlanToProtoConverter.ConverterContext context)
    {
        var plan = new Protobuf.Plan();
        ExtensionsCollector collected = context.ExtensionsCollector;
        plan.ExtensionUrns.AddRange(collected.ExtensionUris.Select((urn, index) =>
            new Protobuf.SimpleExtensionURN { Urn = urn, ExtensionUrnAnchor = (uint)index + 1 }));
        int typeAnchor = -1;
        int variationAnchor = 0;
        foreach (var extension in collected.Extensions)
        {
            plan.Extensions.Add(extension.Type switch
            {
                ExtensionsCollector.ExtensionType.Type => new Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionType = new() { TypeAnchor = (uint)++typeAnchor, Name = extension.Name, ExtensionUrnReference = (uint)extension.ExtensionUriReference + 1 },
                },
                ExtensionsCollector.ExtensionType.TypeVariation => new Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionTypeVariation = new() { TypeVariationAnchor = (uint)++variationAnchor, Name = extension.Name, ExtensionUrnReference = (uint)extension.ExtensionUriReference + 1 },
                },
                _ => throw new InvalidOperationException("Unexpected extension kind in type conversion."),
            });
        }

        return new ExtensionsDictionary.Builder(plan).Build();
    }

    private sealed class RecordingVisitor : DefaultTypeVisitor<List<IType>, VoidOutput>
    {
        protected override VoidOutput DefaultVisit(IType type, List<IType> context)
        {
            context.Add(type);
            return VoidOutput.Instance;
        }
    }

    private sealed class RecordingContext : Context<IType, IType>
    {
        public List<IType> Visited { get; } = [];

        public IType? Result { get; set; }
    }

    private sealed class RewritingVisitor : DefaultTypeVisitor<RecordingContext, IType>
    {
        protected override IType DefaultVisit(IType type, RecordingContext context)
        {
            context.Visited.Add(type);
            context.Result = type switch
            {
                UserDefinedType userDefined => TypeFactory.Of(type.Nullable).UserDefined(
                    userDefined.Anchor,
                    userDefined.Parameters.Select(parameter => parameter is TypeParameter.DataType dataType
                        ? new TypeParameter.DataType(context.GetOutput(dataType.Value)) : parameter),
                    type.TypeVariation,
                    userDefined.Declaration),
                ParameterizedType.Struct record => TypeFactory.Of(type.Nullable).Struct(record.Fields.Select(context.GetOutput), type.TypeVariation),
                PrimitiveType.I32 => TypeFactory.Of(type.Nullable).I64_(type.TypeVariation),
                _ => type,
            };
            return context.Result;
        }
    }

    private sealed class FallbackVisitor : TypeVisitor<object, IType>
    {
        public override IType Visit(IType type, object context) => type;

        public override IType Visit(PrimitiveType.Bool type, object context) => type;

        public override IType Visit(PrimitiveType.I8 type, object context) => type;

        public override IType Visit(PrimitiveType.I16 type, object context) => type;

        public override IType Visit(PrimitiveType.I32 type, object context) => type;

        public override IType Visit(PrimitiveType.I64 type, object context) => type;

        public override IType Visit(PrimitiveType.FP32 type, object context) => type;

        public override IType Visit(PrimitiveType.FP64 type, object context) => type;

        public override IType Visit(PrimitiveType.Str type, object context) => type;

        public override IType Visit(PrimitiveType.Binary type, object context) => type;

        public override IType Visit(PrimitiveType.Date type, object context) => type;

        public override IType Visit(PrimitiveType.Time type, object context) => type;

        public override IType Visit(PrimitiveType.IntervalYear type, object context) => type;

        public override IType Visit(PrimitiveType.IntervalDay type, object context) => type;

        public override IType Visit(ParameterizedType.PrecisionTimestamp type, object context) => type;

        public override IType Visit(ParameterizedType.PrecisionTimestampTZ type, object context) => type;

        public override IType Visit(ParameterizedType.FixedChar type, object context) => type;

        public override IType Visit(ParameterizedType.VarChar type, object context) => type;

        public override IType Visit(ParameterizedType.FixedBinary type, object context) => type;

        public override IType Visit(ParameterizedType.Decimal type, object context) => type;

        public override IType Visit(ParameterizedType.Struct type, object context) => type;
    }
}
