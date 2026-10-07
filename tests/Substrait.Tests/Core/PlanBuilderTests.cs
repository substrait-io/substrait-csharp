// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Substrait.Core.Expression;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using static Substrait.Core.Expression.Expression;
using PlanVersion = Substrait.Core.Plan.Version;

namespace Substrait.Tests.Core;

public sealed class PlanBuilderTests
{
    private static readonly string[] ExpectedRootNames = ["output"];
    private static readonly int[] ExpectedRegistrationOrdinals = [0, 1, 2, 3, 4];

    [Test]
    public async Task LegacyConstructorPreservesRootsInRelations()
    {
        Plan.Root first = new(CreateRead("first"), ["first"]);
        Plan.Root second = new(CreateRead("second"), []);
        Plan plan = new([first, second], PlanVersion.Current);

        await Assert.That(plan.Relations.ToArray()).IsEquivalentTo(new IPlan.IRelation[] { first, second }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(plan.Roots.ToArray()).IsEquivalentTo(new IPlan.IRoot[] { first, second }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task FactoryPreservesMixedEntryOrderAndSnapshotsCollections()
    {
        List<string> names = ["output"];
        Plan.Root first = new(CreateRead("first"), names);
        Plan.Relation shared = new(CreateRead("shared"));
        Plan.Root last = new(CreateRead("last"), []);
        List<IPlan.IRelation> entries = [first, shared, last];

        Plan plan = Plan.FromRelations(entries, PlanVersion.Current);
        entries.Clear();
        names[0] = "changed";

        await Assert.That(plan.Relations.ToArray()).IsEquivalentTo(new IPlan.IRelation[] { first, shared, last }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(plan.Roots.ToArray()).IsEquivalentTo(new IPlan.IRoot[] { first, last }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(plan.Roots[0].Names.ToArray()).IsEquivalentTo(ExpectedRootNames, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(plan.Roots[1].Names.Count).IsEqualTo(0);
    }

    [Test]
    public async Task BuilderAssignsOrdinalsAcrossRootsAndSubplans()
    {
        PlanVersion version = new(1, 2, 3, "hash", "builder-tests");
        PlanBuilder builder = new(version);
        Reference firstRoot = builder.AddRoot(CreateRead("first"), ["renamed"]);
        Reference shared = builder.RegisterSubplan(CreateRead("shared"));
        Reference lastRoot = builder.AddRoot(new Cross(firstRoot, shared), []);

        Plan plan = builder.Build();

        await Assert.That(firstRoot.SubtreeOrdinal).IsEqualTo(0);
        await Assert.That(shared.SubtreeOrdinal).IsEqualTo(1);
        await Assert.That(lastRoot.SubtreeOrdinal).IsEqualTo(2);
        await Assert.That(plan.Version).IsSameReferenceAs(version);
        await Assert.That(plan.Relations.Count).IsEqualTo(3);
        await Assert.That(plan.Roots.Count).IsEqualTo(2);
        await Assert.That(plan.Relations[0].Input).IsSameReferenceAs(firstRoot.Target);
        await Assert.That(plan.Relations[1].Input).IsSameReferenceAs(shared.Target);
        await Assert.That(plan.Relations[2].Input).IsSameReferenceAs(lastRoot.Target);
        await Assert.That(plan.Relations[0]).IsAssignableTo<IPlan.IRoot>();
        await Assert.That(plan.Relations[1]).IsAssignableTo<Plan.Relation>();
        await Assert.That(plan.Relations[2]).IsAssignableTo<IPlan.IRoot>();
    }

    [Test]
    public async Task RegistrationNeverDeduplicatesSameOrEqualSubtrees()
    {
        PlanBuilder builder = new();
        NamedTableRead read = CreateRead();
        Reference first = builder.RegisterSubplan(read);
        Reference sameInstance = builder.RegisterSubplan(read);
        Reference equalInstance = builder.RegisterSubplan(CreateRead());
        Reference root = builder.AddRoot(read, ["output"]);
        Reference repeatedRoot = builder.AddRoot(read, ["output"]);
        Plan plan = builder.Build();

        await Assert.That(new[] { first.SubtreeOrdinal, sameInstance.SubtreeOrdinal, equalInstance.SubtreeOrdinal, root.SubtreeOrdinal, repeatedRoot.SubtreeOrdinal }).IsEquivalentTo(ExpectedRegistrationOrdinals, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(plan.Relations.Count).IsEqualTo(5);
        await Assert.That(plan.Relations[1].Input).IsSameReferenceAs(plan.Relations[0].Input);
        await Assert.That(plan.Relations[2].Input).IsEqualTo(plan.Relations[0].Input);
        await Assert.That(plan.Relations[2].Input).IsNotSameReferenceAs(plan.Relations[0].Input);
    }

    [Test]
    public async Task BuildReturnsImmutableSnapshotsAndDefaultsToCurrentVersion()
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

        await Assert.That(first.Version).IsEqualTo(PlanVersion.Current);
        await Assert.That(first.Relations.Count).IsEqualTo(2);
        await Assert.That(second.Relations.Count).IsEqualTo(4);
        await Assert.That(first.Roots[0].Names[0]).IsEqualTo("before");
        await Assert.That(second.Relations[0].Input).IsSameReferenceAs(first.Relations[0].Input);
        await Assert.That(first.GetHashCode()).IsEqualTo(hashCode);
        await Assert.That(new PlanToProtoConverter().From(first).ToByteArray()).IsEquivalentTo(bytes, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task BuilderRejectsForeignAndUnownedReferencesWithoutConsumingOrdinals()
    {
        NamedTableRead read = CreateRead();
        PlanBuilder owner = new();
        Reference owned = owner.RegisterSubplan(read);
        PlanBuilder other = new();
        other.RegisterSubplan(read);
        Reference unowned = new(0, read);

        await Assert.That(() => other.RegisterSubplan(owned)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => other.AddRoot(new Filter(owned, new Literal.BoolLiteral(true)), [])).ThrowsExactly<ArgumentException>();
        await Assert.That(() => owner.RegisterSubplan(unowned)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => owner.AddRoot(unowned, [])).ThrowsExactly<ArgumentException>();

        Reference next = owner.AddRoot(owned, []);
        await Assert.That(next.SubtreeOrdinal).IsEqualTo(1);
        await Assert.That(owner.Build().Relations.Count).IsEqualTo(2);
        await Assert.That(other.Build().Relations.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task BuilderFindsForeignReferencesInsideEverySubqueryKind(int kind)
    {
        PlanBuilder owner = new();
        Reference reference = owner.RegisterSubplan(CreateRead());
        Project project = new(CreateRead(), [CreateSubquery(kind, reference)]);
        PlanBuilder other = new();

        await Assert.That(() => other.RegisterSubplan(project)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => other.AddRoot(project, [])).ThrowsExactly<ArgumentException>();
        await Assert.That(other.Build().Relations.Count).IsEqualTo(0);

        owner.AddRoot(project, []);
        await Assert.That(owner.Build().Relations.Count).IsEqualTo(2);
    }

    [Test]
    public async Task BuilderFindsDependenciesInNestedSubqueryExpressionOperands()
    {
        PlanBuilder owner = new();
        Reference reference = owner.RegisterSubplan(CreateRead());
        ScalarSubquery hidden = new(reference, TypeFactory.REQUIRED.I64);
        SetComparisonSubquery comparison = new(
            hidden, SetComparisonSubquery.ComparisonOp.Equal, SetComparisonSubquery.ReductionOp.Any, CreateRead());
        InPredicateSubquery predicate = new(CreateRead(), [hidden]);
        PlanBuilder other = new();

        await Assert.That(() =>
            other.RegisterSubplan(new Project(CreateRead(), [comparison]))).ThrowsExactly<ArgumentException>();
        await Assert.That(() =>
            other.RegisterSubplan(new Project(CreateRead(), [predicate]))).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task BuildRevalidatesDependenciesAndCorrelationAfterRegistration()
    {
        PlanBuilder builder = new();
        MutableInput input = new(CreateRead());
        Reference owned = builder.RegisterSubplan(input);
        PlanBuilder other = new();
        Reference foreign = other.RegisterSubplan(CreateRead());

        input.Child = foreign;
        await Assert.That(() => builder.Build()).ThrowsExactly<ArgumentException>();

        input.Child = CreateCorrelatedRelation(0, 1);
        await Assert.That(() => builder.Build()).ThrowsExactly<ArgumentException>();

        input.Child = owned;
        await Assert.That(() => builder.Build()).ThrowsExactly<ArgumentException>();

        input.Child = CreateRead();
        await Assert.That(builder.Build().Relations.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FullPlanRejectsCyclesThroughCorrectlyBoundReferencesAndSubqueries()
    {
        MutableInput input = new(CreateRead());
        Reference self = new(0, input);
        input.Child = self;
        await Assert.That(() =>
            Plan.FromRelations([new Plan.Relation(input)], PlanVersion.Current)).ThrowsExactly<ArgumentException>();

        input.Child = new Project(CreateRead(), [new ScalarSubquery(self, TypeFactory.REQUIRED.I64)]);
        await Assert.That(() =>
            Plan.FromRelations([new Plan.Relation(input)], PlanVersion.Current)).ThrowsExactly<ArgumentException>();

        input.Child = input;
        await Assert.That(() =>
            new PlanBuilder().RegisterSubplan(input)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task SharedInlineObjectsAreValidatedAtEachCorrelationDepth()
    {
        IRel correlated = CreateCorrelatedRelation(0, 1);
        Project nested = new(CreateRead(), [new ScalarSubquery(correlated, TypeFactory.REQUIRED.I64)]);
        PlanBuilder builder = new();

        await Assert.That(() => builder.RegisterSubplan(new Cross(correlated, nested))).ThrowsExactly<ArgumentException>();
        await Assert.That(() => builder.RegisterSubplan(new Cross(nested, correlated))).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 2)]
    [Arguments(2, 3)]
    public async Task RegistrationRejectsCorrelationEscapingTheEntry(int nestingDepth, int subqueryLevels)
    {
        IRel relation = CreateCorrelatedRelation(nestingDepth, subqueryLevels);
        PlanBuilder builder = new();

        await Assert.That(() => builder.RegisterSubplan(relation)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => builder.AddRoot(relation, [])).ThrowsExactly<ArgumentException>();
        await Assert.That(() =>
            Plan.FromRelations([new Plan.Relation(relation)], PlanVersion.Current)).ThrowsExactly<ArgumentException>();
        await Assert.That(builder.Build().Relations.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(1, 1)]
    [Arguments(2, 1)]
    [Arguments(2, 2)]
    public async Task RegistrationAllowsCorrelationContainedWithinTheEntry(int nestingDepth, int subqueryLevels)
    {
        PlanBuilder builder = new();
        Reference reference = builder.RegisterSubplan(CreateCorrelatedRelation(nestingDepth, subqueryLevels));
        builder.AddRoot(reference, []);

        Plan plan = builder.Build();
        IPlan converted = new ProtoToPlanConverter().From(new PlanToProtoConverter().From(plan));

        await Assert.That(converted).IsEqualTo(plan);
    }

    [Test]
    public async Task ReferenceIsAZeroInputOrdinalEdgeWithTargetSchema()
    {
        NamedTableRead read = new(
            new NamedStruct(["first", "second"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, TypeFactory.REQUIRED.STR])),
            ["table"],
            null,
            new Remap([1, 0]));
        Reference reference = new(7, read);
        Reference equalOrdinalDifferentTarget = new(7, CreateRead("different"));

        await Assert.That(reference).IsAssignableTo<ZeroInput>();
        await Assert.That(reference.Inputs.Count).IsEqualTo(0);
        await Assert.That(reference.InputNodes.Any()).IsFalse();
        await Assert.That(reference.Target).IsSameReferenceAs(read);
        await Assert.That(reference.RecordType).IsEqualTo(read.RecordType);
        await Assert.That(reference.RecordType.Fields[0]).IsEqualTo(TypeFactory.REQUIRED.STR);
        await Assert.That(equalOrdinalDifferentTarget).IsEqualTo(reference);
        await Assert.That(equalOrdinalDifferentTarget.GetHashCode()).IsEqualTo(reference.GetHashCode());
        await Assert.That(new Reference(8, read)).IsNotEqualTo(reference);
        await Assert.That(() => new Reference(-1, read)).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task FullPlanValidatesBoundsAndTargetIdentityInsteadOfStructuralEquality()
    {
        NamedTableRead read = CreateRead();
        NamedTableRead equalRead = CreateRead();
        await Assert.That(equalRead).IsEqualTo(read);

        await Assert.That(() => Plan.FromRelations(
            [new Plan.Relation(read), new Plan.Root(new Reference(2, read), [])], PlanVersion.Current)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => Plan.FromRelations(
            [new Plan.Relation(read), new Plan.Root(new Reference(int.MaxValue, read), [])], PlanVersion.Current)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => Plan.FromRelations(
            [new Plan.Relation(read), new Plan.Root(new Reference(0, equalRead), [])], PlanVersion.Current)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => Plan.FromRelations(
            [new Plan.Relation(new Project(read, [new ScalarSubquery(new Reference(1, read), TypeFactory.REQUIRED.I64)]))],
            PlanVersion.Current)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task SerializerUsesCustomPlanRelationsAsAuthoritativeInsteadOfRoots()
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

        await Assert.That(wire.Relations.Count).IsEqualTo(2);
        await Assert.That(wire.Relations[0].RelTypeCase).IsEqualTo(Substrait.Protobuf.PlanRel.RelTypeOneofCase.Rel);
        await Assert.That(wire.Relations[0].Rel.Read.NamedTable.Names[0]).IsEqualTo("orders");
        await Assert.That(wire.Relations[1].RelTypeCase).IsEqualTo(Substrait.Protobuf.PlanRel.RelTypeOneofCase.Root);
        await Assert.That(wire.Relations[1].Root.Names.Count).IsEqualTo(0);
        await Assert.That(wire.Relations[1].Root.Input.Reference.SubtreeOrdinal).IsEqualTo(0);
        await Assert.That(((Reference)converted.Relations[1].Input).Target).IsSameReferenceAs(converted.Relations[0].Input);
        await Assert.That(() =>
            new PlanToProtoConverter().From(new CustomPlan([], unrelatedRoots))).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task SerializerDefensivelyValidatesCustomPlanReferenceBoundsAndIdentity()
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
            await Assert.That(() => new PlanToProtoConverter().From(plan)).ThrowsExactly<ArgumentException>();
        }
    }

    [Test]
    public async Task SerializerDefensivelyRejectsCustomPlanCyclesIncludingSubqueries()
    {
        MutableInput input = new(CreateRead());
        Reference reference = new(0, input);
        CustomPlan custom = new([new Plan.Relation(input)]);

        input.Child = reference;
        await Assert.That(() => new PlanToProtoConverter().From(custom)).ThrowsExactly<ArgumentException>();

        input.Child = new Project(CreateRead(), [new ScalarSubquery(reference, TypeFactory.REQUIRED.I64)]);
        await Assert.That(() => new PlanToProtoConverter().From(custom)).ThrowsExactly<ArgumentException>();

        input.Child = input;
        await Assert.That(() => new PlanToProtoConverter().From(custom)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 2)]
    [Arguments(2, 3)]
    public async Task SerializerDefensivelyRejectsCustomPlanEscapingCorrelation(int nestingDepth, int subqueryLevels)
    {
        CustomPlan custom = new([new Plan.Relation(CreateCorrelatedRelation(nestingDepth, subqueryLevels))]);

        await Assert.That(() => new PlanToProtoConverter().From(custom)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task PlanEqualityIncludesEntryKindOrderTargetsNamesAndVersion()
    {
        Plan baseline = CreateReferencedPlan("orders", "output", PlanVersion.Current);
        Plan equivalent = CreateReferencedPlan("orders", "output", PlanVersion.Current);

        await Assert.That(equivalent).IsEqualTo(baseline);
        await Assert.That(equivalent.GetHashCode()).IsEqualTo(baseline.GetHashCode());
        await Assert.That(CreateReferencedPlan("customers", "output", PlanVersion.Current)).IsNotEqualTo(baseline);
        await Assert.That(CreateReferencedPlan("orders", "renamed", PlanVersion.Current)).IsNotEqualTo(baseline);
        await Assert.That(CreateReferencedPlan("orders", "output", new PlanVersion(1, 0, 0, "", ""))).IsNotEqualTo(baseline);
        await Assert.That(Plan.FromRelations([new Plan.Root(CreateRead(), [])], PlanVersion.Current)).IsNotEqualTo(Plan.FromRelations([new Plan.Relation(CreateRead())], PlanVersion.Current));
        await Assert.That(Plan.FromRelations([new Plan.Relation(CreateRead("second")), new Plan.Relation(CreateRead("first"))], PlanVersion.Current)).IsNotEqualTo(Plan.FromRelations([new Plan.Relation(CreateRead("first")), new Plan.Relation(CreateRead("second"))], PlanVersion.Current));
    }

    [Test]
    public async Task EmptyPlansRemainConstructibleButNotSerializable()
    {
        Plan[] plans =
        [
            new Plan([], PlanVersion.Current),
            Plan.FromRelations([], PlanVersion.Current),
            new PlanBuilder().Build(),
        ];

        foreach (Plan plan in plans)
        {
            await Assert.That(plan.Relations.Count).IsEqualTo(0);
            await Assert.That(plan.Roots.Count).IsEqualTo(0);
            await Assert.That(() => new PlanToProtoConverter().From(plan)).ThrowsExactly<ArgumentException>();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BuilderSupportsLongReferenceChainsWithoutRecursiveSchemaDerivation(bool wrapReferences)
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

        await Assert.That(plan.Relations.Count).IsEqualTo(count + 1);
        await Assert.That(reference.SubtreeOrdinal).IsEqualTo(count - 1);
        await Assert.That(reference.RecordType.Fields[0]).IsEqualTo(TypeFactory.REQUIRED.I64);
        await Assert.That(new PlanToProtoConverter().From(plan).Relations.Count).IsEqualTo(count + 1);
        await Assert.That(builder.Build()).IsEqualTo(plan);
        await Assert.That(builder.Build().GetHashCode()).IsEqualTo(plan.GetHashCode());
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

        public PlanMetadata Metadata => PlanMetadata.Empty;
    }
}
