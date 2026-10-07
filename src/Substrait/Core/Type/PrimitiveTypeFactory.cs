// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Extension.Types;
using Substrait.Tools;
using static Substrait.Core.Type.IType;

namespace Substrait.Core.Type;

/// <summary>
/// Parameterized Type Factory.
/// </summary>
public class PrimitiveTypeFactory
{
    /// <summary>
    /// Creator for non-nullable parameterized types.
    /// </summary>
    public static readonly PrimitiveTypeFactory REQUIRED = new PrimitiveTypeFactory(NullableType.Required);

    /// <summary>
    /// Creator for nullable parameterized types.
    /// </summary>
    public static readonly PrimitiveTypeFactory NULLABLE = new PrimitiveTypeFactory(NullableType.Nullable);

    /// <summary>
    /// Initializes a new instance of the <see cref="PrimitiveTypeFactory"/> class.
    /// </summary>
    /// <param name="nullable">Whether the created types are nullable.</param>
    protected PrimitiveTypeFactory(NullableType nullable)
    {
        this.Nullable = nullable;
        this.BOOL = PrimitiveType.Bool.Of(nullable);
        this.I8 = PrimitiveType.I8.Of(nullable);
        this.I16 = PrimitiveType.I16.Of(nullable);
        this.I32 = PrimitiveType.I32.Of(nullable);
        this.I64 = PrimitiveType.I64.Of(nullable);
        this.FP32 = PrimitiveType.FP32.Of(nullable);
        this.FP64 = PrimitiveType.FP64.Of(nullable);
        this.STR = PrimitiveType.Str.Of(nullable);
        this.BINARY = PrimitiveType.Binary.Of(nullable);
        this.DATE = PrimitiveType.Date.Of(nullable);
        this.UUID = PrimitiveType.Uuid.Of(nullable);
        this.TIME = PrimitiveType.Time.Of(nullable);
        this.INTERVAL_DAY = PrimitiveType.IntervalDay.Of(nullable);
        this.INTERVAL_YEAR = PrimitiveType.IntervalYear.Of(nullable);
    }

    /// <summary>
    /// Gets a boolean type.
    /// </summary>
    public PrimitiveType.Bool BOOL { get; }

    /// <summary>
    /// Gets an I8 type.
    /// </summary>
    public PrimitiveType.I8 I8 { get; }

    /// <summary>
    /// Gets an I16 type.
    /// </summary>
    public PrimitiveType.I16 I16 { get; }

    /// <summary>
    /// Gets an I32 type.
    /// </summary>
    public PrimitiveType.I32 I32 { get; }

    /// <summary>
    /// Gets an I64 type.
    /// </summary>
    public PrimitiveType.I64 I64 { get; }

    /// <summary>
    /// Gets an FP32 type.
    /// </summary>
    public PrimitiveType.FP32 FP32 { get; }

    /// <summary>
    /// Gets an FP64 type.
    /// </summary>
    public PrimitiveType.FP64 FP64 { get; }

    /// <summary>
    /// Gets a string type.
    /// </summary>
    public PrimitiveType.Str STR { get; }

    /// <summary>
    /// Gets a binary type.
    /// </summary>
    public PrimitiveType.Binary BINARY { get; }

    /// <summary>
    /// Gets a date type.
    /// </summary>
    public PrimitiveType.Date DATE { get; }

    /// <summary>Gets a UUID type.</summary>
    public PrimitiveType.Uuid UUID { get; }

    /// <summary>
    /// Gets a time type.
    /// </summary>
    public PrimitiveType.Time TIME { get; }

    /// <summary>
    /// Gets an interval day type.
    /// </summary>
    public PrimitiveType.IntervalDay INTERVAL_DAY { get; }

    /// <summary>
    /// Gets an interval year type.
    /// </summary>
    public PrimitiveType.IntervalYear INTERVAL_YEAR { get; }

    /// <summary>
    /// Gets a value indicating whether the created types are nullable.
    /// </summary>
    protected NullableType Nullable { get; }

    /// <summary>
    /// Returns the primitive type created with the given nullability.
    /// </summary>
    /// <param name="nullable">Whether the created primitive type is nullable.</param>
    /// <returns>The creator for the primitive type.</returns>
    public static PrimitiveTypeFactory Of(NullableType nullable)
    {
        return nullable switch
        {
            NullableType.Required => REQUIRED,
            NullableType.Nullable => NULLABLE,
            _ => throw new NotImplementedException(nullable.ToString()),
        };
    }

    /// <summary>
    /// Resolves the corresponding primitive type based on the given <see cref="PrimitiveType"/>,
    /// applying the nullability rules defined by this factory instance.
    /// </summary>
    /// <param name="type">The primitive type to resolve.</param>
    /// <returns>The resolved primitive type with the appropriate nullability applied.</returns>
    /// <exception cref="NotImplementedException">Thrown if the provided primitive type is not supported by the factory.</exception>
    public IType ResolvePrimitiveTypeWithNullability(PrimitiveType type)
    {
        return this.ResolvePrimitiveTypeWithNullability(type, type.TypeVariation);
    }

    /// <summary>
    /// Resolves the corresponding primitive type based on the given <see cref="PrimitiveType"/>,
    /// applying the nullability rules defined by this factory instance and <paramref name="typeVariation"/>.
    /// </summary>
    /// <param name="type">The primitive type to resolve.</param>
    /// <param name="typeVariation">The type variation to apply.</param>
    /// <returns>The resolved primitive type with the appropriate nullability applied.</returns>
    /// <exception cref="NotImplementedException">Thrown if the provided primitive type is not supported by the factory.</exception>
    public IType ResolvePrimitiveTypeWithNullability(PrimitiveType type, ITypeVariation? typeVariation)
    {
        if (type.Nullable == this.Nullable && typeVariation.EqualsWithNull(type.TypeVariation))
        {
            return type;
        }

        if (typeVariation is not null)
        {
            return type switch
            {
                PrimitiveType.Bool => PrimitiveType.Bool.Of(this.Nullable, typeVariation),
                PrimitiveType.I8 => PrimitiveType.I8.Of(this.Nullable, typeVariation),
                PrimitiveType.I16 => PrimitiveType.I16.Of(this.Nullable, typeVariation),
                PrimitiveType.I32 => PrimitiveType.I32.Of(this.Nullable, typeVariation),
                PrimitiveType.I64 => PrimitiveType.I64.Of(this.Nullable, typeVariation),
                PrimitiveType.FP32 => PrimitiveType.FP32.Of(this.Nullable, typeVariation),
                PrimitiveType.FP64 => PrimitiveType.FP64.Of(this.Nullable, typeVariation),
                PrimitiveType.Str => PrimitiveType.Str.Of(this.Nullable, typeVariation),
                PrimitiveType.Binary => PrimitiveType.Binary.Of(this.Nullable, typeVariation),
                PrimitiveType.Date => PrimitiveType.Date.Of(this.Nullable, typeVariation),
                PrimitiveType.Uuid => PrimitiveType.Uuid.Of(this.Nullable, typeVariation),
                PrimitiveType.Time time => PrimitiveType.Time.Of(time.Precision, this.Nullable, typeVariation),
                PrimitiveType.IntervalDay interval => PrimitiveType.IntervalDay.Of(interval.Precision, this.Nullable, typeVariation),
                PrimitiveType.IntervalYear => PrimitiveType.IntervalYear.Of(this.Nullable, typeVariation),
                _ => throw new NotImplementedException($"Unsupported primitive type: {type.GetType().Name}"),
            };
        }

        return type switch
        {
            PrimitiveType.Bool => this.BOOL,
            PrimitiveType.I8 => this.I8,
            PrimitiveType.I16 => this.I16,
            PrimitiveType.I32 => this.I32,
            PrimitiveType.I64 => this.I64,
            PrimitiveType.FP32 => this.FP32,
            PrimitiveType.FP64 => this.FP64,
            PrimitiveType.Str => this.STR,
            PrimitiveType.Binary => this.BINARY,
            PrimitiveType.Date => this.DATE,
            PrimitiveType.Uuid => this.UUID,
            PrimitiveType.Time time => PrimitiveType.Time.Of(time.Precision, this.Nullable),
            PrimitiveType.IntervalDay interval => PrimitiveType.IntervalDay.Of(interval.Precision, this.Nullable),
            PrimitiveType.IntervalYear => this.INTERVAL_YEAR,
            _ => throw new NotImplementedException($"Unsupported primitive type: {type.GetType().Name}"),
        };
    }

    /// <summary>
    /// Returns a BOOL with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>BOOL type with type variation.</returns>
    public PrimitiveType.Bool Boolean_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.Bool.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an I8 type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>I8 type with type variation.</returns>
    public PrimitiveType.I8 I8_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.I8.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an I16 type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>I8 type with type variation.</returns>
    public PrimitiveType.I16 I16_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.I16.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an I32 type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>I32 type with type variation.</returns>
    public PrimitiveType.I32 I32_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.I32.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an I64 type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>I64 type with type variation.</returns>
    public PrimitiveType.I64 I64_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.I64.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an FP32 type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>FP32 type with type variation.</returns>
    public PrimitiveType.FP32 FP32_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.FP32.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an FP64 type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>FP64 type with type variation.</returns>
    public PrimitiveType.FP64 FP64_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.FP64.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an String type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>String type with type variation.</returns>
    public PrimitiveType.Str String_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.Str.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an Binary type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>Binary type with type variation.</returns>
    public PrimitiveType.Binary Binary_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.Binary.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an Date type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>Date type with type variation.</returns>
    public PrimitiveType.Date Date_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.Date.Of(this.Nullable, typeVariation);
    }

    /// <summary>Returns a UUID type with a type variation.</summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>The UUID type.</returns>
    public PrimitiveType.Uuid Uuid_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.Uuid.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an Time type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>Time type with type variation.</returns>
    public PrimitiveType.Time Time_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.Time.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an IntervalDay type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>IntervalDay type with type variation.</returns>
    public PrimitiveType.IntervalDay IntervalDay_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.IntervalDay.Of(this.Nullable, typeVariation);
    }

    /// <summary>
    /// Returns an IntervalYear type with type variation.
    /// </summary>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>IntervalYear type with type variation.</returns>
    public PrimitiveType.IntervalYear IntervalYear_(ITypeVariation? typeVariation)
    {
        return PrimitiveType.IntervalYear.Of(this.Nullable, typeVariation);
    }
}
