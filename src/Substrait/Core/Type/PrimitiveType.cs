// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Extension.Types;
using Substrait.Tools;
using Substrait.Tools.Visitor;
using static Substrait.Core.Type.IType;
using static Substrait.Tools.TypeUtils;

namespace Substrait.Core.Type;

/// <summary>
/// Base class for primitive types.
/// </summary>
public abstract class PrimitiveType : IType
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrimitiveType"/> class.
    /// </summary>
    /// <param name="nullable">Whether it is nullable.</param>
    /// <param name="typeVariation">variation of this type.</param>
    protected PrimitiveType(NullableType nullable, ITypeVariation? typeVariation = null)
    {
        this.Nullable = nullable;
        this.TypeVariation = typeVariation;
        if (typeVariation is not null && !typeVariation.IsCompatible(this))
        {
            throw new ArgumentException($"Type variation {typeVariation.Namespace}.{typeVariation.Name} requires {typeVariation.BaseTypeName} but got {this.GetType().Name}.");
        }
    }

    /// <inheritdoc/>
    public NullableType Nullable { get; }

    /// <inheritdoc/>
    public ITypeVariation? TypeVariation { get; }

    /// <inheritdoc/>
    public abstract string ShortTypeName { get; }

    /// <inheritdoc/>
    public virtual string TypeName => this.ShortTypeName;

    /// <inheritdoc/>
    public IEnumerable<IType> InputNodes => ImmutableList<IType>.Empty;

    /// <inheritdoc/>
    public abstract TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context);

    /// <inheritdoc/>
    public virtual string ToTypeString() => this.TypeName;

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.ShortTypeName}{(this.Nullable == NullableType.Nullable ? "?" : string.Empty)}";
    }

    /// <inheritdoc/>
    public virtual bool NodeEquals(IType other, ITypeComparison comparison)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (this.GetType() != other.GetType())
        {
            return false;
        }

        if (comparison.IsOn(ITypeComparison.Nullability) && this.Nullable != other.Nullable)
        {
            return false;
        }

        if (comparison.IsOn(ITypeComparison.TypeVariation) && !this.TypeVariation.EqualsWithNull(other.TypeVariation))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Immutable implementation of boolean type.
    /// </summary>
    public sealed class Bool : PrimitiveType
    {
        private static readonly Bool REQUIRED = new Bool(NullableType.Required, typeVariation: null);
        private static readonly Bool NULLABLE = new Bool(NullableType.Nullable, typeVariation: null);

        private Bool(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "bool";

        /// <inheritdoc/>
        public override string TypeName => "boolean";

        /// <summary>
        /// Gets an instance of <see cref="Bool"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="Bool"/> class.</returns>
        public static Bool Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new Bool(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of I8 type.
    /// </summary>
    public sealed class I8 : PrimitiveType
    {
        private static readonly I8 REQUIRED = new I8(NullableType.Required, typeVariation: null);
        private static readonly I8 NULLABLE = new I8(NullableType.Nullable, typeVariation: null);

        private I8(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "i8";

        /// <summary>
        /// Gets an instance of <see cref="I8"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="I8"/> class.</returns>
        public static I8 Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new I8(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of I16 type.
    /// </summary>
    public sealed class I16 : PrimitiveType
    {
        private static readonly I16 REQUIRED = new I16(NullableType.Required, typeVariation: null);
        private static readonly I16 NULLABLE = new I16(NullableType.Nullable, typeVariation: null);

        private I16(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "i16";

        /// <summary>
        /// Gets an of <see cref="I16"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="I16"/> class.</returns>
        public static I16 Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new I16(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of I32 type.
    /// </summary>
    public sealed class I32 : PrimitiveType
    {
        private static readonly I32 REQUIRED = new I32(NullableType.Required, typeVariation: null);
        private static readonly I32 NULLABLE = new I32(NullableType.Nullable, typeVariation: null);

        private I32(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "i32";

        /// <summary>
        /// Gets an instance of <see cref="I32"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="I32"/> class.</returns>
        public static I32 Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new I32(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of I64 type.
    /// </summary>
    public sealed class I64 : PrimitiveType
    {
        private static readonly I64 REQUIRED = new I64(NullableType.Required, typeVariation: null);
        private static readonly I64 NULLABLE = new I64(NullableType.Nullable, typeVariation: null);

        private I64(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "i64";

        /// <summary>
        /// Gets an instance of <see cref="I64"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="I64"/> class.</returns>
        public static I64 Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new I64(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of FP32 type.
    /// </summary>
    public sealed class FP32 : PrimitiveType
    {
        private static readonly FP32 REQUIRED = new FP32(NullableType.Required, typeVariation: null);
        private static readonly FP32 NULLABLE = new FP32(NullableType.Nullable, typeVariation: null);

        private FP32(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "fp32";

        /// <summary>
        /// Gets an instance of <see cref="FP32"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="FP32"/> class.</returns>
        public static FP32 Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new FP32(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of FP64 type.
    /// </summary>
    public sealed class FP64 : PrimitiveType
    {
        private static readonly FP64 REQUIRED = new FP64(NullableType.Required, typeVariation: null);
        private static readonly FP64 NULLABLE = new FP64(NullableType.Nullable, typeVariation: null);

        private FP64(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "fp64";

        /// <summary>
        /// Gets an instance of <see cref="FP64"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="FP64"/> class.</returns>
        public static FP64 Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new FP64(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of string type.
    /// </summary>
    public sealed class Str : PrimitiveType
    {
        private static readonly Str REQUIRED = new Str(NullableType.Required, typeVariation: null);
        private static readonly Str NULLABLE = new Str(NullableType.Nullable, typeVariation: null);

        private Str(NullableType nullable, ITypeVariation? typeVariation = null)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "str";

        /// <inheritdoc/>
        public override string TypeName => "string";

        /// <summary>
        /// Gets an instance of <see cref="Str"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="Str"/> class.</returns>
        public static Str Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new Str(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of binary type.
    /// </summary>
    public sealed class Binary : PrimitiveType
    {
        private static readonly Binary REQUIRED = new Binary(NullableType.Required, typeVariation: null);
        private static readonly Binary NULLABLE = new Binary(NullableType.Nullable, typeVariation: null);

        private Binary(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "vbin";

        /// <inheritdoc/>
        public override string TypeName => "binary";

        /// <summary>
        /// Gets an instance of <see cref="Binary"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="Binary"/> class.</returns>
        public static Binary Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation == null)
            {
                return new Binary(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of date type.
    /// </summary>
    public sealed class Date : PrimitiveType
    {
        private static readonly Date REQUIRED = new Date(NullableType.Required, typeVariation: null);
        private static readonly Date NULLABLE = new Date(NullableType.Nullable, typeVariation: null);

        private Date(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "date";

        /// <summary>
        /// Gets an instance of <see cref="Date"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="Date"/> class.</returns>
        public static Date Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new Date(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of UUID type.
    /// </summary>
    public sealed class Uuid : PrimitiveType, IEquatable<Uuid>
    {
        private static readonly Uuid REQUIRED = new(NullableType.Required, null);
        private static readonly Uuid NULLABLE = new(NullableType.Nullable, null);

        private Uuid(NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "uuid";

        /// <summary>Gets a UUID type with the specified nullability and variation.</summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The UUID type.</returns>
        public static Uuid Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            return nullable switch
            {
                NullableType.Required => typeVariation is null ? REQUIRED : new Uuid(nullable, typeVariation),
                NullableType.Nullable => typeVariation is null ? NULLABLE : new Uuid(nullable, typeVariation),
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public bool Equals(Uuid? other) => other is not null && this.NodeEquals(other, ITypeComparison.Strict);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Uuid other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(this.ShortTypeName, this.Nullable, this.TypeVariation);
    }

    /// <summary>
    /// Immutable implementation of time type, defaulting to microsecond precision.
    /// </summary>
    public sealed class Time : PrimitiveType, IEquatable<Time>
    {
        private static readonly Time REQUIRED = new Time(6, NullableType.Required, typeVariation: null);
        private static readonly Time NULLABLE = new Time(6, NullableType.Nullable, typeVariation: null);

        private Time(int precision, NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
            ParameterizedType.ValidatePrecision(precision);
            this.Precision = precision;
        }

        /// <summary>Gets the number of fractional second digits, from 0 through 12.</summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override string ShortTypeName => "time";

        /// <summary>
        /// Gets an instance of <see cref="Time"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="Time"/> class.</returns>
        public static Time Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            return Of(6, nullable, typeVariation);
        }

        /// <summary>Gets a time type with explicit precision.</summary>
        /// <param name="precision">Fractional second digits, from 0 through 12.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The time type.</returns>
        public static Time Of(int precision, NullableType nullable, ITypeVariation? typeVariation = null)
        {
            return nullable switch
            {
                NullableType.Required => precision == 6 && typeVariation is null ? REQUIRED : new Time(precision, nullable, typeVariation),
                NullableType.Nullable => precision == 6 && typeVariation is null ? NULLABLE : new Time(precision, nullable, typeVariation),
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override string ToTypeString() => this.Precision == 6 ? base.ToTypeString() : $"precision_time<{this.Precision}>";

        /// <inheritdoc/>
        public override string ToString() => this.Precision == 6 ? base.ToString() : $"pt{(this.Nullable == NullableType.Nullable ? "?" : string.Empty)}<{this.Precision}>";

        /// <inheritdoc/>
        public override bool NodeEquals(IType other, ITypeComparison comparison) =>
            base.NodeEquals(other, comparison) && (!comparison.IsOn(ITypeComparison.TypeParameter) || this.Precision == ((Time)other).Precision);

        /// <inheritdoc/>
        public bool Equals(Time? other) => other is not null && this.NodeEquals(other, ITypeComparison.Strict);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Time other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(this.ShortTypeName, this.Nullable, this.Precision, this.TypeVariation);

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of interval year type.
    /// </summary>
    public sealed class IntervalYear : PrimitiveType
    {
        private static readonly IntervalYear REQUIRED = new IntervalYear(NullableType.Required, typeVariation: null);
        private static readonly IntervalYear NULLABLE = new IntervalYear(NullableType.Nullable, typeVariation: null);

        private IntervalYear(NullableType nullable, ITypeVariation? typeVariation = null)
            : base(nullable, typeVariation)
        {
        }

        /// <inheritdoc/>
        public override string ShortTypeName => "iyear";

        /// <inheritdoc/>
        public override string TypeName => "interval_year";

        /// <summary>
        /// Gets an instance of <see cref="IntervalYear"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="IntervalYear"/> class.</returns>
        public static IntervalYear Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            if (typeVariation is not null)
            {
                return new IntervalYear(nullable, typeVariation);
            }

            return nullable switch
            {
                NullableType.Required => REQUIRED,
                NullableType.Nullable => NULLABLE,
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }

    /// <summary>
    /// Immutable implementation of interval day type, defaulting to second precision.
    /// </summary>
    public sealed class IntervalDay : PrimitiveType, IEquatable<IntervalDay>
    {
        private static readonly IntervalDay REQUIRED = new IntervalDay(0, NullableType.Required, typeVariation: null);
        private static readonly IntervalDay NULLABLE = new IntervalDay(0, NullableType.Nullable, typeVariation: null);

        private IntervalDay(int precision, NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
            ParameterizedType.ValidatePrecision(precision);
            this.Precision = precision;
        }

        /// <summary>Gets the number of fractional second digits, from 0 through 12.</summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override string ShortTypeName => "iday";

        /// <inheritdoc/>
        public override string TypeName => "interval_day";

        /// <summary>
        /// Gets an instance of <see cref="IntervalDay"/> class.
        /// </summary>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The instance of <see cref="IntervalDay"/> class.</returns>
        public static IntervalDay Of(NullableType nullable, ITypeVariation? typeVariation = null)
        {
            return Of(0, nullable, typeVariation);
        }

        /// <summary>Gets a day interval type with explicit precision.</summary>
        /// <param name="precision">Fractional second digits, from 0 through 12.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        /// <returns>The day interval type.</returns>
        public static IntervalDay Of(int precision, NullableType nullable, ITypeVariation? typeVariation = null)
        {
            return nullable switch
            {
                NullableType.Required => precision == 0 && typeVariation is null ? REQUIRED : new IntervalDay(precision, nullable, typeVariation),
                NullableType.Nullable => precision == 0 && typeVariation is null ? NULLABLE : new IntervalDay(precision, nullable, typeVariation),
                _ => throw new NotImplementedException(nullable.ToString()),
            };
        }

        /// <inheritdoc/>
        public override string ToTypeString() => this.Precision == 0 ? base.ToTypeString() : $"{this.TypeName}<{this.Precision}>";

        /// <inheritdoc/>
        public override string ToString() => this.Precision == 0 ? base.ToString() : $"{base.ToString()}<{this.Precision}>";

        /// <inheritdoc/>
        public override bool NodeEquals(IType other, ITypeComparison comparison) =>
            base.NodeEquals(other, comparison) && (!comparison.IsOn(ITypeComparison.TypeParameter) || this.Precision == ((IntervalDay)other).Precision);

        /// <inheritdoc/>
        public bool Equals(IntervalDay? other) => other is not null && this.NodeEquals(other, ITypeComparison.Strict);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is IntervalDay other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(this.ShortTypeName, this.Nullable, this.Precision, this.TypeVariation);

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);
    }
}
