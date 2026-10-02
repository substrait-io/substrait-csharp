// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression.Converters;
using Substrait.Core.Extension;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type.Converters;
using Substrait.Protobuf;
using Substrait.Tools;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoRel = Substrait.Protobuf.Rel;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Core.Relation.Converters;

/// <summary>
/// Converts internal relations to protobuf relations.
/// </summary>
public class RelToProtoConverter
{
    private readonly RelBottomUpDispatcher<PlanToProtoConverter.ConverterContext, ProtoRel> dispatcher;

    /// <summary>Initializes a relation converter.</summary>
    public RelToProtoConverter()
    {
        var typeConverter = new TypeToProtoConverter();
        var expressionConverter = new ExpressionToProtoConverter(typeConverter, this);
        this.dispatcher = new(new RelToProtoVisitor(expressionConverter, typeConverter));
    }

    /// <summary>Converts a relation using a new context.</summary>
    public ProtoRel From(IRel relation) => this.From(relation, new PlanToProtoConverter.ConverterContext());

    /// <summary>Converts a relation using a shared context.</summary>
    public ProtoRel From(IRel relation, PlanToProtoConverter.ConverterContext context) =>
        this.dispatcher.Dispatch(relation, context);

    private sealed class RelToProtoVisitor : RelVisitor<PlanToProtoConverter.ConverterContext, ProtoRel>
    {
        private readonly ExpressionToProtoConverter expressionConverter;
        private readonly TypeToProtoConverter typeConverter;

        public RelToProtoVisitor(ExpressionToProtoConverter expressionConverter, TypeToProtoConverter typeConverter)
        {
            this.expressionConverter = expressionConverter;
            this.typeConverter = typeConverter;
        }

        public override ProtoRel Visit(Reference relation, PlanToProtoConverter.ConverterContext context)
        {
            context.ValidateReference(relation);
            return new ProtoRel { Reference = new() { SubtreeOrdinal = relation.SubtreeOrdinal } };
        }

        public override ProtoRel Visit(Filter relation, PlanToProtoConverter.ConverterContext context) =>
            new() { Filter = new() { Input = context.GetOutput(relation.Input), Condition = this.expressionConverter.From(relation.Condition, context), Common = Common(relation), AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto() } };

        public override ProtoRel Visit(Cross relation, PlanToProtoConverter.ConverterContext context) =>
            new() { Cross = new() { Left = context.GetOutput(relation.Left), Right = context.GetOutput(relation.Right), Common = Common(relation), AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto() } };

        public override ProtoRel Visit(Project relation, PlanToProtoConverter.ConverterContext context)
        {
            var project = new ProjectRel { Input = context.GetOutput(relation.Input), Common = Common(relation), AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto() };
            project.Expressions.AddRange(relation.Expressions.Select(expression => this.expressionConverter.From(expression, context)));
            return new ProtoRel { Project = project };
        }

        public override ProtoRel Visit(NamedTableRead relation, PlanToProtoConverter.ConverterContext context)
        {
            ReadRel read = this.CreateRead(relation, context);
            read.NamedTable = new() { Names = { relation.Names }, AdvancedExtension = relation.TableAdvancedExtension?.ToProto() };
            return new ProtoRel { Read = read };
        }

        public override ProtoRel Visit(VirtualTableRead relation, PlanToProtoConverter.ConverterContext context)
        {
            ReadRel read = this.CreateRead(relation, context);
            read.VirtualTable = new();
            read.VirtualTable.Expressions.AddRange(relation.Rows.Select(row =>
            {
                var structure = new ProtoExpression.Types.Nested.Types.Struct();
                structure.Fields.AddRange(row.Fields.Select(field => this.expressionConverter.From(field, context)));
                return structure;
            }));
            return new ProtoRel { Read = read };
        }

        public override ProtoRel Visit(EmptyRead relation, PlanToProtoConverter.ConverterContext context) =>
            new() { Read = this.CreateRead(relation, context) };

        public override ProtoRel Visit(Sort relation, PlanToProtoConverter.ConverterContext context)
        {
            var sort = new SortRel { Input = context.GetOutput(relation.Input), Common = Common(relation), AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto() };
            sort.Sorts.AddRange(relation.SortFields.Select(field => new SortField
            {
                Expr = this.expressionConverter.From(field.Expr, context),
                Direction = field.Direction.ToProto(),
            }));
            return new ProtoRel { Sort = sort };
        }

        public override ProtoRel Visit(Fetch relation, PlanToProtoConverter.ConverterContext context) =>
            new()
            {
                Fetch = new()
                {
                    Input = context.GetOutput(relation.Input),
                    CountExpr = this.expressionConverter.From(relation.Count, context),
                    OffsetExpr = this.expressionConverter.From(relation.Offset, context),
                    Common = Common(relation),
                    AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto(),
                },
            };

        public override ProtoRel Visit(Set relation, PlanToProtoConverter.ConverterContext context)
        {
            var set = new SetRel { Op = relation.SetOperation.ToProto(), Common = Common(relation), AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto() };
            set.Inputs.AddRange(relation.Inputs.Select(context.GetOutput));
            return new ProtoRel { Set = set };
        }

        public override ProtoRel Visit(Aggregate relation, PlanToProtoConverter.ConverterContext context)
        {
            var aggregate = new AggregateRel { Input = context.GetOutput(relation.Input), Common = Common(relation), AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto() };
            aggregate.GroupingExpressions.AddRange(relation.GroupingExpressions.Select(expression => this.expressionConverter.From(expression, context)));
            aggregate.Groupings.AddRange(relation.Groupings.Select(grouping =>
            {
                var result = new AggregateRel.Types.Grouping();
                result.ExpressionReferences.AddRange(grouping.Expressions.Select(reference => (uint)reference));
                return result;
            }));
            aggregate.Measures.AddRange(relation.Measures.Select(measure =>
            {
                var function = new AggregateFunction
                {
                    FunctionReference = (uint)context.AddExtension(ExtensionsCollector.ExtensionType.Function, measure.Function.Namespace, measure.Function.Key),
                    OutputType = this.typeConverter.From(measure.Function.OutputType, context),
                    Phase = measure.Function.Phase.ToProto(),
                    Invocation = measure.Function.Invocation.ToProto(),
                };
                function.Arguments.AddRange(measure.Function.Arguments.Select(argument => argument switch
                {
                    Core.Type.IType type => new FunctionArgument { Type = this.typeConverter.From(type, context) },
                    Core.Expression.IExpression expression => new FunctionArgument { Value = this.expressionConverter.From(expression, context) },
                    Core.Expression.EnumArgumentValue option => new FunctionArgument { Enum = option.Option },
                    _ => throw new NotSupportedException($"Unsupported function argument type: {argument.GetType().Name}"),
                }));
                return new AggregateRel.Types.Measure
                {
                    Measure_ = function,
                    Filter = measure.PreMeasureFilter is null ? null : this.expressionConverter.From(measure.PreMeasureFilter, context),
                };
            }));
            return new ProtoRel { Aggregate = aggregate };
        }

        public override ProtoRel Visit(Join relation, PlanToProtoConverter.ConverterContext context) =>
            new()
            {
                Join = new()
                {
                    Left = context.GetOutput(relation.Left),
                    Right = context.GetOutput(relation.Right),
                    Type = relation.Type.ToProto(),
                    Expression = relation.Condition is null ? null : this.expressionConverter.From(relation.Condition, context),
                    PostJoinFilter = relation.PostJoinFilter is null ? null : this.expressionConverter.From(relation.PostJoinFilter, context),
                    Common = Common(relation),
                    AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto(),
                },
            };

        public override ProtoRel Visit(HashJoin relation, PlanToProtoConverter.ConverterContext context)
        {
            var hashJoin = new HashJoinRel
            {
                Left = context.GetOutput(relation.Left),
                Right = context.GetOutput(relation.Right),
                Type = relation.Type.ToHashJoinProto(),
                BuildInput = relation.BuildLeft ? HashJoinRel.Types.BuildInput.Left : HashJoinRel.Types.BuildInput.Right,
                PostJoinFilter = relation.PostJoinFilter is null ? null : this.expressionConverter.From(relation.PostJoinFilter, context),
                Common = Common(relation),
                AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto(),
            };
            hashJoin.Keys.AddRange(relation.Keys.Select(key => new ComparisonJoinKey
            {
                Left = this.expressionConverter.From(key.Left, context).Selection,
                Right = this.expressionConverter.From(key.Right, context).Selection,
                Comparison = key.Comparison.Simple == PhysicalJoin.ComparisonJoinKey.SimpleComparisonType.Unspecified
                    ? new() { CustomFunctionReference = key.Comparison.CustomFunctionReference }
                    : new() { Simple = key.Comparison.Simple.ToProto() },
            }));
            return new ProtoRel { HashJoin = hashJoin };
        }

        public override ProtoRel Visit(ScatterExchange relation, PlanToProtoConverter.ConverterContext context)
        {
            var exchange = new ExchangeRel
            {
                Input = context.GetOutput(relation.Input),
                PartitionCount = relation.PartitionCount,
                ScatterByFields = new(),
                Common = Common(relation),
                AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto(),
            };
            exchange.ScatterByFields.Fields.AddRange(relation.Fields.Select(field => this.expressionConverter.From(field, context).Selection));
            return new ProtoRel { Exchange = exchange };
        }

        public override ProtoRel Visit(SingleBucketExchange relation, PlanToProtoConverter.ConverterContext context) =>
            new()
            {
                Exchange = new()
                {
                    Input = context.GetOutput(relation.Input),
                    PartitionCount = relation.PartitionCount,
                    SingleTarget = new() { Expression = this.expressionConverter.From(relation.Expression, context) },
                    Common = Common(relation),
                    AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto(),
                },
            };
        public override ProtoRel Visit(IRel other, PlanToProtoConverter.ConverterContext context) => Unsupported(other);

        private static ProtoRel Unsupported(IRel relation) =>
            throw new NotImplementedException($"Conversion for {relation.GetType().Name} is not implemented.");

        private static RelCommon? Common(IRel relation)
        {
            if (relation.Metadata.Common?.RelAnchor == 0)
            {
                throw new ArgumentException("Relation anchor 0 is invalid; an explicitly set anchor must be positive.", nameof(relation));
            }

            if (!Equals(relation.Transmute, relation.Metadata.Transmute))
            {
                throw new ArgumentException("Relation output mapping must match its metadata.", nameof(relation));
            }

            return relation.Metadata.Common?.ToProto();
        }

        private ReadRel CreateRead(Read relation, PlanToProtoConverter.ConverterContext context)
        {
            var read = new ReadRel
            {
                BaseSchema = this.CreateNamedStruct(relation.InitialSchema, context),
                Common = Common(relation),
                AdvancedExtension = relation.Metadata.AdvancedExtension?.ToProto(),
            };
            if (relation.Filter is not null)
            {
                read.Filter = this.expressionConverter.From(relation.Filter, context);
            }

            return read;
        }

        private NamedStruct CreateNamedStruct(Core.Type.NamedStruct schema, PlanToProtoConverter.ConverterContext context)
        {
            var structure = new ProtoType.Types.Struct { Nullability = schema.Struct.Nullable.ToProto() };
            structure.Types_.AddRange(schema.Struct.Fields.Select(field => this.typeConverter.From(field, context)));
            return new NamedStruct { Names = { schema.Names }, Struct = structure };
        }
    }
}
