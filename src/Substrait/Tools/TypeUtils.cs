// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Substrait.Core.Extension.Types;
using Substrait.Core.Type;
using Substrait.Tools.Visitor;

namespace Substrait.Tools;

/// <summary>
/// Various utility methods for type manipulation.
/// </summary>
public static class TypeUtils
{
    /// <summary>
    /// Concatenates two named structs.
    /// </summary>
    /// <param name="a">first named struct.</param>
    /// <param name="b">second named struct.</param>
    /// <param name="nullable">the nullablility of new type.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>a new named struct of a || b.</returns>
    public static NamedStruct Concat(this NamedStruct a, NamedStruct b, IType.NullableType nullable = IType.NullableType.Required, ITypeVariation? typeVariation = null)
    {
        return Concat([a, b], nullable, typeVariation);
    }

    /// <summary>
    /// Concatenates all named structs.
    /// </summary>
    /// <param name="structs">structs to concatenate.</param>
    /// <param name="nullable">nullability of new type.</param>
    /// <param name="typeVariation">Type variation.</param>
    /// <returns>concatenated struct.</returns>
    public static NamedStruct Concat(IEnumerable<NamedStruct> structs, IType.NullableType nullable = IType.NullableType.Required, ITypeVariation? typeVariation = null)
    {
        var names = ImmutableList.CreateBuilder<string>();
        var types = ImmutableList.CreateBuilder<IType>();
        foreach (var schema in structs)
        {
            names.AddRange(schema.Names);
            types.AddRange(schema.Struct.Fields);
        }

        return new NamedStruct(names.ToImmutable(), new ParameterizedType.Struct(types.ToImmutable(), nullable, typeVariation));
    }

    /// <summary>
    /// Renames the struct fields.
    /// </summary>
    /// <param name="namedStruct">struct to rename.</param>
    /// <param name="names">new field names.</param>
    /// <returns>a new named struct with new names.</returns>
    /// <exception cref="ArgumentException">Supplied new names do not match to the nubmer of fields in namedStruct.</exception>
    public static NamedStruct Rename(this NamedStruct namedStruct, IEnumerable<string> names)
    {
        var newNames = names.ToImmutableList();
        if (newNames.Count != namedStruct.Names.Count)
        {
            throw new ArgumentException($"The remapped names must match the number of fields ({newNames.Count} vs {namedStruct.Names.Count}).");
        }

        return new NamedStruct(names.ToImmutableList(), namedStruct.Struct);
    }

    /// <summary>
    /// Returns <paramref name="namedStruct"/>'s struct fields paired with their names, in depth-first
    /// order. Only struct fields consume a name, at any depth: a field reached transparently through
    /// a list element or a map key/value is still paired with a name if it is itself a struct, and
    /// map keys are paired before values. List and map containers contribute no pairs of their own.
    /// </summary>
    /// <param name="namedStruct">The named struct to traverse.</param>
    /// <returns>Each struct field's own declared type paired with its name, in depth-first order.</returns>
    /// <exception cref="ArgumentException"><paramref name="namedStruct"/> does not have exactly as
    /// many names as it has struct fields at every depth.</exception>
    public static IEnumerable<(IType Field, string Name)> GetFieldNames(this NamedStruct namedStruct)
    {
        using IEnumerator<string> names = namedStruct.Names.GetEnumerator();

        // Explicit stack rather than recursion: a struct nested arbitrarily deep inside alternating
        // list/map containers must not be bounded by the CLR call stack.
        var stack = new Stack<(IType Type, bool IsField)>();
        stack.Push((namedStruct.Struct, false));

        while (stack.Count > 0)
        {
            (IType current, bool isField) = stack.Pop();
            if (isField)
            {
                if (!names.MoveNext())
                {
                    throw new ArgumentException("The named struct does not have enough names for its struct fields.");
                }

                yield return (current, names.Current);
            }

            switch (current)
            {
                case ParameterizedType.Struct @struct:
                    // Push in reverse so fields are visited, and named, in declared order.
                    for (int index = @struct.Fields.Count - 1; index >= 0; --index)
                    {
                        stack.Push((@struct.Fields[index], true));
                    }

                    break;
                case ParameterizedType.List list:
                    stack.Push((list.ElementType, false));
                    break;
                case ParameterizedType.Map map:
                    // Value pushed first so key pops - and is visited/named - first.
                    stack.Push((map.ValueType, false));
                    stack.Push((map.KeyType, false));
                    break;
                default:
                    break;
            }
        }

        if (names.MoveNext())
        {
            throw new ArgumentException("The named struct has more names than it has struct fields to name.");
        }
    }

    /// <summary>
    /// Returns whether a type is nullable.
    /// </summary>
    /// <param name="type">type to check.</param>
    /// <returns>True if the type is nullable, false otherwise.</returns>
    public static bool IsNullable(this IType type)
    {
        return type.Nullable switch
        {
            IType.NullableType.Nullable => true,
            IType.NullableType.Required => false,
            _ => throw new NotImplementedException(type.Nullable.ToString()),
        };
    }

    /// <summary>
    /// Returns the other nullability of <paramref name="nullableType"/>.
    /// </summary>
    /// <param name="nullableType">The nullability of the type.</param>
    /// <returns>Inverse of <paramref name="nullableType"/>.</returns>
    public static IType.NullableType Inverse(this IType.NullableType nullableType)
    {
        return nullableType switch
        {
            IType.NullableType.Nullable => IType.NullableType.Required,
            IType.NullableType.Required => IType.NullableType.Nullable,
            _ => throw new NotImplementedException(nullableType.ToString()),
        };
    }

    /// <summary>
    /// Extension methods for IType equality supporting flexible comparison.
    /// </summary>
    /// <param name="t">one type.</param>
    /// <param name="other">antoerh type.</param>
    /// <param name="comparison">comparison mode.</param>
    /// <returns>true if both types are equivalent with respect to comparison mode.</returns>
    public static bool Equals(this IType? t, IType? other, ITypeComparison comparison)
    {
        return ITypeEqualityComparer.Of(comparison).Equals(t, other);
    }

    /// <summary>
    /// EqualityComparer of type.
    /// </summary>
    public sealed class ITypeEqualityComparer(ITypeComparison comparison) : EqualityComparer<IType>
    {
        private static readonly IReadOnlyDictionary<ITypeComparison, ITypeEqualityComparer> DEFAULTINSTANCES
#if NET5_0_OR_GREATER
            = Enum.GetValues<ITypeComparison>().Distinct().ToImmutableDictionary(k => k, k => new ITypeEqualityComparer(k));
#else
            = Enum.GetValues(typeof(ITypeComparison)).Cast<ITypeComparison>().Distinct().ToImmutableDictionary(k => k, k => new ITypeEqualityComparer(k));
#endif

        private readonly NodeEqualsDispatcher dispatcher = new(comparison);

        /// <summary>
        /// Gets the type comparer with custom comparison mode.
        /// </summary>
        /// <param name="comparison">comparison mode.</param>
        /// <returns>type comparer with specified comparison.</returns>
        public static ITypeEqualityComparer Of(ITypeComparison comparison)
        {
            if (DEFAULTINSTANCES.TryGetValue(comparison, out ITypeEqualityComparer? result))
            {
                return result;
            }

            return new ITypeEqualityComparer(comparison);
        }

        /// <inheritdoc/>
        public override bool Equals(IType? x, IType? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            else if (x is null || y is null)
            {
                return false;
            }

            if (IsKnownLeafType(x))
            {
                return x.NodeEquals(y, comparison);
            }

            return x.NodeEqualsImpl(y, this.dispatcher);
        }

        /// <inheritdoc/>
        public override int GetHashCode(IType obj)
        {
            var hash = new HashCode();
            if (IsKnownLeafType(obj))
            {
                this.AddNodeHash(ref hash, obj);
            }
            else
            {
                foreach (IType type in new TopDownTraversal<IType>().Traverse(obj))
                {
                    this.AddNodeHash(ref hash, type);
                }
            }

            return hash.ToHashCode();
        }

        // Only sealed built-in leaves can bypass traversal; custom types may expose children.
        private static bool IsKnownLeafType(IType type) =>
            type is PrimitiveType.Bool
                or PrimitiveType.I8
                or PrimitiveType.I16
                or PrimitiveType.I32
                or PrimitiveType.I64
                or PrimitiveType.FP32
                or PrimitiveType.FP64
                or PrimitiveType.Str
                or PrimitiveType.Binary
                or PrimitiveType.Date
                or PrimitiveType.Uuid
                or PrimitiveType.Time
                or PrimitiveType.IntervalYear
                or PrimitiveType.IntervalDay
                or ParameterizedType.IntervalCompound
                or ParameterizedType.PrecisionTimestamp
                or ParameterizedType.PrecisionTimestampTZ
                or ParameterizedType.FixedChar
                or ParameterizedType.VarChar
                or ParameterizedType.FixedBinary
                or ParameterizedType.Decimal;

        private void AddNodeHash(ref HashCode hash, IType type)
        {
            hash.Add(type.GetType());
            hash.Add(type.InputNodes.Count());

            // UnboundType has no nullability to read, matching its own NodeEquals.
            if (type is not UnboundType && (comparison & ITypeComparison.Nullability) != 0)
            {
                hash.Add(type.Nullable);
            }

            if ((comparison & ITypeComparison.TypeVariation) != 0)
            {
                // TypeVariationImpl compares its base type name without regard to case.
                if (type.TypeVariation is TypeVariationImpl variation)
                {
                    hash.Add(variation.Namespace);
                    hash.Add(variation.Name);
                    hash.Add(variation.BaseTypeName, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    hash.Add(type.TypeVariation);
                }
            }

            if (type is UserDefinedType userDefined)
            {
                hash.Add(userDefined.Anchor);
                foreach (TypeParameter parameter in userDefined.Parameters)
                {
                    hash.Add(parameter.GetType());
                    if ((comparison & ITypeComparison.TypeParameter) != 0 && parameter is not TypeParameter.DataType)
                    {
                        hash.Add(parameter);
                    }
                }
            }
            else if ((comparison & ITypeComparison.TypeParameter) != 0)
            {
                hash.Add(type switch
                {
                    PrimitiveType.Time time => time.Precision,
                    PrimitiveType.IntervalDay interval => interval.Precision,
                    ParameterizedType.IntervalCompound interval => interval.Precision,
                    ParameterizedType.PrecisionTimestamp timestamp => timestamp.Precision,
                    ParameterizedType.PrecisionTimestampTZ timestamp => timestamp.Precision,
                    ParameterizedType.FixedChar text => text.Length,
                    ParameterizedType.VarChar text => text.Length,
                    ParameterizedType.FixedBinary binary => binary.Length,
                    ParameterizedType.Decimal number => HashCode.Combine(number.Precision, number.Scale),
                    _ => 0,
                });
            }
        }

        /// <summary>
        /// Dispatch NodeEquals for equality comparison.
        /// </summary>
        public sealed class NodeEqualsDispatcher : TypeTopDownDispatcher<IEnumerator<IType>, bool>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="NodeEqualsDispatcher"/> class.
            /// </summary>
            public NodeEqualsDispatcher()
                : base(NodeEqualsVisitor.DEFAULT)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="NodeEqualsDispatcher"/> class.
            /// </summary>
            /// <param name="comparison">comparison mode.</param>
            public NodeEqualsDispatcher(ITypeComparison comparison)
                : base(new NodeEqualsVisitor(comparison))
            {
            }

            /// <inheritdoc/>
            protected override bool ShouldBailOut(bool result, IEnumerator<IType> context)
            {
                return !result;
            }

            /// <summary>
            /// NodeEquality visitor.
            /// </summary>
            private sealed class NodeEqualsVisitor(ITypeComparison comparison) : DefaultTypeVisitor<IEnumerator<IType>, bool>
            {
                /// <summary>
                /// Default instance.
                /// </summary>
                internal static readonly NodeEqualsVisitor DEFAULT = new(ITypeComparison.Strict);

                /// <inheritdoc/>
                protected override bool DefaultVisit(IType type, IEnumerator<IType> context)
                {
                    return context.MoveNext()
                        && context.Current is not null
                        && type.InputNodes.Count() == context.Current.InputNodes.Count()
                        && type.NodeEquals(context.Current, comparison);
                }
            }
        }
    }
}
