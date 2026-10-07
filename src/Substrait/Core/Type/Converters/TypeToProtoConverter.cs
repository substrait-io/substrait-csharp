// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Extension;
using Substrait.Core.Plan.Converters;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Core.Type.Converters;

/// <summary>
/// Converts internal types to protobuf types.
/// </summary>
public class TypeToProtoConverter
{
    private readonly TypeBottomUpDispatcher<PlanToProtoConverter.ConverterContext, ProtoType> dispatcher =
        new(new TypeToProtoVisitor());

    /// <summary>Converts a type using a new conversion context.</summary>
    public ProtoType From(IType type) => this.From(type, new PlanToProtoConverter.ConverterContext());

    /// <summary>Converts a type using a shared conversion context.</summary>
    public ProtoType From(IType type, PlanToProtoConverter.ConverterContext context) =>
        this.dispatcher.Dispatch(type, context);

    private sealed class TypeToProtoVisitor : TypeVisitor<PlanToProtoConverter.ConverterContext, ProtoType>
    {
        public override ProtoType Visit(PrimitiveType.Bool type, PlanToProtoConverter.ConverterContext context) =>
            new() { Bool = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.I8 type, PlanToProtoConverter.ConverterContext context) =>
            new() { I8 = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.I16 type, PlanToProtoConverter.ConverterContext context) =>
            new() { I16 = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.I32 type, PlanToProtoConverter.ConverterContext context) =>
            new() { I32 = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.I64 type, PlanToProtoConverter.ConverterContext context) =>
            new() { I64 = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.FP32 type, PlanToProtoConverter.ConverterContext context) =>
            new() { Fp32 = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.FP64 type, PlanToProtoConverter.ConverterContext context) =>
            new() { Fp64 = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.Str type, PlanToProtoConverter.ConverterContext context) =>
            new() { String = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.Binary type, PlanToProtoConverter.ConverterContext context) =>
            new() { Binary = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.Date type, PlanToProtoConverter.ConverterContext context) =>
            new() { Date = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.Uuid type, PlanToProtoConverter.ConverterContext context) =>
            new() { Uuid = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.Time type, PlanToProtoConverter.ConverterContext context) =>
            new() { PrecisionTime = new() { Precision = type.Precision, Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.IntervalYear type, PlanToProtoConverter.ConverterContext context) =>
            new() { IntervalYear = new() { Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(PrimitiveType.IntervalDay type, PlanToProtoConverter.ConverterContext context) =>
            new() { IntervalDay = new() { Precision = type.Precision, Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(ParameterizedType.IntervalCompound type, PlanToProtoConverter.ConverterContext context) =>
            new() { IntervalCompound = new() { Precision = type.Precision, Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(ParameterizedType.PrecisionTimestamp type, PlanToProtoConverter.ConverterContext context) =>
            new() { PrecisionTimestamp = new() { Precision = type.Precision, Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(ParameterizedType.PrecisionTimestampTZ type, PlanToProtoConverter.ConverterContext context) =>
            new() { PrecisionTimestampTz = new() { Precision = type.Precision, Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(ParameterizedType.FixedChar type, PlanToProtoConverter.ConverterContext context) =>
            new() { FixedChar = new() { Length = type.Length, Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(ParameterizedType.VarChar type, PlanToProtoConverter.ConverterContext context) =>
            new() { Varchar = new() { Length = type.Length, Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(ParameterizedType.FixedBinary type, PlanToProtoConverter.ConverterContext context) =>
            new() { FixedBinary = new() { Length = type.Length, Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(ParameterizedType.Decimal type, PlanToProtoConverter.ConverterContext context) =>
            new() { Decimal = new() { Precision = type.Precision, Scale = type.Scale, Nullability = type.Nullable.ToProto(), TypeVariationReference = Variation(type, context) } };

        public override ProtoType Visit(ParameterizedType.Struct type, PlanToProtoConverter.ConverterContext context)
        {
            var result = new ProtoType.Types.Struct
            {
                Nullability = type.Nullable.ToProto(),
                TypeVariationReference = Variation(type, context),
            };
            result.Types_.AddRange(type.Fields.Select(context.GetOutput));
            return new ProtoType { Struct = result };
        }

        public override ProtoType Visit(IType other, PlanToProtoConverter.ConverterContext context) =>
            throw new NotImplementedException($"Conversion for {other.GetType().Name} is not implemented.");

        private static uint Variation(IType type, PlanToProtoConverter.ConverterContext context) =>
            (uint)(type.TypeVariation is null
                ? 0
                : context.AddExtension(
                    ExtensionsCollector.ExtensionType.TypeVariation,
                    type.TypeVariation.Namespace,
                    type.TypeVariation.Name));
    }
}
