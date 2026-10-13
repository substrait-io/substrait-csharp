// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Extension.Types;
using Substrait.Tools;
using static Substrait.Core.Type.IType;

namespace Substrait.Core.Type;

/// <summary>
/// Type Factory.
/// </summary>
public class TypeFactory : PrimitiveTypeFactory
{
    /// <summary>
    /// Creator for non-nullable types.
    /// </summary>
    public static new readonly TypeFactory REQUIRED = new(NullableType.Required);

    /// <summary>
    /// Creator for nullable types.
    /// </summary>
    public static new readonly TypeFactory NULLABLE = new(NullableType.Nullable);

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeFactory"/> class.
    /// </summary>
    /// <param name="nullable">Whether the created type is nullable.</param>
    protected TypeFactory(NullableType nullable)
        : base(nullable)
    {
    }

    /// <summary>Creates an extension-defined type with ordered parameters.</summary>
    public UserDefinedType UserDefined(
        TypeAnchor anchor,
        IEnumerable<TypeParameter> parameters,
        ITypeVariation? typeVariation = null,
        TypeDefinition? declaration = null) =>
        new(anchor, parameters, this.Nullable, typeVariation, declaration);

    /// <summary>
    /// Returns the type creator for the specified nullability.
    /// </summary>
    /// <param name="nullable">Whether the created types should be nullable.</param>
    /// <returns>The type creator.</returns>
    public static new TypeFactory Of(NullableType nullable)
    {
        return nullable switch
        {
            NullableType.Required => REQUIRED,
            NullableType.Nullable => NULLABLE,
            _ => throw new NotImplementedException(nullable.ToString()),
        };
    }

    /// <summary>Returns a time type with explicit precision.</summary>
    /// <param name="precision">Fractional second digits, from 0 through 12.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The time type.</returns>
    public PrimitiveType.Time PrecisionTime(int precision, ITypeVariation? typeVariation = null)
    {
        return PrimitiveType.Time.Of(precision, this.Nullable, typeVariation);
    }

    /// <summary>Returns a day interval type with explicit precision.</summary>
    /// <param name="precision">Fractional second digits, from 0 through 12.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The day interval type.</returns>
    public PrimitiveType.IntervalDay IntervalDay(int precision, ITypeVariation? typeVariation = null)
    {
        return PrimitiveType.IntervalDay.Of(precision, this.Nullable, typeVariation);
    }

    /// <summary>Returns a compound interval type with explicit precision.</summary>
    /// <param name="precision">Fractional second digits, from 0 through 12.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The compound interval type.</returns>
    public ParameterizedType.IntervalCompound IntervalCompound(int precision, ITypeVariation? typeVariation = null)
    {
        return new ParameterizedType.IntervalCompound(precision, this.Nullable, typeVariation);
    }

    /// <summary>
    /// PrecisionTimestamp.
    /// </summary>
    /// <param name="precision">The precision of the timestamp.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The timestamp type.</returns>
    public ParameterizedType.PrecisionTimestamp PrecisionTimestamp(int precision, ITypeVariation? typeVariation = null)
    {
        return new ParameterizedType.PrecisionTimestamp(precision, this.Nullable, typeVariation);
    }

    /// <summary>
    /// PrecisionTimestampTZ.
    /// </summary>
    /// <param name="precision">The precision of the timestamp.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The timestamp type.</returns>
    public ParameterizedType.PrecisionTimestampTZ PrecisionTimestampTZ(int precision, ITypeVariation? typeVariation = null)
    {
        return new ParameterizedType.PrecisionTimestampTZ(precision, this.Nullable, typeVariation);
    }

    /// <summary>
    /// FixedChar.
    /// </summary>
    /// <param name="len">The length of the fixed char.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The fixed char type.</returns>
    public ParameterizedType.FixedChar FixedChar(int len, ITypeVariation? typeVariation = null)
    {
        return new ParameterizedType.FixedChar(len, this.Nullable, typeVariation);
    }

    /// <summary>
    /// VarChar.
    /// </summary>
    /// <param name="len">The length of the varchar.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The varchar type.</returns>
    public ParameterizedType.VarChar VarChar(int len, ITypeVariation? typeVariation = null)
    {
        return new ParameterizedType.VarChar(len, this.Nullable, typeVariation);
    }

    /// <summary>
    /// FixedBinary.
    /// </summary>
    /// <param name="len">The length of the fixed binary.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The fixed binary type.</returns>
    public ParameterizedType.FixedBinary FixedBinary(int len, ITypeVariation? typeVariation = null)
    {
        return new ParameterizedType.FixedBinary(len, this.Nullable, typeVariation);
    }

    /// <summary>
    /// Decimal.
    /// </summary>
    /// <param name="precision">The precision of the decimal.</param>
    /// <param name="scale">The scale of the decimal.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The decimal type.</returns>
    public ParameterizedType.Decimal Decimal(int precision, int scale, ITypeVariation? typeVariation = null)
    {
        return new ParameterizedType.Decimal(precision, scale, this.Nullable, typeVariation);
    }

    /// <summary>
    /// Struct.
    /// </summary>
    /// <param name="fields">The field types in the struct.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The struct type.</returns>
    public ParameterizedType.Struct Struct(IEnumerable<IType> fields, ITypeVariation? typeVariation = null)
    {
        return new ParameterizedType.Struct(fields, this.Nullable, typeVariation);
    }

    /// <summary>
    /// Combines multiple lists of fields into a single ParameterizedType.Struct.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <param name="fields">The field types in the struct, passed as multiple lists.</param>
    /// <returns>A new ParameterizedType.Struct containing the combined fields from the input lists.</returns>
    public ParameterizedType.Struct Struct(ITypeVariation? typeVariation, params IEnumerable<IType>[] fields)
    {
        var builder = ImmutableList.CreateBuilder<IType>();
        foreach (var fieldList in fields)
        {
            builder.AddRange(fieldList);
        }

        return this.Struct(builder.ToImmutable(), typeVariation);
    }

    /// <summary>
    /// Combines multiple lists of fields into a single ParameterizedType.Struct.
    /// </summary>
    /// <param name="fields">The field types in the struct, passed as multiple lists.</param>
    /// <returns>A new ParameterizedType.Struct containing the combined fields from the input lists.</returns>
    public ParameterizedType.Struct Struct(params IEnumerable<IType>[] fields)
    {
        return this.Struct(null, fields);
    }

    /// <summary>
    /// Returns the input type as nullable.
    /// </summary>
    /// <param name="type">The input type.</param>
    /// <returns>The same input type as nullable.</returns>
    public IType ResolveTypeWithNullability(IType type)
    {
        return this.ResolveTypeWithNullability(type, type.TypeVariation);
    }

    /// <summary>
    /// Returns the input type as nullable and <paramref name="typeVariation"/>.
    /// </summary>
    /// <param name="type">The input type.</param>
    /// <param name="typeVariation">The type variation to apply.</param>
    /// <returns>The same input type as nullable with <paramref name="typeVariation"/>.</returns>
    public IType ResolveTypeWithNullability(IType type, ITypeVariation? typeVariation)
    {
        // Check if the input type matches the factory nullability, return it unchanged.
        if (type.Nullable == this.Nullable && typeVariation.EqualsWithNull(type.TypeVariation))
        {
            return type;
        }

        if (type is UserDefinedType userDefined)
        {
            return this.UserDefined(userDefined.Anchor, userDefined.Parameters, typeVariation, userDefined.Declaration);
        }

        // Handle Primitive types
        if (type is PrimitiveType primitiveType)
        {
            return this.ResolvePrimitiveTypeWithNullability(primitiveType, typeVariation);
        }

        // Handle Parameterized types
        if (type is ParameterizedType parameterizedType)
        {
            switch (parameterizedType)
            {
                case ParameterizedType.IntervalCompound interval:
                    return this.IntervalCompound(interval.Precision, typeVariation);

                case ParameterizedType.PrecisionTimestamp pts:
                    return this.PrecisionTimestamp(pts.Precision, typeVariation);

                case ParameterizedType.PrecisionTimestampTZ ptsz:
                    return this.PrecisionTimestampTZ(ptsz.Precision, typeVariation);

                case ParameterizedType.FixedChar fc:
                    return this.FixedChar(fc.Length, typeVariation);

                case ParameterizedType.VarChar vc:
                    return this.VarChar(vc.Length, typeVariation);

                case ParameterizedType.FixedBinary fb:
                    return this.FixedBinary(fb.Length, typeVariation);

                case ParameterizedType.Decimal dec:
                    return this.Decimal(dec.Precision, dec.Scale, typeVariation);

                case ParameterizedType.Struct str:
                    return this.Struct(str.Fields, typeVariation);
            }
        }

        throw new NotImplementedException($"Unsupported type: {type.GetType()}");
    }
}
