// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Expression;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using Substrait.Protobuf;
using CorePlan = Substrait.Core.Plan.Plan;
using Expr = Substrait.Core.Expression.Expression;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoPlan = Substrait.Protobuf.Plan;
using ProtoRel = Substrait.Protobuf.Rel;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class BoundedPlanSerializationTests
{
    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    public async Task RejectsNonPositiveDepth(int depth)
    {
        ArgumentOutOfRangeException error = await Assert.That(() => new PlanToProtoConverter().From(Plan(Read()), depth))
            .ThrowsExactly<ArgumentOutOfRangeException>().And.IsNotNull();
        await Assert.That(error.ParamName).IsEqualTo("maxInlineRelExpressionDepth");
    }

    [Test]
    [Arguments(1, 1, 2)]
    [Arguments(2, 0, 1)]
    [Arguments(3, 0, 0)]
    [Arguments(int.MaxValue, 0, 0)]
    public async Task CountsCombinedNodesAndCutsExactlyAtTheBoundary(int depth, int relations, int expressions)
    {
        CorePlan plan = Plan(Project(Cast(Integer(7))));
        var writer = new PlanToProtoConverter();
        ProtoPlan before = writer.From(plan);
        ProtoPlan wire = writer.From(plan, depth);
        await Assert.That(wire.Relations[0].DetachedRels.Count).IsEqualTo(relations);
        await Assert.That(wire.Relations[0].DetachedExpressions.Count).IsEqualTo(expressions);
        await AssertEncoding(wire, depth);
        await AssertRoundTrip(plan, wire);
        await Assert.That(writer.From(plan)).IsEqualTo(before);
        if (relations == 0 && expressions == 0)
        {
            await Assert.That(wire.ToByteArray().SequenceEqual(before.ToByteArray())).IsTrue();
        }
    }

    [Test]
    [Arguments(1, 2, 2)]
    [Arguments(3, 1, 0)]
    [Arguments(4, 0, 0)]
    public async Task SubqueriesDoNotResetDepthUnlessDetached(int depth, int relations, int expressions)
    {
        CorePlan plan = Plan(Project(Cast(Scalar(Read()))));
        ProtoPlan wire = new PlanToProtoConverter().From(plan, depth);
        await Assert.That(wire.Relations[0].DetachedRels.Count).IsEqualTo(relations);
        await Assert.That(wire.Relations[0].DetachedExpressions.Count).IsEqualTo(expressions);
        await AssertEncoding(wire, depth);
        await AssertRoundTrip(plan, wire);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    public async Task AllSupportedRelationsKeepTheirSerializedSemantics(int depth)
    {
        foreach (IRel relation in RelationMetadataConversionTests.Operators(RelationMetadata.Direct))
        {
            CorePlan plan = Plan(relation);
            ProtoPlan wire = new PlanToProtoConverter().From(plan, depth);
            await AssertEncoding(wire, depth);
            await AssertRoundTrip(plan, wire);
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(5)]
    public async Task AllSubqueriesAndConditionalOperandsShareOneDepthBudget(int depth)
    {
        IRel inner = Project(new FieldReference(TypeFactory.REQUIRED.I64, 0, 1));
        IExpression[] expressions =
        [
            Scalar(inner),
            new Expr.InPredicateSubquery(inner, [Integer(1), Cast(Integer(2))]),
            new Expr.SetPredicateSubquery(inner, Expr.SetPredicateSubquery.PredicateOp.Exists),
            new Expr.SetComparisonSubquery(Cast(Integer(3)), Expr.SetComparisonSubquery.ComparisonOp.Equal,
                Expr.SetComparisonSubquery.ReductionOp.Any, inner),
            new Expr.IfThen([(new Literal.BoolLiteral(true), Cast(Scalar(inner))),
                (new Literal.BoolLiteral(false), Integer(4))], Integer(5)),
            new Expr.ScalarFunctionInvocation("extension:test:bounded", "test:i64",
                [Cast(Integer(6)), TypeFactory.REQUIRED.I64, Scalar(inner)], TypeFactory.REQUIRED.I64, null),
        ];
        CorePlan plan = Plan(new Project(Read(), expressions));
        var writer = new PlanToProtoConverter();
        ProtoPlan wire = writer.From(plan, depth);
        await AssertEncoding(wire, depth);
        await AssertRoundTrip(plan, wire);
        await Assert.That(writer.From(plan, depth).ToByteArray().SequenceEqual(wire.ToByteArray())).IsTrue();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SharedInstancesReceiveDistinctDetachedOccurrences(int depth)
    {
        IExpression expression = Cast(Cast(Integer(7)));
        IRel shared = new Project(Read(), [expression, expression]);
        CorePlan plan = Plan(new Set(Set.SetOp.UnionAll, [shared, new Filter(shared, new Literal.BoolLiteral(true)), shared]));
        ProtoPlan wire = new PlanToProtoConverter().From(plan, depth);
        await AssertEncoding(wire, depth);
        await AssertRoundTrip(plan, wire);
    }

    [Test]
    public async Task WideSiblingsKeepTheirOutputOrder()
    {
        const int width = 256;
        CorePlan plan = Plan(new Set(Set.SetOp.UnionAll,
            Enumerable.Range(0, width).Select(index => Project(Cast(Scalar(Project(Integer(index))))))));
        ProtoPlan wire = new PlanToProtoConverter().From(plan, 2);
        await AssertEncoding(wire, 2);
        await AssertRoundTrip(plan, wire);
    }

    [Test]
    [Arguments("expressions")]
    [Arguments("relations")]
    [Arguments("mixed")]
    public async Task DeepPlansSerializeAndParseWithoutRaisingParserLimits(string kind)
    {
        const int depth = 10000;
        IRel relation = Read();
        IExpression expression = Integer(7);
        for (int index = 0; index < depth; ++index)
        {
            if (kind == "expressions")
            {
                expression = Cast(expression);
            }
            else if (kind == "relations")
            {
                relation = new Filter(relation, new Literal.BoolLiteral(true));
            }
            else
            {
                relation = Project(Cast(Scalar(relation)));
            }
        }

        CorePlan plan = Plan(kind == "expressions" ? Project(expression) : relation);
        ProtoPlan wire = new PlanToProtoConverter().From(plan, 8);
        await AssertEncoding(wire, 8);
        byte[] bytes = wire.ToByteArray();
        string json = JsonFormatter.Default.Format(wire);
        IPlan[] parsed = [Reader().FromBytes(bytes), Reader().FromJson(json)];
        foreach (IPlan imported in parsed)
        {
            IRel current = imported.Relations[0].Input;
            if (kind == "expressions")
            {
                IExpression value = ((Project)current).Expressions[0];
                for (int index = 0; index < depth; ++index)
                {
                    value = ((Expr.Cast)value).Input;
                }

                await Assert.That(((Literal.I64Literal)value).Value).IsEqualTo(7L);
            }
            else
            {
                for (int index = 0; index < depth; ++index)
                {
                    current = kind == "relations" ? ((Filter)current).Input
                        : ((Expr.ScalarSubquery)((Expr.Cast)((Project)current).Expressions[0]).Input).Subquery;
                }

                await Assert.That(current).IsAssignableTo<NamedTableRead>();
            }
        }
    }

    [Test]
    public async Task RetainedTablesArePreservedAndExcludedFromTheBound()
    {
        ProtoExpression retained = new() { Literal = new() { I64 = 9 } };
        for (int index = 0; index < 8; ++index)
        {
            retained = new()
            {
                Cast = new()
                {
                    Input = retained,
                    Type = new() { I64 = new() { Nullability = ProtoType.Types.Nullability.Required } },
                },
            };
        }

        PlanRel tables = new()
        {
            DetachedExpressions = { retained, new ProtoExpression { DetachedExpressionOrdinal = uint.MaxValue } },
            DetachedRels = { new ProtoRel() },
        };
        PlanRelationMetadata encoding = PlanRelationMetadata.FromProto(tables);
        RelationMetadata metadata = new(advancedExtension: ReadOnlyAdvancedExtension.FromProto(new()
        {
            Enhancement = Any.Pack(new ProtoExpression { DetachedExpressionOrdinal = 0 }),
        }));
        IRel relation = new Project(metadata, Read(), [Cast(Integer(7))]);
        IPlan plan = CorePlan.FromRelations([new CorePlan.Root(encoding, relation, ["value"])], Substrait.Core.Plan.Version.Current);
        var writer = new PlanToProtoConverter();
        ProtoPlan wire = writer.From(plan, 1);
        await AssertEncoding(wire, 1, retainedExpressions: 2, retainedRelations: 1);
        await Assert.That(wire.Relations[0].DetachedExpressions.Take(2).SequenceEqual(tables.DetachedExpressions)).IsTrue();
        await Assert.That(wire.Relations[0].DetachedRels[0]).IsEqualTo(tables.DetachedRels[0]);
        await Assert.That(wire.Relations[0].Root.Input.Project.AdvancedExtension.Enhancement.Unpack<ProtoExpression>().DetachedExpressionOrdinal).IsEqualTo(0U);
        await AssertRoundTrip(plan, wire);

        byte[] expected = wire.ToByteArray();
        wire.Relations[0].DetachedExpressions[0].Cast.Input = new() { Literal = new() { I64 = 100 } };
        await Assert.That(writer.From(plan, 1).ToByteArray().SequenceEqual(expected)).IsTrue();
        await Assert.That(encoding.ToProto()).IsEqualTo(tables);
    }

    [Test]
    public async Task PlanReferencesAndContainerOrdinalsRemainSeparate()
    {
        var builder = new PlanBuilder();
        Reference reference = builder.RegisterSubplan(Project(Cast(Integer(7))));
        builder.AddRoot(new Filter(reference, new Literal.BoolLiteral(true)), ["value"]);
        IPlan plan = builder.Build();
        ProtoPlan wire = new PlanToProtoConverter().From(plan, 1);
        await AssertEncoding(wire, 1);
        await AssertRoundTrip(plan, wire);
        await Assert.That(wire.Relations[1].DetachedRels.Single().Reference.SubtreeOrdinal).IsEqualTo(0);
        IPlan imported = Reader().FromBytes(wire.ToByteArray(), ExtensionsDictionary.StrictMode.OFF);
        await Assert.That(((Reference)((Filter)imported.Roots[0].Input).Input).Target).IsSameReferenceAs(imported.Relations[0].Input);
    }

    [Test]
    public async Task ReexportAppendsWithoutRenumberingPreviouslyGeneratedTables()
    {
        CorePlan original = Plan(Project(Cast(Scalar(Project(Cast(Integer(7)))))));
        var writer = new PlanToProtoConverter();
        ProtoPlan first = writer.From(original, 2);
        IPlan imported = Reader().FromBytes(first.ToByteArray());
        ProtoPlan second = writer.From(imported, 1);
        int expressions = first.Relations[0].DetachedExpressions.Count;
        int relations = first.Relations[0].DetachedRels.Count;
        await AssertEncoding(second, 1, expressions, relations);
        await Assert.That(second.Relations[0].DetachedExpressions.Take(expressions).SequenceEqual(first.Relations[0].DetachedExpressions)).IsTrue();
        await Assert.That(second.Relations[0].DetachedRels.Take(relations).SequenceEqual(first.Relations[0].DetachedRels)).IsTrue();
        await AssertRoundTrip(original, second);
    }

    [Test]
    public async Task RelationAnchorsStayOnTheDetachedConcreteNode()
    {
        RelationMetadata metadata = new(ReadOnlyRelCommon.FromProto(new() { RelAnchor = 42 }));
        CorePlan plan = Plan(new Filter(RelationMetadataConversionTests.Read(metadata), new Literal.BoolLiteral(true)));
        ProtoPlan wire = new PlanToProtoConverter().From(plan, 1);
        await Assert.That(wire.Relations[0].DetachedRels[0].Read.Common.RelAnchor).IsEqualTo(42U);
        await AssertEncoding(wire, 1);
        await AssertRoundTrip(plan, wire);
    }

    [Test]
    public async Task EnumArgumentsArePreservedWithoutBecomingDetachedExpressions()
    {
        var option = new EnumArgumentValue(new Substrait.Core.Extension.Functions.EnumArgument(), "option");
        var expression = new Expr.ScalarFunctionInvocation("extension:test:bounded", "test:i64",
            [Integer(7), option, TypeFactory.REQUIRED.I64, Integer(9)], TypeFactory.REQUIRED.I64, null);
        ProtoPlan wire = new PlanToProtoConverter().From(Plan(Project(expression)), 1);
        await AssertEncoding(wire, 1);
        var function = wire.Relations[0].DetachedExpressions.Single(value => value.ScalarFunction is not null).ScalarFunction;
        await Assert.That(function.Arguments[1].Enum).IsEqualTo("option");
        await Assert.That(function.Arguments[2].Type.I64.Nullability).IsEqualTo(ProtoType.Types.Nullability.Required);
        await Assert.That(wire.Relations[0].DetachedExpressions.Count).IsEqualTo(3);
    }

    [Test]
    public async Task LogicalDepthDoesNotPromiseAParserLimitForNestedTypes()
    {
        IType type = TypeFactory.REQUIRED.I64;
        for (int index = 0; index < 128; ++index)
        {
            type = TypeFactory.REQUIRED.List(type);
        }

        var schema = new Substrait.Core.Type.NamedStruct(["value"], TypeFactory.REQUIRED.Struct([type]));
        CorePlan plan = Plan(new NamedTableRead(schema, ["items"], null));
        ProtoPlan wire = new PlanToProtoConverter().From(plan, 1);
        await AssertEncoding(wire, 1);
        await Assert.That(wire.Relations[0].DetachedRels.Count).IsEqualTo(0);
        await Assert.That(wire.Relations[0].DetachedExpressions.Count).IsEqualTo(0);
        await Assert.That(() => ProtoPlan.Parser.ParseFrom(wire.ToByteArray())).ThrowsExactly<InvalidProtocolBufferException>();
        await Assert.That(Reader().From(wire).Relations[0].Input).IsEqualTo(plan.Relations[0].Input);
    }

    [Test]
    public async Task NestedLiteralsAndPhysicalKeysDoNotAddExpressionWrappers()
    {
        var literal = new Literal.StructLiteral([new Literal.I64Literal(1), new Literal.StructLiteral([new Literal.StrLiteral("nested")])]);
        IRel read = Read();
        var field = new FieldReference(TypeFactory.REQUIRED.I64, 0);
        IRel[] relations =
        [
            Project(literal),
            new ScatterExchange(read, 2, [field]),
            new HashJoin(read, Read(), AbstractJoin.JoinType.Inner,
                [new PhysicalJoin.ComparisonJoinKey(field, field,
                    new PhysicalJoin.ComparisonJoinKey.ComparisonType(PhysicalJoin.ComparisonJoinKey.SimpleComparisonType.Eq))],
                null, HashJoin.BuildInput.Left),
        ];
        foreach (IRel relation in relations)
        {
            CorePlan plan = Plan(relation);
            ProtoPlan wire = new PlanToProtoConverter().From(plan, 1);
            await AssertEncoding(wire, 1);
            await AssertRoundTrip(plan, wire);
            await Assert.That(wire.Relations[0].DetachedExpressions.Count).IsEqualTo(relation is Project ? 1 : 0);
        }
    }

    [Test]
    public async Task VirtualRowsAndAggregateArgumentsKeepTheirOrder()
    {
        IRel read = Read();
        var schema = new Substrait.Core.Type.NamedStruct(["first", "second"],
            TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, TypeFactory.REQUIRED.I64]));
        var function = new AggregateFunctionInvocation("extension:test:bounded", "test:i64",
            [Cast(Integer(11)), TypeFactory.REQUIRED.I64, Scalar(Project(Integer(12)))], TypeFactory.REQUIRED.I64,
            AggregateFunctionInvocation.AggregationPhase.InitialToResult, AggregateFunctionInvocation.AggregationInvocation.All, null);
        IRel[] relations =
        [
            new VirtualTableRead(schema, [new Expr.Struct([Integer(1), Cast(Integer(2))]),
                new Expr.Struct([Scalar(Project(Integer(3))), Integer(4)])], new Literal.BoolLiteral(true)),
            new Aggregate(read, [Cast(Integer(10))], [new Aggregate.Grouping([0])],
                [new Aggregate.Measure(function, new Literal.BoolLiteral(true)), new Aggregate.Measure(function, new Literal.BoolLiteral(false))]),
            new Join(read, Read(), AbstractJoin.JoinType.Inner, new Literal.BoolLiteral(true), new Literal.BoolLiteral(false)),
        ];
        foreach (IRel relation in relations)
        {
            CorePlan plan = Plan(relation);
            ProtoPlan wire = new PlanToProtoConverter().From(plan, 1);
            await AssertEncoding(wire, 1);
            await AssertRoundTrip(plan, wire);
        }
    }

    [Test]
    public async Task WriterCanBeReusedAfterFailureAndConcurrently()
    {
        var writer = new PlanToProtoConverter();
        await Assert.That(() => writer.From(Plan(Project(new Expr.Struct([Integer(1)]))), 1)).ThrowsExactly<NotImplementedException>();
        ProtoPlan[] outputs = await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
            Task.Run(() => writer.From(Plan(Project(Cast(Integer(index)))), 1))));
        for (int index = 0; index < outputs.Length; ++index)
        {
            await AssertEncoding(outputs[index], 1);
            var project = (Project)Reader().FromBytes(outputs[index].ToByteArray()).Roots[0].Input;
            await Assert.That(((Literal.I64Literal)((Expr.Cast)project.Expressions[0]).Input).Value).IsEqualTo((long)index);
        }
    }

    private static async Task AssertRoundTrip(IPlan expected, ProtoPlan wire)
    {
        TypeRegistry registry = TypeRegistry.FromMessages(ProtoExpression.Descriptor, ProtoRel.Descriptor);
        var formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithTypeRegistry(registry));
        var parser = new JsonParser(JsonParser.Settings.Default.WithTypeRegistry(registry));
        IPlan[] results =
        [
            Reader().FromBytes(wire.ToByteArray(), ExtensionsDictionary.StrictMode.OFF),
            Reader().FromJson(formatter.Format(wire), ExtensionsDictionary.StrictMode.OFF, parser),
        ];
        foreach (IPlan result in results)
        {
            await Assert.That(result.Relations.Count).IsEqualTo(expected.Relations.Count);
            for (int index = 0; index < expected.Relations.Count; ++index)
            {
                await Assert.That(result.Relations[index].Input).IsEqualTo(expected.Relations[index].Input);
            }

            await Assert.That(result.Roots.SelectMany(root => root.Names)).IsEquivalentTo(expected.Roots.SelectMany(root => root.Names));
            await Assert.That(result.Version).IsEqualTo(expected.Version);
        }
    }

    private static async Task AssertEncoding(ProtoPlan wire, int maxDepth, int retainedExpressions = 0, int retainedRelations = 0)
    {
        foreach (PlanRel entry in wire.Relations)
        {
            var expressionUses = new HashSet<uint>();
            var relationUses = new HashSet<uint>();
            IEnumerable<IMessage> chunks = new IMessage[] { entry.Root?.Input ?? entry.Rel }
                .Concat(entry.DetachedExpressions.Skip(retainedExpressions))
                .Concat(entry.DetachedRels.Skip(retainedRelations));
            foreach (IMessage chunk in chunks)
            {
                var stack = new Stack<(IMessage Message, int Depth)>();
                stack.Push((chunk, 0));
                while (stack.TryPop(out var item))
                {
                    int depth = item.Depth;
                    if (item.Message is ProtoRel relation)
                    {
                        await Assert.That(relation.RelTypeCase).IsNotEqualTo(ProtoRel.RelTypeOneofCase.None);
                        if (relation.RelTypeCase == ProtoRel.RelTypeOneofCase.DetachedRelOrdinal)
                        {
                            await Assert.That(relation.DetachedRelOrdinal >= retainedRelations && relation.DetachedRelOrdinal < entry.DetachedRels.Count).IsTrue();
                            await Assert.That(relationUses.Add(relation.DetachedRelOrdinal)).IsTrue();
                            continue;
                        }

                        depth++;
                    }
                    else if (item.Message is ProtoExpression expression)
                    {
                        await Assert.That(expression.RexTypeCase).IsNotEqualTo(ProtoExpression.RexTypeOneofCase.None);
                        if (expression.RexTypeCase == ProtoExpression.RexTypeOneofCase.DetachedExpressionOrdinal)
                        {
                            await Assert.That(expression.DetachedExpressionOrdinal >= retainedExpressions && expression.DetachedExpressionOrdinal < entry.DetachedExpressions.Count).IsTrue();
                            await Assert.That(expressionUses.Add(expression.DetachedExpressionOrdinal)).IsTrue();
                            continue;
                        }

                        depth++;
                    }

                    await Assert.That(depth).IsLessThanOrEqualTo(maxDepth);
                    if (item.Message is ProtoExpression.Types.Literal or ProtoType or Substrait.Protobuf.NamedStruct or RelCommon or AdvancedExtension or Any)
                    {
                        continue;
                    }

                    foreach (FieldDescriptor field in item.Message.Descriptor.Fields.InFieldNumberOrder())
                    {
                        if (field.FieldType != FieldType.Message)
                        {
                            continue;
                        }

                        object value = field.Accessor.GetValue(item.Message);
                        if (field.IsRepeated)
                        {
                            foreach (IMessage child in (IList)value)
                            {
                                stack.Push((child, depth));
                            }
                        }
                        else if (value is IMessage child)
                        {
                            stack.Push((child, depth));
                        }
                    }
                }
            }

            await Assert.That(expressionUses.Count).IsEqualTo(entry.DetachedExpressions.Count - retainedExpressions);
            await Assert.That(relationUses.Count).IsEqualTo(entry.DetachedRels.Count - retainedRelations);
        }
    }

    private static Literal.I64Literal Integer(long value) => new(value);
    private static Expr.Cast Cast(IExpression expression) => new(TypeFactory.REQUIRED.I64, expression, Expr.Cast.FailureBehavior.ThrowException);
    private static Expr.ScalarSubquery Scalar(IRel relation) => new(relation, TypeFactory.REQUIRED.I64);
    private static NamedTableRead Read() => RelationMetadataConversionTests.Read();
    private static Project Project(IExpression expression) => new(Read(), [expression], new Remap([1]));
    private static CorePlan Plan(IRel relation) => RelationMetadataConversionTests.Plan(relation);
    private static ProtoToPlanConverter Reader() => new(new ExtensionsCollection(), new ExtensionSchemaResolver());

    private sealed class ExtensionSchemaResolver : IExtensionRelationSchemaResolver
    {
        public ParameterizedType.Struct? Resolve(ExtensionRelationKind kind, ReadOnlyAny? detail, IReadOnlyList<IRel> inputs) =>
            TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]);
    }
}
