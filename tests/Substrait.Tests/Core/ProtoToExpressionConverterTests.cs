// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Core.Expression;
using Substrait.Core.Expression.Converters;
using Substrait.Core.Extension;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoLiteral = Substrait.Protobuf.Expression.Types.Literal;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class ProtoToExpressionConverterTests
{
    private readonly ProtoToExpressionConverter converter = new(
        new ExtensionsDictionary.Builder().Build(),
        new ExtensionsCollection(),
        new ProtoToTypeConverter());

    [Test]
    public async Task ConvertsNestedStructLiteralAndPreservesOrder()
    {
        ProtoExpression.Types.Literal proto = new()
        {
            Struct = new ProtoExpression.Types.Literal.Types.Struct
            {
                Fields =
                {
                    new ProtoExpression.Types.Literal { I32 = 1 },
                    new ProtoExpression.Types.Literal
                    {
                        Struct = new ProtoExpression.Types.Literal.Types.Struct
                        {
                            Fields = { new ProtoExpression.Types.Literal { String = "nested" } },
                        },
                    },
                    new ProtoExpression.Types.Literal { Boolean = true },
                },
            },
        };

        Literal.StructLiteral result = (Literal.StructLiteral)this.converter.CreateLiteral(proto);

        await Assert.That(result.Fields.Count).IsEqualTo(3);
        await Assert.That(((Literal.I32Literal)result.Fields[0]).Value).IsEqualTo(1);
        await Assert.That(((Literal.StrLiteral)((Literal.StructLiteral)result.Fields[1]).Fields[0]).Value).IsEqualTo("nested");
        await Assert.That(((Literal.BoolLiteral)result.Fields[2]).Value).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(GetLiteralCases))]
    public async Task ConvertsScalarAndIntervalLiterals(ProtoLiteral protoLiteral, Literal expected)
    {
        await Assert.That(this.converter.CreateLiteral(protoLiteral)).IsEqualTo(expected);
        await Assert.That(new ExpressionToProtoConverter(new TypeToProtoConverter()).From(expected).Literal).IsEqualTo(protoLiteral);
    }

    [Test]
    [Arguments(0)]
    [Arguments(3)]
    [Arguments(9)]
    public async Task RejectsUnsupportedTimePrecision(int precision)
    {
        ProtoLiteral literal = new() { PrecisionTime = new() { Precision = precision, Value = 123 } };

        await Assert.That(() => this.converter.CreateLiteral(literal)).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    [Arguments(6, 1L)]
    [Arguments(6, 0L)]
    [Arguments(0, 1L)]
    public async Task RejectsFractionalIntervalLiterals(int precision, long subseconds)
    {
        ProtoLiteral literal = new()
        {
            IntervalDayToSecond = new() { Days = 1, Seconds = 2, Precision = precision, Subseconds = subseconds },
        };

        await Assert.That(() => this.converter.CreateLiteral(literal)).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task RejectsRelationAnchorOuterReferences()
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        ProtoExpression proto = CreateFieldReference(0);
        proto.Selection.OuterReference = new() { RelReference = 1 };

        await Assert.That(() => this.converter.From(proto, schema, [schema])).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task RoundTripsOffsetOuterReferences()
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        FieldReference expected = new(TypeFactory.REQUIRED.I32, 0, 1);
        ProtoExpression proto = new ExpressionToProtoConverter(new TypeToProtoConverter()).From(expected);

        await Assert.That(this.converter.From(proto, schema, [schema])).IsEqualTo(expected);
    }

    [Test]
    public async Task ConvertsIfThenIteratively()
    {
        ProtoExpression proto = new()
        {
            IfThen = new ProtoExpression.Types.IfThen
            {
                Ifs =
                {
                    new ProtoExpression.Types.IfThen.Types.IfClause
                    {
                        If = new ProtoExpression { Literal = new ProtoExpression.Types.Literal { Boolean = true } },
                        Then = new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I32 = 7 } },
                    },
                },
                Else = new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I32 = 9 } },
            },
        };

        Substrait.Core.Expression.Expression.IfThen result =
            (Substrait.Core.Expression.Expression.IfThen)this.converter.From(proto);

        await Assert.That(((Literal.I32Literal)result.IfClauses[0].Then).Value).IsEqualTo(7);
        await Assert.That(((Literal.I32Literal)result.ElseClause).Value).IsEqualTo(9);
    }

    [Test]
    public async Task ConvertsCastTypeInputAndFailureBehavior()
    {
        ProtoExpression proto = new()
        {
            Cast = new ProtoExpression.Types.Cast
            {
                Input = new ProtoExpression { Literal = new ProtoLiteral { I32 = 42 } },
                Type = new ProtoType
                {
                    I64 = new ProtoType.Types.I64 { Nullability = ProtoType.Types.Nullability.Nullable },
                },
                FailureBehavior = ProtoExpression.Types.Cast.Types.FailureBehavior.ReturnNull,
            },
        };

        var result = (Substrait.Core.Expression.Expression.Cast)this.converter.From(proto);

        await Assert.That(result.Type).IsAssignableTo<PrimitiveType.I64>();
        await Assert.That(result.Type.Nullable).IsEqualTo(IType.NullableType.Nullable);
        await Assert.That(result.Behavior).IsEqualTo(Substrait.Core.Expression.Expression.Cast.FailureBehavior.ReturnNull);
        await Assert.That(result.Input).IsEqualTo(new Literal.I32Literal(42));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ConvertsIfThenWithNullBranch(bool nullInThen)
    {
        ProtoExpression nullExpression = new()
        {
            Literal = new ProtoLiteral
            {
                Null = new ProtoType
                {
                    I32 = new ProtoType.Types.I32 { Nullability = ProtoType.Types.Nullability.Nullable },
                },
            },
        };
        ProtoExpression valueExpression = new() { Literal = new ProtoLiteral { I32 = 3 } };
        ProtoExpression proto = new()
        {
            IfThen = new ProtoExpression.Types.IfThen
            {
                Ifs =
                {
                    new ProtoExpression.Types.IfThen.Types.IfClause
                    {
                        If = new ProtoExpression { Literal = new ProtoLiteral { Boolean = true } },
                        Then = nullInThen ? nullExpression : valueExpression,
                    },
                },
                Else = nullInThen ? valueExpression : nullExpression,
            },
        };

        var result = (Substrait.Core.Expression.Expression.IfThen)this.converter.From(proto);

        await Assert.That(result.Type).IsAssignableTo<PrimitiveType.I32>();
        await Assert.That(result.Type.Nullable).IsEqualTo(IType.NullableType.Nullable);
        await Assert.That(result.IfClauses[0].Then is Literal.NullLiteral).IsEqualTo(nullInThen);
        await Assert.That(result.ElseClause is Literal.NullLiteral).IsEqualTo(!nullInThen);
    }

    [Test]
    public async Task ConvertsRootFieldReferenceAgainstInputSchema()
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32, TypeFactory.NULLABLE.STR]);
        ProtoExpression proto = new()
        {
            Selection = new ProtoExpression.Types.FieldReference
            {
                DirectReference = new ProtoExpression.Types.ReferenceSegment
                {
                    StructField = new ProtoExpression.Types.ReferenceSegment.Types.StructField { Field = 1 },
                },
                RootReference = new ProtoExpression.Types.FieldReference.Types.RootReference(),
            },
        };

        FieldReference result = (FieldReference)this.converter.From(proto, schema, ImmutableList<ParameterizedType.Struct>.Empty);

        await Assert.That(result.FieldIndex).IsEqualTo(1);
        await Assert.That(result.Type).IsEqualTo(TypeFactory.NULLABLE.STR);
    }

    [Test]
    public async Task RejectsRootFieldReferenceOutsideInputSchema()
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        ProtoExpression proto = CreateFieldReference(1);

        SerializationException exception = await Assert.That(() =>
            this.converter.From(proto, schema, ImmutableList<ParameterizedType.Struct>.Empty)).ThrowsExactly<SerializationException>().And.IsNotNull();

        await Assert.That(exception.Message).Contains("field index 1");
    }

    [Test]
    [Arguments(0U)]
    [Arguments(2U)]
    public async Task RejectsOuterReferenceOutsideEnclosingSchemas(uint stepsOut)
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        ProtoExpression proto = CreateFieldReference(0, stepsOut);

        SerializationException exception = await Assert.That(() =>
            this.converter.From(proto, schema, [schema])).ThrowsExactly<SerializationException>().And.IsNotNull();

        await Assert.That(exception.Message).Contains($"outer reference steps {stepsOut}");
    }

    [Test]
    public async Task RejectsFieldReferenceOutsideOuterSchema()
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        ProtoExpression proto = CreateFieldReference(1, 1);

        SerializationException exception = await Assert.That(() =>
            this.converter.From(proto, schema, [schema])).ThrowsExactly<SerializationException>().And.IsNotNull();

        await Assert.That(exception.Message).Contains("field index 1");
    }

    private static ProtoExpression CreateFieldReference(int fieldIndex, uint? stepsOut = null)
    {
        var reference = new ProtoExpression.Types.FieldReference
        {
            DirectReference = new ProtoExpression.Types.ReferenceSegment
            {
                StructField = new ProtoExpression.Types.ReferenceSegment.Types.StructField { Field = fieldIndex },
            },
        };

        if (stepsOut.HasValue)
        {
#pragma warning disable CS0612 // Exercise the supported legacy offset representation.
            reference.OuterReference = new ProtoExpression.Types.FieldReference.Types.OuterReference { StepsOut = stepsOut.Value };
#pragma warning restore CS0612
        }
        else
        {
            reference.RootReference = new ProtoExpression.Types.FieldReference.Types.RootReference();
        }

        return new ProtoExpression { Selection = reference };
    }
    public static IEnumerable<Func<(ProtoLiteral ProtoLiteral, Literal Expected)>> GetLiteralCases()
    {
        ByteString binaryValue = ByteString.CopyFromUtf8("binary data");
        ByteString decimalValue = ByteString.CopyFromUtf8("3.14159");

        yield return
        () => (new ProtoLiteral
        {
            Null = new ProtoType
            {
                Bool = new ProtoType.Types.Boolean { Nullability = ProtoType.Types.Nullability.Nullable },
            },
        }, new Literal.NullLiteral(TypeFactory.NULLABLE.BOOL));
        yield return () => (new ProtoLiteral { Boolean = true, Nullable = true }, new Literal.BoolLiteral(true, IType.NullableType.Nullable));
        yield return () => (new ProtoLiteral { I8 = 42 }, new Literal.I8Literal(42));
        yield return () => (new ProtoLiteral { I16 = 1096 }, new Literal.I16Literal(1096));
        yield return () => (new ProtoLiteral { I32 = 462018 }, new Literal.I32Literal(462018));
        yield return () => (new ProtoLiteral { I64 = 3152021 }, new Literal.I64Literal(3152021));
        yield return () => (new ProtoLiteral { Fp32 = 3.14f }, new Literal.FP32Literal(3.14f));
        yield return () => (new ProtoLiteral { Fp64 = 2.71828 }, new Literal.FP64Literal(2.71828));
        yield return () => (new ProtoLiteral { String = "Hello, World!" }, new Literal.StrLiteral("Hello, World!"));
        yield return () => (new ProtoLiteral { Binary = binaryValue }, new Literal.BinaryLiteral(binaryValue));
        yield return () => (new ProtoLiteral { Date = 600 }, new Literal.DateLiteral(600));
        yield return () => (new ProtoLiteral { PrecisionTime = new() { Precision = 6, Value = 3122016 } }, new Literal.TimeLiteral(3122016));
        yield return
        () => (new ProtoLiteral
        {
            IntervalYearToMonth = new ProtoLiteral.Types.IntervalYearToMonth { Years = 2, Months = 5 },
        }, new Literal.IntervalYearLiteral(2, 5));
        yield return
        () => (new ProtoLiteral
        {
            IntervalDayToSecond = new ProtoLiteral.Types.IntervalDayToSecond { Days = 2, Seconds = 45 },
        }, new Literal.IntervalDayLiteral(2, 45));
        yield return () => (new ProtoLiteral { FixedChar = "ABC" }, new Literal.FixedCharLiteral("ABC"));
        yield return
        () => (new ProtoLiteral
        {
            VarChar = new ProtoLiteral.Types.VarChar { Value = "Hello, World!", Length = 13 },
        }, new Literal.VarCharLiteral("Hello, World!", 13));
        yield return () => (new ProtoLiteral { FixedBinary = binaryValue }, new Literal.FixedBinaryLiteral(binaryValue));
        yield return
        () => (new ProtoLiteral
        {
            Decimal = new ProtoLiteral.Types.Decimal { Value = decimalValue, Precision = 6, Scale = 5 },
        }, new Literal.DecimalLiteral(decimalValue, 6, 5));
    }
}
