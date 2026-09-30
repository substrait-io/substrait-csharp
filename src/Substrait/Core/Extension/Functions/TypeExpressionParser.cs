// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Substrait.Antlr.SubstraitType;
using static Substrait.Core.Extension.Functions.ParameterizedTypeExpression;
using static Substrait.Core.Type.IType;

namespace Substrait.Core.Extension.Functions;

/// <summary>
/// TypeExpressionParser class.
/// </summary>
public class TypeExpressionParser
{
    /// <summary>
    /// Parse a type from the Substrait type language.
    /// </summary>
    /// <param name="typeExpressionStr">The string to parse.</param>
    /// <returns>The type generated.</returns>
    /// <exception cref="NotSupportedException">Thrown if a node that is not supported is visited.</exception>
    public static ITypeExpression Parse(string typeExpressionStr)
    {
        SubstraitTypeLexer lexer = new(new Antlr4.Runtime.AntlrInputStream(typeExpressionStr));
        SubstraitTypeParser parser = new(new Antlr4.Runtime.CommonTokenStream(lexer));
        return parser.startRule().Accept(new TypeExpressionParserVisitor());
    }

    /// <summary>
    /// Visitor for parsing types defined in the Substrait type language used by the extension definitions.
    /// </summary>
    internal class TypeExpressionParserVisitor : AbstractParseTreeVisitor<ITypeExpression>, ISubstraitTypeVisitor<ITypeExpression>
    {
        /// <inheritdoc/>
        public ITypeExpression VisitStartRule(SubstraitTypeParser.StartRuleContext context)
        {
            return context.expr().Accept(this);
        }

        /// <inheritdoc/>
        public ITypeExpression VisitTypeStatement(SubstraitTypeParser.TypeStatementContext context)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitTypeLiteral(SubstraitTypeParser.TypeLiteralContext context)
        {
            return context.typeDef().Accept(this);
        }

        /// <inheritdoc/>
        public ITypeExpression VisitTypeDef(SubstraitTypeParser.TypeDefContext context)
        {
            if (context.scalarType() != null)
            {
                return context.scalarType().Accept(this);
            }
            else if (context.parameterizedType() != null)
            {
                return context.parameterizedType().Accept(this);
            }
            else
            {
                return context.anyType().Accept(this);
            }
        }

        /// <inheritdoc/>
        public ITypeExpression VisitBoolean(SubstraitTypeParser.BooleanContext context)
        {
            return WithNullability(context).BOOL;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitI8(SubstraitTypeParser.I8Context context)
        {
            return WithNullability(context).I8;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitI16(SubstraitTypeParser.I16Context context)
        {
            return WithNullability(context).I16;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitI32(SubstraitTypeParser.I32Context context)
        {
            return WithNullability(context).I32;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitI64(SubstraitTypeParser.I64Context context)
        {
            return WithNullability(context).I64;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitFp32(SubstraitTypeParser.Fp32Context context)
        {
            return WithNullability(context).FP32;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitFp64(SubstraitTypeParser.Fp64Context context)
        {
            return WithNullability(context).FP64;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitString(SubstraitTypeParser.StringContext context)
        {
            return WithNullability(context).STR;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitBinary(SubstraitTypeParser.BinaryContext context)
        {
            return WithNullability(context).BINARY;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitDate(SubstraitTypeParser.DateContext context)
        {
            return WithNullability(context).DATE;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitPrecisionIntervalCompound(SubstraitTypeParser.PrecisionIntervalCompoundContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitIntervalYear(SubstraitTypeParser.IntervalYearContext context)
        {
            return WithNullability(context).INTERVAL_YEAR;
        }

        /// <inheritdoc/>
        public ITypeExpression VisitPrecisionIntervalDay(SubstraitTypeParser.PrecisionIntervalDayContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitFixedChar(SubstraitTypeParser.FixedCharContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitVarChar(SubstraitTypeParser.VarCharContext context)
        {
            bool nullable = context.isnull != null;
            ITypeExpression length = context.length.Accept(this);
            if (length is StringLiteral stringLiteral)
            {
                return TypeExpressionFactory.Of(GetNullableType(nullable)).VarChar(stringLiteral);
            }

            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitFixedBinary(SubstraitTypeParser.FixedBinaryContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitDecimal(SubstraitTypeParser.DecimalContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitPrecisionTimestamp(SubstraitTypeParser.PrecisionTimestampContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitPrecisionTimestampTZ(SubstraitTypeParser.PrecisionTimestampTZContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitAnyType(SubstraitTypeParser.AnyTypeContext context)
        {
            bool nullable = ((SubstraitTypeParser.TypeDefContext)context.Parent).isnull != null;
            return TypeExpressionFactory.Of(GetNullableType(nullable)).StringLiteral("any");
        }

        /// <inheritdoc/>
        public ITypeExpression VisitNumericLiteral(SubstraitTypeParser.NumericLiteralContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitStruct(SubstraitTypeParser.StructContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitList(SubstraitTypeParser.ListContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitOr(SubstraitTypeParser.OrContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitAnd(SubstraitTypeParser.AndContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitMulDiv(SubstraitTypeParser.MulDivContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitAddSub(SubstraitTypeParser.AddSubContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitComparison(SubstraitTypeParser.ComparisonContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitEquality(SubstraitTypeParser.EqualityContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitFunctionCall(SubstraitTypeParser.FunctionCallContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitIfExpr(SubstraitTypeParser.IfExprContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitLiteralNumber(SubstraitTypeParser.LiteralNumberContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitMap(SubstraitTypeParser.MapContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitMultilineDefinition(SubstraitTypeParser.MultilineDefinitionContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitNotExpr(SubstraitTypeParser.NotExprContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitNStruct(SubstraitTypeParser.NStructContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitNumericExpression(SubstraitTypeParser.NumericExpressionContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitNumericParameterName(SubstraitTypeParser.NumericParameterNameContext context)
        {
            return TypeExpressionFactory.Of(GetNullableType(false)).StringLiteral(context.GetText());
        }

        /// <inheritdoc/>
        public ITypeExpression VisitParenExpression(SubstraitTypeParser.ParenExpressionContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitTernary(SubstraitTypeParser.TernaryContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitFunc(SubstraitTypeParser.FuncContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitSingleFuncParam(SubstraitTypeParser.SingleFuncParamContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitFuncParamsWithParens(SubstraitTypeParser.FuncParamsWithParensContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitParameterName(SubstraitTypeParser.ParameterNameContext context)
        {
            bool nullable = context.isnull != null;
            return TypeExpressionFactory.Of(GetNullableType(nullable)).StringLiteral(context.GetText());
        }

        /// <inheritdoc/>
        public ITypeExpression VisitPrecisionTime([NotNull] SubstraitTypeParser.PrecisionTimeContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitUserDefined(SubstraitTypeParser.UserDefinedContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ITypeExpression VisitUuid(SubstraitTypeParser.UuidContext context)
        {
            throw new NotSupportedException();
        }

        private static NullableType GetNullableType(bool nullable)
        {
            return nullable ? NullableType.Nullable : NullableType.Required;
        }

        private static TypeExpressionFactory WithNullability(SubstraitTypeParser.ScalarTypeContext required)
        {
            SubstraitTypeParser.TypeDefContext parent = (SubstraitTypeParser.TypeDefContext)required.Parent;
            return TypeExpressionFactory.Of(GetNullableType(parent.isnull != null));
        }
    }
}
