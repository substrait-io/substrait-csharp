// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Serialization;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using static Substrait.Core.Type.IType;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoFieldReference = Substrait.Protobuf.Expression.Types.FieldReference;
using ProtoFunctionArgument = Substrait.Protobuf.FunctionArgument;
using ProtoLiteral = Substrait.Protobuf.Expression.Types.Literal;
using ProtoSubquery = Substrait.Protobuf.Expression.Types.Subquery;

namespace Substrait.Core.Expression.Converters;

/// <summary>
/// Converts protobuf expressions to the internal representation.
/// </summary>
public class ProtoToExpressionConverter
{
    private readonly ExtensionsDictionary lookup;
    private readonly ExtensionsDictionary.StrictMode strictMode;
    private readonly ExtensionsCollection extensions;
    private readonly ProtoToTypeConverter protoTypeConverter;
    private readonly ProtoToRelConverter? protoRelConverter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtoToExpressionConverter"/> class.
    /// </summary>
    /// <param name="lookup">The extension lookup.</param>
    /// <param name="extensions">The available extensions.</param>
    /// <param name="protoTypeConverter">The type converter.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    public ProtoToExpressionConverter(
        ExtensionsDictionary lookup,
        ExtensionsCollection extensions,
        ProtoToTypeConverter protoTypeConverter,
        ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT)
        : this(lookup, extensions, protoTypeConverter, null, strictMode)
    {
    }

    /// <summary>
    /// Initializes a converter that supports subqueries.
    /// </summary>
    /// <param name="lookup">The extension lookup.</param>
    /// <param name="extensions">The available extensions.</param>
    /// <param name="protoTypeConverter">The type converter.</param>
    /// <param name="protoRelConverter">The relation converter.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    public ProtoToExpressionConverter(
        ExtensionsDictionary lookup,
        ExtensionsCollection extensions,
        ProtoToTypeConverter protoTypeConverter,
        ProtoToRelConverter? protoRelConverter,
        ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT)
    {
        this.lookup = lookup;
        this.strictMode = strictMode;
        this.extensions = extensions;
        this.protoTypeConverter = protoTypeConverter;
        this.protoRelConverter = protoRelConverter;
    }

    /// <summary>
    /// Converts an expression that does not contain references or subqueries.
    /// </summary>
    /// <param name="protoExpression">The protobuf expression.</param>
    /// <returns>The internal expression.</returns>
    public IExpression From(ProtoExpression protoExpression) =>
        this.From(protoExpression, ParameterizedType.Struct.Empty, ImmutableList<ParameterizedType.Struct>.Empty);

    /// <summary>
    /// Converts a protobuf expression.
    /// </summary>
    /// <param name="protoExpression">The protobuf expression.</param>
    /// <param name="inputSchema">The input schema.</param>
    /// <param name="enclosingSchemas">Schemas for enclosing contexts.</param>
    /// <returns>The internal expression.</returns>
    public IExpression From(
        ProtoExpression protoExpression,
        ParameterizedType.Struct inputSchema,
        IReadOnlyList<ParameterizedType.Struct> enclosingSchemas)
    {
        var stack = new Stack<(ProtoExpression Expression, List<IExpression> Inputs, List<IExpression> Output, int InputCount)>();
        var output = new List<IExpression>();
        stack.Push((protoExpression, new List<IExpression>(), output, -1));

        while (stack.Count > 0)
        {
            var (current, inputs, destination, inputCount) = stack.Pop();
            switch (current.RexTypeCase)
            {
                case ProtoExpression.RexTypeOneofCase.Selection:
                    destination.Add(this.CreateReference(current.Selection, inputSchema, enclosingSchemas));
                    break;
                case ProtoExpression.RexTypeOneofCase.Literal:
                    destination.Add(this.CreateLiteral(current.Literal));
                    break;
                case ProtoExpression.RexTypeOneofCase.ScalarFunction:
                    if (inputs.Count == inputCount)
                    {
                        destination.Add(this.CreateScalarFunction(current, inputs));
                    }
                    else
                    {
                        int valueCount = current.ScalarFunction.Arguments.Count(argument => argument.ArgTypeCase == ProtoFunctionArgument.ArgTypeOneofCase.Value);
                        stack.Push((current, inputs, destination, valueCount));
                        for (int index = current.ScalarFunction.Arguments.Count - 1; index >= 0; index--)
                        {
                            ProtoFunctionArgument argument = current.ScalarFunction.Arguments[index];
                            if (argument.ArgTypeCase == ProtoFunctionArgument.ArgTypeOneofCase.Value)
                            {
                                stack.Push((argument.Value, new List<IExpression>(), inputs, -1));
                            }
                        }
                    }

                    break;
                case ProtoExpression.RexTypeOneofCase.Cast:
                    if (inputs.Count == inputCount)
                    {
                        destination.Add(new Expression.Cast(
                            this.protoTypeConverter.From(current.Cast.Type),
                            inputs[0],
                            current.Cast.FailureBehavior.FromProto()));
                    }
                    else
                    {
                        stack.Push((current, inputs, destination, 1));
                        stack.Push((current.Cast.Input, new List<IExpression>(), inputs, -1));
                    }

                    break;
                case ProtoExpression.RexTypeOneofCase.IfThen:
                    if (inputs.Count == inputCount)
                    {
                        var clauses = new List<(IExpression Condition, IExpression Then)>((inputs.Count - 1) / 2);
                        for (int index = 0; index < inputs.Count - 1; index += 2)
                        {
                            clauses.Add((inputs[index], inputs[index + 1]));
                        }

                        destination.Add(new Expression.IfThen(clauses, inputs[^1]));
                    }
                    else
                    {
                        stack.Push((current, inputs, destination, (current.IfThen.Ifs.Count * 2) + 1));
                        stack.Push((current.IfThen.Else, new List<IExpression>(), inputs, -1));
                        for (int index = current.IfThen.Ifs.Count - 1; index >= 0; index--)
                        {
                            stack.Push((current.IfThen.Ifs[index].Then, new List<IExpression>(), inputs, -1));
                            stack.Push((current.IfThen.Ifs[index].If, new List<IExpression>(), inputs, -1));
                        }
                    }

                    break;
                case ProtoExpression.RexTypeOneofCase.Subquery:
                    destination.Add(this.CreateSubquery(current.Subquery, inputSchema, enclosingSchemas));
                    break;
                default:
                    throw new NotImplementedException(current.RexTypeCase.ToString());
            }
        }

        return GetSingleOutput(output, protoExpression, "expressions");
    }

    /// <summary>
    /// Converts a protobuf literal.
    /// </summary>
    /// <param name="protoLiteral">The protobuf literal.</param>
    /// <returns>The internal literal.</returns>
    public Literal CreateLiteral(ProtoLiteral protoLiteral)
    {
        var stack = new Stack<(ProtoLiteral Literal, List<Literal> Inputs, List<Literal> Output, int InputCount)>();
        var output = new List<Literal>();
        stack.Push((protoLiteral, new List<Literal>(), output, -1));

        while (stack.Count > 0)
        {
            var (current, inputs, destination, inputCount) = stack.Pop();
            NullableType nullable = current.Nullable ? NullableType.Nullable : NullableType.Required;
            switch (current.LiteralTypeCase)
            {
                case ProtoLiteral.LiteralTypeOneofCase.Null: destination.Add(new Literal.NullLiteral(this.protoTypeConverter.From(current.Null))); break;
                case ProtoLiteral.LiteralTypeOneofCase.Boolean: destination.Add(new Literal.BoolLiteral(current.Boolean, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.I8: destination.Add(new Literal.I8Literal(current.I8, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.I16: destination.Add(new Literal.I16Literal(current.I16, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.I32: destination.Add(new Literal.I32Literal(current.I32, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.I64: destination.Add(new Literal.I64Literal(current.I64, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.Fp32: destination.Add(new Literal.FP32Literal(current.Fp32, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.Fp64: destination.Add(new Literal.FP64Literal(current.Fp64, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.String: destination.Add(new Literal.StrLiteral(current.String, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.Binary: destination.Add(new Literal.BinaryLiteral(current.Binary, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.Date: destination.Add(new Literal.DateLiteral(current.Date, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.PrecisionTime:
                    if (current.PrecisionTime.Precision != 6)
                    {
                        throw new NotSupportedException("Only microsecond-precision time literals are supported.");
                    }

                    destination.Add(new Literal.TimeLiteral(current.PrecisionTime.Value, nullable));
                    break;
                case ProtoLiteral.LiteralTypeOneofCase.IntervalYearToMonth: destination.Add(new Literal.IntervalYearLiteral(current.IntervalYearToMonth.Years, current.IntervalYearToMonth.Months, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.IntervalDayToSecond:
                    if (current.IntervalDayToSecond.Precision != 0 || current.IntervalDayToSecond.Subseconds != 0)
                    {
                        throw new NotSupportedException("Only second-precision day interval literals are supported.");
                    }

                    destination.Add(new Literal.IntervalDayLiteral(current.IntervalDayToSecond.Days, current.IntervalDayToSecond.Seconds, nullable));
                    break;
                case ProtoLiteral.LiteralTypeOneofCase.FixedChar: destination.Add(new Literal.FixedCharLiteral(current.FixedChar, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.VarChar: destination.Add(new Literal.VarCharLiteral(current.VarChar.Value, (int)current.VarChar.Length, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.FixedBinary: destination.Add(new Literal.FixedBinaryLiteral(current.FixedBinary, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.Decimal: destination.Add(new Literal.DecimalLiteral(current.Decimal.Value, current.Decimal.Precision, current.Decimal.Scale, nullable)); break;
                case ProtoLiteral.LiteralTypeOneofCase.Struct:
                    if (inputs.Count == inputCount)
                    {
                        destination.Add(new Literal.StructLiteral(inputs, nullable));
                    }
                    else
                    {
                        stack.Push((current, inputs, destination, current.Struct.Fields.Count));
                        for (int index = current.Struct.Fields.Count - 1; index >= 0; index--)
                        {
                            stack.Push((current.Struct.Fields[index], new List<Literal>(), inputs, -1));
                        }
                    }

                    break;
                default: throw new NotImplementedException(current.LiteralTypeCase.ToString());
            }
        }

        return GetSingleOutput(output, protoLiteral, "literals");
    }

    /// <summary>
    /// Converts a protobuf field reference.
    /// </summary>
    /// <param name="fieldReference">The protobuf field reference.</param>
    /// <param name="inputSchema">The input schema.</param>
    /// <param name="enclosingSchemas">Schemas for enclosing contexts.</param>
    /// <returns>The internal field reference.</returns>
    public FieldReference CreateReference(
        ProtoFieldReference fieldReference,
        ParameterizedType.Struct inputSchema,
        IReadOnlyList<ParameterizedType.Struct> enclosingSchemas)
    {
        if (fieldReference.ReferenceTypeCase is not ProtoFieldReference.ReferenceTypeOneofCase.DirectReference)
        {
            throw new NotImplementedException(fieldReference.ReferenceTypeCase.ToString());
        }

        ProtoExpression.Types.ReferenceSegment directReference = fieldReference.DirectReference;
        if (directReference.ReferenceTypeCase is not ProtoExpression.Types.ReferenceSegment.ReferenceTypeOneofCase.StructField ||
            directReference.StructField.Child is not null)
        {
            throw new NotImplementedException("Only direct top-level struct field references are supported.");
        }

        int fieldIndex = directReference.StructField.Field;
        return fieldReference.RootTypeCase switch
        {
            ProtoFieldReference.RootTypeOneofCase.RootReference => CreateRootReference(inputSchema, fieldIndex),
            ProtoFieldReference.RootTypeOneofCase.OuterReference => CreateOuterReference(fieldReference, enclosingSchemas, fieldIndex),
            _ => throw new NotImplementedException(fieldReference.RootTypeCase.ToString()),
        };
    }

    private static T GetSingleOutput<T>(IReadOnlyList<T> output, object input, string description)
    {
        if (output.Count != 1)
        {
            throw new SerializationException($"Deserialization error: Expected one result for '{input}', but found {output.Count} {description}.");
        }

        return output[0];
    }

    private static FieldReference CreateRootReference(ParameterizedType.Struct inputSchema, int fieldIndex)
    {
        if (fieldIndex < 0 || fieldIndex >= inputSchema.Fields.Count)
        {
            throw new SerializationException($"Deserialization error: field index {fieldIndex} is outside the input schema with {inputSchema.Fields.Count} fields.");
        }

        return new FieldReference(inputSchema.Fields[fieldIndex], fieldIndex);
    }

    private static FieldReference CreateOuterReference(ProtoFieldReference fieldReference, IReadOnlyList<ParameterizedType.Struct> enclosingSchemas, int fieldIndex)
    {
        if (fieldReference.OuterReference.OuterReferenceTypeCase == ProtoFieldReference.Types.OuterReference.OuterReferenceTypeOneofCase.RelReference)
        {
            throw new NotSupportedException("Relation-anchor outer references are not supported.");
        }

#pragma warning disable CS0612 // Retain support for the offset-based internal reference model.
        uint stepsOut = fieldReference.OuterReference.StepsOut;
#pragma warning restore CS0612
        if (stepsOut == 0 || stepsOut > enclosingSchemas.Count)
        {
            throw new SerializationException($"Deserialization error: outer reference steps {stepsOut} is outside the {enclosingSchemas.Count} enclosing schemas.");
        }

        ParameterizedType.Struct schema = enclosingSchemas[^(int)stepsOut];
        if (fieldIndex < 0 || fieldIndex >= schema.Fields.Count)
        {
            throw new SerializationException($"Deserialization error: field index {fieldIndex} is outside the referenced outer schema with {schema.Fields.Count} fields.");
        }

        return new FieldReference(schema.Fields[fieldIndex], fieldIndex, (int)stepsOut);
    }

    private IExpression CreateScalarFunction(ProtoExpression expression, IReadOnlyList<IExpression> valueArguments)
    {
        var scalarFunction = expression.ScalarFunction;
        var anchor = this.lookup.GetFunctionAnchor((int)scalarFunction.FunctionReference);
        bool found = this.lookup.TryGetScalarFunction(anchor, this.extensions, this.strictMode, out ScalarFunctionImpl? declaration);
        Debug.Assert(found || !this.strictMode.IsOn(ExtensionsDictionary.StrictMode.FUNCTION), "Function resolution must succeed in strict mode.");

        var arguments = new List<IFunctionArg>(scalarFunction.Arguments.Count);
        int valueIndex = 0;
        for (int index = 0; index < scalarFunction.Arguments.Count; index++)
        {
            ProtoFunctionArgument argument = scalarFunction.Arguments[index];
            switch (argument.ArgTypeCase)
            {
                case ProtoFunctionArgument.ArgTypeOneofCase.Type:
                    arguments.Add(this.protoTypeConverter.From(argument.Type));
                    break;
                case ProtoFunctionArgument.ArgTypeOneofCase.Value:
                    arguments.Add(valueArguments[valueIndex++]);
                    break;
                case ProtoFunctionArgument.ArgTypeOneofCase.Enum when declaration is not null:
                    arguments.Add(new EnumArgumentValue((EnumArgument)declaration.Args[index], argument.Enum));
                    break;
                case ProtoFunctionArgument.ArgTypeOneofCase.Enum:
                    throw new ArgumentException("Enum arguments require a function declaration.");
                default:
                    throw new NotSupportedException($"Unable to convert FunctionArgument {argument}.");
            }
        }

        return new Expression.ScalarFunctionInvocation(
            anchor.Namespace,
            anchor.Key,
            arguments,
            this.protoTypeConverter.From(scalarFunction.OutputType),
            declaration);
    }

    private IExpression CreateSubquery(
        ProtoSubquery subquery,
        ParameterizedType.Struct inputSchema,
        IReadOnlyList<ParameterizedType.Struct> enclosingSchemas)
    {
        if (this.protoRelConverter is null)
        {
            throw new InvalidOperationException("Subquery conversion requires a relation converter.");
        }

        IReadOnlyList<ParameterizedType.Struct> nestedSchemas = enclosingSchemas.Append(inputSchema).ToImmutableList();
        return subquery.SubqueryTypeCase switch
        {
            ProtoSubquery.SubqueryTypeOneofCase.Scalar => this.CreateScalarSubquery(subquery, nestedSchemas),
            ProtoSubquery.SubqueryTypeOneofCase.InPredicate => new Expression.InPredicateSubquery(
                this.protoRelConverter.ToRel(subquery.InPredicate.Haystack, nestedSchemas),
                subquery.InPredicate.Needles.Select(needle => this.From(needle, inputSchema, enclosingSchemas)).ToImmutableList()),
            ProtoSubquery.SubqueryTypeOneofCase.SetPredicate => new Expression.SetPredicateSubquery(
                this.protoRelConverter.ToRel(subquery.SetPredicate.Tuples, nestedSchemas),
                subquery.SetPredicate.PredicateOp.FromProto()),
            ProtoSubquery.SubqueryTypeOneofCase.SetComparison => new Expression.SetComparisonSubquery(
                this.From(subquery.SetComparison.Left, inputSchema, enclosingSchemas),
                subquery.SetComparison.ComparisonOp.FromProto(),
                subquery.SetComparison.ReductionOp.FromProto(),
                this.protoRelConverter.ToRel(subquery.SetComparison.Right, nestedSchemas)),
            _ => throw new NotImplementedException(subquery.SubqueryTypeCase.ToString()),
        };
    }

    private IExpression CreateScalarSubquery(
        ProtoSubquery subquery,
        IReadOnlyList<ParameterizedType.Struct> enclosingSchemas)
    {
        var relation = this.protoRelConverter!.ToRel(subquery.Scalar.Input, enclosingSchemas);
        if (relation.RecordType.Fields.Count != 1)
        {
            throw new InvalidOperationException($"Subquery must yield exactly 1 column but {relation.RecordType.Fields.Count} columns.");
        }

        return new Expression.ScalarSubquery(relation, relation.RecordType.Fields[0]);
    }
}
