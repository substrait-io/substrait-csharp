// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Core.Expression;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Extension.Types;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using Substrait.Protobuf;
using CoreExpression = Substrait.Core.Expression.Expression;
using CorePlan = Substrait.Core.Plan.Plan;
using PlanVersion = Substrait.Core.Plan.Version;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoPlan = Substrait.Protobuf.Plan;
using ProtoRel = Substrait.Protobuf.Rel;

namespace Substrait.Tests.Core;

public sealed class PlanReferenceConversionTests
{
    [Test]
    public async Task RoundTripsIndependentInterleavedEntriesIncludingEmptyRootNames()
    {
        CorePlan plan = CorePlan.FromRelations(
            [
                new CorePlan.Root(CreateRead("first"), ["renamed"]),
                new CorePlan.Relation(CreateRead("shared")),
                new CorePlan.Root(CreateRead("last"), []),
            ],
            new PlanVersion(1, 2, 3, "hash", "tests"));

        ProtoPlan wire = await AssertRoundTrips(plan);
        IPlan converted = Deserialize(wire);

        await Assert.That(converted.Relations.Count).IsEqualTo(3);
        await Assert.That(converted.Roots.Count).IsEqualTo(2);
        await Assert.That(wire.Relations[0].RelTypeCase).IsEqualTo(PlanRel.RelTypeOneofCase.Root);
        await Assert.That(wire.Relations[1].RelTypeCase).IsEqualTo(PlanRel.RelTypeOneofCase.Rel);
        await Assert.That(wire.Relations[2].RelTypeCase).IsEqualTo(PlanRel.RelTypeOneofCase.Root);
        await Assert.That(wire.Relations[2].Root.Names.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RoundTripsNonRootOnlyPlansAndDoesNotInventReferencesForInlineReuse()
    {
        NamedTableRead read = CreateRead();
        CorePlan plan = CorePlan.FromRelations(
            [new CorePlan.Relation(new Cross(read, read)), new CorePlan.Relation(read)], PlanVersion.Current);

        ProtoPlan wire = await AssertRoundTrips(plan);

        await Assert.That(Deserialize(wire).Roots.Count).IsEqualTo(0);
        await Assert.That(wire.Relations[0].Rel.Cross.Left.RelTypeCase).IsEqualTo(ProtoRel.RelTypeOneofCase.Read);
        await Assert.That(wire.Relations[0].Rel.Cross.Right.RelTypeCase).IsEqualTo(ProtoRel.RelTypeOneofCase.Read);
        await Assert.That(wire.Relations[1].Rel.RelTypeCase).IsEqualTo(ProtoRel.RelTypeOneofCase.Read);
        await Assert.That(wire.Relations.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RepeatedSameAndEqualRegistrationsRemainIndependentWireEntries()
    {
        NamedTableRead read = CreateRead();
        PlanBuilder builder = new();
        builder.RegisterSubplan(read);
        builder.RegisterSubplan(read);
        builder.RegisterSubplan(CreateRead());
        builder.AddRoot(read, ["output"]);
        builder.AddRoot(read, ["output"]);

        ProtoPlan wire = await AssertRoundTrips(builder.Build());
        IPlan converted = Deserialize(wire);

        await Assert.That(wire.Relations.Count).IsEqualTo(5);
        for (int ordinal = 0; ordinal < wire.Relations.Count; ++ordinal)
        {
            ProtoRel relation = ordinal < 3 ? wire.Relations[ordinal].Rel : wire.Relations[ordinal].Root.Input;
            await Assert.That(relation.RelTypeCase).IsEqualTo(ProtoRel.RelTypeOneofCase.Read);
            await Assert.That(converted.Relations[ordinal].Input).IsAssignableTo<NamedTableRead>();
            await Assert.That(converted.Relations[ordinal].Input).IsEqualTo(converted.Relations[0].Input);
            for (int previous = 0; previous < ordinal; ++previous)
            {
                await Assert.That(converted.Relations[ordinal].Input).IsNotSameReferenceAs(converted.Relations[previous].Input);
            }
        }
    }

    [Test]
    public async Task ResolvesForwardBackwardAndRootReferencesWithSharedTargetIdentity()
    {
        ProtoPlan wire = CreateWirePlan(
            new PlanRel { Root = new RelRoot { Input = WireCross(WireReference(2), WireReference(2)), Names = { "left", "right" } } },
            new PlanRel { Rel = WireReference(0) },
            new PlanRel { Root = new RelRoot { Input = WireRead(), Names = { "renamed" } } },
            new PlanRel { Rel = WireReference(1) });

        IPlan plan = Deserialize(wire);
        Cross first = (Cross)plan.Relations[0].Input;
        Reference left = (Reference)first.Left;
        Reference right = (Reference)first.Right;
        Reference second = (Reference)plan.Relations[1].Input;
        Reference last = (Reference)plan.Relations[3].Input;

        await Assert.That(left.SubtreeOrdinal).IsEqualTo(2);
        await Assert.That(left.Target).IsSameReferenceAs(plan.Relations[2].Input);
        await Assert.That(right.Target).IsSameReferenceAs(left.Target);
        await Assert.That(second.Target).IsSameReferenceAs(first);
        await Assert.That(last.Target).IsSameReferenceAs(second);
        await Assert.That(left.RecordType).IsEqualTo(plan.Relations[2].Input.RecordType);
        await Assert.That(last.RecordType.Fields.Count).IsEqualTo(2);
        await Assert.That(((NamedTableRead)left.Target).InitialSchema.Names[0]).IsEqualTo("value");
        await AssertRoundTrips(plan);
    }

    [Test]
    public async Task FactorySupportsAdvancedForwardReferenceComposition()
    {
        NamedTableRead target = CreateRead();
        Reference forward = new(1, target);
        CorePlan plan = CorePlan.FromRelations(
            [new CorePlan.Root(forward, []), new CorePlan.Relation(target)], PlanVersion.Current);

        ProtoPlan wire = await AssertRoundTrips(plan);

        await Assert.That(wire.Relations[0].Root.Input.Reference.SubtreeOrdinal).IsEqualTo(1);
        await Assert.That(forward.Target).IsSameReferenceAs(plan.Relations[1].Input);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(1)]
    [Arguments(int.MaxValue)]
    public async Task RejectsInvalidWireReferenceOrdinals(int ordinal)
    {
        ProtoPlan wire = CreateWirePlan(new PlanRel { Rel = WireReference(ordinal) });

        await Assert.That(() => Deserialize(wire)).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task RejectsUnsetPlanAndRelationVariants()
    {
        await Assert.That(() => Deserialize(CreateWirePlan(new PlanRel()))).ThrowsExactly<SerializationException>();
        await Assert.That(() => Deserialize(CreateWirePlan(new PlanRel { Rel = new ProtoRel() }))).ThrowsExactly<SerializationException>();
        await Assert.That(() => Deserialize(CreateWirePlan(new PlanRel { Root = new RelRoot() }))).ThrowsExactly<SerializationException>();
        await Assert.That(() => Deserialize(CreateWirePlan(
            new PlanRel { Root = new RelRoot { Input = new ProtoRel() } }))).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task RejectsSelfCyclesAndMultiEntryCycles()
    {
        await Assert.That(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireReference(0) }))).ThrowsExactly<SerializationException>();
        await Assert.That(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireReference(1) },
            new PlanRel { Root = new RelRoot { Input = WireReference(0) } }))).ThrowsExactly<SerializationException>();
        await Assert.That(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireReference(1) },
            new PlanRel { Rel = WireReference(2) },
            new PlanRel { Rel = WireReference(0) }))).ThrowsExactly<SerializationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ResolvesDependenciesHiddenInsideEverySubqueryKind(int kind)
    {
        ProtoPlan wire = CreateWirePlan(
            new PlanRel { Root = new RelRoot { Input = WireProject(WireSubquery(kind, WireReference(2))) } },
            new PlanRel { Rel = WireRead() },
            new PlanRel { Rel = WireReference(1) });

        IPlan plan = Deserialize(wire);
        Project project = (Project)plan.Relations[0].Input;
        Reference reference = (Reference)GetSubquery(project.Expressions[0]);

        await Assert.That(reference.SubtreeOrdinal).IsEqualTo(2);
        await Assert.That(reference.Target).IsSameReferenceAs(plan.Relations[2].Input);
        await Assert.That(((Reference)reference.Target).Target).IsSameReferenceAs(plan.Relations[1].Input);
        await AssertRoundTrips(plan);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task RejectsCyclesAndMissingDependenciesHiddenInsideEverySubqueryKind(int kind)
    {
        await Assert.That(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireProject(WireSubquery(kind, WireReference(0))) }))).ThrowsExactly<SerializationException>();
        await Assert.That(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireProject(WireSubquery(kind, WireReference(1))) },
            new PlanRel { Rel = WireReference(0) }))).ThrowsExactly<SerializationException>();
        await Assert.That(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireProject(WireSubquery(kind, WireReference(9))) }))).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task ResolvesDependenciesInSubqueryNeedlesAndComparisonOperands()
    {
        ProtoExpression scalar = WireSubquery(0, WireReference(1));
        ProtoExpression predicate = WireSubquery(1, WireRead());
        predicate.Subquery.InPredicate.Needles.Clear();
        predicate.Subquery.InPredicate.Needles.Add(scalar);
        ProtoExpression comparison = WireSubquery(3, WireRead());
        comparison.Subquery.SetComparison.Left = scalar.Clone();
        ProtoPlan wire = CreateWirePlan(
            new PlanRel { Root = new RelRoot { Input = WireProject(predicate, comparison) } },
            new PlanRel { Rel = WireRead() });

        IPlan plan = Deserialize(wire);
        Project project = (Project)plan.Relations[0].Input;
        var convertedPredicate = (CoreExpression.InPredicateSubquery)project.Expressions[0];
        var convertedComparison = (CoreExpression.SetComparisonSubquery)project.Expressions[1];
        Reference needle = (Reference)((CoreExpression.ScalarSubquery)convertedPredicate.Values[0]).Subquery;
        Reference operand = (Reference)((CoreExpression.ScalarSubquery)convertedComparison.Expression).Subquery;

        await Assert.That(needle.Target).IsSameReferenceAs(plan.Relations[1].Input);
        await Assert.That(operand.Target).IsSameReferenceAs(needle.Target);
        await AssertRoundTrips(plan);

        wire.Relations[1].Rel = WireReference(0);
        await Assert.That(() => Deserialize(wire)).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task EachEntryHasItsOwnCorrelationBoundaryEvenWhenUsedAsASubquery()
    {
        ProtoRel correlated = WireProject(WireOuterField(1));
        ProtoPlan invalid = CreateWirePlan(
            new PlanRel { Rel = correlated },
            new PlanRel { Root = new RelRoot { Input = WireProject(WireSubquery(0, WireReference(0))) } });

        await Assert.That(() => Deserialize(invalid)).ThrowsExactly<SerializationException>();

        ProtoPlan valid = CreateWirePlan(
            new PlanRel { Rel = WireProject(WireSubquery(0, correlated)) },
            new PlanRel { Root = new RelRoot { Input = WireReference(0) } });

        await AssertRoundTrips(Deserialize(valid));
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 2)]
    [Arguments(2, 3)]
    public async Task RejectsWireCorrelationEscapingEntryNesting(int depth, int levels)
    {
        ProtoRel relation = WireProject(WireOuterField((uint)levels));
        for (int i = 0; i < depth; ++i)
        {
            relation = WireProject(WireSubquery(0, relation));
        }

        await Assert.That(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = relation }))).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task StandaloneConvertersRejectReferencesWithoutPlanContext()
    {
        ProtoToRelConverter reader = new(
            new ExtensionsDictionary.Builder().Build(), new ExtensionsCollection(), ExtensionsDictionary.StrictMode.OFF);
        Reference reference = new(0, CreateRead());

        await Assert.That(() => reader.ToRel(WireReference(0))).ThrowsExactly<SerializationException>();
        await Assert.That(() => new RelToProtoConverter().From(reference)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => reader.ToRel(WireProject(WireSubquery(0, WireReference(0))))).ThrowsExactly<SerializationException>();
        await Assert.That(() => new RelToProtoConverter().From(
            new Project(CreateRead(), [new CoreExpression.ScalarSubquery(reference, TypeFactory.REQUIRED.I64)]))).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task ResolvesLongReferenceChainsWithoutRecursion(bool forward, bool wrapReferences)
    {
        const int count = 10001;
        ProtoPlan wire = CreateWirePlan();
        for (int i = 0; i < count; ++i)
        {
            bool terminal = forward ? i == count - 1 : i == 0;
            ProtoRel relation = terminal ? WireRead() : WireReference(forward ? i + 1 : i - 1);
            if (wrapReferences && !terminal)
            {
                relation = new ProtoRel
                {
                    Filter = new()
                    {
                        Input = relation,
                        Condition = new() { Literal = new() { Boolean = true } },
                        Common = new RelCommon(),
                    },
                };
            }

            wire.Relations.Add(new PlanRel { Rel = relation });
        }

        IPlan plan = Deserialize(wire);
        IRel head = plan.Relations[forward ? 0 : count - 1].Input;

        await Assert.That(plan.Relations.Count).IsEqualTo(count);
        await Assert.That(head.RecordType.Fields[0]).IsEqualTo(TypeFactory.REQUIRED.I64);
        for (int i = 0; i < count - 1; ++i)
        {
            Reference reference = (Reference)(head is Filter filter ? filter.Input : head);
            await Assert.That(reference.Target).IsSameReferenceAs(plan.Relations[reference.SubtreeOrdinal].Input);
            head = reference.Target;
        }

        await Assert.That(head).IsAssignableTo<NamedTableRead>();
        ProtoPlan serialized = new PlanToProtoConverter().From(plan);
        IPlan roundTrip = Deserialize(ProtoPlan.Parser.ParseFrom(serialized.ToByteArray()));
        await Assert.That(roundTrip).IsEqualTo(plan);
        await Assert.That(roundTrip.GetHashCode()).IsEqualTo(plan.GetHashCode());
        await Assert.That(serialized.Relations.Count).IsEqualTo(count);
    }

    [Test]
    public async Task CollectsSharedExtensionAnchorsAcrossAllEntriesDeterministically()
    {
        var variation = new TypeVariationImpl("extension:example:types", "i64", "custom", "", FunctionBehavior.INHERITS);
        NamedTableRead read = new(
            new Substrait.Core.Type.NamedStruct(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64_(variation)])),
            ["orders"],
            null);
        CoreExpression.ScalarFunctionInvocation identity = new(
            "extension:example:functions", "identity:i64", [new Literal.I64Literal(1)], TypeFactory.REQUIRED.I64, null);
        CoreExpression.ScalarFunctionInvocation another = new(
            "extension:example:other", "another:i64", [new Literal.I64Literal(2)], TypeFactory.REQUIRED.I64, null);
        PlanBuilder builder = new();
        Reference shared = builder.RegisterSubplan(new Project(read, [identity]));
        builder.AddRoot(new Project(shared, [identity, another]), []);
        builder.RegisterSubplan(new Project(read, [another]));
        CorePlan plan = builder.Build();

        ProtoPlan wire = new PlanToProtoConverter().From(plan);
        ExtensionsCollection extensions = new([variation], [], [], []);
        ProtoPlan[] roundTrips =
        [
            ProtoPlan.Parser.ParseFrom(wire.ToByteArray()),
            JsonParser.Default.Parse<ProtoPlan>(JsonFormatter.Default.Format(wire)),
        ];
        foreach (ProtoPlan roundTrip in roundTrips)
        {
            IPlan converted = new ProtoToPlanConverter(extensions).From(roundTrip, ExtensionsDictionary.StrictMode.OFF);
            await Assert.That(converted.Relations[0].Input.RecordType.Fields[0].TypeVariation).IsEqualTo(variation);
            await Assert.That(new PlanToProtoConverter().From(converted).ToByteArray()).IsEquivalentTo(wire.ToByteArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }

        SimpleExtensionDeclaration[] functions = wire.Extensions.Where(extension => extension.ExtensionFunction is not null).ToArray();
        SimpleExtensionDeclaration[] variations = wire.Extensions.Where(extension => extension.ExtensionTypeVariation is not null).ToArray();

        await Assert.That(wire.ExtensionUrns.Count).IsEqualTo(3);
        await Assert.That(functions.Length).IsEqualTo(2);
        await Assert.That(variations.Length).IsEqualTo(1);
        await Assert.That(variations[0].ExtensionTypeVariation.TypeVariationAnchor).IsEqualTo(1U);
        await Assert.That(wire.Relations[1].Root.Input.Project.Expressions[0].ScalarFunction.FunctionReference).IsEqualTo(wire.Relations[0].Rel.Project.Expressions[0].ScalarFunction.FunctionReference);
        await Assert.That(wire.Relations[2].Rel.Project.Expressions[0].ScalarFunction.FunctionReference).IsEqualTo(wire.Relations[1].Root.Input.Project.Expressions[1].ScalarFunction.FunctionReference);
        await Assert.That(functions.Select(extension => extension.ExtensionFunction.FunctionAnchor).Distinct().Count()).IsEqualTo(2);
        foreach (SimpleExtensionDeclaration function in functions)
        {
            await Assert.That(wire.ExtensionUrns.Any(urn => urn.ExtensionUrnAnchor == function.ExtensionFunction.ExtensionUrnReference)).IsTrue();
        }

        var serializer = new PlanToProtoConverter();
        ProtoPlan unrelated = serializer.From(new CorePlan([new CorePlan.Root(CreateRead(), [])], PlanVersion.Current));
        await Assert.That(unrelated.Extensions.Count).IsEqualTo(0);
        await Assert.That(unrelated.ExtensionUrns.Count).IsEqualTo(0);
        await Assert.That(serializer.From(plan).ToByteArray()).IsEquivalentTo(wire.ToByteArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task EmptyWirePlansRemainDeserializableButCannotBeSerialized()
    {
        IPlan plan = Deserialize(CreateWirePlan());

        await Assert.That(plan.Relations.Count).IsEqualTo(0);
        await Assert.That(plan.Roots.Count).IsEqualTo(0);
        await Assert.That(() => new PlanToProtoConverter().From(plan)).ThrowsExactly<ArgumentException>();
    }

    private static async Task<ProtoPlan> AssertRoundTrips(IPlan plan)
    {
        PlanToProtoConverter serializer = new();
        ProtoPlan wire = serializer.From(plan);
        byte[] bytes = wire.ToByteArray();
        await Assert.That(serializer.From(plan).ToByteArray()).IsEquivalentTo(bytes, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        ProtoPlan[] roundTrips =
        [
            ProtoPlan.Parser.ParseFrom(bytes),
            JsonParser.Default.Parse<ProtoPlan>(JsonFormatter.Default.Format(wire)),
        ];
        foreach (ProtoPlan roundTrip in roundTrips)
        {
            IPlan converted = Deserialize(roundTrip);
            await Assert.That(converted).IsEqualTo(plan);
            await Assert.That(converted.GetHashCode()).IsEqualTo(plan.GetHashCode());
            await Assert.That(serializer.From(converted).ToByteArray()).IsEquivalentTo(bytes, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }

        return wire;
    }

    private static IPlan Deserialize(ProtoPlan plan) =>
        new ProtoToPlanConverter(new ExtensionsCollection()).From(plan, ExtensionsDictionary.StrictMode.OFF);

    private static NamedTableRead CreateRead(string name = "orders") =>
        new(new Substrait.Core.Type.NamedStruct(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])), [name], null);

    private static ProtoPlan CreateWirePlan(params PlanRel[] relations)
    {
        ProtoPlan plan = new()
        {
            Version = new Substrait.Protobuf.Version { MajorNumber = 1, MinorNumber = 2, PatchNumber = 3, Producer = "tests" },
        };
        plan.Relations.AddRange(relations);
        return plan;
    }

    private static ProtoRel WireRead() => new RelToProtoConverter().From(CreateRead());

    private static ProtoRel WireReference(int ordinal) => new() { Reference = new() { SubtreeOrdinal = ordinal } };

    private static ProtoRel WireCross(ProtoRel left, ProtoRel right) =>
        new() { Cross = new() { Left = left, Right = right, Common = new RelCommon() } };

    private static ProtoRel WireProject(params ProtoExpression[] expressions)
    {
        ProjectRel project = new()
        {
            Input = WireRead(),
            Common = new RelCommon { Emit = new() { OutputMapping = { 1 } } },
        };
        project.Expressions.AddRange(expressions);
        return new ProtoRel { Project = project };
    }

    internal static ProtoExpression WireSubquery(int kind, ProtoRel relation) => kind switch
    {
        0 => new() { Subquery = new() { Scalar = new() { Input = relation } } },
        1 => new()
        {
            Subquery = new()
            {
                InPredicate = new()
                {
                    Haystack = relation,
                    Needles = { new ProtoExpression { Literal = new() { I64 = 1 } } },
                },
            },
        },
        2 => new()
        {
            Subquery = new()
            {
                SetPredicate = new()
                {
                    Tuples = relation,
                    PredicateOp = ProtoExpression.Types.Subquery.Types.SetPredicate.Types.PredicateOp.Exists,
                },
            },
        },
        3 => new()
        {
            Subquery = new()
            {
                SetComparison = new()
                {
                    Left = new() { Literal = new() { I64 = 1 } },
                    Right = relation,
                    ComparisonOp = ProtoExpression.Types.Subquery.Types.SetComparison.Types.ComparisonOp.Eq,
                    ReductionOp = ProtoExpression.Types.Subquery.Types.SetComparison.Types.ReductionOp.Any,
                },
            },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static ProtoExpression WireOuterField(uint levels) => new()
    {
        Selection = new()
        {
            DirectReference = new() { StructField = new() { Field = 0 } },
#pragma warning disable CS0612 // Exercise the supported legacy offset representation.
            OuterReference = new() { StepsOut = levels },
#pragma warning restore CS0612
        },
    };

    internal static IRel GetSubquery(IExpression expression) => expression switch
    {
        CoreExpression.ScalarSubquery scalar => scalar.Subquery,
        CoreExpression.InPredicateSubquery predicate => predicate.Subquery,
        CoreExpression.SetPredicateSubquery predicate => predicate.Subquery,
        CoreExpression.SetComparisonSubquery comparison => comparison.Subquery,
        _ => throw new ArgumentException("Expected a subquery.", nameof(expression)),
    };
}
