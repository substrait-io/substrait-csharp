// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Types;
using Substrait.Tools;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Core.Type.Converters;

/// <summary>
/// Converts protobuf types to internal types.
/// </summary>
public class ProtoToTypeConverter
{
    private readonly ExtensionsDictionary lookup;
    private readonly ExtensionsDictionary.StrictMode strictMode;
    private readonly ExtensionsCollection extensions;

    /// <summary>Initializes a converter using the standard extensions.</summary>
    public ProtoToTypeConverter()
        : this(new ExtensionsDictionary.Builder().Build(), ExtensionUtils.LoadDefaults())
    {
    }

    /// <summary>Initializes a converter.</summary>
    public ProtoToTypeConverter(
        ExtensionsDictionary lookup,
        ExtensionsCollection extensions,
        ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT)
    {
        this.lookup = lookup;
        this.strictMode = strictMode;
        this.extensions = extensions;
    }

    /// <summary>Converts a protobuf type to its internal representation.</summary>
    /// <param name="protoType">The protobuf type.</param>
    /// <returns>The internal type.</returns>
    public IType From(ProtoType protoType)
    {
        var stack = new Stack<(ProtoType ProtoType, List<IType> CollectedTypes, List<IType>? NestedTypes)>();
        var rootTypes = new List<IType>();
        stack.Push((protoType, rootTypes, null));

        while (stack.Count > 0)
        {
            var (current, collectedTypes, nestedTypes) = stack.Pop();
            ITypeVariation? typeVariation;
            switch (current.KindCase)
            {
                case ProtoType.KindOneofCase.Bool:
                    typeVariation = this.GetTypeVariation(current.Bool.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.Bool.Nullability).Boolean_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.I8:
                    typeVariation = this.GetTypeVariation(current.I8.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.I8.Nullability).I8_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.I16:
                    typeVariation = this.GetTypeVariation(current.I16.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.I16.Nullability).I16_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.I32:
                    typeVariation = this.GetTypeVariation(current.I32.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.I32.Nullability).I32_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.I64:
                    typeVariation = this.GetTypeVariation(current.I64.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.I64.Nullability).I64_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.Fp32:
                    typeVariation = this.GetTypeVariation(current.Fp32.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.Fp32.Nullability).FP32_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.Fp64:
                    typeVariation = this.GetTypeVariation(current.Fp64.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.Fp64.Nullability).FP64_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.String:
                    typeVariation = this.GetTypeVariation(current.String.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.String.Nullability).String_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.Binary:
                    typeVariation = this.GetTypeVariation(current.Binary.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.Binary.Nullability).Binary_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.Date:
                    typeVariation = this.GetTypeVariation(current.Date.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.Date.Nullability).Date_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.PrecisionTime:
                    if (current.PrecisionTime.Precision != 6)
                    {
                        throw new NotSupportedException("Only microsecond-precision time types are supported.");
                    }

                    typeVariation = this.GetTypeVariation(current.PrecisionTime.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.PrecisionTime.Nullability).Time_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.PrecisionTimestamp:
                    typeVariation = this.GetTypeVariation(current.PrecisionTimestamp.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.PrecisionTimestamp.Nullability).PrecisionTimestamp(current.PrecisionTimestamp.Precision, typeVariation));
                    break;
                case ProtoType.KindOneofCase.PrecisionTimestampTz:
                    typeVariation = this.GetTypeVariation(current.PrecisionTimestampTz.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.PrecisionTimestampTz.Nullability).PrecisionTimestampTZ(current.PrecisionTimestampTz.Precision, typeVariation));
                    break;
                case ProtoType.KindOneofCase.IntervalYear:
                    typeVariation = this.GetTypeVariation(current.IntervalYear.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.IntervalYear.Nullability).IntervalYear_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.IntervalDay:
                    if (!current.IntervalDay.HasPrecision || current.IntervalDay.Precision != 0)
                    {
                        throw new NotSupportedException("Only explicitly second-precision day intervals are supported.");
                    }

                    typeVariation = this.GetTypeVariation(current.IntervalDay.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.IntervalDay.Nullability).IntervalDay_(typeVariation));
                    break;
                case ProtoType.KindOneofCase.FixedChar:
                    typeVariation = this.GetTypeVariation(current.FixedChar.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.FixedChar.Nullability).FixedChar(current.FixedChar.Length, typeVariation));
                    break;
                case ProtoType.KindOneofCase.Varchar:
                    typeVariation = this.GetTypeVariation(current.Varchar.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.Varchar.Nullability).VarChar(current.Varchar.Length, typeVariation));
                    break;
                case ProtoType.KindOneofCase.FixedBinary:
                    typeVariation = this.GetTypeVariation(current.FixedBinary.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.FixedBinary.Nullability).FixedBinary(current.FixedBinary.Length, typeVariation));
                    break;
                case ProtoType.KindOneofCase.Decimal:
                    typeVariation = this.GetTypeVariation(current.Decimal.TypeVariationReference);
                    collectedTypes.Add(GetFactory(current.Decimal.Nullability).Decimal(current.Decimal.Precision, current.Decimal.Scale, typeVariation));
                    break;
                case ProtoType.KindOneofCase.Struct:
                    typeVariation = this.GetTypeVariation(current.Struct.TypeVariationReference);
                    if (nestedTypes is not null)
                    {
                        collectedTypes.Add(GetFactory(current.Struct.Nullability).Struct(nestedTypes, typeVariation));
                    }
                    else
                    {
                        var childTypes = new List<IType>(current.Struct.Types_.Count);
                        stack.Push((current, collectedTypes, childTypes));
                        for (int index = current.Struct.Types_.Count - 1; index >= 0; index--)
                        {
                            stack.Push((current.Struct.Types_[index], childTypes, null));
                        }
                    }

                    break;
                default:
                    throw new NotImplementedException(current.KindCase.ToString());
            }
        }

        return rootTypes.Count switch
        {
            0 => throw new SerializationException($"Deserialization error: no type was produced for '{protoType}'."),
            1 => rootTypes[0],
            _ => throw new SerializationException($"Deserialization error: multiple types were produced for '{protoType}'."),
        };
    }

    private static TypeFactory GetFactory(ProtoType.Types.Nullability nullability)
    {
        return nullability switch
        {
            ProtoType.Types.Nullability.Nullable => TypeFactory.NULLABLE,
            ProtoType.Types.Nullability.Required => TypeFactory.REQUIRED,
            _ => throw new NotImplementedException(nullability.ToString()),
        };
    }

    private TypeVariationImpl? GetTypeVariation(uint reference)
    {
        if (reference == 0)
        {
            return null;
        }

        if (!this.lookup.TryGetTypeVariationAnchor((int)reference, out TypeVariationImplAnchor? anchor))
        {
            if (this.strictMode.IsOn(ExtensionsDictionary.StrictMode.TYPE_VARIATION))
            {
                throw new SerializationException($"Deserialization error: no type variation reference is defined for ID {reference}.");
            }

            return null;
        }

        if (anchor is null)
        {
            throw new SerializationException("Deserialization error: the type variation anchor cannot be null.");
        }

        if (!this.lookup.TryGetTypeVariation(anchor, this.extensions, this.strictMode, out TypeVariationImpl? typeVariation))
        {
            if (this.strictMode.IsOn(ExtensionsDictionary.StrictMode.TYPE_VARIATION))
            {
                throw new SerializationException($"Deserialization error: no type variation exists for namespace '{anchor.Namespace}' and key '{anchor.Key}'.");
            }

            return null;
        }

        return typeVariation;
    }
}
