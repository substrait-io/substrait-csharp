// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Substrait.Protobuf;
using ProtoExpression = Substrait.Protobuf.Expression;

namespace Substrait.Core.Plan.Converters;

internal sealed class DetachedPlanContext
{
    private readonly PlanRel entry;
    private readonly int ordinal;
    private readonly HashSet<uint> usedExpressions = new();
    private readonly HashSet<uint> usedRelations = new();

    internal DetachedPlanContext(PlanRel entry, int ordinal)
    {
        this.entry = entry;
        this.ordinal = ordinal;
    }

    internal ProtoExpression ResolveExpression(uint index, bool validateUse = false)
    {
        if (index >= this.entry.DetachedExpressions.Count)
        {
            throw this.Error("expression", index, $"is outside detached_expressions range [0, {this.entry.DetachedExpressions.Count})");
        }

        ProtoExpression result = this.entry.DetachedExpressions[(int)index];
        if (result.RexTypeCase is ProtoExpression.RexTypeOneofCase.None or ProtoExpression.RexTypeOneofCase.DetachedExpressionOrdinal)
        {
            throw this.Error("expression", index, "must have a concrete, non-ordinal expression variant");
        }

        if (validateUse && !this.usedExpressions.Add(index))
        {
            throw this.Error("expression", index, "is referenced more than once or participates in a cycle");
        }

        return result;
    }

    internal Rel ResolveRel(uint index, bool validateUse = false)
    {
        if (index >= this.entry.DetachedRels.Count)
        {
            throw this.Error("relation", index, $"is outside detached_rels range [0, {this.entry.DetachedRels.Count})");
        }

        Rel result = this.entry.DetachedRels[(int)index];
        if (result.RelTypeCase is Rel.RelTypeOneofCase.None or Rel.RelTypeOneofCase.DetachedRelOrdinal)
        {
            throw this.Error("relation", index, "must have a concrete, non-ordinal relation variant");
        }

        if (validateUse && !this.usedRelations.Add(index))
        {
            throw this.Error("relation", index, "is referenced more than once or participates in a cycle");
        }

        return result;
    }

    private SerializationException Error(string kind, uint index, string message) =>
        new($"Plan relation ordinal {this.ordinal}: detached {kind} ordinal {index} {message}.");
}
