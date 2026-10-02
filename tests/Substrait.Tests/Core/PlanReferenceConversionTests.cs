// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

[TestClass]
public sealed class PlanReferenceConversionTests
{
    [TestMethod]
    public void RoundTripsIndependentInterleavedEntriesIncludingEmptyRootNames()
    {
        CorePlan plan = CorePlan.FromRelations(
            [
                new CorePlan.Root(CreateRead("first"), ["renamed"]),
                new CorePlan.Relation(CreateRead("shared")),
                new CorePlan.Root(CreateRead("last"), []),
            ],
            new PlanVersion(1, 2, 3, "hash", "tests"));

        ProtoPlan wire = AssertRoundTrips(plan);
        IPlan converted = Deserialize(wire);

        Assert.AreEqual(3, converted.Relations.Count);
        Assert.AreEqual(2, converted.Roots.Count);
        Assert.AreEqual(PlanRel.RelTypeOneofCase.Root, wire.Relations[0].RelTypeCase);
        Assert.AreEqual(PlanRel.RelTypeOneofCase.Rel, wire.Relations[1].RelTypeCase);
        Assert.AreEqual(PlanRel.RelTypeOneofCase.Root, wire.Relations[2].RelTypeCase);
        Assert.AreEqual(0, wire.Relations[2].Root.Names.Count);
    }

    [TestMethod]
    public void RoundTripsNonRootOnlyPlansAndDoesNotInventReferencesForInlineReuse()
    {
        NamedTableRead read = CreateRead();
        CorePlan plan = CorePlan.FromRelations(
            [new CorePlan.Relation(new Cross(read, read)), new CorePlan.Relation(read)], PlanVersion.Current);

        ProtoPlan wire = AssertRoundTrips(plan);

        Assert.AreEqual(0, Deserialize(wire).Roots.Count);
        Assert.AreEqual(ProtoRel.RelTypeOneofCase.Read, wire.Relations[0].Rel.Cross.Left.RelTypeCase);
        Assert.AreEqual(ProtoRel.RelTypeOneofCase.Read, wire.Relations[0].Rel.Cross.Right.RelTypeCase);
        Assert.AreEqual(ProtoRel.RelTypeOneofCase.Read, wire.Relations[1].Rel.RelTypeCase);
        Assert.AreEqual(2, wire.Relations.Count);
    }

    [TestMethod]
    public void RepeatedSameAndEqualRegistrationsRemainIndependentWireEntries()
    {
        NamedTableRead read = CreateRead();
        PlanBuilder builder = new();
        builder.RegisterSubplan(read);
        builder.RegisterSubplan(read);
        builder.RegisterSubplan(CreateRead());
        builder.AddRoot(read, ["output"]);
        builder.AddRoot(read, ["output"]);

        ProtoPlan wire = AssertRoundTrips(builder.Build());
        IPlan converted = Deserialize(wire);

        Assert.AreEqual(5, wire.Relations.Count);
        for (int ordinal = 0; ordinal < wire.Relations.Count; ++ordinal)
        {
            ProtoRel relation = ordinal < 3 ? wire.Relations[ordinal].Rel : wire.Relations[ordinal].Root.Input;
            Assert.AreEqual(ProtoRel.RelTypeOneofCase.Read, relation.RelTypeCase);
            Assert.IsInstanceOfType<NamedTableRead>(converted.Relations[ordinal].Input);
            Assert.AreEqual(converted.Relations[0].Input, converted.Relations[ordinal].Input);
            for (int previous = 0; previous < ordinal; ++previous)
            {
                Assert.AreNotSame(converted.Relations[previous].Input, converted.Relations[ordinal].Input);
            }
        }
    }

    [TestMethod]
    public void ResolvesForwardBackwardAndRootReferencesWithSharedTargetIdentity()
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

        Assert.AreEqual(2, left.SubtreeOrdinal);
        Assert.AreSame(plan.Relations[2].Input, left.Target);
        Assert.AreSame(left.Target, right.Target);
        Assert.AreSame(first, second.Target);
        Assert.AreSame(second, last.Target);
        Assert.AreEqual(plan.Relations[2].Input.RecordType, left.RecordType);
        Assert.AreEqual(2, last.RecordType.Fields.Count);
        Assert.AreEqual("value", ((NamedTableRead)left.Target).InitialSchema.Names[0]);
        AssertRoundTrips(plan);
    }

    [TestMethod]
    public void FactorySupportsAdvancedForwardReferenceComposition()
    {
        NamedTableRead target = CreateRead();
        Reference forward = new(1, target);
        CorePlan plan = CorePlan.FromRelations(
            [new CorePlan.Root(forward, []), new CorePlan.Relation(target)], PlanVersion.Current);

        ProtoPlan wire = AssertRoundTrips(plan);

        Assert.AreEqual(1, wire.Relations[0].Root.Input.Reference.SubtreeOrdinal);
        Assert.AreSame(plan.Relations[1].Input, forward.Target);
    }

    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    [DataRow(int.MaxValue)]
    public void RejectsInvalidWireReferenceOrdinals(int ordinal)
    {
        ProtoPlan wire = CreateWirePlan(new PlanRel { Rel = WireReference(ordinal) });

        Assert.ThrowsException<SerializationException>(() => Deserialize(wire));
    }

    [TestMethod]
    public void RejectsUnsetPlanAndRelationVariants()
    {
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(new PlanRel())));
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(new PlanRel { Rel = new ProtoRel() })));
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(new PlanRel { Root = new RelRoot() })));
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(
            new PlanRel { Root = new RelRoot { Input = new ProtoRel() } })));
    }

    [TestMethod]
    public void RejectsSelfCyclesAndMultiEntryCycles()
    {
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireReference(0) })));
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireReference(1) },
            new PlanRel { Root = new RelRoot { Input = WireReference(0) } })));
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireReference(1) },
            new PlanRel { Rel = WireReference(2) },
            new PlanRel { Rel = WireReference(0) })));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void ResolvesDependenciesHiddenInsideEverySubqueryKind(int kind)
    {
        ProtoPlan wire = CreateWirePlan(
            new PlanRel { Root = new RelRoot { Input = WireProject(WireSubquery(kind, WireReference(2))) } },
            new PlanRel { Rel = WireRead() },
            new PlanRel { Rel = WireReference(1) });

        IPlan plan = Deserialize(wire);
        Project project = (Project)plan.Relations[0].Input;
        Reference reference = (Reference)GetSubquery(project.Expressions[0]);

        Assert.AreEqual(2, reference.SubtreeOrdinal);
        Assert.AreSame(plan.Relations[2].Input, reference.Target);
        Assert.AreSame(plan.Relations[1].Input, ((Reference)reference.Target).Target);
        AssertRoundTrips(plan);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void RejectsCyclesAndMissingDependenciesHiddenInsideEverySubqueryKind(int kind)
    {
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireProject(WireSubquery(kind, WireReference(0))) })));
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireProject(WireSubquery(kind, WireReference(1))) },
            new PlanRel { Rel = WireReference(0) })));
        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = WireProject(WireSubquery(kind, WireReference(9))) })));
    }

    [TestMethod]
    public void ResolvesDependenciesInSubqueryNeedlesAndComparisonOperands()
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

        Assert.AreSame(plan.Relations[1].Input, needle.Target);
        Assert.AreSame(needle.Target, operand.Target);
        AssertRoundTrips(plan);

        wire.Relations[1].Rel = WireReference(0);
        Assert.ThrowsException<SerializationException>(() => Deserialize(wire));
    }

    [TestMethod]
    public void EachEntryHasItsOwnCorrelationBoundaryEvenWhenUsedAsASubquery()
    {
        ProtoRel correlated = WireProject(WireOuterField(1));
        ProtoPlan invalid = CreateWirePlan(
            new PlanRel { Rel = correlated },
            new PlanRel { Root = new RelRoot { Input = WireProject(WireSubquery(0, WireReference(0))) } });

        Assert.ThrowsException<SerializationException>(() => Deserialize(invalid));

        ProtoPlan valid = CreateWirePlan(
            new PlanRel { Rel = WireProject(WireSubquery(0, correlated)) },
            new PlanRel { Root = new RelRoot { Input = WireReference(0) } });

        AssertRoundTrips(Deserialize(valid));
    }

    [DataTestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 3)]
    public void RejectsWireCorrelationEscapingEntryNesting(int depth, int levels)
    {
        ProtoRel relation = WireProject(WireOuterField((uint)levels));
        for (int i = 0; i < depth; ++i)
        {
            relation = WireProject(WireSubquery(0, relation));
        }

        Assert.ThrowsException<SerializationException>(() => Deserialize(CreateWirePlan(
            new PlanRel { Rel = relation })));
    }

    [TestMethod]
    public void StandaloneConvertersRejectReferencesWithoutPlanContext()
    {
        ProtoToRelConverter reader = new(
            new ExtensionsDictionary.Builder().Build(), new ExtensionsCollection(), ExtensionsDictionary.StrictMode.OFF);
        Reference reference = new(0, CreateRead());

        Assert.ThrowsException<SerializationException>(() => reader.ToRel(WireReference(0)));
        Assert.ThrowsException<InvalidOperationException>(() => new RelToProtoConverter().From(reference));
        Assert.ThrowsException<SerializationException>(() => reader.ToRel(WireProject(WireSubquery(0, WireReference(0)))));
        Assert.ThrowsException<InvalidOperationException>(() => new RelToProtoConverter().From(
            new Project(CreateRead(), [new CoreExpression.ScalarSubquery(reference, TypeFactory.REQUIRED.I64)])));
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ResolvesLongReferenceChainsWithoutRecursion(bool forward, bool wrapReferences)
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

        Assert.AreEqual(count, plan.Relations.Count);
        Assert.AreEqual(TypeFactory.REQUIRED.I64, head.RecordType.Fields[0]);
        for (int i = 0; i < count - 1; ++i)
        {
            Reference reference = (Reference)(head is Filter filter ? filter.Input : head);
            Assert.AreSame(plan.Relations[reference.SubtreeOrdinal].Input, reference.Target);
            head = reference.Target;
        }

        Assert.IsInstanceOfType<NamedTableRead>(head);
        ProtoPlan serialized = new PlanToProtoConverter().From(plan);
        IPlan roundTrip = Deserialize(ProtoPlan.Parser.ParseFrom(serialized.ToByteArray()));
        Assert.AreEqual(plan, roundTrip);
        Assert.AreEqual(plan.GetHashCode(), roundTrip.GetHashCode());
        Assert.AreEqual(count, serialized.Relations.Count);
    }

    [TestMethod]
    public void CollectsSharedExtensionAnchorsAcrossAllEntriesDeterministically()
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
            Assert.AreEqual(variation, converted.Relations[0].Input.RecordType.Fields[0].TypeVariation);
            CollectionAssert.AreEqual(wire.ToByteArray(), new PlanToProtoConverter().From(converted).ToByteArray());
        }

        SimpleExtensionDeclaration[] functions = wire.Extensions.Where(extension => extension.ExtensionFunction is not null).ToArray();
        SimpleExtensionDeclaration[] variations = wire.Extensions.Where(extension => extension.ExtensionTypeVariation is not null).ToArray();

        Assert.AreEqual(3, wire.ExtensionUrns.Count);
        Assert.AreEqual(2, functions.Length);
        Assert.AreEqual(1, variations.Length);
        Assert.AreEqual(1U, variations[0].ExtensionTypeVariation.TypeVariationAnchor);
        Assert.AreEqual(
            wire.Relations[0].Rel.Project.Expressions[0].ScalarFunction.FunctionReference,
            wire.Relations[1].Root.Input.Project.Expressions[0].ScalarFunction.FunctionReference);
        Assert.AreEqual(
            wire.Relations[1].Root.Input.Project.Expressions[1].ScalarFunction.FunctionReference,
            wire.Relations[2].Rel.Project.Expressions[0].ScalarFunction.FunctionReference);
        Assert.AreEqual(2, functions.Select(extension => extension.ExtensionFunction.FunctionAnchor).Distinct().Count());
        foreach (SimpleExtensionDeclaration function in functions)
        {
            Assert.IsTrue(wire.ExtensionUrns.Any(urn => urn.ExtensionUrnAnchor == function.ExtensionFunction.ExtensionUrnReference));
        }

        var serializer = new PlanToProtoConverter();
        ProtoPlan unrelated = serializer.From(new CorePlan([new CorePlan.Root(CreateRead(), [])], PlanVersion.Current));
        Assert.AreEqual(0, unrelated.Extensions.Count);
        Assert.AreEqual(0, unrelated.ExtensionUrns.Count);
        CollectionAssert.AreEqual(wire.ToByteArray(), serializer.From(plan).ToByteArray());
    }

    [TestMethod]
    public void EmptyWirePlansRemainDeserializableButCannotBeSerialized()
    {
        IPlan plan = Deserialize(CreateWirePlan());

        Assert.AreEqual(0, plan.Relations.Count);
        Assert.AreEqual(0, plan.Roots.Count);
        Assert.ThrowsException<ArgumentException>(() => new PlanToProtoConverter().From(plan));
    }

    private static ProtoPlan AssertRoundTrips(IPlan plan)
    {
        PlanToProtoConverter serializer = new();
        ProtoPlan wire = serializer.From(plan);
        byte[] bytes = wire.ToByteArray();
        CollectionAssert.AreEqual(bytes, serializer.From(plan).ToByteArray());
        ProtoPlan[] roundTrips =
        [
            ProtoPlan.Parser.ParseFrom(bytes),
            JsonParser.Default.Parse<ProtoPlan>(JsonFormatter.Default.Format(wire)),
        ];
        foreach (ProtoPlan roundTrip in roundTrips)
        {
            IPlan converted = Deserialize(roundTrip);
            Assert.AreEqual(plan, converted);
            Assert.AreEqual(plan.GetHashCode(), converted.GetHashCode());
            CollectionAssert.AreEqual(bytes, serializer.From(converted).ToByteArray());
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

    private static ProtoExpression WireSubquery(int kind, ProtoRel relation) => kind switch
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

    private static IRel GetSubquery(IExpression expression) => expression switch
    {
        CoreExpression.ScalarSubquery scalar => scalar.Subquery,
        CoreExpression.InPredicateSubquery predicate => predicate.Subquery,
        CoreExpression.SetPredicateSubquery predicate => predicate.Subquery,
        CoreExpression.SetComparisonSubquery comparison => comparison.Subquery,
        _ => throw new ArgumentException("Expected a subquery.", nameof(expression)),
    };
}
