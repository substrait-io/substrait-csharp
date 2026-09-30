// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.Serialization;
using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Expression;
using Substrait.Core.Expression.Converters;
using Substrait.Core.Extension;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoLiteral = Substrait.Protobuf.Expression.Types.Literal;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

[TestClass]
public sealed class ProtoToExpressionConverterTests
{
    private readonly ProtoToExpressionConverter converter = new(
        new ExtensionsDictionary.Builder().Build(),
        new ExtensionsCollection(),
        new ProtoToTypeConverter());

    [TestMethod]
    public void ConvertsNestedStructLiteralAndPreservesOrder()
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

        Assert.AreEqual(3, result.Fields.Count);
        Assert.AreEqual(1, ((Literal.I32Literal)result.Fields[0]).Value);
        Assert.AreEqual("nested", ((Literal.StrLiteral)((Literal.StructLiteral)result.Fields[1]).Fields[0]).Value);
        Assert.IsTrue(((Literal.BoolLiteral)result.Fields[2]).Value);
    }

    [DataTestMethod]
    [DynamicData(nameof(GetLiteralCases), DynamicDataSourceType.Method)]
    public void ConvertsScalarAndIntervalLiterals(ProtoLiteral protoLiteral, Literal expected)
    {
        Assert.AreEqual(expected, this.converter.CreateLiteral(protoLiteral));
        Assert.AreEqual(protoLiteral, new ExpressionToProtoConverter(new TypeToProtoConverter()).From(expected).Literal);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(9)]
    public void RejectsUnsupportedTimePrecision(int precision)
    {
        ProtoLiteral literal = new() { PrecisionTime = new() { Precision = precision, Value = 123 } };

        Assert.ThrowsException<NotSupportedException>(() => this.converter.CreateLiteral(literal));
    }

    [TestMethod]
    [DataRow(6, 1L)]
    [DataRow(6, 0L)]
    [DataRow(0, 1L)]
    public void RejectsFractionalIntervalLiterals(int precision, long subseconds)
    {
        ProtoLiteral literal = new()
        {
            IntervalDayToSecond = new() { Days = 1, Seconds = 2, Precision = precision, Subseconds = subseconds },
        };

        Assert.ThrowsException<NotSupportedException>(() => this.converter.CreateLiteral(literal));
    }

    [TestMethod]
    public void RejectsRelationAnchorOuterReferences()
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        ProtoExpression proto = CreateFieldReference(0);
        proto.Selection.OuterReference = new() { RelReference = 1 };

        Assert.ThrowsException<NotSupportedException>(() => this.converter.From(proto, schema, [schema]));
    }

    [TestMethod]
    public void RoundTripsOffsetOuterReferences()
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        FieldReference expected = new(TypeFactory.REQUIRED.I32, 0, 1);
        ProtoExpression proto = new ExpressionToProtoConverter(new TypeToProtoConverter()).From(expected);

        Assert.AreEqual(expected, this.converter.From(proto, schema, [schema]));
    }

    [TestMethod]
    public void ConvertsIfThenIteratively()
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

        Assert.AreEqual(7, ((Literal.I32Literal)result.IfClauses[0].Then).Value);
        Assert.AreEqual(9, ((Literal.I32Literal)result.ElseClause).Value);
    }

    [TestMethod]
    public void ConvertsCastTypeInputAndFailureBehavior()
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

        Assert.IsInstanceOfType<PrimitiveType.I64>(result.Type);
        Assert.AreEqual(IType.NullableType.Nullable, result.Type.Nullable);
        Assert.AreEqual(Substrait.Core.Expression.Expression.Cast.FailureBehavior.ReturnNull, result.Behavior);
        Assert.AreEqual(new Literal.I32Literal(42), result.Input);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ConvertsIfThenWithNullBranch(bool nullInThen)
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

        Assert.IsInstanceOfType<PrimitiveType.I32>(result.Type);
        Assert.AreEqual(IType.NullableType.Nullable, result.Type.Nullable);
        Assert.AreEqual(nullInThen, result.IfClauses[0].Then is Literal.NullLiteral);
        Assert.AreEqual(!nullInThen, result.ElseClause is Literal.NullLiteral);
    }

    [TestMethod]
    public void ConvertsRootFieldReferenceAgainstInputSchema()
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

        Assert.AreEqual(1, result.FieldIndex);
        Assert.AreEqual(TypeFactory.NULLABLE.STR, result.Type);
    }

    [TestMethod]
    public void RejectsRootFieldReferenceOutsideInputSchema()
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        ProtoExpression proto = CreateFieldReference(1);

        SerializationException exception = Assert.ThrowsException<SerializationException>(() =>
            this.converter.From(proto, schema, ImmutableList<ParameterizedType.Struct>.Empty));

        StringAssert.Contains(exception.Message, "field index 1");
    }

    [TestMethod]
    [DataRow(0U)]
    [DataRow(2U)]
    public void RejectsOuterReferenceOutsideEnclosingSchemas(uint stepsOut)
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        ProtoExpression proto = CreateFieldReference(0, stepsOut);

        SerializationException exception = Assert.ThrowsException<SerializationException>(() =>
            this.converter.From(proto, schema, [schema]));

        StringAssert.Contains(exception.Message, $"outer reference steps {stepsOut}");
    }

    [TestMethod]
    public void RejectsFieldReferenceOutsideOuterSchema()
    {
        ParameterizedType.Struct schema = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32]);
        ProtoExpression proto = CreateFieldReference(1, 1);

        SerializationException exception = Assert.ThrowsException<SerializationException>(() =>
            this.converter.From(proto, schema, [schema]));

        StringAssert.Contains(exception.Message, "field index 1");
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

    private static IEnumerable<object?[]> GetLiteralCases()
    {
        ByteString binaryValue = ByteString.CopyFromUtf8("binary data");
        ByteString decimalValue = ByteString.CopyFromUtf8("3.14159");

        yield return
        [
            new ProtoLiteral
            {
                Null = new ProtoType
                {
                    Bool = new ProtoType.Types.Boolean { Nullability = ProtoType.Types.Nullability.Nullable },
                },
            },
            new Literal.NullLiteral(TypeFactory.NULLABLE.BOOL),
        ];
        yield return [new ProtoLiteral { Boolean = true, Nullable = true }, new Literal.BoolLiteral(true, IType.NullableType.Nullable)];
        yield return [new ProtoLiteral { I8 = 42 }, new Literal.I8Literal(42)];
        yield return [new ProtoLiteral { I16 = 1096 }, new Literal.I16Literal(1096)];
        yield return [new ProtoLiteral { I32 = 462018 }, new Literal.I32Literal(462018)];
        yield return [new ProtoLiteral { I64 = 3152021 }, new Literal.I64Literal(3152021)];
        yield return [new ProtoLiteral { Fp32 = 3.14f }, new Literal.FP32Literal(3.14f)];
        yield return [new ProtoLiteral { Fp64 = 2.71828 }, new Literal.FP64Literal(2.71828)];
        yield return [new ProtoLiteral { String = "Hello, World!" }, new Literal.StrLiteral("Hello, World!")];
        yield return [new ProtoLiteral { Binary = binaryValue }, new Literal.BinaryLiteral(binaryValue)];
        yield return [new ProtoLiteral { Date = 600 }, new Literal.DateLiteral(600)];
        yield return [new ProtoLiteral { PrecisionTime = new() { Precision = 6, Value = 3122016 } }, new Literal.TimeLiteral(3122016)];
        yield return
        [
            new ProtoLiteral
            {
                IntervalYearToMonth = new ProtoLiteral.Types.IntervalYearToMonth { Years = 2, Months = 5 },
            },
            new Literal.IntervalYearLiteral(2, 5),
        ];
        yield return
        [
            new ProtoLiteral
            {
                IntervalDayToSecond = new ProtoLiteral.Types.IntervalDayToSecond { Days = 2, Seconds = 45 },
            },
            new Literal.IntervalDayLiteral(2, 45),
        ];
        yield return [new ProtoLiteral { FixedChar = "ABC" }, new Literal.FixedCharLiteral("ABC")];
        yield return
        [
            new ProtoLiteral
            {
                VarChar = new ProtoLiteral.Types.VarChar { Value = "Hello, World!", Length = 13 },
            },
            new Literal.VarCharLiteral("Hello, World!", 13),
        ];
        yield return [new ProtoLiteral { FixedBinary = binaryValue }, new Literal.FixedBinaryLiteral(binaryValue)];
        yield return
        [
            new ProtoLiteral
            {
                Decimal = new ProtoLiteral.Types.Decimal { Value = decimalValue, Precision = 6, Scale = 5 },
            },
            new Literal.DecimalLiteral(decimalValue, 6, 5),
        ];
    }
}
