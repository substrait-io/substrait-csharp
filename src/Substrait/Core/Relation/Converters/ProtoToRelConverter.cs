// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Serialization;
using Substrait.Core.Expression;
using Substrait.Core.Expression.Converters;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Protobuf;
using Substrait.Tools;
using ConversionFrame = Substrait.Core.Plan.Converters.ProtoConversionContext.Frame;
using ConversionPhase = Substrait.Core.Plan.Converters.ProtoConversionContext.Phase;
using ProtoRel = Substrait.Protobuf.Rel;
using SortField = Substrait.Core.Expression.SortField;
using Struct = Substrait.Core.Type.ParameterizedType.Struct;
using StructExpression = Substrait.Core.Expression.Expression.Struct;

namespace Substrait.Core.Relation.Converters;

/// <summary>
/// Converts protobuf relations to the internal representation.
/// </summary>
public class ProtoToRelConverter : IProtoConverter
{
    private readonly ExtensionsCollection extensions;
    private readonly ProtoToExpressionConverter expressionConverter;
    private readonly ExtensionsDictionary lookup;
    private readonly ExtensionsDictionary.StrictMode strictMode;
    private readonly ProtoToTypeConverter typeConverter;
    private readonly bool ownsMetadata;
    private readonly IExtensionRelationSchemaResolver? extensionSchemaResolver;

    internal Func<int, IRel>? ReferenceResolver { get; set; }

    /// <summary>
    /// Initializes a converter using standard extensions.
    /// </summary>
    /// <param name="lookup">The extension lookup.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    public ProtoToRelConverter(
        ExtensionsDictionary lookup,
        ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT)
        : this(lookup, ExtensionUtils.LoadDefaults(), strictMode)
    {
    }

    /// <summary>
    /// Initializes a converter.
    /// </summary>
    /// <param name="lookup">The extension lookup.</param>
    /// <param name="extensions">The available extensions.</param>
    /// <param name="strictMode">The extension resolution mode.</param>
    public ProtoToRelConverter(
        ExtensionsDictionary lookup,
        ExtensionsCollection extensions,
        ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT)
        : this(lookup, extensions, extensionSchemaResolver: null, strictMode)
    {
    }

    /// <summary>Initializes a converter with an explicit custom-relation schema contract.</summary>
    /// <param name="lookup">The extension lookup.</param>
    /// <param name="extensions">The available function and type extensions.</param>
    /// <param name="extensionSchemaResolver">The custom-relation schema resolver, or null to retain unresolved schemas.</param>
    /// <param name="strictMode">The function and type extension resolution mode.</param>
    public ProtoToRelConverter(
        ExtensionsDictionary lookup,
        ExtensionsCollection extensions,
        IExtensionRelationSchemaResolver? extensionSchemaResolver,
        ExtensionsDictionary.StrictMode strictMode = ExtensionsDictionary.StrictMode.STRICT)
        : this(lookup, extensions, strictMode, ownsMetadata: false, extensionSchemaResolver)
    {
    }

    internal ProtoToRelConverter(
        ExtensionsDictionary lookup,
        ExtensionsCollection extensions,
        ExtensionsDictionary.StrictMode strictMode,
        bool ownsMetadata,
        IExtensionRelationSchemaResolver? extensionSchemaResolver)
    {
        this.ownsMetadata = ownsMetadata;
        this.extensionSchemaResolver = extensionSchemaResolver;
        this.lookup = lookup;
        this.extensions = extensions;
        this.strictMode = strictMode;
        this.typeConverter = new ProtoToTypeConverter(lookup, extensions, strictMode);
        this.expressionConverter = new ProtoToExpressionConverter(lookup, extensions, this.typeConverter, this, strictMode);
    }

    /// <summary>
    /// Converts a protobuf relation.
    /// </summary>
    /// <param name="protoRel">The protobuf relation.</param>
    /// <returns>The internal relation.</returns>
    public IRel ToRel(ProtoRel protoRel) =>
        this.ToRel(protoRel, ImmutableList<Core.Type.ParameterizedType.Struct>.Empty);

    /// <summary>
    /// Converts a protobuf relation in an enclosing schema context.
    /// </summary>
    /// <param name="protoRel">The protobuf relation.</param>
    /// <param name="enclosingSchemas">Schemas for enclosing contexts.</param>
    /// <returns>The internal relation.</returns>
    public IRel ToRel(ProtoRel protoRel, IReadOnlyList<Core.Type.ParameterizedType.Struct> enclosingSchemas) =>
        this.ToRelCore(protoRel, enclosingSchemas, detached: null);

    internal IRel ToRel(ProtoRel protoRel, DetachedPlanContext detached) =>
        this.ToRelCore(protoRel, ImmutableList<Struct>.Empty, detached);

    private IRel ToRelCore(ProtoRel protoRel, IReadOnlyList<Struct> enclosingSchemas, DetachedPlanContext? detached)
    {
        try
        {
            return new ProtoConversionContext(detached).Convert(this, protoRel, enclosingSchemas);
        }
        catch (ExtensionSchemaUnavailableException error)
        {
            throw new SerializationException($"Cannot convert {protoRel.RelTypeCase}: {error.Message}", error);
        }
    }

    void IProtoConverter.Convert(ConversionFrame frame, ProtoConversionContext context)
    {
        ProtoRel current = (ProtoRel)frame.Node;
        switch (current.RelTypeCase)
        {
            case ProtoRel.RelTypeOneofCase.Reference:
                int ordinal = current.Reference.SubtreeOrdinal;
                if (this.ReferenceResolver is null)
                {
                    throw new SerializationException($"Reference ordinal {ordinal} requires a plan reference context. Convert the containing plan instead.");
                }

                context.Complete(frame, new Reference(ordinal, this.ReferenceResolver(ordinal)));
                break;
            case ProtoRel.RelTypeOneofCase.Read:
                if (frame.Phase == ConversionPhase.Enter)
                {
                    var schema = this.CreateReadSchema(current.Read);
                    if (current.Read.Filter is null && (current.Read.VirtualTable?.Expressions.Count ?? 0) == 0)
                    {
                        context.Complete(frame, this.CreateRead(current.Read, schema, frame, context));
                        return;
                    }

                    context.Resume(frame, ConversionPhase.Complete, schema);
                    if (current.Read.VirtualTable is { } table)
                    {
                        for (int row = table.Expressions.Count - 1; row >= 0; --row)
                        {
                            for (int field = table.Expressions[row].Fields.Count - 1; field >= 0; --field)
                            {
                                context.PushExpression(this.expressionConverter, table.Expressions[row].Fields[field],
                                    Struct.Empty, ImmutableList<Struct>.Empty);
                            }
                        }
                    }

                    if (current.Read.Filter is not null)
                    {
                        context.PushExpression(this.expressionConverter, current.Read.Filter, schema, frame.EnclosingSchemas);
                    }

                    return;
                }

                context.Complete(frame, this.CreateRead(current.Read, frame.InputSchema!, frame, context));
                break;
            case ProtoRel.RelTypeOneofCase.Filter:
                if (context.ScheduleInputs(frame, current.Filter.Input))
                {
                    return;
                }

                if (frame.Phase == ConversionPhase.Inputs)
                {
                    var schema = context.Relation(frame).RecordType;
                    context.Resume(frame, ConversionPhase.Complete);
                    context.PushExpression(this.expressionConverter, current.Filter.Condition, schema, frame.EnclosingSchemas);
                    return;
                }

                context.Complete(frame, new Filter(this.Metadata(current.Filter.Common, current.Filter.AdvancedExtension),
                    context.Relation(frame), context.Expression(frame)));
                break;
            case ProtoRel.RelTypeOneofCase.Project:
                if (context.ScheduleInputs(frame, current.Project.Input))
                {
                    return;
                }

                if (frame.Phase == ConversionPhase.Inputs && current.Project.Expressions.Count > 0)
                {
                    var schema = context.Relation(frame).RecordType;
                    context.Resume(frame, ConversionPhase.Complete);
                    for (int index = current.Project.Expressions.Count - 1; index >= 0; --index)
                    {
                        context.PushExpression(this.expressionConverter, current.Project.Expressions[index], schema, frame.EnclosingSchemas);
                    }

                    return;
                }

                context.Complete(frame, new Project(this.Metadata(current.Project.Common, current.Project.AdvancedExtension),
                    context.Relation(frame), context.CopyExpressions(frame, current.Project.Expressions.Count)));
                break;
            case ProtoRel.RelTypeOneofCase.Fetch:
                if (context.ScheduleInputs(frame, current.Fetch.Input))
                {
                    return;
                }

                if (frame.Phase == ConversionPhase.Inputs)
                {
                    var schema = context.Relation(frame).RecordType;
                    var offset = current.Fetch.OffsetExpr ?? new Protobuf.Expression
                    {
                        Literal = new() { I64 = 0 },
                    };
                    var count = current.Fetch.CountExpr ?? new Protobuf.Expression
                    {
                        Literal = new()
                        {
                            Null = new() { I64 = new() { Nullability = Protobuf.Type.Types.Nullability.Nullable } },
                        },
                    };
                    context.Resume(frame, ConversionPhase.Complete);
                    context.PushExpression(this.expressionConverter, offset, schema, frame.EnclosingSchemas);
                    context.PushExpression(this.expressionConverter, count, schema, frame.EnclosingSchemas);
                    return;
                }

                context.Complete(frame, new Fetch(this.Metadata(current.Fetch.Common, current.Fetch.AdvancedExtension),
                    context.Relation(frame), context.Expression(frame), context.Expression(frame, 1)));
                break;
            case ProtoRel.RelTypeOneofCase.Sort:
                if (context.ScheduleInputs(frame, current.Sort.Input))
                {
                    return;
                }

                if (frame.Phase == ConversionPhase.Inputs && current.Sort.Sorts.Count > 0)
                {
                    var schema = context.Relation(frame).RecordType;
                    context.Resume(frame, ConversionPhase.Complete);
                    for (int index = current.Sort.Sorts.Count - 1; index >= 0; --index)
                    {
                        context.PushExpression(this.expressionConverter, current.Sort.Sorts[index].Expr, schema, frame.EnclosingSchemas);
                    }

                    return;
                }

                context.Complete(frame, new Sort(this.Metadata(current.Sort.Common, current.Sort.AdvancedExtension),
                    context.Relation(frame), current.Sort.Sorts.Select((field, index) =>
                        new SortField(context.Expression(frame, index), field.Direction.FromProto()))));
                break;
            case ProtoRel.RelTypeOneofCase.Aggregate:
                if (context.ScheduleInputs(frame, current.Aggregate.Input))
                {
                    return;
                }

                if (frame.Phase == ConversionPhase.Inputs)
                {
                    ValidateAggregate(current.Aggregate);
                    bool needsSchema = current.Aggregate.GroupingExpressions.Count > 0
                        || current.Aggregate.Measures.Any(measure => measure.Measure_.Arguments.Count > 0 || measure.Filter is not null);
                    var schema = needsSchema ? context.Relation(frame).RecordType : Struct.Empty;
                    context.Resume(frame, ConversionPhase.Complete);
                    for (int index = current.Aggregate.Measures.Count - 1; index >= 0; --index)
                    {
                        var measure = current.Aggregate.Measures[index];
                        if (measure.Filter is not null)
                        {
                            context.PushExpression(this.expressionConverter, measure.Filter, schema, frame.EnclosingSchemas);
                        }

                        for (int argument = measure.Measure_.Arguments.Count - 1; argument >= 0; --argument)
                        {
                            if (measure.Measure_.Arguments[argument].Value is { } value)
                            {
                                context.PushExpression(this.expressionConverter, value, schema, frame.EnclosingSchemas);
                            }
                        }
                    }

                    for (int index = current.Aggregate.GroupingExpressions.Count - 1; index >= 0; --index)
                    {
                        context.PushExpression(this.expressionConverter, current.Aggregate.GroupingExpressions[index], schema, frame.EnclosingSchemas);
                    }

                    return;
                }

                context.Complete(frame, this.CreateAggregate(current.Aggregate, frame, context));
                break;
            case ProtoRel.RelTypeOneofCase.Cross:
                if (context.ScheduleInputs(frame, current.Cross.Left, current.Cross.Right))
                {
                    return;
                }

                context.Complete(frame, new Cross(this.Metadata(current.Cross.Common, current.Cross.AdvancedExtension),
                    context.Relation(frame), context.Relation(frame, 1)));
                break;
            case ProtoRel.RelTypeOneofCase.Join:
                if (context.ScheduleInputs(frame, current.Join.Left, current.Join.Right))
                {
                    return;
                }

                if (frame.Phase == ConversionPhase.Inputs)
                {
                    var schema = TypeFactory.REQUIRED.Struct(context.Relation(frame).RecordType.Fields, context.Relation(frame, 1).RecordType.Fields);
                    context.Resume(frame, ConversionPhase.Complete);
                    if (current.Join.PostJoinFilter is not null)
                    {
                        context.PushExpression(this.expressionConverter, current.Join.PostJoinFilter, schema, frame.EnclosingSchemas);
                    }

                    if (current.Join.Expression is not null)
                    {
                        context.PushExpression(this.expressionConverter, current.Join.Expression, schema, frame.EnclosingSchemas);
                    }

                    return;
                }

                context.Complete(frame, new Join(this.Metadata(current.Join.Common, current.Join.AdvancedExtension),
                    context.Relation(frame), context.Relation(frame, 1), current.Join.Type.FromProto(),
                    current.Join.Expression is null ? null : context.Expression(frame),
                    current.Join.PostJoinFilter is null ? null : context.Expression(frame, current.Join.Expression is null ? 0 : 1)));
                break;
            case ProtoRel.RelTypeOneofCase.HashJoin:
                if (context.ScheduleInputs(frame, current.HashJoin.Left, current.HashJoin.Right))
                {
                    return;
                }

                if (frame.Phase == ConversionPhase.Inputs)
                {
                    var schema = TypeFactory.REQUIRED.Struct(context.Relation(frame).RecordType.Fields, context.Relation(frame, 1).RecordType.Fields);
                    context.Resume(frame, ConversionPhase.Complete, schema);
                    if (current.HashJoin.PostJoinFilter is not null)
                    {
                        context.PushExpression(this.expressionConverter, current.HashJoin.PostJoinFilter, schema, frame.EnclosingSchemas);
                    }

                    return;
                }

                context.Complete(frame, this.CreateHashJoin(current.HashJoin, frame, context));
                break;
            case ProtoRel.RelTypeOneofCase.Set:
                if (context.ScheduleInputs(frame, current.Set.Inputs))
                {
                    return;
                }

                context.Complete(frame, new Set(this.Metadata(current.Set.Common, current.Set.AdvancedExtension),
                    current.Set.Op.FromProto(), context.CopyRelations(frame, current.Set.Inputs.Count)));
                break;
            case ProtoRel.RelTypeOneofCase.Exchange:
                if (context.ScheduleInputs(frame, current.Exchange.Input))
                {
                    return;
                }

                if (current.Exchange.Targets.Count != 0)
                {
                    throw new NotImplementedException("Exchange targets are not supported.");
                }

                if (frame.Phase == ConversionPhase.Inputs && current.Exchange.ExchangeKindCase == ExchangeRel.ExchangeKindOneofCase.SingleTarget)
                {
                    var schema = context.Relation(frame).RecordType;
                    context.Resume(frame, ConversionPhase.Complete);
                    context.PushExpression(this.expressionConverter, current.Exchange.SingleTarget.Expression, schema, frame.EnclosingSchemas);
                    return;
                }

                context.Complete(frame, this.CreateExchange(current.Exchange, frame, context));
                break;
            case ProtoRel.RelTypeOneofCase.ExtensionLeaf:
                context.Complete(frame, this.CreateExtension(ExtensionRelationKind.Leaf,
                    current.ExtensionLeaf.Common, current.ExtensionLeaf.Detail, []));
                break;
            case ProtoRel.RelTypeOneofCase.ExtensionSingle:
                if (context.ScheduleInputs(frame, current.ExtensionSingle.Input ?? throw new SerializationException("ExtensionSingle requires an input relation.")))
                {
                    return;
                }

                context.Complete(frame, this.CreateExtension(ExtensionRelationKind.Single,
                    current.ExtensionSingle.Common, current.ExtensionSingle.Detail, context.CopyRelations(frame, 1)));
                break;
            case ProtoRel.RelTypeOneofCase.ExtensionMulti:
                if (context.ScheduleInputs(frame, current.ExtensionMulti.Inputs))
                {
                    return;
                }

                context.Complete(frame, this.CreateExtension(ExtensionRelationKind.Multi,
                    current.ExtensionMulti.Common, current.ExtensionMulti.Detail, context.CopyRelations(frame, current.ExtensionMulti.Inputs.Count)));
                break;
            default:
                throw new NotImplementedException(current.RelTypeCase.ToString());
        }
    }

    private RelationMetadata Metadata(RelCommon? common, AdvancedExtension? advancedExtension)
    {
        if (common is { HasRelAnchor: true, RelAnchor: 0 })
        {
            throw new SerializationException("Relation anchor 0 is invalid; an explicitly set anchor must be positive.");
        }

        return RelationMetadata.Capture(common, advancedExtension, this.ownsMetadata);
    }

    private IRel CreateExtension(
        ExtensionRelationKind kind,
        RelCommon? common,
        Google.Protobuf.WellKnownTypes.Any? detail,
        IReadOnlyList<IRel> inputs)
    {
        RelationMetadata metadata = this.Metadata(common, null);
        ReadOnlyAny? payload = detail is null ? null : this.ownsMetadata
            ? ReadOnlyAny.FromOwnedProto(detail) : ReadOnlyAny.FromProto(detail);
        ImmutableList<IRel> immutableInputs = inputs.ToImmutableList();
        Core.Type.ParameterizedType.Struct? schema = this.extensionSchemaResolver?.Resolve(kind, payload, immutableInputs);
        return kind switch
        {
            ExtensionRelationKind.Leaf => new ExtensionLeaf(metadata, payload, schema),
            ExtensionRelationKind.Single => new ExtensionSingle(metadata, immutableInputs[0], payload, schema),
            ExtensionRelationKind.Multi => new ExtensionMulti(metadata, immutableInputs, payload, schema),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown extension relation kind."),
        };
    }

    private static void ValidateAggregate(AggregateRel aggregate)
    {
        if (aggregate.Groupings.Count > 1)
        {
            throw new NotImplementedException("Grouping sets are not supported.");
        }

        if (aggregate.Groupings.Count == 0 && aggregate.Measures.Count == 0)
        {
            throw new SerializationException("Deserialization error: either grouping or measure must exist.");
        }

        foreach (AggregateRel.Types.Grouping grouping in aggregate.Groupings)
        {
            var references = new HashSet<int>(grouping.ExpressionReferences.Select(reference => (int)reference));
            if (references.Any(reference => reference < 0 || reference >= aggregate.GroupingExpressions.Count))
            {
                throw new SerializationException("Deserialization error: grouping expression reference index is out of bounds.");
            }

            if (references.Count != aggregate.GroupingExpressions.Count)
            {
                throw new SerializationException("Deserialization error: unused grouping expressions.");
            }
        }
    }

    private Aggregate CreateAggregate(AggregateRel aggregate, ConversionFrame frame, ProtoConversionContext context)
    {
        var functionArgConverter = new ProtoToFunctionArgConverter(this.expressionConverter, this.typeConverter);
        int valueIndex = aggregate.GroupingExpressions.Count;
        var measures = aggregate.Measures.Select(measure =>
        {
            Protobuf.AggregateFunction function = measure.Measure_;
            var anchor = this.lookup.GetFunctionAnchor((int)function.FunctionReference);
            bool found = this.lookup.TryGetAggregateFunction(anchor, this.extensions, this.strictMode, out var declaration);
            Debug.Assert(found || (this.strictMode & ExtensionsDictionary.StrictMode.FUNCTION) == 0);
            return new Aggregate.Measure(
                new AggregateFunctionInvocation(
                    anchor.Namespace,
                    anchor.Key,
                    function.Arguments.Select(argument => functionArgConverter.FromConverted(argument,
                        argument.ArgTypeCase == FunctionArgument.ArgTypeOneofCase.Value ? context.Expression(frame, valueIndex++) : null)),
                    this.typeConverter.From(function.OutputType),
                    function.Phase.FromProto(),
                    function.Invocation.FromProto(),
                    declaration),
                measure.Filter is null ? null : context.Expression(frame, valueIndex++));
        });

        return new Aggregate(
            this.Metadata(aggregate.Common, aggregate.AdvancedExtension),
            context.Relation(frame),
            context.CopyExpressions(frame, aggregate.GroupingExpressions.Count),
            aggregate.Groupings.Select(grouping => new Aggregate.Grouping(grouping.ExpressionReferences.Select(reference => (int)reference))),
            measures);
    }

    private HashJoin CreateHashJoin(HashJoinRel hashJoin, ConversionFrame frame, ProtoConversionContext context)
    {
        return new HashJoin(
            this.Metadata(hashJoin.Common, hashJoin.AdvancedExtension),
            context.Relation(frame),
            context.Relation(frame, 1),
            hashJoin.Type.FromProto(),
            hashJoin.Keys.Select(key => new PhysicalJoin.ComparisonJoinKey(
                this.expressionConverter.CreateReference(key.Left, frame.InputSchema!, frame.EnclosingSchemas),
                this.expressionConverter.CreateReference(key.Right, frame.InputSchema!, frame.EnclosingSchemas),
                key.Comparison.HasSimple
                    ? new PhysicalJoin.ComparisonJoinKey.ComparisonType(key.Comparison.Simple.FromProto())
                    : new PhysicalJoin.ComparisonJoinKey.ComparisonType(key.Comparison.CustomFunctionReference))),
            hashJoin.PostJoinFilter is null ? null : context.Expression(frame),
            EnumUtils.Cast<HashJoinRel.Types.BuildInput, HashJoin.BuildInput>(hashJoin.BuildInput));
    }

    private Exchange CreateExchange(ExchangeRel exchange, ConversionFrame frame, ProtoConversionContext context)
    {
        IRel input = context.Relation(frame);
        return exchange.ExchangeKindCase switch
        {
            ExchangeRel.ExchangeKindOneofCase.ScatterByFields => new ScatterExchange(
                this.Metadata(exchange.Common, exchange.AdvancedExtension),
                input,
                exchange.PartitionCount,
                exchange.ScatterByFields.Fields.Select(field => this.expressionConverter.CreateReference(field, input.RecordType, frame.EnclosingSchemas))),
            ExchangeRel.ExchangeKindOneofCase.SingleTarget => new SingleBucketExchange(
                this.Metadata(exchange.Common, exchange.AdvancedExtension),
                input,
                exchange.PartitionCount,
                context.Expression(frame)),
            _ => throw new NotImplementedException(exchange.ExchangeKindCase.ToString()),
        };
    }

    private IRel CreateRead(ReadRel read, Struct recordType, ConversionFrame frame, ProtoConversionContext context)
    {
        var schema = new Core.Type.NamedStruct(read.BaseSchema.Names.ToImmutableList(), recordType);
        IExpression? filter = read.Filter is null
            ? null
            : context.Expression(frame);

        return read.ReadTypeCase switch
        {
            ReadRel.ReadTypeOneofCase.NamedTable => new NamedTableRead(
                this.Metadata(read.Common, read.AdvancedExtension), schema, read.NamedTable.Names, filter,
                RelationMetadata.CaptureExtension(read.NamedTable.AdvancedExtension, this.ownsMetadata)),
            ReadRel.ReadTypeOneofCase.VirtualTable => new VirtualTableRead(
                this.Metadata(read.Common, read.AdvancedExtension),
                schema,
                CreateRows(read, frame, context),
                filter),
            ReadRel.ReadTypeOneofCase.None => new EmptyRead(this.Metadata(read.Common, read.AdvancedExtension), schema, filter),
            _ => throw new NotImplementedException(read.ReadTypeCase.ToString()),
        };
    }

    private static IEnumerable<StructExpression> CreateRows(ReadRel read, ConversionFrame frame, ProtoConversionContext context)
    {
        int offset = read.Filter is null ? 0 : 1;
        foreach (var row in read.VirtualTable.Expressions)
        {
            yield return new StructExpression(context.CopyExpressions(frame, row.Fields.Count, offset));
            offset += row.Fields.Count;
        }
    }

    private Struct CreateReadSchema(ReadRel read)
    {
        return new Struct(
            read.BaseSchema.Struct.Types_.Select(this.typeConverter.From).ToImmutableList(),
            read.BaseSchema.Struct.Nullability.FromProto());
    }
}
