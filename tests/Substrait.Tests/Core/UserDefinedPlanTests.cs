// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Substrait.Core.Expression;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Types;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using Substrait.Tools;
using CorePlan = Substrait.Core.Plan.Plan;
using NullableType = Substrait.Core.Type.IType.NullableType;
using PlanVersion = Substrait.Core.Plan.Version;
using ProtoPlan = Substrait.Protobuf.Plan;
using StrictMode = Substrait.Core.Extension.ExtensionsDictionary.StrictMode;

namespace Substrait.Tests.Core;

public sealed class UserDefinedPlanTests
{
    private const string Urn = "extension:example:types";
    private static readonly TypeAnchor Anchor = new(Urn, "shape");

    [Test]
    [Arguments(NullableType.Required, true)]
    [Arguments(NullableType.Nullable, true)]
    [Arguments(NullableType.Required, false)]
    [Arguments(NullableType.Nullable, false)]
    public async Task RoundTripsSelfContainedPlansWithNestedUserDefinedTypes(NullableType nullable, bool registerTypes)
    {
        const string functionUrn = "extension:io.substrait:functions_arithmetic";
        var nestedAnchor = new TypeAnchor("extension:example:nested", Anchor.Key);
        var variation = new TypeVariationImpl(Urn, Anchor.Key, "custom", string.Empty, FunctionBehavior.INHERITS);
        var nestedVariation = new TypeVariationImpl(nestedAnchor.Namespace, nestedAnchor.Key, "custom", string.Empty, FunctionBehavior.INHERITS);
        var leafVariation = new TypeVariationImpl(nestedAnchor.Namespace, "i64", "leaf", string.Empty, FunctionBehavior.INHERITS);
        var nestedType = TypeFactory.NULLABLE.UserDefined(
            nestedAnchor,
            [new TypeParameter.DataType(TypeFactory.NULLABLE.I64_(leafVariation)), new TypeParameter.Integer(long.MinValue)],
            nestedVariation);
        var type = TypeFactory.Of(nullable).UserDefined(
            Anchor,
            [
                new TypeParameter.Null(),
                new TypeParameter.String(string.Empty),
                new TypeParameter.DataType(nestedType),
                new TypeParameter.Boolean(false),
                new TypeParameter.Enum(string.Empty),
                new TypeParameter.Integer(0),
                new TypeParameter.Integer(long.MaxValue),
            ],
            variation);
        var equalType = TypeFactory.Of(nullable).UserDefined(new TypeAnchor(Urn, Anchor.Key), type.Parameters, variation);
        var defaults = ExtensionUtils.LoadDefaults();
        var functionDefinition = defaults.ScalarFunctionImpls.Single(definition => definition.Uri == functionUrn && definition.Key == "add:i64_i64");
        var function = new Substrait.Core.Expression.Expression.ScalarFunctionInvocation(
            functionUrn, "add:i64_i64", [new Literal.I64Literal(1), new Literal.I64Literal(2)], TypeFactory.REQUIRED.I64, functionDefinition);
        CorePlan original = CorePlan.FromRelations(
            [
                new CorePlan.Root(new Project(CreateRead(type), [function]), ["value", "sum"]),
                new CorePlan.Relation(CreateRead(equalType)),
                new CorePlan.Root(CreateRead(nestedType), ["nested"]),
            ],
            PlanVersion.Current);
        var definitions = new ExtensionsCollection(
            registerTypes ? [new TypeDefinition(Anchor), new TypeDefinition(nestedAnchor)] : [],
            [variation, nestedVariation, leafVariation], [], [], []);
        var decoder = new ProtoToPlanConverter(defaults.Merge(definitions));
        var mode = registerTypes ? StrictMode.STRICT : StrictMode.FUNCTION | StrictMode.TYPE_VARIATION;
        var encoder = new PlanToProtoConverter();

        ProtoPlan wire = encoder.From(original);

        var lookup = new ExtensionsDictionary.Builder(wire).Build();
        var urns = wire.ExtensionUrns.ToDictionary(urn => urn.ExtensionUrnAnchor, urn => urn.Urn);
        var typeDeclarations = wire.Extensions.Where(extension => extension.ExtensionType is not null)
            .Select(extension => extension.ExtensionType).ToArray();
        var variationDeclarations = wire.Extensions.Where(extension => extension.ExtensionTypeVariation is not null)
            .Select(extension => extension.ExtensionTypeVariation).ToArray();
        var functionDeclaration = wire.Extensions.Single(extension => extension.ExtensionFunction is not null).ExtensionFunction;
        var outer = wire.Relations[0].Root.Input.Project.Input.Read.BaseSchema.Struct.Types_[0].UserDefined;
        var nested = outer.TypeParameters[2].DataType.UserDefined;
        await Assert.That(wire.ExtensionUrns.Count).IsEqualTo(3);
        await Assert.That(wire.Extensions.Count).IsEqualTo(6);
        await Assert.That(typeDeclarations.Select(declaration => declaration.TypeAnchor).Order().SequenceEqual(new uint[] { 0, 1 })).IsTrue();
        await Assert.That(variationDeclarations.Select(declaration => declaration.TypeVariationAnchor).Order().SequenceEqual(new uint[] { 1, 2, 3 })).IsTrue();
        await Assert.That(lookup.GetTypeAnchor(outer.TypeReference)).IsEqualTo(Anchor);
        await Assert.That(lookup.GetTypeAnchor(nested.TypeReference)).IsEqualTo(nestedAnchor);
        await Assert.That(lookup.GetTypeVariationAnchor((int)outer.TypeVariationReference)).IsEqualTo(variation.Anchor);
        await Assert.That(lookup.GetTypeVariationAnchor((int)nested.TypeVariationReference)).IsEqualTo(nestedVariation.Anchor);
        await Assert.That(lookup.GetTypeVariationAnchor((int)nested.TypeParameters[0].DataType.I64.TypeVariationReference)).IsEqualTo(leafVariation.Anchor);
        await Assert.That(functionDeclaration.FunctionAnchor).IsEqualTo(0U);
        await Assert.That(urns[functionDeclaration.ExtensionUrnReference]).IsEqualTo(functionUrn);
        await Assert.That(wire.Relations[0].Root.Input.Project.Expressions[0].ScalarFunction.FunctionReference).IsEqualTo(functionDeclaration.FunctionAnchor);
        await Assert.That(wire.Relations[1].Rel.Read.BaseSchema.Struct.Types_[0].UserDefined).IsEqualTo(outer);
        await Assert.That(wire.Relations[2].Root.Input.Read.BaseSchema.Struct.Types_[0].UserDefined).IsEqualTo(nested);
        await Assert.That(encoder.From(original).ToByteArray().SequenceEqual(wire.ToByteArray())).IsTrue();

        IPlan expected = CorePlan.FromRelations(
            new PlanMetadata(null, null,
                wire.ExtensionUrns.Select(ReadOnlySimpleExtensionURN.FromProto),
                wire.Extensions.Select(ReadOnlySimpleExtensionDeclaration.FromProto)),
            original.Relations, original.Version);
        IPlan[] imports =
        [
            decoder.From(wire, mode),
            decoder.FromBytes(wire.ToByteArray(), mode),
            decoder.FromJson(JsonFormatter.Default.Format(wire), mode),
        ];
        foreach (IPlan imported in imports)
        {
            await Assert.That(imported.Equals(expected)).IsTrue();
            await Assert.That(expected.Equals(imported)).IsTrue();
            await Assert.That(encoder.From(imported)).IsEqualTo(wire);
            var importedType = (UserDefinedType)((NamedTableRead)imported.Relations[1].Input).InitialSchema.Struct.Fields[0];
            var importedNested = (UserDefinedType)((TypeParameter.DataType)importedType.Parameters[2]).Value;
            await Assert.That(importedType.Declaration is not null).IsEqualTo(registerTypes);
            await Assert.That(importedNested.Declaration is not null).IsEqualTo(registerTypes);
        }
    }

    [Test]
    public async Task StrictImportRejectsUnregisteredUserDefinedTypes()
    {
        IPlan plan = new CorePlan(
            [new CorePlan.Root(CreateRead(TypeFactory.REQUIRED.UserDefined(Anchor, [])), ["value"])],
            PlanVersion.Current);
        ProtoPlan wire = new PlanToProtoConverter().From(plan);
        var decoder = new ProtoToPlanConverter(new ExtensionsCollection());

        await Assert.That(() => decoder.From(wire)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments(StrictMode.STRICT)]
    [Arguments(StrictMode.OFF)]
    public async Task ImportRejectsUndeclaredUserDefinedTypeReferences(StrictMode mode)
    {
        IPlan plan = new CorePlan(
            [new CorePlan.Root(CreateRead(TypeFactory.REQUIRED.UserDefined(Anchor, [])), ["value"])],
            PlanVersion.Current);
        ProtoPlan wire = new PlanToProtoConverter().From(plan);
        wire.Extensions.Clear();
        var decoder = new ProtoToPlanConverter(new ExtensionsCollection());

        ArgumentException exception = await Assert.That(() => decoder.From(wire, mode)).ThrowsExactly<ArgumentException>().And.IsNotNull();
        await Assert.That(exception.Message).Contains("Invalid type ID: 0.");
    }

    private static NamedTableRead CreateRead(IType type) =>
        new(new NamedStruct(["value"], TypeFactory.REQUIRED.Struct([type])), ["shapes"], null);
}
