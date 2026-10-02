// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Protobuf;
using Substrait.Tools;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoLiteral = Substrait.Protobuf.Expression.Types.Literal;
using ProtoMask = Substrait.Protobuf.Expression.Types.MaskExpression;
using ProtoOuterReference = Substrait.Protobuf.Expression.Types.FieldReference.Types.OuterReference;
using ProtoSubquery = Substrait.Protobuf.Expression.Types.Subquery;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Core.Plan.Converters;

internal static class ProtoPlanDependencies
{
    internal static IReadOnlyList<int> Find(Protobuf.Rel input, int ordinal, int count, Dictionary<uint, string> anchors)
    {
        var dependencies = new HashSet<int>();
        var active = new HashSet<IMessage>(ReferenceEqualityComparer.Instance);
        int nextOccurrence = 0;
        var stack = new Stack<(IMessage Message, int Depth, bool Complete, int Occurrence)>();
        stack.Push((input, 0, false, 0));
        while (stack.Count > 0)
        {
            var (message, depth, complete, occurrence) = stack.Pop();
            if (complete)
            {
                active.Remove(message);
                continue;
            }

            if (!active.Add(message))
            {
                throw new SerializationException($"Plan relation ordinal {ordinal} contains a cycle in its protobuf tree.");
            }

            if (message is Protobuf.Rel)
            {
                occurrence = nextOccurrence++;
            }

            stack.Push((message, depth, true, occurrence));
            if (message is Protobuf.RelCommon { HasRelAnchor: true } common)
            {
                PlanValidation.RegisterAnchor(common.RelAnchor, anchors, $"Plan relation ordinal {ordinal}, relation occurrence {occurrence}", Error);
            }
            if (message is Protobuf.Rel { RelTypeCase: Protobuf.Rel.RelTypeOneofCase.None })
            {
                throw new SerializationException($"Plan relation ordinal {ordinal} contains an unset relation variant.");
            }

            if (message is Protobuf.ReferenceRel reference)
            {
                PlanValidation.ValidateOrdinal(reference.SubtreeOrdinal, count, $"Plan relation ordinal {ordinal}", Error);
                dependencies.Add(reference.SubtreeOrdinal);
            }

            if (message is ProtoOuterReference outer)
            {
                if (outer.OuterReferenceTypeCase == ProtoOuterReference.OuterReferenceTypeOneofCase.RelReference)
                {
                    throw new NotSupportedException($"Plan relation ordinal {ordinal}: relation-anchor outer references are not supported.");
                }

#pragma warning disable CS0612 // Retain support for the offset-based internal reference model.
                uint steps = outer.StepsOut;
#pragma warning restore CS0612
                if (steps == 0 || steps > depth)
                {
                    throw new SerializationException($"Plan relation ordinal {ordinal}: outer reference steps {steps} is outside subquery depth {depth}. Caller-dependent correlation across plan entries is not supported.");
                }
            }

            foreach (IMessage? child in Children(message))
            {
                if (child is null)
                {
                    continue;
                }

                bool nested = child is Protobuf.Rel && message is
                    ProtoSubquery.Types.Scalar or ProtoSubquery.Types.InPredicate or
                    ProtoSubquery.Types.SetPredicate or ProtoSubquery.Types.SetComparison;
                stack.Push((child, nested ? depth + 1 : depth, false, occurrence));
            }
        }

        return dependencies.ToArray();
    }

    private static SerializationException Error(string message) => new(message);

    private static IEnumerable<IMessage?> Children(IMessage message) => message switch
    {
        Protobuf.Rel relation => GetRelationChildren(relation),
        ProtoExpression expression => GetExpressionChildren(expression),
        _ => GetNestedChildren(message),
    };

    private static IEnumerable<IMessage?> GetRelationChildren(Protobuf.Rel relation)
    {
        switch (relation.RelTypeCase)
        {
            case Protobuf.Rel.RelTypeOneofCase.Read:
                ReadRel read = relation.Read;
                yield return read.Common;
                yield return read.BaseSchema?.Struct;
                yield return read.Filter;
                yield return read.Projection;
                if (read.VirtualTable is not null)
                {
                    foreach (var row in read.VirtualTable.Expressions)
                    {
                        yield return row;
                    }
                }

                yield return read.BestEffortFilter;
                break;
            case Protobuf.Rel.RelTypeOneofCase.Filter:
                yield return relation.Filter.Common;
                yield return relation.Filter.Input;
                yield return relation.Filter.Condition;
                break;
            case Protobuf.Rel.RelTypeOneofCase.Project:
                yield return relation.Project.Common;
                yield return relation.Project.Input;
                foreach (ProtoExpression expression in relation.Project.Expressions)
                {
                    yield return expression;
                }

                break;
            case Protobuf.Rel.RelTypeOneofCase.Fetch:
                yield return relation.Fetch.Common;
                yield return relation.Fetch.Input;
                yield return relation.Fetch.OffsetExpr;
                yield return relation.Fetch.CountExpr;
                break;
            case Protobuf.Rel.RelTypeOneofCase.Sort:
                yield return relation.Sort.Common;
                yield return relation.Sort.Input;
                foreach (SortField field in relation.Sort.Sorts)
                {
                    yield return field.Expr;
                }

                break;
            case Protobuf.Rel.RelTypeOneofCase.Aggregate:
                yield return relation.Aggregate.Common;
                yield return relation.Aggregate.Input;
                foreach (var measure in relation.Aggregate.Measures)
                {
                    yield return measure.Measure_;
                    yield return measure.Filter;
                }

                foreach (ProtoExpression expression in relation.Aggregate.GroupingExpressions)
                {
                    yield return expression;
                }

                break;
            case Protobuf.Rel.RelTypeOneofCase.Cross:
                yield return relation.Cross.Common;
                yield return relation.Cross.Left;
                yield return relation.Cross.Right;
                break;
            case Protobuf.Rel.RelTypeOneofCase.Join:
                yield return relation.Join.Common;
                yield return relation.Join.Left;
                yield return relation.Join.Right;
                yield return relation.Join.Expression;
                yield return relation.Join.PostJoinFilter;
                break;
            case Protobuf.Rel.RelTypeOneofCase.HashJoin:
                yield return relation.HashJoin.Common;
                yield return relation.HashJoin.Left;
                yield return relation.HashJoin.Right;
                yield return relation.HashJoin.PostJoinFilter;
                foreach (ComparisonJoinKey key in relation.HashJoin.Keys)
                {
                    yield return key.Left;
                    yield return key.Right;
                }

                yield return relation.HashJoin.ResidualExpression;
                break;
            case Protobuf.Rel.RelTypeOneofCase.Set:
                yield return relation.Set.Common;
                foreach (Protobuf.Rel input in relation.Set.Inputs)
                {
                    yield return input;
                }

                break;
            case Protobuf.Rel.RelTypeOneofCase.Exchange:
                yield return relation.Exchange.Common;
                yield return relation.Exchange.Input;
                if (relation.Exchange.ScatterByFields is not null)
                {
                    foreach (var field in relation.Exchange.ScatterByFields.Fields)
                    {
                        yield return field;
                    }
                }

                yield return relation.Exchange.SingleTarget?.Expression;
                yield return relation.Exchange.MultiTarget?.Expression;
                break;
            case Protobuf.Rel.RelTypeOneofCase.Reference:
                yield return relation.Reference;
                break;
            default:
                throw new NotImplementedException(relation.RelTypeCase.ToString());
        }
    }

    private static IEnumerable<IMessage?> GetExpressionChildren(ProtoExpression expression)
    {
        switch (expression.RexTypeCase)
        {
            case ProtoExpression.RexTypeOneofCase.Literal:
                yield return expression.Literal;
                break;
            case ProtoExpression.RexTypeOneofCase.Selection:
                yield return expression.Selection;
                break;
            case ProtoExpression.RexTypeOneofCase.ScalarFunction:
                yield return expression.ScalarFunction.OutputType;
                foreach (FunctionArgument argument in expression.ScalarFunction.Arguments)
                {
                    yield return argument.Type;
                    yield return argument.Value;
                }

                break;
            case ProtoExpression.RexTypeOneofCase.Cast:
                yield return expression.Cast.Type;
                yield return expression.Cast.Input;
                break;
            case ProtoExpression.RexTypeOneofCase.IfThen:
                foreach (var clause in expression.IfThen.Ifs)
                {
                    yield return clause.If;
                    yield return clause.Then;
                }

                yield return expression.IfThen.Else;
                break;
            case ProtoExpression.RexTypeOneofCase.Subquery:
                ProtoSubquery subquery = expression.Subquery;
                yield return subquery.SubqueryTypeCase switch
                {
                    ProtoSubquery.SubqueryTypeOneofCase.Scalar => subquery.Scalar,
                    ProtoSubquery.SubqueryTypeOneofCase.InPredicate => subquery.InPredicate,
                    ProtoSubquery.SubqueryTypeOneofCase.SetPredicate => subquery.SetPredicate,
                    ProtoSubquery.SubqueryTypeOneofCase.SetComparison => subquery.SetComparison,
                    _ => throw new NotImplementedException(subquery.SubqueryTypeCase.ToString()),
                };
                break;
            default:
                throw new NotImplementedException(expression.RexTypeCase.ToString());
        }
    }

    private static IEnumerable<IMessage?> GetNestedChildren(IMessage message)
    {
        switch (message)
        {
            case RelCommon:
            case ReferenceRel:
            case ProtoOuterReference:
                // Metadata beyond the anchor is acyclic and cannot contain dependencies.
                break;
            case AggregateFunction function:
                foreach (SortField field in function.Sorts)
                {
                    yield return field.Expr;
                }

                yield return function.OutputType;
                foreach (FunctionArgument argument in function.Arguments)
                {
                    yield return argument.Type;
                    yield return argument.Value;
                }

                break;
            case ProtoExpression.Types.Nested.Types.Struct row:
                foreach (ProtoExpression field in row.Fields)
                {
                    yield return field;
                }

                break;
            case ProtoSubquery.Types.Scalar scalar:
                yield return scalar.Input;
                break;
            case ProtoSubquery.Types.InPredicate predicate:
                foreach (ProtoExpression needle in predicate.Needles)
                {
                    yield return needle;
                }

                yield return predicate.Haystack;
                break;
            case ProtoSubquery.Types.SetPredicate predicate:
                yield return predicate.Tuples;
                break;
            case ProtoSubquery.Types.SetComparison comparison:
                yield return comparison.Left;
                yield return comparison.Right;
                break;
            case ProtoExpression.Types.FieldReference reference:
                yield return reference.DirectReference;
                yield return reference.MaskedReference;
                yield return reference.Expression;
                yield return reference.OuterReference;
                break;
            // Recursive schema, literal, and selection messages still need object-cycle checks.
            case ProtoExpression.Types.ReferenceSegment segment:
                yield return segment.MapKey?.MapKey_;
                yield return segment.MapKey?.Child;
                yield return segment.StructField?.Child;
                yield return segment.ListElement?.Child;
                break;
            case ProtoMask mask:
                yield return mask.Select;
                break;
            case ProtoMask.Types.Select select:
                yield return select.Struct;
                yield return select.List?.Child;
                yield return select.Map?.Child;
                break;
            case ProtoMask.Types.StructSelect select:
                foreach (var item in select.StructItems)
                {
                    yield return item.Child;
                }

                break;
            case ProtoType type:
                yield return type.Struct;
                yield return type.List;
                yield return type.Map;
                yield return type.UserDefined;
                yield return type.Func;
                break;
            case ProtoType.Types.Struct structure:
                foreach (ProtoType field in structure.Types_)
                {
                    yield return field;
                }

                break;
            case ProtoType.Types.List list:
                yield return list.Type;
                break;
            case ProtoType.Types.Map map:
                yield return map.Key;
                yield return map.Value;
                break;
            case ProtoType.Types.UserDefined userDefined:
                foreach (var parameter in userDefined.TypeParameters)
                {
                    yield return parameter.DataType;
                }

                break;
            case ProtoType.Types.Func function:
                foreach (ProtoType parameter in function.ParameterTypes)
                {
                    yield return parameter;
                }

                yield return function.ReturnType;
                break;
            case ProtoLiteral literal:
                yield return literal.Struct;
                yield return literal.Map;
                yield return literal.Null;
                yield return literal.List;
                yield return literal.EmptyList;
                yield return literal.EmptyMap;
                yield return literal.UserDefined;
                break;
            case ProtoLiteral.Types.Struct structure:
                foreach (ProtoLiteral field in structure.Fields)
                {
                    yield return field;
                }

                break;
            case ProtoLiteral.Types.List list:
                foreach (ProtoLiteral value in list.Values)
                {
                    yield return value;
                }

                break;
            case ProtoLiteral.Types.Map map:
                foreach (var entry in map.KeyValues)
                {
                    yield return entry.Key;
                    yield return entry.Value;
                }

                break;
            case ProtoLiteral.Types.UserDefined userDefined:
                foreach (var parameter in userDefined.TypeParameters)
                {
                    yield return parameter.DataType;
                }

                yield return userDefined.Struct;
                break;
            default:
                throw new NotImplementedException(message.GetType().FullName);
        }
    }
}
