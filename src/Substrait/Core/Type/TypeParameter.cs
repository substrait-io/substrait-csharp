// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Substrait.Tools;

namespace Substrait.Core.Type;

/// <summary>A case-tagged parameter of an extension-defined type.</summary>
public abstract class TypeParameter : IEquatable<TypeParameter>
{
    private TypeParameter()
    {
    }

    /// <summary>Compares the case and value, including nested type metadata.</summary>
    public bool Equals(TypeParameter? other) => (this, other) switch
    {
        (Null, Null) => true,
        (DataType left, DataType right) => left.Value.Equals(right.Value, ITypeComparison.Strict),
        (Boolean left, Boolean right) => left.Value == right.Value,
        (Integer left, Integer right) => left.Value == right.Value,
        (Enum left, Enum right) => left.Value == right.Value,
        (String left, String right) => left.Value == right.Value,
        _ => false,
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TypeParameter other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.GetType(), this switch
    {
        Null => 0,
        DataType type => TypeUtils.ITypeEqualityComparer.Of(ITypeComparison.Strict).GetHashCode(type.Value),
        Boolean boolean => boolean.Value.GetHashCode(),
        Integer integer => integer.Value.GetHashCode(),
        Enum enumeration => enumeration.Value.GetHashCode(),
        String text => text.Value.GetHashCode(),
        _ => throw new NotSupportedException($"Unsupported type parameter {this.GetType().Name}."),
    });

    /// <inheritdoc/>
    public override string ToString() => this switch
    {
        Null => "null",
        DataType type => $"data_type({type.Value.ToTypeString()})",
        Boolean boolean => boolean.Value ? "boolean(true)" : "boolean(false)",
        Integer integer => $"integer({integer.Value.ToString(CultureInfo.InvariantCulture)})",
        Enum enumeration => $"enum({Quote(enumeration.Value)})",
        String text => $"string({Quote(text.Value)})",
        _ => throw new NotSupportedException($"Unsupported type parameter {this.GetType().Name}."),
    };

    private static string Quote(string value) => JsonFormatter.Default.Format(new StringValue { Value = value });

    /// <summary>An explicit null parameter selecting the extension's default, if any.</summary>
    public sealed class Null : TypeParameter;

    /// <summary>A data-type parameter, including its nested metadata.</summary>
    public sealed class DataType : TypeParameter
    {
        /// <summary>Initializes a data-type parameter.</summary>
        public DataType(IType value)
        {
            this.Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Gets the parameter's type.</summary>
        public IType Value { get; }

    }

    /// <summary>A boolean parameter; false is a present value.</summary>
    /// <param name="value">The boolean value.</param>
    public sealed class Boolean(bool value) : TypeParameter
    {
        /// <summary>Gets the boolean value.</summary>
        public bool Value { get; } = value;
    }

    /// <summary>A signed 64-bit integer parameter.</summary>
    /// <param name="value">The integer value.</param>
    public sealed class Integer(long value) : TypeParameter
    {
        /// <summary>Gets the signed 64-bit integer value.</summary>
        public long Value { get; } = value;
    }

    /// <summary>An enumeration parameter, distinct from a string with the same text.</summary>
    public sealed class Enum : TypeParameter
    {
        /// <summary>Initializes an enumeration parameter.</summary>
        public Enum(string value)
        {
            this.Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Gets the enumeration value, which may be empty.</summary>
        public string Value { get; }
    }

    /// <summary>A string parameter, distinct from an enumeration with the same text.</summary>
    public sealed class String : TypeParameter
    {
        /// <summary>Initializes a string parameter.</summary>
        public String(string value)
        {
            this.Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Gets the string value, which may be empty.</summary>
        public string Value { get; }
    }
}
