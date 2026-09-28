// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Extension;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using static Substrait.Core.Expression.Literal;
using static Substrait.Tools.TypeUtils;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoLiteral = Substrait.Protobuf.Expression.Types.Literal;
using ProtoSubquery = Substrait.Protobuf.Expression.Types.Subquery;

namespace Substrait.Core.Expression.Converters;

/// <summary>
/// Converts internal expressions to protobuf expressions.
/// </summary>
public class ExpressionToProtoConverter
{
    private readonly ExpressionBottomUpDispatcher<PlanToProtoConverter.ConverterContext, ProtoExpression> dispatcher;

    /// <summary>Initializes an expression converter.</summary>
    public ExpressionToProtoConverter(TypeToProtoConverter typeConverter)
        : this(typeConverter, null)
    {
    }

    /// <summary>Initializes an expression converter with optional subquery support.</summary>
    public ExpressionToProtoConverter(TypeToProtoConverter typeConverter, RelToProtoConverter? relationConverter)
    {
        this.dispatcher = new(new ExpressionToProtoVisitor(typeConverter, relationConverter));
    }

    /// <summary>Converts an expression using a new context.</summary>
    public ProtoExpression From(IExpression expression) =>
        this.From(expression, new PlanToProtoConverter.ConverterContext());

    /// <summary>Converts an expression using a shared context.</summary>
    public ProtoExpression From(IExpression expression, PlanToProtoConverter.ConverterContext context) =>
        this.dispatcher.Dispatch(expression, context);

    private sealed class ExpressionToProtoVisitor : ExpressionVisitor<PlanToProtoConverter.ConverterContext, ProtoExpression>
    {
        private static readonly ProtoExpression.Types.FieldReference.Types.RootReference RootReference = new();
        private readonly RelToProtoConverter? relationConverter;
        private readonly TypeToProtoConverter typeConverter;

        public ExpressionToProtoVisitor(TypeToProtoConverter typeConverter, RelToProtoConverter? relationConverter)
        {
            this.typeConverter = typeConverter;
            this.relationConverter = relationConverter;
        }

        public override ProtoExpression Visit(FieldReference expression, PlanToProtoConverter.ConverterContext context)
        {
            var reference = new ProtoExpression.Types.FieldReference
            {
                DirectReference = new ProtoExpression.Types.ReferenceSegment
                {
                    StructField = new ProtoExpression.Types.ReferenceSegment.Types.StructField { Field = expression.FieldIndex },
                },
            };
            if (expression.SubqueryLevels > 0)
            {
#pragma warning disable CS0612 // The internal model still uses subquery offsets, not relation anchors.
                reference.OuterReference = new() { StepsOut = (uint)expression.SubqueryLevels };
#pragma warning restore CS0612
            }
            else
            {
                reference.RootReference = RootReference;
            }

            return new ProtoExpression { Selection = reference };
        }

        public override ProtoExpression Visit(NullLiteral expression, PlanToProtoConverter.ConverterContext context) =>
            Wrap(new ProtoLiteral { Null = this.typeConverter.From(expression.Type, context) });

        public override ProtoExpression Visit(BoolLiteral expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { Boolean = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(I8Literal expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { I8 = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(I16Literal expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { I16 = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(I32Literal expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { I32 = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(I64Literal expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { I64 = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(FP32Literal expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { Fp32 = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(FP64Literal expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { Fp64 = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(StrLiteral expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { String = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(BinaryLiteral expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { Binary = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(DateLiteral expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { Date = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(TimeLiteral expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { PrecisionTime = new() { Precision = 6, Value = expression.Value }, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(FixedCharLiteral expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { FixedChar = expression.Value, Nullable = expression.Type.IsNullable() });
        public override ProtoExpression Visit(FixedBinaryLiteral expression, PlanToProtoConverter.ConverterContext context) => Wrap(new() { FixedBinary = expression.Value, Nullable = expression.Type.IsNullable() });

        public override ProtoExpression Visit(PrecisionTimestampLiteral expression, PlanToProtoConverter.ConverterContext context) =>
            throw new NotImplementedException($"Conversion for {expression.GetType().Name} is not implemented.");

        public override ProtoExpression Visit(PrecisionTimestampTZLiteral expression, PlanToProtoConverter.ConverterContext context) =>
            throw new NotImplementedException($"Conversion for {expression.GetType().Name} is not implemented.");

        public override ProtoExpression Visit(IntervalYearLiteral expression, PlanToProtoConverter.ConverterContext context) =>
            Wrap(new() { IntervalYearToMonth = new() { Years = expression.Years, Months = expression.Months }, Nullable = expression.Type.IsNullable() });

        public override ProtoExpression Visit(IntervalDayLiteral expression, PlanToProtoConverter.ConverterContext context) =>
            Wrap(new() { IntervalDayToSecond = new() { Days = expression.Days, Seconds = expression.Seconds }, Nullable = expression.Type.IsNullable() });

        public override ProtoExpression Visit(VarCharLiteral expression, PlanToProtoConverter.ConverterContext context) =>
            Wrap(new() { VarChar = new() { Value = expression.Value, Length = (uint)expression.Length }, Nullable = expression.Type.IsNullable() });

        public override ProtoExpression Visit(DecimalLiteral expression, PlanToProtoConverter.ConverterContext context) =>
            Wrap(new() { Decimal = new() { Value = expression.Value, Precision = expression.Precision, Scale = expression.Scale }, Nullable = expression.Type.IsNullable() });

        public override ProtoExpression Visit(StructLiteral expression, PlanToProtoConverter.ConverterContext context)
        {
            var structure = new ProtoLiteral.Types.Struct();
            structure.Fields.AddRange(expression.Fields.Select(field =>
                context.GetOutput(field).Literal ?? throw new InvalidOperationException($"Literal is not set for field: {field}")));
            return Wrap(new ProtoLiteral { Struct = structure, Nullable = expression.Type.IsNullable() });
        }

        public override ProtoExpression Visit(Expression.ScalarFunctionInvocation expression, PlanToProtoConverter.ConverterContext context)
        {
            var function = new ProtoExpression.Types.ScalarFunction
            {
                FunctionReference = (uint)context.AddExtension(ExtensionsCollector.ExtensionType.Function, expression.Namespace, expression.Key),
                OutputType = this.typeConverter.From(expression.Type, context),
            };
            function.Arguments.AllocateAndAddRange(expression.Arguments.Count, expression.Arguments.Select(argument => argument switch
            {
                IType type => new Protobuf.FunctionArgument { Type = this.typeConverter.From(type, context) },
                IExpression value => new Protobuf.FunctionArgument { Value = context.GetOutput(value) },
                EnumArgumentValue option => new Protobuf.FunctionArgument { Enum = option.Option },
                _ => throw new NotSupportedException($"Unsupported function argument type: {argument.GetType().Name}"),
            }));
            return new ProtoExpression { ScalarFunction = function };
        }

        public override ProtoExpression Visit(Expression.Cast expression, PlanToProtoConverter.ConverterContext context) =>
            new() { Cast = new() { Type = this.typeConverter.From(expression.Type, context), Input = context.GetOutput(expression.Input), FailureBehavior = expression.Behavior.ToProto() } };

        public override ProtoExpression Visit(Expression.IfThen expression, PlanToProtoConverter.ConverterContext context)
        {
            var result = new ProtoExpression.Types.IfThen { Else = context.GetOutput(expression.ElseClause) };
            result.Ifs.AddRange(expression.IfClauses.Select(clause => new ProtoExpression.Types.IfThen.Types.IfClause
            {
                If = context.GetOutput(clause.Condition),
                Then = context.GetOutput(clause.Then),
            }));
            return new ProtoExpression { IfThen = result };
        }

        public override ProtoExpression Visit(Expression.Struct expression, PlanToProtoConverter.ConverterContext context) =>
            throw new NotImplementedException($"Conversion for {expression.GetType().Name} is not implemented.");

        public override ProtoExpression Visit(Expression.ScalarSubquery expression, PlanToProtoConverter.ConverterContext context) =>
            new() { Subquery = new() { Scalar = new() { Input = this.GetRelationConverter().From(expression.Subquery, context) } } };

        public override ProtoExpression Visit(Expression.InPredicateSubquery expression, PlanToProtoConverter.ConverterContext context)
        {
            var predicate = new ProtoSubquery.Types.InPredicate
            {
                Haystack = this.GetRelationConverter().From(expression.Subquery, context),
            };
            predicate.Needles.AddRange(expression.Values.Select(context.GetOutput));
            return new ProtoExpression { Subquery = new ProtoSubquery { InPredicate = predicate } };
        }

        public override ProtoExpression Visit(Expression.SetPredicateSubquery expression, PlanToProtoConverter.ConverterContext context) =>
            new()
            {
                Subquery = new()
                {
                    SetPredicate = new()
                    {
                        Tuples = this.GetRelationConverter().From(expression.Subquery, context),
                        PredicateOp = expression.Operation.ToProto(),
                    },
                },
            };

        public override ProtoExpression Visit(Expression.SetComparisonSubquery expression, PlanToProtoConverter.ConverterContext context) =>
            new()
            {
                Subquery = new()
                {
                    SetComparison = new()
                    {
                        Left = context.GetOutput(expression.Expression),
                        ComparisonOp = expression.Comparison.ToProto(),
                        ReductionOp = expression.Reduction.ToProto(),
                        Right = this.GetRelationConverter().From(expression.Subquery, context),
                    },
                },
            };

        public override ProtoExpression Visit(IExpression other, PlanToProtoConverter.ConverterContext context) =>
            throw new NotImplementedException($"Conversion for {other.GetType().Name} is not implemented.");

        private static ProtoExpression Wrap(ProtoLiteral literal) => new() { Literal = literal };

        private RelToProtoConverter GetRelationConverter() =>
            this.relationConverter ?? throw new InvalidOperationException("Subquery conversion requires a relation converter.");
    }
}
