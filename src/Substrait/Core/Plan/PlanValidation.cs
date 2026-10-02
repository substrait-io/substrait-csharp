// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Relation;
using Substrait.Tools;
using Expr = Substrait.Core.Expression.Expression;

namespace Substrait.Core.Plan;

internal static class PlanValidation
{
    internal static void Validate(IReadOnlyList<IPlan.IRelation> relations)
    {
        var dependencies = new List<IReadOnlyList<int>>(relations.Count);
        for (int ordinal = 0; ordinal < relations.Count; ++ordinal)
        {
            if (relations[ordinal]?.Input is not IRel input)
            {
                throw new ArgumentException($"Plan relation ordinal {ordinal} must have an input.");
            }

            dependencies.Add(ValidateEntry(input, ordinal, relations));
        }

        _ = GetDependencyOrder(dependencies, message => new ArgumentException(message));
    }

    internal static IReadOnlyList<int> ValidateEntry(
        IRel input,
        int ordinal,
        IReadOnlyList<IPlan.IRelation> relations,
        object? owner = null)
    {
        var dependencies = new HashSet<int>();
        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var visited = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(object Node, int Depth, bool Complete)>();
        stack.Push((input, 0, false));
        while (stack.Count > 0)
        {
            var (node, depth, complete) = stack.Pop();
            if (complete)
            {
                active.Remove(node);
                visited[node] = depth;
                continue;
            }

            if (active.Contains(node))
            {
                throw new ArgumentException($"Plan relation ordinal {ordinal} contains a cycle in its relation or expression tree.");
            }

            if (visited.TryGetValue(node, out int previousDepth) && previousDepth <= depth)
            {
                continue;
            }

            active.Add(node);
            stack.Push((node, depth, true));
            if (node is Reference reference)
            {
                if (owner is not null && !ReferenceEquals(reference.Owner, owner))
                {
                    throw new ArgumentException($"Plan relation ordinal {ordinal} contains reference ordinal {reference.SubtreeOrdinal} from another builder or without builder ownership.");
                }

                ValidateBinding(reference, relations, ordinal);
                dependencies.Add(reference.SubtreeOrdinal);
            }
            else if (node is IRel relation)
            {
                foreach (IRel child in relation.Inputs)
                {
                    stack.Push((child, depth, false));
                }

                foreach (IExpression expression in GetExpressions(relation))
                {
                    stack.Push((expression, depth, false));
                }
            }
            else if (node is IExpression expression)
            {
                if (expression is FieldReference field && (field.SubqueryLevels < 0 || field.SubqueryLevels > depth))
                {
                    throw new ArgumentException($"Plan relation ordinal {ordinal} contains caller-dependent outer reference steps {field.SubqueryLevels} at subquery depth {depth}. Correlation must be wholly contained within one plan entry.");
                }

                foreach (IExpression child in expression.InputNodes)
                {
                    stack.Push((child, depth, false));
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
                    stack.Push((subquery, depth + 1, false));
                }
            }
            else
            {
                throw new ArgumentException($"Plan relation ordinal {ordinal} contains a missing or invalid tree node.");
            }
        }

        return dependencies.ToArray();
    }

    internal static void ValidateBinding(Reference reference, IReadOnlyList<IPlan.IRelation> relations, int? sourceOrdinal = null)
    {
        string context = sourceOrdinal is int ordinal ? $"Plan relation ordinal {ordinal}" : "Plan";
        ValidateOrdinal(reference.SubtreeOrdinal, relations.Count, context, message => new ArgumentException(message));
        if (!ReferenceEquals(reference.Target, relations[reference.SubtreeOrdinal]?.Input))
        {
            throw new ArgumentException($"{context}: reference ordinal {reference.SubtreeOrdinal} is bound to a different target than that entry's input.");
        }
    }

    internal static void ValidateOrdinal(int ordinal, int count, string context, Func<string, Exception> error)
    {
        if (ordinal < 0 || ordinal >= count)
        {
            throw error($"{context}: reference ordinal {ordinal} is outside the plan entry range [0, {count}).");
        }
    }

    internal static IReadOnlyList<int> GetDependencyOrder(
        IReadOnlyList<IReadOnlyList<int>> dependencies,
        Func<string, Exception> error)
    {
        var remaining = new int[dependencies.Count];
        var dependents = new List<int>[dependencies.Count];
        for (int ordinal = 0; ordinal < dependencies.Count; ++ordinal)
        {
            dependents[ordinal] = new List<int>();
        }

        var ready = new Queue<int>();
        for (int ordinal = 0; ordinal < dependencies.Count; ++ordinal)
        {
            remaining[ordinal] = dependencies[ordinal].Count;
            if (remaining[ordinal] == 0)
            {
                ready.Enqueue(ordinal);
            }

            foreach (int target in dependencies[ordinal])
            {
                ValidateOrdinal(target, dependencies.Count, $"Plan relation ordinal {ordinal}", error);
                dependents[target].Add(ordinal);
            }
        }

        var order = new List<int>(dependencies.Count);
        while (ready.Count > 0)
        {
            int ordinal = ready.Dequeue();
            order.Add(ordinal);
            foreach (int dependent in dependents[ordinal])
            {
                if (--remaining[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        if (order.Count != dependencies.Count)
        {
            int ordinal = Array.FindIndex(remaining, count => count > 0);
            throw error($"Plan relation ordinal {ordinal} has cyclic reference dependencies (self or indirect cycle).");
        }

        return order;
    }

    private static IEnumerable<IExpression> GetExpressions(IRel relation)
    {
        switch (relation)
        {
            case Read read:
                if (read.Filter is not null)
                {
                    yield return read.Filter;
                }

                if (read is VirtualTableRead table)
                {
                    foreach (var row in table.Rows)
                    {
                        yield return row;
                    }
                }

                break;
            case Filter filter:
                yield return filter.Condition;
                break;
            case Project project:
                foreach (IExpression expression in project.Expressions)
                {
                    yield return expression;
                }

                break;
            case Fetch fetch:
                yield return fetch.Count;
                yield return fetch.Offset;
                break;
            case Sort sort:
                foreach (SortField field in sort.SortFields)
                {
                    yield return field.Expr;
                }

                break;
            case Aggregate aggregate:
                foreach (IExpression expression in aggregate.GroupingExpressions)
                {
                    yield return expression;
                }

                foreach (Aggregate.Measure measure in aggregate.Measures)
                {
                    if (measure.PreMeasureFilter is not null)
                    {
                        yield return measure.PreMeasureFilter;
                    }

                    foreach (IExpression argument in measure.Function.Arguments.OfType<IExpression>())
                    {
                        yield return argument;
                    }

                    foreach (SortField field in measure.Function.Sort)
                    {
                        yield return field.Expr;
                    }
                }

                break;
            case AbstractJoin join:
                if (join.PostJoinFilter is not null)
                {
                    yield return join.PostJoinFilter;
                }

                if (join is Join { Condition: not null } logical)
                {
                    yield return logical.Condition;
                }

                if (join is HashJoin hash)
                {
                    foreach (PhysicalJoin.ComparisonJoinKey key in hash.Keys)
                    {
                        yield return key.Left;
                        yield return key.Right;
                    }
                }

                break;
            case SingleBucketExchange exchange:
                yield return exchange.Expression;
                break;
            case ScatterExchange exchange:
                foreach (FieldReference field in exchange.Fields)
                {
                    yield return field;
                }

                break;
        }
    }
}
