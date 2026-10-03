// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Serialization;
using Substrait.Core.Expression;
using Substrait.Core.Expression.Converters;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Protobuf;
using Substrait.Tools;
using ProtoRel = Substrait.Protobuf.Rel;
using SortField = Substrait.Core.Expression.SortField;
using StructExpression = Substrait.Core.Expression.Expression.Struct;

namespace Substrait.Core.Relation.Converters;

/// <summary>
/// Converts protobuf relations to the internal representation.
/// </summary>
public class ProtoToRelConverter
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
    public IRel ToRel(ProtoRel protoRel, IReadOnlyList<Core.Type.ParameterizedType.Struct> enclosingSchemas)
    {
        try
        {
            return this.ToRelCore(protoRel, enclosingSchemas);
        }
        catch (ExtensionSchemaUnavailableException error)
        {
            throw new SerializationException($"Cannot convert {protoRel.RelTypeCase}: {error.Message}", error);
        }
    }

    private IRel ToRelCore(ProtoRel protoRel, IReadOnlyList<Core.Type.ParameterizedType.Struct> enclosingSchemas)
    {
        var stack = new Stack<(ProtoRel Relation, List<IRel> Inputs, List<IRel> Output, int InputCount)>();
        var output = new List<IRel>();
        stack.Push((protoRel, new List<IRel>(), output, -1));

        while (stack.Count > 0)
        {
            var (current, inputs, destination, inputCount) = stack.Pop();
            switch (current.RelTypeCase)
            {
                case ProtoRel.RelTypeOneofCase.Reference:
                    int ordinal = current.Reference.SubtreeOrdinal;
                    if (this.ReferenceResolver is null)
                    {
                        throw new SerializationException($"Reference ordinal {ordinal} requires a plan reference context. Convert the containing plan instead.");
                    }

                    destination.Add(new Reference(ordinal, this.ReferenceResolver(ordinal)));
                    break;
                case ProtoRel.RelTypeOneofCase.Aggregate:
                    ProcessSingleInput(current, current.Aggregate.Input, inputs, destination, inputCount,
                        input => this.CreateAggregate(current.Aggregate, input, enclosingSchemas), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.Read:
                    destination.Add(this.CreateRead(current.Read, enclosingSchemas));
                    break;
                case ProtoRel.RelTypeOneofCase.Filter:
                    ProcessSingleInput(current, current.Filter.Input, inputs, destination, inputCount,
                        input => new Filter(this.Metadata(current.Filter.Common, current.Filter.AdvancedExtension), input, this.expressionConverter.From(current.Filter.Condition, input.RecordType, enclosingSchemas)), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.Project:
                    ProcessSingleInput(current, current.Project.Input, inputs, destination, inputCount,
                        input => new Project(this.Metadata(current.Project.Common, current.Project.AdvancedExtension), input, current.Project.Expressions.Select(expression => this.expressionConverter.From(expression, input.RecordType, enclosingSchemas))), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.Fetch:
                    ProcessSingleInput(current, current.Fetch.Input, inputs, destination, inputCount,
                        input => new Fetch(
                            this.Metadata(current.Fetch.Common, current.Fetch.AdvancedExtension),
                            input,
                            this.expressionConverter.From(current.Fetch.CountExpr, input.RecordType, enclosingSchemas),
                            this.expressionConverter.From(current.Fetch.OffsetExpr, input.RecordType, enclosingSchemas)), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.Sort:
                    ProcessSingleInput(current, current.Sort.Input, inputs, destination, inputCount,
                        input => new Sort(
                            this.Metadata(current.Sort.Common, current.Sort.AdvancedExtension),
                            input,
                            current.Sort.Sorts.Select(field => new SortField(
                                this.expressionConverter.From(field.Expr, input.RecordType, enclosingSchemas),
                                field.Direction.FromProto()))), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.Cross:
                    ProcessInputs(current, [current.Cross.Left, current.Cross.Right], inputs, destination, inputCount,
                        relations => new Cross(this.Metadata(current.Cross.Common, current.Cross.AdvancedExtension), relations[0], relations[1]), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.Join:
                    ProcessInputs(current, [current.Join.Left, current.Join.Right], inputs, destination, inputCount,
                        relations => this.CreateJoin(current.Join, relations[0], relations[1], enclosingSchemas), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.HashJoin:
                    ProcessInputs(current, [current.HashJoin.Left, current.HashJoin.Right], inputs, destination, inputCount,
                        relations => this.CreateHashJoin(current.HashJoin, relations[0], relations[1], enclosingSchemas), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.Set:
                    ProcessInputs(current, current.Set.Inputs, inputs, destination, inputCount,
                        relations => new Set(this.Metadata(current.Set.Common, current.Set.AdvancedExtension), current.Set.Op.FromProto(), relations), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.Exchange:
                    ProcessSingleInput(current, current.Exchange.Input, inputs, destination, inputCount,
                        input => this.CreateExchange(current.Exchange, input, enclosingSchemas), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.ExtensionLeaf:
                    destination.Add(this.CreateExtension(ExtensionRelationKind.Leaf,
                        current.ExtensionLeaf.Common, current.ExtensionLeaf.Detail, []));
                    break;
                case ProtoRel.RelTypeOneofCase.ExtensionSingle:
                    ProcessSingleInput(current,
                        current.ExtensionSingle.Input ?? throw new SerializationException("ExtensionSingle requires an input relation."),
                        inputs, destination, inputCount,
                        input => this.CreateExtension(ExtensionRelationKind.Single,
                            current.ExtensionSingle.Common, current.ExtensionSingle.Detail, [input]), stack);
                    break;
                case ProtoRel.RelTypeOneofCase.ExtensionMulti:
                    ProcessInputs(current, current.ExtensionMulti.Inputs, inputs, destination, inputCount,
                        relations => this.CreateExtension(ExtensionRelationKind.Multi,
                            current.ExtensionMulti.Common, current.ExtensionMulti.Detail, relations), stack);
                    break;
                default:
                    throw new NotImplementedException(current.RelTypeCase.ToString());
            }
        }

        if (output.Count != 1)
        {
            throw new SerializationException($"Deserialization error: Expected one relation for '{protoRel}', but found {output.Count}.");
        }

        return output[0];
    }

    private static void ProcessSingleInput(
        ProtoRel current,
        ProtoRel input,
        List<IRel> inputs,
        List<IRel> destination,
        int inputCount,
        Func<IRel, IRel> create,
        Stack<(ProtoRel Relation, List<IRel> Inputs, List<IRel> Output, int InputCount)> stack)
    {
        if (inputs.Count == inputCount)
        {
            destination.Add(create(inputs[0]));
        }
        else
        {
            stack.Push((current, inputs, destination, 1));
            stack.Push((input, new List<IRel>(), inputs, -1));
        }
    }

    private static void ProcessInputs(
        ProtoRel current,
        IReadOnlyList<ProtoRel> protoInputs,
        List<IRel> inputs,
        List<IRel> destination,
        int inputCount,
        Func<IReadOnlyList<IRel>, IRel> create,
        Stack<(ProtoRel Relation, List<IRel> Inputs, List<IRel> Output, int InputCount)> stack)
    {
        if (inputs.Count == inputCount)
        {
            destination.Add(create(inputs));
        }
        else
        {
            stack.Push((current, inputs, destination, protoInputs.Count));
            for (int index = protoInputs.Count - 1; index >= 0; index--)
            {
                stack.Push((protoInputs[index], new List<IRel>(), inputs, -1));
            }
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

    private Aggregate CreateAggregate(
        AggregateRel aggregate,
        IRel input,
        IReadOnlyList<Core.Type.ParameterizedType.Struct> enclosingSchemas)
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

        var functionArgConverter = new ProtoToFunctionArgConverter(this.expressionConverter, this.typeConverter);
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
                    function.Arguments.Select(argument => functionArgConverter.From(argument, input.RecordType, enclosingSchemas)),
                    this.typeConverter.From(function.OutputType),
                    function.Phase.FromProto(),
                    function.Invocation.FromProto(),
                    declaration),
                measure.Filter is null ? null : this.expressionConverter.From(measure.Filter, input.RecordType, enclosingSchemas));
        });

        return new Aggregate(
            this.Metadata(aggregate.Common, aggregate.AdvancedExtension),
            input,
            aggregate.GroupingExpressions.Select(expression => this.expressionConverter.From(expression, input.RecordType, enclosingSchemas)),
            aggregate.Groupings.Select(grouping => new Aggregate.Grouping(grouping.ExpressionReferences.Select(reference => (int)reference))),
            measures);
    }

    private Join CreateJoin(
        JoinRel join,
        IRel left,
        IRel right,
        IReadOnlyList<Core.Type.ParameterizedType.Struct> enclosingSchemas)
    {
        var schema = TypeFactory.REQUIRED.Struct(left.RecordType.Fields, right.RecordType.Fields);
        return new Join(
            this.Metadata(join.Common, join.AdvancedExtension),
            left,
            right,
            join.Type.FromProto(),
            join.Expression is null ? null : this.expressionConverter.From(join.Expression, schema, enclosingSchemas),
            join.PostJoinFilter is null ? null : this.expressionConverter.From(join.PostJoinFilter, schema, enclosingSchemas));
    }

    private HashJoin CreateHashJoin(
        HashJoinRel hashJoin,
        IRel left,
        IRel right,
        IReadOnlyList<Core.Type.ParameterizedType.Struct> enclosingSchemas)
    {
        var schema = TypeFactory.REQUIRED.Struct(left.RecordType.Fields, right.RecordType.Fields);
        return new HashJoin(
            this.Metadata(hashJoin.Common, hashJoin.AdvancedExtension),
            left,
            right,
            hashJoin.Type.FromProto(),
            hashJoin.Keys.Select(key => new PhysicalJoin.ComparisonJoinKey(
                this.expressionConverter.CreateReference(key.Left, schema, enclosingSchemas),
                this.expressionConverter.CreateReference(key.Right, schema, enclosingSchemas),
                key.Comparison.HasSimple
                    ? new PhysicalJoin.ComparisonJoinKey.ComparisonType(key.Comparison.Simple.FromProto())
                    : new PhysicalJoin.ComparisonJoinKey.ComparisonType(key.Comparison.CustomFunctionReference))),
            hashJoin.PostJoinFilter is null ? null : this.expressionConverter.From(hashJoin.PostJoinFilter, schema, enclosingSchemas),
            EnumUtils.Cast<HashJoinRel.Types.BuildInput, HashJoin.BuildInput>(hashJoin.BuildInput));
    }

    private Exchange CreateExchange(
        ExchangeRel exchange,
        IRel input,
        IReadOnlyList<Core.Type.ParameterizedType.Struct> enclosingSchemas)
    {
        if (exchange.Targets.Count != 0)
        {
            throw new NotImplementedException("Exchange targets are not supported.");
        }

        return exchange.ExchangeKindCase switch
        {
            ExchangeRel.ExchangeKindOneofCase.ScatterByFields => new ScatterExchange(
                this.Metadata(exchange.Common, exchange.AdvancedExtension),
                input,
                exchange.PartitionCount,
                exchange.ScatterByFields.Fields.Select(field => this.expressionConverter.CreateReference(field, input.RecordType, enclosingSchemas))),
            ExchangeRel.ExchangeKindOneofCase.SingleTarget => new SingleBucketExchange(
                this.Metadata(exchange.Common, exchange.AdvancedExtension),
                input,
                exchange.PartitionCount,
                this.expressionConverter.From(exchange.SingleTarget.Expression, input.RecordType, enclosingSchemas)),
            _ => throw new NotImplementedException(exchange.ExchangeKindCase.ToString()),
        };
    }

    private IRel CreateRead(ReadRel read, IReadOnlyList<Core.Type.ParameterizedType.Struct> enclosingSchemas)
    {
        Core.Type.NamedStruct schema = this.CreateNamedStruct(read);
        IExpression? filter = read.Filter is null
            ? null
            : this.expressionConverter.From(read.Filter, schema.Struct, enclosingSchemas);

        return read.ReadTypeCase switch
        {
            ReadRel.ReadTypeOneofCase.NamedTable => new NamedTableRead(
                this.Metadata(read.Common, read.AdvancedExtension), schema, read.NamedTable.Names, filter,
                RelationMetadata.CaptureExtension(read.NamedTable.AdvancedExtension, this.ownsMetadata)),
            ReadRel.ReadTypeOneofCase.VirtualTable => new VirtualTableRead(
                this.Metadata(read.Common, read.AdvancedExtension),
                schema,
                read.VirtualTable.Expressions.Select(row => new StructExpression(row.Fields.Select(this.expressionConverter.From))),
                filter),
            ReadRel.ReadTypeOneofCase.None => new EmptyRead(this.Metadata(read.Common, read.AdvancedExtension), schema, filter),
            _ => throw new NotImplementedException(read.ReadTypeCase.ToString()),
        };
    }

    private Core.Type.NamedStruct CreateNamedStruct(ReadRel read)
    {
        return new Core.Type.NamedStruct(
            read.BaseSchema.Names.ToImmutableList(),
            new Core.Type.ParameterizedType.Struct(
                read.BaseSchema.Struct.Types_.Select(this.typeConverter.From).ToImmutableList(),
                read.BaseSchema.Struct.Nullability.FromProto()));
    }
}
