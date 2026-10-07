// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Tools.Visitor;
using static Substrait.Core.Type.ParameterizedType;
using static Substrait.Core.Type.PrimitiveType;

namespace Substrait.Core.Type;

/// <summary>
/// Visitor for processing types in a tree.
/// </summary>
/// <typeparam name="TContext">The type of the context used during visitation.</typeparam>
/// <typeparam name="TOutput">The type of the output produced by the visitation.</typeparam>
public abstract class TypeVisitor<TContext, TOutput> : IVisitor<IType, TContext, TOutput>
{
    /// <summary>
    /// Visits an <see cref="Bool"/> type.
    /// </summary>
    /// <param name="type">Bool type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Bool type, TContext context);

    /// <summary>
    /// Visits a <see cref="I8"/> type.
    /// </summary>
    /// <param name="type">I8 type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(I8 type, TContext context);

    /// <summary>
    /// Visits a <see cref="I16"/> type.
    /// </summary>
    /// <param name="type">I16 type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(I16 type, TContext context);

    /// <summary>
    /// Visits a <see cref="I32"/> type.
    /// </summary>
    /// <param name="type">I32 type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(I32 type, TContext context);

    /// <summary>
    /// Visits a <see cref="I64"/> type.
    /// </summary>
    /// <param name="type">I64 type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(I64 type, TContext context);

    /// <summary>
    /// Visits a <see cref="FP32"/> type.
    /// </summary>
    /// <param name="type">FP32 type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(FP32 type, TContext context);

    /// <summary>
    /// Visits a <see cref="FP64"/> type.
    /// </summary>
    /// <param name="type">FP64 type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(FP64 type, TContext context);

    /// <summary>
    /// Visits a <see cref="Str"/> type.
    /// </summary>
    /// <param name="type">Str type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Str type, TContext context);

    /// <summary>
    /// Visits a <see cref="Binary"/> type.
    /// </summary>
    /// <param name="type">Binary type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Binary type, TContext context);

    /// <summary>
    /// Visits a <see cref="Date"/> type.
    /// </summary>
    /// <param name="type">Date type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Date type, TContext context);

    /// <summary>Visits a UUID type, falling back to the general type handler.</summary>
    /// <param name="type">UUID type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The result of the visit.</returns>
    public virtual TOutput Visit(Uuid type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a compound interval, falling back to the general type handler.</summary>
    /// <param name="type">Compound interval type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The result of the visit.</returns>
    public virtual TOutput Visit(IntervalCompound type, TContext context) => this.Visit((IType)type, context);

    /// <summary>
    /// Visits a <see cref="Time"/> type.
    /// </summary>
    /// <param name="type">Time type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Time type, TContext context);

    /// <summary>
    /// Visits a <see cref="IntervalYear"/> type.
    /// </summary>
    /// <param name="type">IntervalYear type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(IntervalYear type, TContext context);

    /// <summary>
    /// Visits a <see cref="IntervalDay"/> type.
    /// </summary>
    /// <param name="type">IntervalDay type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(IntervalDay type, TContext context);

    /// <summary>
    /// Visits a <see cref="PrecisionTimestamp"/> type.
    /// </summary>
    /// <param name="type">PrecisionTimestamp type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(PrecisionTimestamp type, TContext context);

    /// <summary>
    /// Visits a <see cref="PrecisionTimestampTZ"/> type.
    /// </summary>
    /// <param name="type">PrecisionTimestampTZ type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(PrecisionTimestampTZ type, TContext context);

    /// <summary>
    /// Visits a <see cref="FixedChar"/> type.
    /// </summary>
    /// <param name="type">FixedChar type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(FixedChar type, TContext context);

    /// <summary>
    /// Visits a <see cref="VarChar"/> type.
    /// </summary>
    /// <param name="type">VarChar type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(VarChar type, TContext context);

    /// <summary>
    /// Visits a <see cref="FixedBinary"/> type.
    /// </summary>
    /// <param name="type">FixedBinary type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(FixedBinary type, TContext context);

    /// <summary>
    /// Visits a <see cref="Decimal"/> type.
    /// </summary>
    /// <param name="type">Decimal type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(ParameterizedType.Decimal type, TContext context);

    /// <summary>
    /// Visits a <see cref="Struct"/> type.
    /// </summary>
    /// <param name="type">Struct type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The output as a result of the visit.</returns>
    public abstract TOutput Visit(Struct type, TContext context);

    /// <inheritdoc/>
    public abstract TOutput Visit(IType other, TContext context);
}
