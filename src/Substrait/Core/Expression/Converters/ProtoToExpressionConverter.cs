// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Serialization;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using static Substrait.Core.Type.IType;
using ConversionFrame = Substrait.Core.Plan.Converters.ProtoConversionContext.Frame;
using ConversionPhase = Substrait.Core.Plan.Converters.ProtoConversionContext.Phase;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoFieldReference = Substrait.Protobuf.Expression.Types.FieldReference;
using ProtoFunctionArgument = Substrait.Protobuf.FunctionArgument;
using ProtoLiteral = Substrait.Protobuf.Expression.Types.Literal;
using ProtoSubquery = Substrait.Protobuf.Expression.Types.Subquery;

namespace Substrait.Core.Expression.Converters;

/// <summary>
/// Converts protobuf expressions to the internal representation.
/// </summary>
public class ProtoToExpressionConverter : IProtoConverter
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
        IReadOnlyList<ParameterizedType.Struct> enclosingSchemas) =>
        new ProtoConversionContext().Convert(this, protoExpression, inputSchema, enclosingSchemas);

    void IProtoConverter.Convert(ConversionFrame frame, ProtoConversionContext context)
    {
        ProtoExpression current = (ProtoExpression)frame.Node;
        ParameterizedType.Struct inputSchema = frame.InputSchema!;
        switch (current.RexTypeCase)
        {
            case ProtoExpression.RexTypeOneofCase.Selection:
                context.Complete(frame, this.CreateReference(current.Selection, inputSchema, frame.EnclosingSchemas));
                break;
            case ProtoExpression.RexTypeOneofCase.Literal:
                context.Complete(frame, this.CreateLiteral(current.Literal));
                break;
            case ProtoExpression.RexTypeOneofCase.ScalarFunction:
                if (frame.Phase == ConversionPhase.Enter)
                {
                    context.Resume(frame, ConversionPhase.Complete);
                    for (int index = current.ScalarFunction.Arguments.Count - 1; index >= 0; --index)
                    {
                        ProtoFunctionArgument argument = current.ScalarFunction.Arguments[index];
                        if (argument.ArgTypeCase == ProtoFunctionArgument.ArgTypeOneofCase.Value)
                        {
                            context.PushExpression(this, argument.Value, inputSchema, frame.EnclosingSchemas);
                        }
                    }

                    return;
                }

                context.Complete(frame, this.CreateScalarFunction(current, frame, context));
                break;
            case ProtoExpression.RexTypeOneofCase.Cast:
                if (frame.Phase == ConversionPhase.Enter)
                {
                    context.Resume(frame, ConversionPhase.Complete);
                    context.PushExpression(this, current.Cast.Input, inputSchema, frame.EnclosingSchemas);
                    return;
                }

                context.Complete(frame, new Expression.Cast(
                    this.protoTypeConverter.From(current.Cast.Type), context.Expression(frame), current.Cast.FailureBehavior.FromProto()));
                break;
            case ProtoExpression.RexTypeOneofCase.IfThen:
                if (frame.Phase == ConversionPhase.Enter)
                {
                    context.Resume(frame, ConversionPhase.Complete);
                    context.PushExpression(this, current.IfThen.Else, inputSchema, frame.EnclosingSchemas);
                    for (int index = current.IfThen.Ifs.Count - 1; index >= 0; --index)
                    {
                        context.PushExpression(this, current.IfThen.Ifs[index].Then, inputSchema, frame.EnclosingSchemas);
                        context.PushExpression(this, current.IfThen.Ifs[index].If, inputSchema, frame.EnclosingSchemas);
                    }

                    return;
                }

                var clauses = new List<(IExpression Condition, IExpression Then)>(current.IfThen.Ifs.Count);
                for (int index = 0; index < current.IfThen.Ifs.Count; ++index)
                {
                    clauses.Add((context.Expression(frame, index * 2), context.Expression(frame, (index * 2) + 1)));
                }

                context.Complete(frame, new Expression.IfThen(clauses, context.Expression(frame, current.IfThen.Ifs.Count * 2)));
                break;
            case ProtoExpression.RexTypeOneofCase.Subquery:
                this.ConvertSubquery(current.Subquery, frame, context);
                break;
            default:
                throw new NotImplementedException(current.RexTypeCase.ToString());
        }
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
            var variation = current.LiteralTypeCase == ProtoLiteral.LiteralTypeOneofCase.Null ? null : this.protoTypeConverter.GetTypeVariation(current.TypeVariationReference);
            switch (current.LiteralTypeCase)
            {
                case ProtoLiteral.LiteralTypeOneofCase.Null: destination.Add(new Literal.NullLiteral(this.protoTypeConverter.From(current.Null))); break;
                case ProtoLiteral.LiteralTypeOneofCase.Boolean: destination.Add(new Literal.BoolLiteral(current.Boolean, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.I8: destination.Add(new Literal.I8Literal(current.I8, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.I16: destination.Add(new Literal.I16Literal(current.I16, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.I32: destination.Add(new Literal.I32Literal(current.I32, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.I64: destination.Add(new Literal.I64Literal(current.I64, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.Fp32: destination.Add(new Literal.FP32Literal(current.Fp32, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.Fp64: destination.Add(new Literal.FP64Literal(current.Fp64, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.String: destination.Add(new Literal.StrLiteral(current.String, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.Binary: destination.Add(new Literal.BinaryLiteral(current.Binary, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.Date: destination.Add(new Literal.DateLiteral(current.Date, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.PrecisionTime: destination.Add(new Literal.TimeLiteral(current.PrecisionTime.Value, (int)current.PrecisionTime.Precision, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.PrecisionTimestamp: destination.Add(new Literal.PrecisionTimestampLiteral(current.PrecisionTimestamp.Value, (int)current.PrecisionTimestamp.Precision, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.PrecisionTimestampTz: destination.Add(new Literal.PrecisionTimestampTZLiteral(current.PrecisionTimestampTz.Value, (int)current.PrecisionTimestampTz.Precision, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.Uuid: destination.Add(new Literal.UuidLiteral(current.Uuid, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.IntervalYearToMonth: destination.Add(new Literal.IntervalYearLiteral(current.IntervalYearToMonth.Years, current.IntervalYearToMonth.Months, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.IntervalDayToSecond:
                    destination.Add(new Literal.IntervalDayLiteral(
                        current.IntervalDayToSecond.Days,
                        current.IntervalDayToSecond.Seconds,
                        current.IntervalDayToSecond.Subseconds,
                        (int)current.IntervalDayToSecond.Precision,
                        nullable)
                    { TypeVariation = variation });
                    break;
                case ProtoLiteral.LiteralTypeOneofCase.IntervalCompound:
                    destination.Add(new Literal.IntervalCompoundLiteral(
                        current.IntervalCompound.IntervalYearToMonth?.Years ?? 0,
                        current.IntervalCompound.IntervalYearToMonth?.Months ?? 0,
                        current.IntervalCompound.IntervalDayToSecond?.Days ?? 0,
                        current.IntervalCompound.IntervalDayToSecond?.Seconds ?? 0,
                        current.IntervalCompound.IntervalDayToSecond?.Subseconds ?? 0,
                        (int)(current.IntervalCompound.IntervalDayToSecond?.Precision ?? 0),
                        nullable)
                    { TypeVariation = variation });
                    break;
                case ProtoLiteral.LiteralTypeOneofCase.EmptyList:
                    {
                        var listType = (ParameterizedType.List)this.protoTypeConverter.From(new Substrait.Protobuf.Type { List = current.EmptyList });
                        destination.Add(new Literal.EmptyListLiteral(listType.ElementType, listType.Nullable) { TypeVariation = DeclaredVariation(variation, listType.TypeVariation) });
                        break;
                    }

                case ProtoLiteral.LiteralTypeOneofCase.EmptyMap:
                    {
                        var mapType = (ParameterizedType.Map)this.protoTypeConverter.From(new Substrait.Protobuf.Type { Map = current.EmptyMap });
                        destination.Add(new Literal.EmptyMapLiteral(mapType.KeyType, mapType.ValueType, mapType.Nullable) { TypeVariation = DeclaredVariation(variation, mapType.TypeVariation) });
                        break;
                    }

                case ProtoLiteral.LiteralTypeOneofCase.List:
                    if (inputs.Count == inputCount)
                    {
                        destination.Add(new Literal.ListLiteral(inputs, nullable) { TypeVariation = variation });
                    }
                    else
                    {
                        stack.Push((current, inputs, destination, current.List.Values.Count));
                        for (int index = current.List.Values.Count - 1; index >= 0; index--)
                        {
                            stack.Push((current.List.Values[index], new List<Literal>(), inputs, -1));
                        }
                    }

                    break;
                case ProtoLiteral.LiteralTypeOneofCase.Map:
                    if (inputs.Count == inputCount)
                    {
                        var entries = new List<KeyValuePair<Literal, Literal>>(inputCount / 2);
                        for (int index = 0; index < inputs.Count; index += 2)
                        {
                            entries.Add(new KeyValuePair<Literal, Literal>(inputs[index], inputs[index + 1]));
                        }

                        destination.Add(new Literal.MapLiteral(entries, nullable) { TypeVariation = variation });
                    }
                    else
                    {
                        stack.Push((current, inputs, destination, current.Map.KeyValues.Count * 2));
                        for (int index = current.Map.KeyValues.Count - 1; index >= 0; index--)
                        {
                            stack.Push((current.Map.KeyValues[index].Value, new List<Literal>(), inputs, -1));
                            stack.Push((current.Map.KeyValues[index].Key, new List<Literal>(), inputs, -1));
                        }
                    }

                    break;
                case ProtoLiteral.LiteralTypeOneofCase.UserDefined:
                    if (current.UserDefined.Struct is not null && inputs.Count != inputCount)
                    {
                        stack.Push((current, inputs, destination, current.UserDefined.Struct.Fields.Count));
                        for (int index = current.UserDefined.Struct.Fields.Count - 1; index >= 0; index--)
                        {
                            stack.Push((current.UserDefined.Struct.Fields[index], new List<Literal>(), inputs, -1));
                        }

                        break;
                    }

                    var userDefinedProto = new Substrait.Protobuf.Type.Types.UserDefined
                    {
                        TypeReference = current.UserDefined.TypeReference,
                        TypeVariationReference = current.TypeVariationReference,
                        Nullability = current.Nullable ? Substrait.Protobuf.Type.Types.Nullability.Nullable : Substrait.Protobuf.Type.Types.Nullability.Required,
                    };
                    userDefinedProto.TypeParameters.AddRange(current.UserDefined.TypeParameters);
                    var userDefinedType = (UserDefinedType)this.protoTypeConverter.From(new Substrait.Protobuf.Type { UserDefined = userDefinedProto });
                    if (current.UserDefined.Struct is not null)
                    {
                        destination.Add(new Literal.UserDefinedLiteral(userDefinedType.Anchor, userDefinedType.Parameters, inputs, nullable, userDefinedType.Declaration) { TypeVariation = userDefinedType.TypeVariation });
                    }
                    else if (current.UserDefined.Value is not null)
                    {
                        destination.Add(new Literal.UserDefinedLiteral(userDefinedType.Anchor, userDefinedType.Parameters, current.UserDefined.Value, nullable, userDefinedType.Declaration) { TypeVariation = userDefinedType.TypeVariation });
                    }
                    else
                    {
                        throw new SerializationException("User-defined literal must set either a value or a struct.");
                    }

                    break;
                case ProtoLiteral.LiteralTypeOneofCase.FixedChar: destination.Add(new Literal.FixedCharLiteral(current.FixedChar, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.VarChar: destination.Add(new Literal.VarCharLiteral(current.VarChar.Value, (int)current.VarChar.Length, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.FixedBinary: destination.Add(new Literal.FixedBinaryLiteral(current.FixedBinary, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.Decimal: destination.Add(new Literal.DecimalLiteral(current.Decimal.Value, current.Decimal.Precision, current.Decimal.Scale, nullable) { TypeVariation = variation }); break;
                case ProtoLiteral.LiteralTypeOneofCase.Struct:
                    if (inputs.Count == inputCount)
                    {
                        destination.Add(new Literal.StructLiteral(inputs, nullable) { TypeVariation = variation });
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

    private static ITypeVariation? DeclaredVariation(ITypeVariation? literalVariation, ITypeVariation? typeVariation)
    {
        if (literalVariation is not null && typeVariation is not null && !literalVariation.Equals(typeVariation))
        {
            throw new SerializationException(
                $"Literal type variation {literalVariation.Namespace}:{literalVariation.Name} conflicts with type variation {typeVariation.Namespace}:{typeVariation.Name} on its type.");
        }

        return literalVariation ?? typeVariation;
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

    private IExpression CreateScalarFunction(ProtoExpression expression, ConversionFrame frame, ProtoConversionContext context)
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
                    arguments.Add(context.Expression(frame, valueIndex++));
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

    private void ConvertSubquery(ProtoSubquery subquery, ConversionFrame frame, ProtoConversionContext context)
    {
        if (this.protoRelConverter is null)
        {
            throw new InvalidOperationException("Subquery conversion requires a relation converter.");
        }

        if (frame.Phase == ConversionPhase.Enter)
        {
            var enclosing = frame.EnclosingSchemas as ImmutableList<ParameterizedType.Struct> ?? frame.EnclosingSchemas.ToImmutableList();
            var nested = enclosing.Add(frame.InputSchema!);
            context.Resume(frame, ConversionPhase.Complete);
            switch (subquery.SubqueryTypeCase)
            {
                case ProtoSubquery.SubqueryTypeOneofCase.Scalar:
                    context.PushRelation(this.protoRelConverter, subquery.Scalar.Input, nested);
                    break;
                case ProtoSubquery.SubqueryTypeOneofCase.InPredicate:
                    for (int index = subquery.InPredicate.Needles.Count - 1; index >= 0; --index)
                    {
                        context.PushExpression(this, subquery.InPredicate.Needles[index], frame.InputSchema!, frame.EnclosingSchemas);
                    }

                    context.PushRelation(this.protoRelConverter, subquery.InPredicate.Haystack, nested);
                    break;
                case ProtoSubquery.SubqueryTypeOneofCase.SetPredicate:
                    context.PushRelation(this.protoRelConverter, subquery.SetPredicate.Tuples, nested);
                    break;
                case ProtoSubquery.SubqueryTypeOneofCase.SetComparison:
                    context.PushRelation(this.protoRelConverter, subquery.SetComparison.Right, nested);
                    context.PushExpression(this, subquery.SetComparison.Left, frame.InputSchema!, frame.EnclosingSchemas);
                    break;
                default:
                    throw new NotImplementedException(subquery.SubqueryTypeCase.ToString());
            }

            return;
        }

        var relation = context.Relation(frame);
        switch (subquery.SubqueryTypeCase)
        {
            case ProtoSubquery.SubqueryTypeOneofCase.Scalar:
                if (relation.RecordType.Fields.Count != 1)
                {
                    throw new InvalidOperationException($"Subquery must yield exactly 1 column but {relation.RecordType.Fields.Count} columns.");
                }

                context.Complete(frame, new Expression.ScalarSubquery(relation, relation.RecordType.Fields[0]));
                break;
            case ProtoSubquery.SubqueryTypeOneofCase.InPredicate:
                context.Complete(frame, new Expression.InPredicateSubquery(relation, context.CopyExpressions(frame, subquery.InPredicate.Needles.Count)));
                break;
            case ProtoSubquery.SubqueryTypeOneofCase.SetPredicate:
                context.Complete(frame, new Expression.SetPredicateSubquery(relation, subquery.SetPredicate.PredicateOp.FromProto()));
                break;
            case ProtoSubquery.SubqueryTypeOneofCase.SetComparison:
                context.Complete(frame, new Expression.SetComparisonSubquery(context.Expression(frame), subquery.SetComparison.ComparisonOp.FromProto(),
                    subquery.SetComparison.ReductionOp.FromProto(), relation));
                break;
            default:
                throw new NotImplementedException(subquery.SubqueryTypeCase.ToString());
        }
    }
}
