// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Expression;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using static Substrait.Core.Expression.Expression;
using PlanVersion = Substrait.Core.Plan.Version;

namespace Substrait.Tests.Core;

[TestClass]
public sealed class PlanBuilderTests
{
    private static readonly string[] ExpectedRootNames = ["output"];
    private static readonly int[] ExpectedRegistrationOrdinals = [0, 1, 2, 3, 4];

    [TestMethod]
    public void LegacyConstructorPreservesRootsInRelations()
    {
        Plan.Root first = new(CreateRead("first"), ["first"]);
        Plan.Root second = new(CreateRead("second"), []);
        Plan plan = new([first, second], PlanVersion.Current);

        CollectionAssert.AreEqual(new IPlan.IRelation[] { first, second }, plan.Relations.ToArray());
        CollectionAssert.AreEqual(new IPlan.IRoot[] { first, second }, plan.Roots.ToArray());
    }

    [TestMethod]
    public void FactoryPreservesMixedEntryOrderAndSnapshotsCollections()
    {
        List<string> names = ["output"];
        Plan.Root first = new(CreateRead("first"), names);
        Plan.Relation shared = new(CreateRead("shared"));
        Plan.Root last = new(CreateRead("last"), []);
        List<IPlan.IRelation> entries = [first, shared, last];

        Plan plan = Plan.FromRelations(entries, PlanVersion.Current);
        entries.Clear();
        names[0] = "changed";

        CollectionAssert.AreEqual(new IPlan.IRelation[] { first, shared, last }, plan.Relations.ToArray());
        CollectionAssert.AreEqual(new IPlan.IRoot[] { first, last }, plan.Roots.ToArray());
        CollectionAssert.AreEqual(ExpectedRootNames, plan.Roots[0].Names.ToArray());
        Assert.AreEqual(0, plan.Roots[1].Names.Count);
    }

    [TestMethod]
    public void BuilderAssignsOrdinalsAcrossRootsAndSubplans()
    {
        PlanVersion version = new(1, 2, 3, "hash", "builder-tests");
        PlanBuilder builder = new(version);
        Reference firstRoot = builder.AddRoot(CreateRead("first"), ["renamed"]);
        Reference shared = builder.RegisterSubplan(CreateRead("shared"));
        Reference lastRoot = builder.AddRoot(new Cross(firstRoot, shared), []);

        Plan plan = builder.Build();

        Assert.AreEqual(0, firstRoot.SubtreeOrdinal);
        Assert.AreEqual(1, shared.SubtreeOrdinal);
        Assert.AreEqual(2, lastRoot.SubtreeOrdinal);
        Assert.AreSame(version, plan.Version);
        Assert.AreEqual(3, plan.Relations.Count);
        Assert.AreEqual(2, plan.Roots.Count);
        Assert.AreSame(firstRoot.Target, plan.Relations[0].Input);
        Assert.AreSame(shared.Target, plan.Relations[1].Input);
        Assert.AreSame(lastRoot.Target, plan.Relations[2].Input);
        Assert.IsInstanceOfType<IPlan.IRoot>(plan.Relations[0]);
        Assert.IsInstanceOfType<Plan.Relation>(plan.Relations[1]);
        Assert.IsInstanceOfType<IPlan.IRoot>(plan.Relations[2]);
    }

    [TestMethod]
    public void RegistrationNeverDeduplicatesSameOrEqualSubtrees()
    {
        PlanBuilder builder = new();
        NamedTableRead read = CreateRead();
        Reference first = builder.RegisterSubplan(read);
        Reference sameInstance = builder.RegisterSubplan(read);
        Reference equalInstance = builder.RegisterSubplan(CreateRead());
        Reference root = builder.AddRoot(read, ["output"]);
        Reference repeatedRoot = builder.AddRoot(read, ["output"]);
        Plan plan = builder.Build();

        CollectionAssert.AreEqual(
            ExpectedRegistrationOrdinals,
            new[] { first.SubtreeOrdinal, sameInstance.SubtreeOrdinal, equalInstance.SubtreeOrdinal, root.SubtreeOrdinal, repeatedRoot.SubtreeOrdinal });
        Assert.AreEqual(5, plan.Relations.Count);
        Assert.AreSame(plan.Relations[0].Input, plan.Relations[1].Input);
        Assert.AreEqual(plan.Relations[0].Input, plan.Relations[2].Input);
        Assert.AreNotSame(plan.Relations[0].Input, plan.Relations[2].Input);
    }

    [TestMethod]
    public void BuildReturnsImmutableSnapshotsAndDefaultsToCurrentVersion()
    {
        PlanBuilder builder = new();
        Reference shared = builder.RegisterSubplan(CreateRead());
        List<string> names = ["before"];
        builder.AddRoot(shared, names);
        Plan first = builder.Build();
        int hashCode = first.GetHashCode();
        byte[] bytes = new PlanToProtoConverter().From(first).ToByteArray();
        names[0] = "after";

        builder.RegisterSubplan(CreateRead("later"));
        builder.AddRoot(shared, ["later"]);
        Plan second = builder.Build();

        Assert.AreEqual(PlanVersion.Current, first.Version);
        Assert.AreEqual(2, first.Relations.Count);
        Assert.AreEqual(4, second.Relations.Count);
        Assert.AreEqual("before", first.Roots[0].Names[0]);
        Assert.AreSame(first.Relations[0].Input, second.Relations[0].Input);
        Assert.AreEqual(hashCode, first.GetHashCode());
        CollectionAssert.AreEqual(bytes, new PlanToProtoConverter().From(first).ToByteArray());
    }

    [TestMethod]
    public void BuilderRejectsForeignAndUnownedReferencesWithoutConsumingOrdinals()
    {
        NamedTableRead read = CreateRead();
        PlanBuilder owner = new();
        Reference owned = owner.RegisterSubplan(read);
        PlanBuilder other = new();
        other.RegisterSubplan(read);
        Reference unowned = new(0, read);

        Assert.ThrowsException<ArgumentException>(() => other.RegisterSubplan(owned));
        Assert.ThrowsException<ArgumentException>(() => other.AddRoot(new Filter(owned, new Literal.BoolLiteral(true)), []));
        Assert.ThrowsException<ArgumentException>(() => owner.RegisterSubplan(unowned));
        Assert.ThrowsException<ArgumentException>(() => owner.AddRoot(unowned, []));

        Reference next = owner.AddRoot(owned, []);
        Assert.AreEqual(1, next.SubtreeOrdinal);
        Assert.AreEqual(2, owner.Build().Relations.Count);
        Assert.AreEqual(1, other.Build().Relations.Count);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void BuilderFindsForeignReferencesInsideEverySubqueryKind(int kind)
    {
        PlanBuilder owner = new();
        Reference reference = owner.RegisterSubplan(CreateRead());
        Project project = new(CreateRead(), [CreateSubquery(kind, reference)]);
        PlanBuilder other = new();

        Assert.ThrowsException<ArgumentException>(() => other.RegisterSubplan(project));
        Assert.ThrowsException<ArgumentException>(() => other.AddRoot(project, []));
        Assert.AreEqual(0, other.Build().Relations.Count);

        owner.AddRoot(project, []);
        Assert.AreEqual(2, owner.Build().Relations.Count);
    }

    [TestMethod]
    public void BuilderFindsDependenciesInNestedSubqueryExpressionOperands()
    {
        PlanBuilder owner = new();
        Reference reference = owner.RegisterSubplan(CreateRead());
        ScalarSubquery hidden = new(reference, TypeFactory.REQUIRED.I64);
        SetComparisonSubquery comparison = new(
            hidden, SetComparisonSubquery.ComparisonOp.Equal, SetComparisonSubquery.ReductionOp.Any, CreateRead());
        InPredicateSubquery predicate = new(CreateRead(), [hidden]);
        PlanBuilder other = new();

        Assert.ThrowsException<ArgumentException>(() =>
            other.RegisterSubplan(new Project(CreateRead(), [comparison])));
        Assert.ThrowsException<ArgumentException>(() =>
            other.RegisterSubplan(new Project(CreateRead(), [predicate])));
    }

    [TestMethod]
    public void BuildRevalidatesDependenciesAndCorrelationAfterRegistration()
    {
        PlanBuilder builder = new();
        MutableInput input = new(CreateRead());
        Reference owned = builder.RegisterSubplan(input);
        PlanBuilder other = new();
        Reference foreign = other.RegisterSubplan(CreateRead());

        input.Child = foreign;
        Assert.ThrowsException<ArgumentException>(() => builder.Build());

        input.Child = CreateCorrelatedRelation(0, 1);
        Assert.ThrowsException<ArgumentException>(() => builder.Build());

        input.Child = owned;
        Assert.ThrowsException<ArgumentException>(() => builder.Build());

        input.Child = CreateRead();
        Assert.AreEqual(1, builder.Build().Relations.Count);
    }

    [TestMethod]
    public void FullPlanRejectsCyclesThroughCorrectlyBoundReferencesAndSubqueries()
    {
        MutableInput input = new(CreateRead());
        Reference self = new(0, input);
        input.Child = self;
        Assert.ThrowsException<ArgumentException>(() =>
            Plan.FromRelations([new Plan.Relation(input)], PlanVersion.Current));

        input.Child = new Project(CreateRead(), [new ScalarSubquery(self, TypeFactory.REQUIRED.I64)]);
        Assert.ThrowsException<ArgumentException>(() =>
            Plan.FromRelations([new Plan.Relation(input)], PlanVersion.Current));

        input.Child = input;
        Assert.ThrowsException<ArgumentException>(() =>
            new PlanBuilder().RegisterSubplan(input));
    }

    [TestMethod]
    public void SharedInlineObjectsAreValidatedAtEachCorrelationDepth()
    {
        IRel correlated = CreateCorrelatedRelation(0, 1);
        Project nested = new(CreateRead(), [new ScalarSubquery(correlated, TypeFactory.REQUIRED.I64)]);
        PlanBuilder builder = new();

        Assert.ThrowsException<ArgumentException>(() => builder.RegisterSubplan(new Cross(correlated, nested)));
        Assert.ThrowsException<ArgumentException>(() => builder.RegisterSubplan(new Cross(nested, correlated)));
    }

    [DataTestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 3)]
    public void RegistrationRejectsCorrelationEscapingTheEntry(int nestingDepth, int subqueryLevels)
    {
        IRel relation = CreateCorrelatedRelation(nestingDepth, subqueryLevels);
        PlanBuilder builder = new();

        Assert.ThrowsException<ArgumentException>(() => builder.RegisterSubplan(relation));
        Assert.ThrowsException<ArgumentException>(() => builder.AddRoot(relation, []));
        Assert.ThrowsException<ArgumentException>(() =>
            Plan.FromRelations([new Plan.Relation(relation)], PlanVersion.Current));
        Assert.AreEqual(0, builder.Build().Relations.Count);
    }

    [DataTestMethod]
    [DataRow(1, 1)]
    [DataRow(2, 1)]
    [DataRow(2, 2)]
    public void RegistrationAllowsCorrelationContainedWithinTheEntry(int nestingDepth, int subqueryLevels)
    {
        PlanBuilder builder = new();
        Reference reference = builder.RegisterSubplan(CreateCorrelatedRelation(nestingDepth, subqueryLevels));
        builder.AddRoot(reference, []);

        Plan plan = builder.Build();
        IPlan converted = new ProtoToPlanConverter().From(new PlanToProtoConverter().From(plan));

        Assert.AreEqual(plan, converted);
    }

    [TestMethod]
    public void ReferenceIsAZeroInputOrdinalEdgeWithTargetSchema()
    {
        NamedTableRead read = new(
            new NamedStruct(["first", "second"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, TypeFactory.REQUIRED.STR])),
            ["table"],
            null,
            new Remap([1, 0]));
        Reference reference = new(7, read);
        Reference equalOrdinalDifferentTarget = new(7, CreateRead("different"));

        Assert.IsInstanceOfType<ZeroInput>(reference);
        Assert.AreEqual(0, reference.Inputs.Count);
        Assert.IsFalse(reference.InputNodes.Any());
        Assert.AreSame(read, reference.Target);
        Assert.AreEqual(read.RecordType, reference.RecordType);
        Assert.AreEqual(TypeFactory.REQUIRED.STR, reference.RecordType.Fields[0]);
        Assert.AreEqual(reference, equalOrdinalDifferentTarget);
        Assert.AreEqual(reference.GetHashCode(), equalOrdinalDifferentTarget.GetHashCode());
        Assert.AreNotEqual(reference, new Reference(8, read));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Reference(-1, read));
    }

    [TestMethod]
    public void FullPlanValidatesBoundsAndTargetIdentityInsteadOfStructuralEquality()
    {
        NamedTableRead read = CreateRead();
        NamedTableRead equalRead = CreateRead();
        Assert.AreEqual(read, equalRead);

        Assert.ThrowsException<ArgumentException>(() => Plan.FromRelations(
            [new Plan.Relation(read), new Plan.Root(new Reference(2, read), [])], PlanVersion.Current));
        Assert.ThrowsException<ArgumentException>(() => Plan.FromRelations(
            [new Plan.Relation(read), new Plan.Root(new Reference(int.MaxValue, read), [])], PlanVersion.Current));
        Assert.ThrowsException<ArgumentException>(() => Plan.FromRelations(
            [new Plan.Relation(read), new Plan.Root(new Reference(0, equalRead), [])], PlanVersion.Current));
        Assert.ThrowsException<ArgumentException>(() => Plan.FromRelations(
            [new Plan.Relation(new Project(read, [new ScalarSubquery(new Reference(1, read), TypeFactory.REQUIRED.I64)]))],
            PlanVersion.Current));
    }

    [TestMethod]
    public void SerializerUsesCustomPlanRelationsAsAuthoritativeInsteadOfRoots()
    {
        NamedTableRead read = CreateRead();
        IPlan.IRelation[] entries =
        [
            new Plan.Relation(read),
            new Plan.Root(new Reference(0, read), []),
        ];
        IPlan.IRoot[] unrelatedRoots = [new Plan.Root(CreateRead("not-an-entry"), ["ignored"])];
        CustomPlan custom = new(entries, unrelatedRoots);

        Substrait.Protobuf.Plan wire = new PlanToProtoConverter().From(custom);
        IPlan converted = new ProtoToPlanConverter().From(wire);

        Assert.AreEqual(2, wire.Relations.Count);
        Assert.AreEqual(Substrait.Protobuf.PlanRel.RelTypeOneofCase.Rel, wire.Relations[0].RelTypeCase);
        Assert.AreEqual("orders", wire.Relations[0].Rel.Read.NamedTable.Names[0]);
        Assert.AreEqual(Substrait.Protobuf.PlanRel.RelTypeOneofCase.Root, wire.Relations[1].RelTypeCase);
        Assert.AreEqual(0, wire.Relations[1].Root.Names.Count);
        Assert.AreEqual(0, wire.Relations[1].Root.Input.Reference.SubtreeOrdinal);
        Assert.AreSame(converted.Relations[0].Input, ((Reference)converted.Relations[1].Input).Target);
        Assert.ThrowsException<ArgumentException>(() =>
            new PlanToProtoConverter().From(new CustomPlan([], unrelatedRoots)));
    }

    [TestMethod]
    public void SerializerDefensivelyValidatesCustomPlanReferenceBoundsAndIdentity()
    {
        NamedTableRead read = CreateRead();
        NamedTableRead equalRead = CreateRead();
        CustomPlan[] invalidPlans =
        [
            new([new Plan.Relation(read), new Plan.Root(new Reference(2, read), [])]),
            new([new Plan.Relation(read), new Plan.Root(new Reference(int.MaxValue, read), [])]),
            new([new Plan.Relation(read), new Plan.Root(new Reference(0, equalRead), [])]),
            new([new Plan.Relation(new Project(read, [new ScalarSubquery(new Reference(1, read), TypeFactory.REQUIRED.I64)]))]),
        ];

        foreach (CustomPlan plan in invalidPlans)
        {
            Assert.ThrowsException<ArgumentException>(() => new PlanToProtoConverter().From(plan));
        }
    }

    [TestMethod]
    public void SerializerDefensivelyRejectsCustomPlanCyclesIncludingSubqueries()
    {
        MutableInput input = new(CreateRead());
        Reference reference = new(0, input);
        CustomPlan custom = new([new Plan.Relation(input)]);

        input.Child = reference;
        Assert.ThrowsException<ArgumentException>(() => new PlanToProtoConverter().From(custom));

        input.Child = new Project(CreateRead(), [new ScalarSubquery(reference, TypeFactory.REQUIRED.I64)]);
        Assert.ThrowsException<ArgumentException>(() => new PlanToProtoConverter().From(custom));

        input.Child = input;
        Assert.ThrowsException<ArgumentException>(() => new PlanToProtoConverter().From(custom));
    }

    [DataTestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 3)]
    public void SerializerDefensivelyRejectsCustomPlanEscapingCorrelation(int nestingDepth, int subqueryLevels)
    {
        CustomPlan custom = new([new Plan.Relation(CreateCorrelatedRelation(nestingDepth, subqueryLevels))]);

        Assert.ThrowsException<ArgumentException>(() => new PlanToProtoConverter().From(custom));
    }

    [TestMethod]
    public void PlanEqualityIncludesEntryKindOrderTargetsNamesAndVersion()
    {
        Plan baseline = CreateReferencedPlan("orders", "output", PlanVersion.Current);
        Plan equivalent = CreateReferencedPlan("orders", "output", PlanVersion.Current);

        Assert.AreEqual(baseline, equivalent);
        Assert.AreEqual(baseline.GetHashCode(), equivalent.GetHashCode());
        Assert.AreNotEqual(baseline, CreateReferencedPlan("customers", "output", PlanVersion.Current));
        Assert.AreNotEqual(baseline, CreateReferencedPlan("orders", "renamed", PlanVersion.Current));
        Assert.AreNotEqual(baseline, CreateReferencedPlan("orders", "output", new PlanVersion(1, 0, 0, "", "")));
        Assert.AreNotEqual(
            Plan.FromRelations([new Plan.Relation(CreateRead())], PlanVersion.Current),
            Plan.FromRelations([new Plan.Root(CreateRead(), [])], PlanVersion.Current));
        Assert.AreNotEqual(
            Plan.FromRelations([new Plan.Relation(CreateRead("first")), new Plan.Relation(CreateRead("second"))], PlanVersion.Current),
            Plan.FromRelations([new Plan.Relation(CreateRead("second")), new Plan.Relation(CreateRead("first"))], PlanVersion.Current));
    }

    [TestMethod]
    public void EmptyPlansRemainConstructibleButNotSerializable()
    {
        Plan[] plans =
        [
            new Plan([], PlanVersion.Current),
            Plan.FromRelations([], PlanVersion.Current),
            new PlanBuilder().Build(),
        ];

        foreach (Plan plan in plans)
        {
            Assert.AreEqual(0, plan.Relations.Count);
            Assert.AreEqual(0, plan.Roots.Count);
            Assert.ThrowsException<ArgumentException>(() => new PlanToProtoConverter().From(plan));
        }
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BuilderSupportsLongReferenceChainsWithoutRecursiveSchemaDerivation(bool wrapReferences)
    {
        const int count = 10001;
        PlanBuilder builder = new();
        Reference reference = builder.RegisterSubplan(CreateRead());
        for (int i = 1; i < count; ++i)
        {
            reference = builder.RegisterSubplan(
                wrapReferences ? new Filter(reference, new Literal.BoolLiteral(true)) : reference);
        }

        builder.AddRoot(reference, ["value"]);
        Plan plan = builder.Build();

        Assert.AreEqual(count + 1, plan.Relations.Count);
        Assert.AreEqual(count - 1, reference.SubtreeOrdinal);
        Assert.AreEqual(TypeFactory.REQUIRED.I64, reference.RecordType.Fields[0]);
        Assert.AreEqual(count + 1, new PlanToProtoConverter().From(plan).Relations.Count);
        Assert.AreEqual(plan, builder.Build());
        Assert.AreEqual(plan.GetHashCode(), builder.Build().GetHashCode());
    }

    private static NamedTableRead CreateRead(string name = "orders") =>
        new(new NamedStruct(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])), [name], null);

    private static IExpression CreateSubquery(int kind, IRel relation) => kind switch
    {
        0 => new ScalarSubquery(relation, TypeFactory.REQUIRED.I64),
        1 => new InPredicateSubquery(relation, [new Literal.I64Literal(1)]),
        2 => new SetPredicateSubquery(relation, SetPredicateSubquery.PredicateOp.Exists),
        3 => new SetComparisonSubquery(new Literal.I64Literal(1), SetComparisonSubquery.ComparisonOp.Equal, SetComparisonSubquery.ReductionOp.Any, relation),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static IRel CreateCorrelatedRelation(int nestingDepth, int subqueryLevels)
    {
        IRel relation = new Project(
            CreateRead(), [new FieldReference(TypeFactory.REQUIRED.I64, 0, subqueryLevels)], new Remap([1]));
        for (int i = 0; i < nestingDepth; ++i)
        {
            relation = new Project(CreateRead(), [new ScalarSubquery(relation, TypeFactory.REQUIRED.I64)], new Remap([1]));
        }

        return relation;
    }

    private static Plan CreateReferencedPlan(string table, string name, IVersion version)
    {
        PlanBuilder builder = new(version);
        Reference reference = builder.RegisterSubplan(CreateRead(table));
        builder.AddRoot(reference, [name]);
        return builder.Build();
    }

    private sealed class MutableInput(IRel input) : SingleInput
    {
        public IRel Child { get; set; } = input;

        public override IRel Input => this.Child;

        public override Remap? Transmute => null;

        public override TOutput Accept<TContext, TOutput>(RelVisitor<TContext, TOutput> visitor, TContext context) =>
            visitor.Visit((IRel)this, context);

        public override int GetNodeHashCode() => 0;

        public override bool NodeEquals(IRel other) => ReferenceEquals(this, other);

        protected override ParameterizedType.Struct DeriveRecordType() => this.Input.RecordType;
    }

    private sealed class CustomPlan(
        IReadOnlyList<IPlan.IRelation> relations,
        IReadOnlyList<IPlan.IRoot>? roots = null) : IPlan
    {
        public IReadOnlyList<IPlan.IRelation> Relations { get; } = relations;

        public IReadOnlyList<IPlan.IRoot> Roots { get; } = roots ?? relations.OfType<IPlan.IRoot>().ToArray();

        public IVersion Version => PlanVersion.Current;
    }
}
