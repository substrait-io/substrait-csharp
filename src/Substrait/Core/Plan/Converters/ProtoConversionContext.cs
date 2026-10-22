// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Core.Expression;
using Substrait.Core.Expression.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoRel = Substrait.Protobuf.Rel;

namespace Substrait.Core.Plan.Converters;

internal interface IProtoConverter
{
    void Convert(ProtoConversionContext.Frame frame, ProtoConversionContext context);
}

internal sealed class ProtoConversionContext
{
    private readonly Stack<Frame> pending = new();
    private readonly List<IRel> relations = new();
    private readonly List<IExpression> expressions = new();

    internal enum Phase
    {
        Enter,
        Inputs,
        Complete,
    }

    internal readonly record struct Frame(
        IMessage Node,
        IProtoConverter Converter,
        IReadOnlyList<ParameterizedType.Struct> EnclosingSchemas,
        ParameterizedType.Struct? InputSchema = null,
        Phase Phase = Phase.Enter,
        int RelationStart = 0,
        int ExpressionStart = 0);

    internal IRel Convert(ProtoToRelConverter converter, ProtoRel relation, IReadOnlyList<ParameterizedType.Struct> enclosingSchemas)
    {
        this.Run(new(relation, converter, enclosingSchemas));
        return this.relations.Count == 1 && this.expressions.Count == 0
            ? this.relations[0]
            : throw new SerializationException("Relation conversion did not produce exactly one result.");
    }

    internal IExpression Convert(
        ProtoToExpressionConverter converter, ProtoExpression expression,
        ParameterizedType.Struct inputSchema, IReadOnlyList<ParameterizedType.Struct> enclosingSchemas)
    {
        this.Run(new(expression, converter, enclosingSchemas, inputSchema));
        return this.expressions.Count == 1 && this.relations.Count == 0
            ? this.expressions[0]
            : throw new SerializationException("Expression conversion did not produce exactly one result.");
    }

    internal void PushRelation(IProtoConverter converter, ProtoRel relation, IReadOnlyList<ParameterizedType.Struct> enclosingSchemas) =>
        this.pending.Push(new(relation, converter, enclosingSchemas));

    internal void PushExpression(
        ProtoToExpressionConverter converter, ProtoExpression expression,
        ParameterizedType.Struct inputSchema, IReadOnlyList<ParameterizedType.Struct> enclosingSchemas) =>
        this.pending.Push(new(expression, converter, enclosingSchemas, inputSchema));

    internal void Resume(Frame frame, Phase phase, ParameterizedType.Struct? schema = null) =>
        this.pending.Push(frame with { Phase = phase, InputSchema = schema ?? frame.InputSchema });

    internal bool ScheduleInputs(Frame frame, ProtoRel input)
    {
        if (frame.Phase != Phase.Enter)
        {
            return false;
        }

        this.Resume(frame, Phase.Inputs);
        this.PushRelation(frame.Converter, input, frame.EnclosingSchemas);
        return true;
    }

    internal bool ScheduleInputs(Frame frame, ProtoRel left, ProtoRel right)
    {
        if (frame.Phase != Phase.Enter)
        {
            return false;
        }

        this.Resume(frame, Phase.Inputs);
        this.PushRelation(frame.Converter, right, frame.EnclosingSchemas);
        this.PushRelation(frame.Converter, left, frame.EnclosingSchemas);
        return true;
    }

    internal bool ScheduleInputs(Frame frame, IReadOnlyList<ProtoRel> inputs)
    {
        if (frame.Phase != Phase.Enter)
        {
            return false;
        }

        this.Resume(frame, Phase.Inputs);
        for (int index = inputs.Count - 1; index >= 0; --index)
        {
            this.PushRelation(frame.Converter, inputs[index], frame.EnclosingSchemas);
        }

        return true;
    }

    internal IRel Relation(Frame frame, int index = 0) => this.relations[frame.RelationStart + index];

    internal IExpression Expression(Frame frame, int index = 0) => this.expressions[frame.ExpressionStart + index];

    internal ImmutableList<IRel> CopyRelations(Frame frame, int count)
    {
        var values = ImmutableList.CreateBuilder<IRel>();
        for (int index = 0; index < count; ++index)
        {
            values.Add(this.Relation(frame, index));
        }

        return values.ToImmutable();
    }

    internal ImmutableList<IExpression> CopyExpressions(Frame frame, int count, int offset = 0)
    {
        var values = ImmutableList.CreateBuilder<IExpression>();
        for (int index = 0; index < count; ++index)
        {
            values.Add(this.Expression(frame, offset + index));
        }

        return values.ToImmutable();
    }

    internal void Complete(Frame frame, IRel result)
    {
        this.ReleaseInputs(frame);
        this.relations.Add(result);
    }

    internal void Complete(Frame frame, IExpression result)
    {
        this.ReleaseInputs(frame);
        this.expressions.Add(result);
    }

    private void Run(Frame frame)
    {
        while (true)
        {
            // Offsets are captured when work starts, not when siblings are scheduled.
            if (frame.Phase == Phase.Enter)
            {
                frame = frame with { RelationStart = this.relations.Count, ExpressionStart = this.expressions.Count };
            }

            try
            {
                frame.Converter.Convert(frame, this);
            }
            catch (ExtensionSchemaUnavailableException error) when (frame.Node is ProtoRel relation)
            {
                throw new SerializationException($"Cannot convert {relation.RelTypeCase}: {error.Message}", error);
            }

            if (this.pending.Count == 0)
            {
                return;
            }

            frame = this.pending.Pop();
        }
    }

    private void ReleaseInputs(Frame frame)
    {
        this.relations.RemoveRange(frame.RelationStart, this.relations.Count - frame.RelationStart);
        this.expressions.RemoveRange(frame.ExpressionStart, this.expressions.Count - frame.ExpressionStart);
    }
}
