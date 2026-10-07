// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;
using Substrait.Core.Extension.Types;
using Substrait.Tools;
using static Substrait.Core.Type.IType;
using static Substrait.Tools.TypeUtils;

namespace Substrait.Core.Type;

/// <summary>
/// Base class for parameterized types.
/// </summary>
public abstract class ParameterizedType : IType
{
    /// <summary>
    /// The maximum sub-second precision (picoseconds) a temporal type may declare.
    /// </summary>
    public const int MaxSubsecondPrecision = 12;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParameterizedType"/> class.
    /// </summary>
    /// <param name="nullable">Whether it is nullable.</param>
    /// <param name="typeVariation">Variation of this type.</param>
    protected ParameterizedType(NullableType nullable, ITypeVariation? typeVariation = null)
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
    public virtual IEnumerable<IType> InputNodes => ImmutableList<IType>.Empty;

    /// <summary>
    /// Gets a value indicating whether the parameterized type is a wildcard.
    /// </summary>
    public virtual bool IsWildcard => false;

    /// <inheritdoc/>
    public ITypeVariation? TypeVariation { get; }

    /// <inheritdoc/>
    public abstract string ShortTypeName { get; }

    /// <inheritdoc/>
    public abstract string TypeName { get; }

    /// <inheritdoc/>
    public abstract TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context);

    /// <inheritdoc/>
    public bool NodeEquals(IType other, ITypeComparison comparison)
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

        if (comparison.IsOn(ITypeComparison.TypeParameter) && !this.NodeEqualTypeParameters(other))
        {
            return false;
        }

        if (comparison.IsOn(ITypeComparison.TypeVariation) && !this.TypeVariation.EqualsWithNull(other.TypeVariation))
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public abstract string ToTypeString();

    /// <summary>
    /// Checks whether <paramref name="other"/> has equal type parameters except nested type parameters.
    /// </summary>
    /// <param name="other">other type.</param>
    /// <returns>true if <paramref name="other"/> has equal non-nested type parameters.</returns>
    protected abstract bool NodeEqualTypeParameters(IType other);

    internal static void ValidatePrecision(int precision)
    {
        if (precision < 0 || precision > MaxSubsecondPrecision)
        {
            throw new ArgumentOutOfRangeException(nameof(precision), precision, $"0-{MaxSubsecondPrecision} (seconds to picoseconds)");
        }
    }

    /// <summary>Immutable compound interval with explicit fractional second precision.</summary>
    public sealed class IntervalCompound : ParameterizedType, IEquatable<IntervalCompound>
    {
        /// <summary>Initializes a new instance of the <see cref="IntervalCompound"/> class.</summary>
        /// <param name="precision">Fractional second digits, from 0 through 12.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        public IntervalCompound(int precision, NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
            ValidatePrecision(precision);
            this.Precision = precision;
        }

        /// <summary>Gets the number of fractional second digits.</summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override string ShortTypeName => "icompound";

        /// <inheritdoc/>
        public override string TypeName => "interval_compound";

        /// <inheritdoc/>
        public override string ToTypeString() => $"{this.TypeName}<{this.Precision}>";

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public bool Equals(IntervalCompound? other) => other is not null && this.NodeEquals(other, ITypeComparison.Strict);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is IntervalCompound other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(this.ShortTypeName, this.Nullable, this.Precision, this.TypeVariation);

        /// <inheritdoc/>
        protected override bool NodeEqualTypeParameters(IType other) => other is IntervalCompound interval && this.Precision == interval.Precision;
    }

    /// <summary>
    /// Immutable implementation of PrecisionTimestamp type.
    /// </summary>
    public sealed class PrecisionTimestamp : ParameterizedType, IEquatable<PrecisionTimestamp>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrecisionTimestamp"/> class.
        /// </summary>
        /// <param name="precision">Precision of the timestamp.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        public PrecisionTimestamp(int precision, NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
            ValidatePrecision(precision);
            this.Precision = precision;
        }

        /// <summary>
        /// Gets precision.
        /// </summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override string ShortTypeName => "pts";

        /// <inheritdoc/>
        public override string TypeName => "precision_timestamp";

        /// <inheritdoc/>
        public override string ToTypeString() => $"{this.TypeName}<{this.Precision}>";

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public bool Equals(PrecisionTimestamp? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Precision == other?.Precision && this.Nullable == other?.Nullable;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as PrecisionTimestamp);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.ShortTypeName, this.Nullable, this.Precision);
        }

        /// <inheritdoc/>
        protected override bool NodeEqualTypeParameters(IType other)
        {
            return other is PrecisionTimestamp p && this.Precision == p.Precision;
        }
    }

    /// <summary>
    /// Immutable implementation of PrecisionTimestampTZ type.
    /// </summary>
    public sealed class PrecisionTimestampTZ : ParameterizedType, IEquatable<PrecisionTimestampTZ>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrecisionTimestampTZ"/> class.
        /// </summary>
        /// <param name="precision">Precision of the timestamp.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        public PrecisionTimestampTZ(int precision, NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
            ValidatePrecision(precision);
            this.Precision = precision;
        }

        /// <summary>
        /// Gets precision.
        /// </summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override string ShortTypeName => "ptstz";

        /// <inheritdoc/>
        public override string TypeName => "precision_timestamp_tz";

        /// <inheritdoc/>
        public override string ToTypeString() => $"{this.TypeName}<{this.Precision}>";

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public bool Equals(PrecisionTimestampTZ? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Precision == other?.Precision && this.Nullable == other?.Nullable;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as PrecisionTimestampTZ);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.ShortTypeName, this.Nullable, this.Precision);
        }

        /// <inheritdoc/>
        protected override bool NodeEqualTypeParameters(IType other)
        {
            return other is PrecisionTimestampTZ p && this.Precision == p.Precision;
        }
    }

    /// <summary>
    /// Immutable implementation of FixedChar type.
    /// </summary>
    public sealed class FixedChar : ParameterizedType, IEquatable<FixedChar>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FixedChar"/> class.
        /// </summary>
        /// <param name="length">Length of the fixed char.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        public FixedChar(int length, NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "> 0");
            }

            this.Length = length;
        }

        /// <summary>
        /// Gets length.
        /// </summary>
        public int Length { get; }

        /// <inheritdoc/>
        public override string ShortTypeName => "fchar";

        /// <inheritdoc/>
        public override string TypeName => "fixedchar";

        /// <inheritdoc/>
        public override string ToTypeString() => $"{this.TypeName}<{this.Length}>";

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public bool Equals(FixedChar? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Length == other?.Length && this.Nullable == other?.Nullable;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as FixedChar);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.ShortTypeName, this.Nullable, this.Length);
        }

        /// <inheritdoc/>
        protected override bool NodeEqualTypeParameters(IType other)
        {
            return other is FixedChar t && this.Length == t.Length;
        }
    }

    /// <summary>
    /// Immutable implementation of VarChar type.
    /// </summary>
    public sealed class VarChar : ParameterizedType, IEquatable<VarChar>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VarChar"/> class.
        /// </summary>
        /// <param name="length">The length of the varchar.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        public VarChar(int length, NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "> 0");
            }

            this.Length = length;
        }

        /// <summary>
        /// Gets length.
        /// </summary>
        public int Length { get; }

        /// <inheritdoc/>
        public override string ShortTypeName => "vchar";

        /// <inheritdoc/>
        public override string TypeName => "varchar";

        /// <inheritdoc/>
        public override string ToTypeString() => $"{this.TypeName}<{this.Length}>";

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public bool Equals(VarChar? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Length == other?.Length && this.Nullable == other?.Nullable;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as VarChar);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.ShortTypeName, this.Nullable, this.Length);
        }

        /// <inheritdoc/>
        protected override bool NodeEqualTypeParameters(IType other)
        {
            return other is VarChar t && this.Length == t.Length;
        }
    }

    /// <summary>
    /// Immutable implementation of FixedBinary type.
    /// </summary>
    public sealed class FixedBinary : ParameterizedType, IEquatable<FixedBinary>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FixedBinary"/> class.
        /// </summary>
        /// <param name="length">Length of the fixed binary.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        public FixedBinary(int length, NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "> 0");
            }

            this.Length = length;
        }

        /// <summary>
        /// Gets length.
        /// </summary>
        public int Length { get; }

        /// <inheritdoc/>
        public override string ShortTypeName => "fbinary";

        /// <inheritdoc/>
        public override string TypeName => "fixedbinary";

        /// <inheritdoc/>
        public override string ToTypeString() => $"{this.TypeName}<{this.Length}>";

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public bool Equals(FixedBinary? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Length == other?.Length && this.Nullable == other?.Nullable;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as FixedBinary);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.ShortTypeName, this.Nullable, this.Length);
        }

        /// <inheritdoc/>
        protected override bool NodeEqualTypeParameters(IType other)
        {
            return other is FixedBinary t && this.Length == t.Length;
        }
    }

    /// <summary>
    /// Immutable implementation of decimal type.
    /// </summary>
    public sealed class Decimal : ParameterizedType, IEquatable<Decimal>
    {
        /// <summary>
        /// The maximum precision (total digits) a <see cref="Decimal"/> may declare.
        /// </summary>
        public const int MaxPrecision = 38;

        /// <summary>
        /// Initializes a new instance of the <see cref="Decimal"/> class.
        /// </summary>
        /// <param name="precision">The precision.</param>
        /// <param name="scale">The scale.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        public Decimal(int precision, int scale, NullableType nullable, ITypeVariation? typeVariation)
            : base(nullable, typeVariation)
        {
            if (precision < 1 || precision > MaxPrecision)
            {
                throw new ArgumentOutOfRangeException(nameof(precision), precision, $"1-{MaxPrecision}");
            }

            if (scale < 0 || scale > precision)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), scale, $"0-{precision} (the decimal's precision)");
            }

            this.Precision = precision;
            this.Scale = scale;
        }

        /// <summary>
        /// Gets precision.
        /// </summary>
        public int Precision { get; }

        /// <summary>
        /// Gets scale.
        /// </summary>
        public int Scale { get; }

        /// <inheritdoc/>
        public override string ShortTypeName => "dec";

        /// <inheritdoc/>
        public override string TypeName => "decimal";

        /// <inheritdoc/>
        public override string ToTypeString() => $"{this.TypeName}<{this.Precision},{this.Scale}>";

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public bool Equals(Decimal? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Precision == other?.Precision && this.Scale == other.Scale && this.Nullable == other.Nullable;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as Decimal);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.ShortTypeName, this.Nullable, this.Precision, this.Scale);
        }

        /// <inheritdoc/>
        protected override bool NodeEqualTypeParameters(IType other)
        {
            return other is Decimal t && this.Precision == t.Precision && this.Scale == t.Scale;
        }
    }

    /// <summary>
    /// Immutable implementation of struct type.
    /// </summary>
    public sealed class Struct : ParameterizedType, IEquatable<Struct>
    {
        /// <summary>
        /// A predefined empty struct for convenience.
        /// </summary>
        public static readonly Struct Empty = new Struct(ImmutableList<IType>.Empty, NullableType.Required, typeVariation: null);

        /// <summary>
        /// Initializes a new instance of the <see cref="Struct"/> class.
        /// </summary>
        /// <param name="fields">Fields of the struct.</param>
        /// <param name="nullable">Whether it is nullable.</param>
        /// <param name="typeVariation">Type variation.</param>
        public Struct(IEnumerable<IType> fields, NullableType nullable, ITypeVariation? typeVariation = null)
            : base(nullable, typeVariation)
        {
            this.Fields = fields.ToImmutableList();
        }

        /// <summary>
        /// Gets fields.
        /// </summary>
        public IReadOnlyList<IType> Fields { get; }

        /// <inheritdoc/>
        public override IEnumerable<IType> InputNodes => this.Fields;

        /// <inheritdoc/>
        public override string ShortTypeName => "struct";

        /// <inheritdoc/>
        public override string TypeName => "struct";

        /// <inheritdoc/>
        public override string ToTypeString()
        {
            var buf = new StringBuilder(128);
            buf.Append(this.TypeName);
            buf.Append('<');
            foreach (var field in this.Fields)
            {
                buf.Append(field.ToTypeString()).Append(',');
            }

            buf.TrimEnd([',']);
            buf.Append('>');
            return buf.ToString();
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public bool Equals(Struct? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Nullable == other?.Nullable && this.Fields.SequenceEqual(other.Fields);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as Struct);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.ShortTypeName, this.Nullable, this.Fields.CombineHashCodes());
        }

        /// <inheritdoc/>
        protected override bool NodeEqualTypeParameters(IType other)
        {
            // Field types are calculated recursively.
            return true;
        }
    }
}
