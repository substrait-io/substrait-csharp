// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Runtime.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Substrait.Tools;
using ProtoOuterReference = Substrait.Protobuf.Expression.Types.FieldReference.Types.OuterReference;
using ProtoSubquery = Substrait.Protobuf.Expression.Types.Subquery;

namespace Substrait.Core.Plan.Converters;

internal static class ProtoPlanDependencies
{
    internal static IReadOnlyList<int> Find(Protobuf.Rel input, int ordinal, int count)
    {
        var dependencies = new HashSet<int>();
        var active = new HashSet<IMessage>(ReferenceEqualityComparer.Instance);
        var visited = new Dictionary<IMessage, int>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(IMessage Message, int Depth, bool Complete)>();
        stack.Push((input, 0, false));
        while (stack.Count > 0)
        {
            var (message, depth, complete) = stack.Pop();
            if (complete)
            {
                active.Remove(message);
                visited[message] = depth;
                continue;
            }

            if (active.Contains(message))
            {
                throw new SerializationException($"Plan relation ordinal {ordinal} contains a cycle in its protobuf tree.");
            }

            if (visited.TryGetValue(message, out int previousDepth) && previousDepth <= depth)
            {
                continue;
            }

            active.Add(message);
            stack.Push((message, depth, true));
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

            // Inspect all message fields so dependencies inside expression subqueries
            // are discovered as well as ordinary relation inputs.
            foreach (IMessage child in Children(message))
            {
                bool nested = child is Protobuf.Rel && message is
                    ProtoSubquery.Types.Scalar or ProtoSubquery.Types.InPredicate or
                    ProtoSubquery.Types.SetPredicate or ProtoSubquery.Types.SetComparison;
                stack.Push((child, nested ? depth + 1 : depth, false));
            }
        }

        return dependencies.ToArray();
    }

    private static SerializationException Error(string message) => new(message);

    private static IEnumerable<IMessage> Children(IMessage message)
    {
        foreach (FieldDescriptor field in message.Descriptor.Fields.InFieldNumberOrder())
        {
            if (field.FieldType != FieldType.Message)
            {
                continue;
            }

            object value = field.Accessor.GetValue(message);
            if (value is IMessage child)
            {
                yield return child;
            }
            else if (value is IDictionary map)
            {
                foreach (object item in map.Values)
                {
                    if (item is IMessage mapChild)
                    {
                        yield return mapChild;
                    }
                }
            }
            else if (value is IEnumerable repeated)
            {
                foreach (IMessage item in repeated)
                {
                    yield return item;
                }
            }
        }
    }
}
