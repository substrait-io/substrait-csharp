// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Immutable;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Extension.Types;
using Substrait.Core.Type;
using Substrait.Tools;
using Substrait.Tools.Visitor;
using static Substrait.Core.Type.IType;

namespace Substrait.Core.Expression;

/// <summary>
/// Base class for literal expressions.
/// </summary>
public abstract class Literal : IExpression, IEquatable<Literal>, INodeEquatable<IExpression>
{
    private readonly Lazy<int> hashCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="Literal"/> class.
    /// </summary>
    /// <param name="type">Type of the literal.</param>
    /// <param name="nullable">Whether the literal type is nullable.</param>
    protected Literal(IType type, NullableType nullable)
    {
        this.Type = type;
        this.Nullable = nullable;
        this.hashCode = new Lazy<int>(() => { return new Expression.GetNodeHashCodeDispatcher().Dispatch(this, NoOpContext<IExpression, int>.DEFAULT); });
    }

    /// <inheritdoc/>
    public IType Type { get; private init; }

    /// <inheritdoc/>
    public virtual IEnumerable<IExpression> InputNodes => ImmutableList<IExpression>.Empty;

    /// <summary>
    /// Gets a value indicating whether the literal type is nullable.
    /// </summary>
    public NullableType Nullable { get; }

    /// <summary>
    /// Gets the type variation of the literal type.
    /// </summary>
    public ITypeVariation? TypeVariation
    {
        get => this.Type.TypeVariation;
        init => this.Type = TypeFactory.Of(this.Nullable).ResolveTypeWithNullability(this.Type, value);
    }

    /// <inheritdoc/>
    public bool HasHashCode => this.hashCode.IsValueCreated;

    /// <inheritdoc/>
    public abstract TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context);

    /// <inheritdoc/>
    public abstract int GetNodeHashCode();

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return this.hashCode.Value;
    }

    /// <inheritdoc/>
    public abstract bool NodeEquals(IExpression other);

    /// <inheritdoc/>
    public bool Equals(Literal? other)
    {
        return this.NodeEqualsImpl<IExpression, Expression.NodeEqualsDispatcher>(other);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as Literal);
    }

    private static IType HomogeneousType(IReadOnlyList<Literal> literals, string description, string paramName)
    {
        var comparer = TypeUtils.ITypeEqualityComparer.Of(ITypeComparison.Strict);
        for (int index = 1; index < literals.Count; index++)
        {
            if (!comparer.Equals(literals[index].Type, literals[0].Type))
            {
                throw new ArgumentException($"{description} {index} has type {literals[index].Type}, but {description} 0 has type {literals[0].Type}.", paramName);
            }
        }

        return literals[0].Type;
    }

    /// <summary>
    /// An immutable implementation of null literal.
    /// </summary>
    public sealed class NullLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NullLiteral"/> class.
        /// </summary>
        /// <param name="type">Type of the null literal.</param>
        /// <remarks>Null literal type is nullable by definition as per the Substrait specification.</remarks>
        public NullLiteral(IType type)
            : base(TypeFactory.NULLABLE.ResolveTypeWithNullability(type), NullableType.Nullable)
        {
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return nameof(NullLiteral).GetHashCode();
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is NullLiteral;
        }
    }

    /// <summary>
    /// An immutable implementation of boolean literal.
    /// </summary>
    public sealed class BoolLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BoolLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public BoolLiteral(bool value)
            : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BoolLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public BoolLiteral(bool value, NullableType nullable)
            : base(TypeFactory.Of(nullable).BOOL, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets a value indicating whether value property.
        /// </summary>
        public bool Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(BoolLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is BoolLiteral o && this.Value == o.Value;
        }
    }

    /// <summary>
    /// An immutable implementation of integer 8 literal.
    /// </summary>
    public sealed class I8Literal : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="I8Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public I8Literal(int value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="I8Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public I8Literal(int value, NullableType nullable)
            : base(TypeFactory.Of(nullable).I8, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public int Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(I8Literal), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is I8Literal o && this.Value == o.Value;
        }
    }

    /// <summary>
    /// An immutable implementation of integer 16 literal.
    /// </summary>
    public sealed class I16Literal : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="I16Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public I16Literal(int value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="I16Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public I16Literal(int value, NullableType nullable)
            : base(TypeFactory.Of(nullable).I16, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public int Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(I16Literal), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is I16Literal o && this.Value == o.Value;
        }
    }

    /// <summary>
    /// An immutable implementation of integer 32 literal.
    /// </summary>
    public sealed class I32Literal : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="I32Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public I32Literal(int value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="I32Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public I32Literal(int value, NullableType nullable)
            : base(TypeFactory.Of(nullable).I32, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public int Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(I32Literal), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is I32Literal o && this.Value == o.Value;
        }
    }

    /// <summary>
    /// An immutable implementation of integer 64 literal.
    /// </summary>
    public sealed class I64Literal : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="I64Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public I64Literal(long value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="I64Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public I64Literal(long value, NullableType nullable)
            : base(TypeFactory.Of(nullable).I64, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public long Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(I64Literal), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is I64Literal o && this.Value == o.Value;
        }
    }

    /// <summary>
    /// An immutable implementation of floating point 32 literal.
    /// </summary>
    public sealed class FP32Literal : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FP32Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public FP32Literal(float value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FP32Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public FP32Literal(float value, NullableType nullable)
            : base(TypeFactory.Of(nullable).FP32, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public float Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(FP32Literal), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is FP32Literal o && this.Value == o.Value;
        }
    }

    /// <summary>
    /// An immutable implementation of floating point 64 literal.
    /// </summary>
    public sealed class FP64Literal : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FP64Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public FP64Literal(double value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FP64Literal"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public FP64Literal(double value, NullableType nullable)
            : base(TypeFactory.Of(nullable).FP64, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public double Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(FP64Literal), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is FP64Literal o && this.Value == o.Value;
        }
    }

    /// <summary>
    /// An immutable implementation of string literal.
    /// </summary>
    public sealed class StrLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public StrLiteral(string value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StrLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public StrLiteral(string value, NullableType nullable)
            : base(TypeFactory.Of(nullable).STR, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public string Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(StrLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is StrLiteral o && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of binary literal.
    /// </summary>
    public sealed class BinaryLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public BinaryLiteral(ByteString value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public BinaryLiteral(ByteString value, NullableType nullable)
            : base(TypeFactory.Of(nullable).BINARY, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public ByteString Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(BinaryLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is BinaryLiteral o && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of precision timestamp literal.
    /// </summary>
    public sealed class PrecisionTimestampLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrecisionTimestampLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="precision">Precision of the timestamp.</param>
        public PrecisionTimestampLiteral(long value, int precision)
          : this(value, precision, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrecisionTimestampLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="precision">Precision of the timestamp.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public PrecisionTimestampLiteral(long value, int precision, NullableType nullable)
            : base(TypeFactory.Of(nullable).PrecisionTimestamp(precision), nullable)
        {
            this.Value = value;
            this.Precision = precision;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public long Value { get; }

        /// <summary>
        /// Gets precision property.
        /// </summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(PrecisionTimestampLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is PrecisionTimestampLiteral o && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of precision timestamp with time zone literal.
    /// </summary>
    public sealed class PrecisionTimestampTZLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrecisionTimestampTZLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="precision">Precision of the timestamp.</param>
        public PrecisionTimestampTZLiteral(long value, int precision)
          : this(value, precision, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrecisionTimestampTZLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="precision">Precision of the timestamp.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public PrecisionTimestampTZLiteral(long value, int precision, NullableType nullable)
            : base(TypeFactory.Of(nullable).PrecisionTimestampTZ(precision), nullable)
        {
            this.Value = value;
            this.Precision = precision;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public long Value { get; }

        /// <summary>
        /// Gets precision property.
        /// </summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(PrecisionTimestampTZLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is PrecisionTimestampTZLiteral o && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of time literal.
    /// </summary>
    public sealed class TimeLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TimeLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public TimeLiteral(long value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TimeLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public TimeLiteral(long value, NullableType nullable)
            : this(value, 6, nullable)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TimeLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal, in units of 10^-precision seconds since midnight.</param>
        /// <param name="precision">Precision of the time.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public TimeLiteral(long value, int precision, NullableType nullable)
            : base(TypeFactory.Of(nullable).PrecisionTime(precision), nullable)
        {
            this.Value = value;
            this.Precision = precision;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public long Value { get; }

        /// <summary>
        /// Gets precision property.
        /// </summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(TimeLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is TimeLiteral o && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of date literal.
    /// </summary>
    public sealed class DateLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DateLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public DateLiteral(int value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DateLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public DateLiteral(int value, NullableType nullable)
            : base(TypeFactory.Of(nullable).DATE, nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public int Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(DateLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is DateLiteral o && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of interval year literal.
    /// </summary>
    public sealed class IntervalYearLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IntervalYearLiteral"/> class.
        /// </summary>
        /// <param name="years">Value of the years.</param>
        /// <param name="months">Value of the months.</param>
        public IntervalYearLiteral(int years, int months)
          : this(years, months, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntervalYearLiteral"/> class.
        /// </summary>
        /// <param name="years">Value of the years.</param>
        /// <param name="months">Value of the months.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public IntervalYearLiteral(int years, int months, NullableType nullable)
            : base(TypeFactory.Of(nullable).INTERVAL_YEAR, nullable)
        {
            this.Years = years;
            this.Months = months;
        }

        /// <summary>
        /// Gets years property.
        /// </summary>
        public int Years { get; }

        /// <summary>
        /// Gets months property.
        /// </summary>
        public int Months { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(IntervalYearLiteral), this.Years, this.Months);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is IntervalYearLiteral o && this.Years == o.Years && this.Months == o.Months;
        }
    }

    /// <summary>
    /// An immutable implementation of interval day literal.
    /// </summary>
    public sealed class IntervalDayLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IntervalDayLiteral"/> class.
        /// </summary>
        /// <param name="days">Value of the days.</param>
        /// <param name="seconds">Value of the seconds.</param>
        public IntervalDayLiteral(int days, int seconds)
          : this(days, seconds, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntervalDayLiteral"/> class.
        /// </summary>
        /// <param name="days">Value of the days.</param>
        /// <param name="seconds">Value of the seconds.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public IntervalDayLiteral(int days, int seconds, NullableType nullable)
            : this(days, seconds, 0, 0, nullable)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntervalDayLiteral"/> class.
        /// </summary>
        /// <param name="days">Value of the days.</param>
        /// <param name="seconds">Value of the seconds.</param>
        /// <param name="subseconds">Value of the subseconds, in units of 10^-precision seconds.</param>
        /// <param name="precision">Precision of the subseconds.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public IntervalDayLiteral(int days, int seconds, long subseconds, int precision, NullableType nullable)
            : base(TypeFactory.Of(nullable).IntervalDay(precision), nullable)
        {
            this.Days = days;
            this.Seconds = seconds;
            this.Subseconds = subseconds;
            this.Precision = precision;
        }

        /// <summary>
        /// Gets days property.
        /// </summary>
        public int Days { get; }

        /// <summary>
        /// Gets seconds property.
        /// </summary>
        public int Seconds { get; }

        /// <summary>
        /// Gets subseconds property.
        /// </summary>
        public long Subseconds { get; }

        /// <summary>
        /// Gets precision property.
        /// </summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(IntervalDayLiteral), this.Days, this.Seconds, this.Subseconds);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is IntervalDayLiteral o && this.Days == o.Days && this.Seconds == o.Seconds && this.Subseconds == o.Subseconds;
        }
    }

    /// <summary>
    /// An immutable implementation of fixed char literal.
    /// </summary>
    public sealed class FixedCharLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FixedCharLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public FixedCharLiteral(string value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FixedCharLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public FixedCharLiteral(string value, NullableType nullable)
            : base(TypeFactory.Of(nullable).FixedChar(value.Length), nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public string Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(FixedCharLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is FixedCharLiteral o && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of varchar literal.
    /// </summary>
    public sealed class VarCharLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VarCharLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="length">Length of the varchar.</param>
        public VarCharLiteral(string value, int length)
          : this(value, length, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VarCharLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="length">Length of the varchar.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public VarCharLiteral(string value, int length, NullableType nullable)
            : base(TypeFactory.Of(nullable).VarChar(length), nullable)
        {
            this.Value = value;
            this.Length = length;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Gets length property.
        /// </summary>
        public int Length { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(VarCharLiteral), this.Length, this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is VarCharLiteral o && this.Length == o.Length && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of fixed binary literal.
    /// </summary>
    public sealed class FixedBinaryLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FixedBinaryLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        public FixedBinaryLiteral(ByteString value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FixedBinaryLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public FixedBinaryLiteral(ByteString value, NullableType nullable)
            : base(TypeFactory.Of(nullable).FixedBinary(value.Length), nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public ByteString Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(FixedBinaryLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is FixedBinaryLiteral o && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of decimal literal.
    /// </summary>
    public sealed class DecimalLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DecimalLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="precision">Precision of the decimal.</param>
        /// <param name="scale">Scale of the decimal.</param>
        public DecimalLiteral(ByteString value, int precision, int scale)
          : this(value, precision, scale, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DecimalLiteral"/> class.
        /// </summary>
        /// <param name="value">Value of the literal.</param>
        /// <param name="precision">Precision of the decimal.</param>
        /// <param name="scale">Scale of the decimal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public DecimalLiteral(ByteString value, int precision, int scale, NullableType nullable)
            : base(TypeFactory.Of(nullable).Decimal(precision, scale), nullable)
        {
            this.Value = value;
            this.Precision = precision;
            this.Scale = scale;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public ByteString Value { get; }

        /// <summary>
        /// Gets precision property.
        /// </summary>
        public int Precision { get; }

        /// <summary>
        /// Gets scale property.
        /// </summary>
        public int Scale { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(DecimalLiteral), this.Precision, this.Scale, this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is DecimalLiteral o && this.Precision == o.Precision && this.Scale == o.Scale && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of struct literal.
    /// </summary>
    public sealed class StructLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StructLiteral"/> class.
        /// </summary>
        /// <param name="fields">Fields of the literal.</param>
        public StructLiteral(IEnumerable<Literal> fields)
          : this(fields, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StructLiteral"/> class.
        /// </summary>
        /// <param name="fields">Fields of the literal.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public StructLiteral(IEnumerable<Literal> fields, NullableType nullable)
            : base(TypeFactory.Of(nullable).Struct(fields.Select(_ => _.Type).ToImmutableList()), nullable)
        {
            this.Fields = fields.ToImmutableList();
        }

        /// <summary>
        /// Gets struct property.
        /// </summary>
        public IReadOnlyList<Literal> Fields { get; }

        /// <inheritdoc/>
        public override IEnumerable<IExpression> InputNodes => this.Fields;

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(StructLiteral));
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is StructLiteral;
        }
    }

    /// <summary>
    /// An immutable implementation of UUID literal.
    /// </summary>
    public sealed class UuidLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UuidLiteral"/> class.
        /// </summary>
        /// <param name="value">The 16 bytes of the UUID.</param>
        public UuidLiteral(ByteString value)
          : this(value, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UuidLiteral"/> class.
        /// </summary>
        /// <param name="value">The 16 bytes of the UUID.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public UuidLiteral(ByteString value, NullableType nullable)
            : base(PrimitiveType.Uuid.Of(nullable), nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Gets value property.
        /// </summary>
        public ByteString Value { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(UuidLiteral), this.Value);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is UuidLiteral o && this.Value.Equals(o.Value);
        }
    }

    /// <summary>
    /// An immutable implementation of precision-aware compound interval literal.
    /// </summary>
    public sealed class IntervalCompoundLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IntervalCompoundLiteral"/> class.
        /// </summary>
        /// <param name="years">Value of the years.</param>
        /// <param name="months">Value of the months.</param>
        /// <param name="days">Value of the days.</param>
        /// <param name="seconds">Value of the seconds.</param>
        /// <param name="subseconds">Value of the subseconds, in units of 10^-precision seconds.</param>
        /// <param name="precision">Precision of the subseconds.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public IntervalCompoundLiteral(int years, int months, int days, int seconds, long subseconds, int precision, NullableType nullable)
            : base(TypeFactory.Of(nullable).IntervalCompound(precision), nullable)
        {
            this.Years = years;
            this.Months = months;
            this.Days = days;
            this.Seconds = seconds;
            this.Subseconds = subseconds;
            this.Precision = precision;
        }

        /// <summary>
        /// Gets years property.
        /// </summary>
        public int Years { get; }

        /// <summary>
        /// Gets months property.
        /// </summary>
        public int Months { get; }

        /// <summary>
        /// Gets days property.
        /// </summary>
        public int Days { get; }

        /// <summary>
        /// Gets seconds property.
        /// </summary>
        public int Seconds { get; }

        /// <summary>
        /// Gets subseconds property.
        /// </summary>
        public long Subseconds { get; }

        /// <summary>
        /// Gets precision property.
        /// </summary>
        public int Precision { get; }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(IntervalCompoundLiteral), this.Years, this.Months, this.Days, this.Seconds, this.Subseconds);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is IntervalCompoundLiteral o
                && this.Years == o.Years
                && this.Months == o.Months
                && this.Days == o.Days
                && this.Seconds == o.Seconds
                && this.Subseconds == o.Subseconds;
        }
    }

    /// <summary>
    /// An immutable implementation of non-empty list literal.
    /// </summary>
    public sealed class ListLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ListLiteral"/> class.
        /// </summary>
        /// <param name="values">Values of the list, at least one.</param>
        public ListLiteral(IEnumerable<Literal> values)
          : this(values, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ListLiteral"/> class.
        /// </summary>
        /// <param name="values">Values of the list, at least one.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public ListLiteral(IEnumerable<Literal> values, NullableType nullable)
            : this(values.ToImmutableList(), nullable)
        {
        }

        private ListLiteral(ImmutableList<Literal> values, NullableType nullable)
            : base(TypeFactory.Of(nullable).List(ElementTypeOf(values)), nullable)
        {
            this.Values = values;
        }

        /// <summary>
        /// Gets values property.
        /// </summary>
        public IReadOnlyList<Literal> Values { get; }

        /// <inheritdoc/>
        public override IEnumerable<IExpression> InputNodes => this.Values;

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(ListLiteral));
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is ListLiteral;
        }

        private static IType ElementTypeOf(ImmutableList<Literal> values)
        {
            if (values.Count == 0)
            {
                throw new ArgumentException("A list literal needs at least one value; use EmptyListLiteral.", nameof(values));
            }

            return HomogeneousType(values, "List value", nameof(values));
        }
    }

    /// <summary>
    /// An immutable implementation of non-empty map literal.
    /// </summary>
    public sealed class MapLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MapLiteral"/> class.
        /// </summary>
        /// <param name="entries">Key-value pairs of the map, at least one.</param>
        public MapLiteral(IEnumerable<KeyValuePair<Literal, Literal>> entries)
          : this(entries, NullableType.Required)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MapLiteral"/> class.
        /// </summary>
        /// <param name="entries">Key-value pairs of the map, at least one.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public MapLiteral(IEnumerable<KeyValuePair<Literal, Literal>> entries, NullableType nullable)
            : this(entries.ToImmutableList(), nullable)
        {
        }

        private MapLiteral(ImmutableList<KeyValuePair<Literal, Literal>> entries, NullableType nullable)
            : base(TypeFactory.Of(nullable).Map(KeyTypeOf(entries), ValueTypeOf(entries)), nullable)
        {
            this.Entries = entries;
        }

        /// <summary>
        /// Gets entries property.
        /// </summary>
        public IReadOnlyList<KeyValuePair<Literal, Literal>> Entries { get; }

        /// <inheritdoc/>
        public override IEnumerable<IExpression> InputNodes => this.Entries.SelectMany(e => new IExpression[] { e.Key, e.Value });

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(MapLiteral));
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is MapLiteral;
        }

        private static IType KeyTypeOf(ImmutableList<KeyValuePair<Literal, Literal>> entries)
        {
            if (entries.Count == 0)
            {
                throw new ArgumentException("A map literal needs at least one entry; use EmptyMapLiteral.", nameof(entries));
            }

            int nullKey = entries.FindIndex(entry => entry.Key is NullLiteral);
            if (nullKey >= 0)
            {
                throw new ArgumentException($"Map key {nullKey} is null; map keys may not be null.", nameof(entries));
            }

            return HomogeneousType(entries.Select(entry => entry.Key).ToList(), "Map key", nameof(entries));
        }

        private static IType ValueTypeOf(ImmutableList<KeyValuePair<Literal, Literal>> entries) =>
            HomogeneousType(entries.Select(entry => entry.Value).ToList(), "Map value", nameof(entries));
    }

    /// <summary>
    /// An immutable implementation of typed empty list literal.
    /// </summary>
    public sealed class EmptyListLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EmptyListLiteral"/> class.
        /// </summary>
        /// <param name="elementType">Element type of the list.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public EmptyListLiteral(IType elementType, NullableType nullable)
            : base(TypeFactory.Of(nullable).List(elementType), nullable)
        {
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return nameof(EmptyListLiteral).GetHashCode();
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is EmptyListLiteral;
        }
    }

    /// <summary>
    /// An immutable implementation of typed empty map literal.
    /// </summary>
    public sealed class EmptyMapLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EmptyMapLiteral"/> class.
        /// </summary>
        /// <param name="keyType">Key type of the map.</param>
        /// <param name="valueType">Value type of the map.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public EmptyMapLiteral(IType keyType, IType valueType, NullableType nullable)
            : base(TypeFactory.Of(nullable).Map(keyType, valueType), nullable)
        {
        }

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return nameof(EmptyMapLiteral).GetHashCode();
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is EmptyMapLiteral;
        }
    }

    /// <summary>
    /// An immutable implementation of user-defined literal.
    /// </summary>
    public sealed class UserDefinedLiteral : Literal
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UserDefinedLiteral"/> class with an opaque value.
        /// </summary>
        /// <param name="anchor">Anchor of the user-defined type.</param>
        /// <param name="parameters">Type parameters of the user-defined type.</param>
        /// <param name="value">Opaque encoded value.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        /// <param name="declaration">Declaration of the user-defined type.</param>
        public UserDefinedLiteral(
            TypeAnchor anchor,
            IEnumerable<TypeParameter> parameters,
            Any value,
            NullableType nullable = NullableType.Required,
            TypeDefinition? declaration = null)
            : base(TypeFactory.Of(nullable).UserDefined(anchor, parameters, null, declaration), nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UserDefinedLiteral"/> class with a structured value.
        /// </summary>
        /// <param name="anchor">Anchor of the user-defined type.</param>
        /// <param name="parameters">Type parameters of the user-defined type.</param>
        /// <param name="fields">Structured value fields.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        /// <param name="declaration">Declaration of the user-defined type.</param>
        public UserDefinedLiteral(
            TypeAnchor anchor,
            IEnumerable<TypeParameter> parameters,
            IEnumerable<Literal> fields,
            NullableType nullable = NullableType.Required,
            TypeDefinition? declaration = null)
            : base(TypeFactory.Of(nullable).UserDefined(anchor, parameters, null, declaration), nullable)
        {
            this.Fields = fields.ToImmutableList();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UserDefinedLiteral"/> class with an opaque value whose type is a plan-local type alias.
        /// </summary>
        /// <param name="aliasAnchor">Anchor of the type alias.</param>
        /// <param name="value">Opaque encoded value.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public UserDefinedLiteral(uint aliasAnchor, Any value, NullableType nullable = NullableType.Required)
            : base(TypeFactory.Of(nullable).AliasReference(aliasAnchor), nullable)
        {
            this.Value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UserDefinedLiteral"/> class with a structured value whose type is a plan-local type alias.
        /// </summary>
        /// <param name="aliasAnchor">Anchor of the type alias.</param>
        /// <param name="fields">Structured value fields.</param>
        /// <param name="nullable">Whether the literal type is nullable.</param>
        public UserDefinedLiteral(uint aliasAnchor, IEnumerable<Literal> fields, NullableType nullable = NullableType.Required)
            : base(TypeFactory.Of(nullable).AliasReference(aliasAnchor), nullable)
        {
            this.Fields = fields.ToImmutableList();
        }

        /// <summary>
        /// Gets the opaque encoded value, or null when the value is structured.
        /// </summary>
        public Any? Value { get; }

        /// <summary>
        /// Gets the structured value fields, or null when the value is opaque.
        /// </summary>
        public IReadOnlyList<Literal>? Fields { get; }

        /// <inheritdoc/>
        public override IEnumerable<IExpression> InputNodes => this.Fields ?? ImmutableList<Literal>.Empty;

        /// <inheritdoc/>
        public override TOutput Accept<TContext, TOutput>(ExpressionVisitor<TContext, TOutput> visitor, TContext context) => visitor.Visit(this, context);

        /// <inheritdoc/>
        public override int GetNodeHashCode()
        {
            return HashCode.Combine(nameof(UserDefinedLiteral), this.Value, this.Fields is null);
        }

        /// <inheritdoc/>
        public override bool NodeEquals(IExpression other)
        {
            return other is UserDefinedLiteral o
                && (this.Value?.Equals(o.Value) ?? o.Value is null)
                && this.Fields?.Count == o.Fields?.Count;
        }
    }
}
