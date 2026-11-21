// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Expression;
using Substrait.Core.Expression.Converters;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Types;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using ProtoLiteral = Substrait.Protobuf.Expression.Types.Literal;

namespace Substrait.Tests.Core;

public sealed class LiteralVariantTests
{
    private readonly ProtoToExpressionConverter converter;

    public LiteralVariantTests()
    {
        Substrait.Protobuf.Plan plan = new()
        {
            ExtensionUrns = { new Substrait.Protobuf.SimpleExtensionURN { ExtensionUrnAnchor = 1, Urn = "extension:test:shapes" } },
            Extensions =
            {
                new Substrait.Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionType = new Substrait.Protobuf.SimpleExtensionDeclaration.Types.ExtensionType
                    {
                        ExtensionUrnReference = 1,
                        TypeAnchor = 7,
                        Name = "point",
                    },
                },
                new Substrait.Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionTypeVariation = new() { ExtensionUrnReference = 1, TypeVariationAnchor = 9, Name = "small" },
                },
                new Substrait.Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionTypeVariation = new() { ExtensionUrnReference = 1, TypeVariationAnchor = 10, Name = "sparse" },
                },
            },
        };
        ExtensionsDictionary lookup = new ExtensionsDictionary.Builder(plan).Build();
        ExtensionsCollection extensions = new(
            [],
            [
                new TypeVariationImpl("extension:test:shapes", "i32", "small", string.Empty, FunctionBehavior.INHERITS),
                new TypeVariationImpl("extension:test:shapes", "list", "sparse", string.Empty, FunctionBehavior.INHERITS),
            ],
            [],
            [],
            []);
        this.converter = new ProtoToExpressionConverter(
            lookup,
            extensions,
            new ProtoToTypeConverter(lookup, extensions, ExtensionsDictionary.StrictMode.OFF));
    }

    [Test]
    public async Task UserDefinedLiteralPreservesOpaqueValue()
    {
        Any value = Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue { Value = "x" });
        ProtoLiteral proto = new()
        {
            Nullable = true,
            UserDefined = new ProtoLiteral.Types.UserDefined
            {
                TypeReference = 7,
                TypeParameters = { new Substrait.Protobuf.Type.Types.Parameter { Integer = 3 } },
                Value = value,
            },
        };

        Literal.UserDefinedLiteral literal = (Literal.UserDefinedLiteral)this.converter.CreateLiteral(proto);
        UserDefinedType type = (UserDefinedType)literal.Type;

        await Assert.That(literal.Value!.ToProto()).IsEqualTo(value);
        await Assert.That(literal.Fields).IsNull();
        await Assert.That(type.Anchor).IsEqualTo(new TypeAnchor("extension:test:shapes", "point"));
        await Assert.That(type.Parameters.Count).IsEqualTo(1);
        await Assert.That(type.Nullable).IsEqualTo(IType.NullableType.Nullable);

        ProtoLiteral back = new ExpressionToProtoConverter(new TypeToProtoConverter()).From(literal).Literal;
        await Assert.That(back.UserDefined.Value).IsEqualTo(value);
        await Assert.That(back.UserDefined.TypeParameters.Count).IsEqualTo(1);
        await Assert.That(back.Nullable).IsTrue();
    }

    [Test]
    public async Task UserDefinedLiteralPreservesStructuredValue()
    {
        ProtoLiteral proto = new()
        {
            UserDefined = new ProtoLiteral.Types.UserDefined
            {
                TypeReference = 7,
                Struct = new ProtoLiteral.Types.Struct
                {
                    Fields = { new ProtoLiteral { I32 = 1 }, new ProtoLiteral { String = "a" } },
                },
            },
        };

        Literal.UserDefinedLiteral literal = (Literal.UserDefinedLiteral)this.converter.CreateLiteral(proto);

        await Assert.That(literal.Value).IsNull();
        await Assert.That(literal.Fields!.Count).IsEqualTo(2);

        ProtoLiteral back = new ExpressionToProtoConverter(new TypeToProtoConverter()).From(literal).Literal;
        await Assert.That(back.UserDefined.ValCase).IsEqualTo(ProtoLiteral.Types.UserDefined.ValOneofCase.Struct);
        await Assert.That(back.UserDefined.Struct.Fields.Count).IsEqualTo(2);
    }

    [Test]
    public async Task UserDefinedLiteralIsIsolatedFromInputPayloadMutation()
    {
        Any value = Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue { Value = "x" });
        ProtoLiteral proto = new() { UserDefined = new ProtoLiteral.Types.UserDefined { TypeReference = 7, Value = value } };
        Literal original = this.converter.CreateLiteral(proto.Clone());
        Literal literal = this.converter.CreateLiteral(proto);
        HashSet<Literal> set = [literal];

        value.Value = ByteString.CopyFromUtf8("changed");

        await Assert.That(literal).IsEqualTo(original);
        await Assert.That(set.Contains(original)).IsTrue();
    }

    [Test]
    public async Task UserDefinedLiteralIsIsolatedFromExportedPayloadMutation()
    {
        ProtoLiteral proto = new()
        {
            UserDefined = new ProtoLiteral.Types.UserDefined { TypeReference = 7, Value = Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue { Value = "x" }) },
        };
        Literal original = this.converter.CreateLiteral(proto.Clone());
        Literal literal = this.converter.CreateLiteral(proto);
        HashSet<Literal> set = [literal];

        new ExpressionToProtoConverter(new TypeToProtoConverter()).From(literal).Literal.UserDefined.Value.Value = ByteString.CopyFromUtf8("changed");

        await Assert.That(literal).IsEqualTo(original);
        await Assert.That(set.Contains(original)).IsTrue();
    }

    [Test]
    public async Task UserDefinedLiteralSnapshotsMessagePayload()
    {
        Google.Protobuf.WellKnownTypes.StringValue message = new() { Value = "x" };
        Any any = Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue { Value = "y" });
        Literal.UserDefinedLiteral packed = new(new TypeAnchor("extension:test:shapes", "point"), [], message);
        Literal.UserDefinedLiteral copied = new(37, any);

        message.Value = "changed";
        any.Value = ByteString.CopyFromUtf8("changed");

        await Assert.That(packed.Value!.ToProto()).IsEqualTo(Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue { Value = "x" }));
        await Assert.That(copied.Value!.ToProto()).IsEqualTo(Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue { Value = "y" }));
    }

    [Test]
    public async Task RejectsUserDefinedLiteralWithoutValue()
    {
        ProtoLiteral proto = new() { UserDefined = new ProtoLiteral.Types.UserDefined { TypeReference = 7 } };

        await Assert.That(() => this.converter.CreateLiteral(proto)).Throws<System.Runtime.Serialization.SerializationException>();
    }

    [Test]
    public async Task UserDefinedLiteralPreservesTypeAlias()
    {
        Any value = Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue { Value = "x" });
        ProtoLiteral proto = new()
        {
            UserDefined = new ProtoLiteral.Types.UserDefined { TypeAliasReference = 37, Value = value },
        };

        Literal literal = this.converter.CreateLiteral(proto);

        await Assert.That(literal.Type).IsEqualTo(new ParameterizedType.AliasReference(37, IType.NullableType.Required));

        ProtoLiteral back = new ExpressionToProtoConverter(new TypeToProtoConverter()).From(literal).Literal;
        await Assert.That(back.UserDefined.TypeAnchorTypeCase).IsEqualTo(ProtoLiteral.Types.UserDefined.TypeAnchorTypeOneofCase.TypeAliasReference);
        await Assert.That(back.UserDefined.TypeAliasReference).IsEqualTo(37u);
        await Assert.That(back.UserDefined.Value).IsEqualTo(value);
    }

    [Test]
    public async Task RejectsTypeAliasUserDefinedLiteralWithParametersOrVariation()
    {
        Any value = Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue { Value = "x" });
        ProtoLiteral withParameters = new()
        {
            UserDefined = new ProtoLiteral.Types.UserDefined
            {
                TypeAliasReference = 37,
                TypeParameters = { new Substrait.Protobuf.Type.Types.Parameter { Integer = 3 } },
                Value = value,
            },
        };
        ProtoLiteral withVariation = new()
        {
            TypeVariationReference = 9,
            UserDefined = new ProtoLiteral.Types.UserDefined { TypeAliasReference = 37, Value = value },
        };

        await Assert.That(() => this.converter.CreateLiteral(withParameters)).Throws<System.Runtime.Serialization.SerializationException>();
        await Assert.That(() => this.converter.CreateLiteral(withVariation)).Throws<System.Runtime.Serialization.SerializationException>();
    }

    [Test]
    public async Task RejectsUserDefinedLiteralWithoutTypeAnchor()
    {
        ProtoLiteral proto = new()
        {
            UserDefined = new ProtoLiteral.Types.UserDefined { Value = Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue { Value = "x" }) },
        };

        await Assert.That(() => this.converter.CreateLiteral(proto)).Throws<System.Runtime.Serialization.SerializationException>();
    }

    [Test]
    public async Task RejectsEmptyListAndMapLiterals()
    {
        await Assert.That(() => new Literal.ListLiteral([])).Throws<ArgumentException>();
        await Assert.That(() => new Literal.MapLiteral([])).Throws<ArgumentException>();
    }

    [Test]
    public async Task LiteralTypeVariationRoundTrips()
    {
        Literal literal = this.converter.CreateLiteral(new ProtoLiteral { I32 = 5, TypeVariationReference = 9 });

        await Assert.That(literal.TypeVariation!.Name).IsEqualTo("small");
        await Assert.That(literal).IsNotEqualTo(new Literal.I32Literal(5));
        await Assert.That(new Literal.I32Literal(5) { TypeVariation = literal.TypeVariation }).IsEqualTo(literal);
        await Assert.That(new ExpressionToProtoConverter(new TypeToProtoConverter()).From(literal).Literal.TypeVariationReference).IsNotEqualTo(0u);
    }

    [Test]
    public async Task EmptyListLiteralTakesVariationFromItsType()
    {
        ProtoLiteral proto = new()
        {
            EmptyList = new Substrait.Protobuf.Type.Types.List
            {
                Type = new Substrait.Protobuf.Type { I32 = new() { Nullability = Substrait.Protobuf.Type.Types.Nullability.Required } },
                Nullability = Substrait.Protobuf.Type.Types.Nullability.Required,
                TypeVariationReference = 10,
            },
        };

        Literal literal = this.converter.CreateLiteral(proto);

        await Assert.That(literal.TypeVariation!.Name).IsEqualTo("sparse");
        ProtoLiteral back = new ExpressionToProtoConverter(new TypeToProtoConverter()).From(literal).Literal;
        await Assert.That(back.EmptyList.TypeVariationReference).IsNotEqualTo(0u);
        await Assert.That(back.TypeVariationReference).IsEqualTo(back.EmptyList.TypeVariationReference);
    }

    [Test]
    public async Task EmptyListLiteralAcceptsVariationOnTheLiteral()
    {
        ProtoLiteral proto = new()
        {
            TypeVariationReference = 10,
            EmptyList = new Substrait.Protobuf.Type.Types.List
            {
                Type = new Substrait.Protobuf.Type { I32 = new() { Nullability = Substrait.Protobuf.Type.Types.Nullability.Required } },
                Nullability = Substrait.Protobuf.Type.Types.Nullability.Required,
            },
        };

        await Assert.That(this.converter.CreateLiteral(proto).TypeVariation!.Name).IsEqualTo("sparse");
    }

    [Test]
    public async Task RejectsEmptyListLiteralWithConflictingVariations()
    {
        ProtoLiteral proto = new()
        {
            TypeVariationReference = 9,
            EmptyList = new Substrait.Protobuf.Type.Types.List
            {
                Type = new Substrait.Protobuf.Type { I32 = new() { Nullability = Substrait.Protobuf.Type.Types.Nullability.Required } },
                Nullability = Substrait.Protobuf.Type.Types.Nullability.Required,
                TypeVariationReference = 10,
            },
        };

        await Assert.That(() => this.converter.CreateLiteral(proto)).Throws<System.Runtime.Serialization.SerializationException>();
    }

    [Test]
    public async Task TypedNullIgnoresVariationOnTheLiteral()
    {
        ProtoLiteral proto = new()
        {
            TypeVariationReference = 9,
            Null = new Substrait.Protobuf.Type { I32 = new() { Nullability = Substrait.Protobuf.Type.Types.Nullability.Nullable } },
        };

        await Assert.That(this.converter.CreateLiteral(proto).TypeVariation).IsNull();
    }

    [Test]
    public async Task RejectsListLiteralWithMismatchedValueTypes()
    {
        await Assert.That(() => new Literal.ListLiteral([new Literal.I32Literal(1), new Literal.I64Literal(2)])).Throws<ArgumentException>();
        await Assert.That(() => new Literal.ListLiteral([new Literal.I32Literal(1), new Literal.I32Literal(2, IType.NullableType.Nullable)])).Throws<ArgumentException>();
    }

    [Test]
    public async Task RejectsMapLiteralWithMismatchedOrNullKeysOrValues()
    {
        static KeyValuePair<Literal, Literal> Entry(Literal key, Literal value) => new(key, value);

        await Assert.That(() => new Literal.MapLiteral([Entry(new Literal.I32Literal(1), new Literal.StrLiteral("a")), Entry(new Literal.I64Literal(2), new Literal.StrLiteral("b"))]))
            .Throws<ArgumentException>();
        await Assert.That(() => new Literal.MapLiteral([Entry(new Literal.I32Literal(1), new Literal.StrLiteral("a")), Entry(new Literal.I32Literal(2), new Literal.I32Literal(3))]))
            .Throws<ArgumentException>();
        await Assert.That(() => new Literal.MapLiteral([Entry(new Literal.NullLiteral(TypeFactory.NULLABLE.I32), new Literal.StrLiteral("a"))]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PrecisionAndSubsecondsParticipateInEquality()
    {
        await Assert.That(new Literal.TimeLiteral(1, 3, IType.NullableType.Required))
            .IsNotEqualTo(new Literal.TimeLiteral(1, 6, IType.NullableType.Required));
        await Assert.That(new Literal.IntervalDayLiteral(1, 2, 3, 6, IType.NullableType.Required))
            .IsNotEqualTo(new Literal.IntervalDayLiteral(1, 2, 4, 6, IType.NullableType.Required));
        await Assert.That(new Literal.PrecisionTimestampLiteral(1, 3, IType.NullableType.Required))
            .IsNotEqualTo(new Literal.PrecisionTimestampLiteral(1, 6, IType.NullableType.Required));
        await Assert.That(new Literal.UuidLiteral(ByteString.CopyFrom(new byte[16])).GetHashCode())
            .IsEqualTo(new Literal.UuidLiteral(ByteString.CopyFrom(new byte[16])).GetHashCode());
    }
}
