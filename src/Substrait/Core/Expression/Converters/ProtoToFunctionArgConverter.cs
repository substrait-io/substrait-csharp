// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Type.Converters;
using Substrait.Protobuf;

namespace Substrait.Core.Expression.Converters;

/// <summary>
/// Converts protobuf function arguments to the internal representation.
/// </summary>
public class ProtoToFunctionArgConverter
{
    private readonly ProtoToExpressionConverter expressionConverter;
    private readonly ProtoToTypeConverter typeConverter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtoToFunctionArgConverter"/> class.
    /// </summary>
    /// <param name="expressionConverter">The expression converter.</param>
    /// <param name="typeConverter">The type converter.</param>
    public ProtoToFunctionArgConverter(
        ProtoToExpressionConverter expressionConverter,
        ProtoToTypeConverter typeConverter)
    {
        this.expressionConverter = expressionConverter;
        this.typeConverter = typeConverter;
    }

    /// <summary>
    /// Converts a protobuf function argument.
    /// </summary>
    /// <param name="functionArgument">The protobuf argument.</param>
    /// <param name="inputSchema">The input schema.</param>
    /// <param name="enclosingSchemas">Schemas for enclosing contexts.</param>
    /// <returns>The internal function argument.</returns>
    public IFunctionArg From(
        FunctionArgument functionArgument,
        Type.ParameterizedType.Struct inputSchema,
        IReadOnlyList<Type.ParameterizedType.Struct> enclosingSchemas) =>
        this.FromConverted(functionArgument, functionArgument.ArgTypeCase == FunctionArgument.ArgTypeOneofCase.Value
            ? this.expressionConverter.From(functionArgument.Value, inputSchema, enclosingSchemas) : null);

    internal IFunctionArg FromConverted(FunctionArgument functionArgument, IExpression? value)
    {
        return functionArgument.ArgTypeCase switch
        {
            FunctionArgument.ArgTypeOneofCase.Value => value ?? throw new ArgumentException("A converted value argument is required.", nameof(value)),
            FunctionArgument.ArgTypeOneofCase.Type => this.typeConverter.From(functionArgument.Type),
            _ => throw new NotSupportedException($"Unable to convert FunctionArgument {functionArgument}."),
        };
    }
}
