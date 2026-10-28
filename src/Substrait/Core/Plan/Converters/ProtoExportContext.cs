// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Expr = Substrait.Core.Expression.Expression;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoRel = Substrait.Protobuf.Rel;

namespace Substrait.Core.Plan.Converters;

internal sealed class ProtoExportContext
{
    private readonly PlanToProtoConverter.ConverterContext context;
    private readonly int maxDepth;
    private readonly Stack<Frame> pending = new();
    private readonly List<Child> children = new();
    private readonly List<(IRel Node, ProtoRel Output)> relations = new();
    private readonly List<(IExpression Node, ProtoExpression Output)> expressions = new();
    private int nextRelation;
    private int nextExpression;

    internal ProtoExportContext(PlanToProtoConverter.ConverterContext context, int maxDepth)
    {
        this.context = context;
        this.maxDepth = maxDepth;
    }

    internal ProtoRel Convert(IRel root, Protobuf.PlanRel entry, RelToProtoConverter converter)
    {
        this.pending.Push(new(root, 1));
        while (this.pending.Count > 0)
        {
            Frame frame = this.pending.Pop();
            if (!frame.Complete)
            {
                bool detach = frame.Depth > this.maxDepth;
                frame = frame with
                {
                    Depth = detach ? 1 : frame.Depth,
                    Detach = detach,
                    Complete = true,
                    RelationStart = this.relations.Count,
                    ExpressionStart = this.expressions.Count,
                };
                this.pending.Push(frame);
                this.children.AddRange(Children(frame.Node));
                for (int index = this.children.Count - 1; index >= 0; --index)
                {
                    Child child = this.children[index];
                    this.pending.Push(new(child.Node, child.Wrapped ? frame.Depth + 1L : frame.Depth));
                }

                this.children.Clear();
                continue;
            }

            this.nextRelation = frame.RelationStart;
            this.nextExpression = frame.ExpressionStart;
            if (frame.Node is IRel relation)
            {
                ProtoRel result = converter.ConvertSingle(relation, this.context);
                this.ReleaseChildren(frame);
                if (frame.Detach)
                {
                    uint ordinal = (uint)entry.DetachedRels.Count;
                    entry.DetachedRels.Add(result);
                    result = new() { DetachedRelOrdinal = ordinal };
                }

                this.relations.Add((relation, result));
            }
            else if (frame.Node is IExpression expression)
            {
                ProtoExpression result = converter.ConvertSingle(expression, this.context);
                this.ReleaseChildren(frame);
                if (frame.Detach)
                {
                    uint ordinal = (uint)entry.DetachedExpressions.Count;
                    entry.DetachedExpressions.Add(result);
                    result = new() { DetachedExpressionOrdinal = ordinal };
                }

                this.expressions.Add((expression, result));
            }
            else
            {
                throw new InvalidOperationException("Export traversal encountered a missing or unsupported node.");
            }
        }

        if (this.relations.Count != 1 || this.expressions.Count != 0)
        {
            throw new InvalidOperationException("Export traversal did not produce exactly one relation.");
        }

        ProtoRel output = this.relations[0].Output;
        this.relations.Clear();
        return output;
    }

    internal ProtoRel GetOutput(IRel node)
    {
        if (this.nextRelation >= this.relations.Count || !ReferenceEquals(node, this.relations[this.nextRelation].Node))
        {
            throw new InvalidOperationException("Relation serialization does not match its scheduled children.");
        }

        return this.relations[this.nextRelation++].Output;
    }

    internal ProtoExpression GetOutput(IExpression node)
    {
        if (this.nextExpression >= this.expressions.Count || !ReferenceEquals(node, this.expressions[this.nextExpression].Node))
        {
            throw new InvalidOperationException("Expression serialization does not match its scheduled children.");
        }

        return this.expressions[this.nextExpression++].Output;
    }

    private static IEnumerable<Child> Children(object node)
    {
        if (node is IRel relation)
        {
            if (relation is Reference)
            {
                yield break;
            }

            foreach (IRel input in relation.Inputs)
            {
                yield return new(input);
            }

            foreach (Child expression in RelationExpressions(relation))
            {
                yield return expression;
            }
        }
        else if (node is IExpression expression)
        {
            foreach (IExpression input in expression.InputNodes)
            {
                yield return new(input, Wrapped: expression is not Literal);
            }

            IRel? subquery = expression switch
            {
                Expr.ScalarSubquery scalar => scalar.Subquery,
                Expr.InPredicateSubquery predicate => predicate.Subquery,
                Expr.SetPredicateSubquery predicate => predicate.Subquery,
                Expr.SetComparisonSubquery comparison => comparison.Subquery,
                _ => null,
            };
            if (subquery is not null)
            {
                yield return new(subquery);
            }
        }
    }

    private static IEnumerable<Child> RelationExpressions(IRel relation)
    {
        // Match the serializer's consumption order. Not every core expression has
        // an Expression wrapper on the wire (virtual rows and physical keys).
        switch (relation)
        {
            case Read read:
                if (read.Filter is not null)
                {
                    yield return new(read.Filter);
                }

                if (read is VirtualTableRead table)
                {
                    foreach (var row in table.Rows)
                    {
                        foreach (IExpression field in row.Fields)
                        {
                            yield return new(field);
                        }
                    }
                }

                break;
            case Filter filter:
                yield return new(filter.Condition);
                break;
            case Project project:
                foreach (IExpression expression in project.Expressions)
                {
                    yield return new(expression);
                }

                break;
            case Fetch fetch:
                yield return new(fetch.Count);
                yield return new(fetch.Offset);
                break;
            case Sort sort:
                foreach (SortField field in sort.SortFields)
                {
                    yield return new(field.Expr);
                }

                break;
            case Aggregate aggregate:
                foreach (IExpression expression in aggregate.GroupingExpressions)
                {
                    yield return new(expression);
                }

                foreach (Aggregate.Measure measure in aggregate.Measures)
                {
                    foreach (IExpression argument in measure.Function.Arguments.OfType<IExpression>())
                    {
                        yield return new(argument);
                    }

                    if (measure.PreMeasureFilter is not null)
                    {
                        yield return new(measure.PreMeasureFilter);
                    }
                }

                break;
            case Join join:
                if (join.Condition is not null)
                {
                    yield return new(join.Condition);
                }

                if (join.PostJoinFilter is not null)
                {
                    yield return new(join.PostJoinFilter);
                }

                break;
            case HashJoin join:
                if (join.PostJoinFilter is not null)
                {
                    yield return new(join.PostJoinFilter);
                }

                foreach (PhysicalJoin.ComparisonJoinKey key in join.Keys)
                {
                    yield return new(key.Left, Wrapped: false);
                    yield return new(key.Right, Wrapped: false);
                }

                break;
            case SingleBucketExchange exchange:
                yield return new(exchange.Expression);
                break;
            case ScatterExchange exchange:
                foreach (FieldReference field in exchange.Fields)
                {
                    yield return new(field, Wrapped: false);
                }

                break;
        }
    }

    private void ReleaseChildren(Frame frame)
    {
        if (this.nextRelation != this.relations.Count || this.nextExpression != this.expressions.Count)
        {
            throw new InvalidOperationException("Serialization did not consume all scheduled children.");
        }

        this.relations.RemoveRange(frame.RelationStart, this.relations.Count - frame.RelationStart);
        this.expressions.RemoveRange(frame.ExpressionStart, this.expressions.Count - frame.ExpressionStart);
    }

    private readonly record struct Child(object Node, bool Wrapped = true);

    private readonly record struct Frame(
        object Node, long Depth, bool Complete = false, bool Detach = false,
        int RelationStart = 0, int ExpressionStart = 0);
}
