// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Tools.Visitor;
using static Substrait.Core.Type.ParameterizedType;
using static Substrait.Core.Type.PrimitiveType;

namespace Substrait.Core.Type;

/// <summary>
/// Visitor for processing types in a tree. Every type-specific overload falls back to
/// <see cref="Visit(IType, TContext)"/>, the single required override point: implement it alone
/// for uniform handling, or override individual overloads for type-specific behavior.
/// </summary>
/// <typeparam name="TContext">The type of the context used during visitation.</typeparam>
/// <typeparam name="TOutput">The type of the output produced by the visitation.</typeparam>
public abstract class TypeVisitor<TContext, TOutput> : IVisitor<IType, TContext, TOutput>
{
    /// <summary>Visits an extension-defined type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(UserDefinedType type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="Bool"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(Bool type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits an <see cref="I8"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(I8 type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits an <see cref="I16"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(I16 type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits an <see cref="I32"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(I32 type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits an <see cref="I64"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(I64 type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits an <see cref="FP32"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(FP32 type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits an <see cref="FP64"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(FP64 type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="Str"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(Str type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="Binary"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(Binary type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="Date"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(Date type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a UUID type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(Uuid type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a compound interval, falling back to the general type handler.</summary>
    public virtual TOutput Visit(IntervalCompound type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="Time"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(Time type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits an <see cref="IntervalYear"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(IntervalYear type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits an <see cref="IntervalDay"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(IntervalDay type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="PrecisionTimestamp"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(PrecisionTimestamp type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="PrecisionTimestampTZ"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(PrecisionTimestampTZ type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="FixedChar"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(FixedChar type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="VarChar"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(VarChar type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="FixedBinary"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(FixedBinary type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="Decimal"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(ParameterizedType.Decimal type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a <see cref="Struct"/> type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(Struct type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a list type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(List type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a map type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(Map type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits an alias reference type, falling back to the general type handler.</summary>
    /// <param name="type">Alias reference type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The result of the visit.</returns>
    public virtual TOutput Visit(AliasReference type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits a function type, falling back to the general type handler.</summary>
    /// <param name="type">Function type.</param>
    /// <param name="context">Input context.</param>
    /// <returns>The result of the visit.</returns>
    public virtual TOutput Visit(Func type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits the unbound placeholder type, falling back to the general type handler.</summary>
    public virtual TOutput Visit(UnboundType type, TContext context) => this.Visit((IType)type, context);

    /// <summary>Visits any type not handled by a more specific overload. The single required override.</summary>
    public abstract TOutput Visit(IType other, TContext context);
}
