// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text;
using Substrait.Core.Expression;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using Substrait.Tools.Visitor;
using static Substrait.Core.Expression.Literal;
using Assembly = System.Reflection.Assembly;

namespace Substrait.Tests.Core;

public sealed class RelationVisitorTests
{
    [Test]
    public async Task RelVisitorContainsAllSealedRelationTypes()
    {
        List<System.Type> relationTypes = Assembly.GetAssembly(typeof(IRel))!
            .GetTypes()
            .Where(type => typeof(IRel).IsAssignableFrom(type)
                && type.IsClass
                && type.IsSealed
                && type.Namespace == "Substrait.Core.Relation")
            .ToList();

        List<System.Type> visitedTypes = typeof(RelVisitor<,>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Visit" && method.GetParameters().Length == 2)
            .Select(method => method.GetParameters()[0].ParameterType)
            .ToList();

        foreach (System.Type relationType in relationTypes)
        {
            await Assert.That(visitedTypes.Contains(relationType)).IsTrue().Because($"RelVisitor does not contain a Visit method for {relationType.Name}");
        }
    }

    [Test]
    public async Task DispatchersTraverseSyntheticRelationTree()
    {
        IRel root = CreateRelationTree(true);
        StringBuilderContext topDownContext = new();
        StringBuilderContext bottomUpContext = new();

        new RelTopDownDispatcher<StringBuilderContext, VoidOutput>(new RelationPrinter())
            .Dispatch(root, topDownContext);
        new RelBottomUpDispatcher<StringBuilderContext, VoidOutput>(new RelationPrinter())
            .Dispatch(root, bottomUpContext);

        await Assert.That(topDownContext.ToString()).IsEqualTo("Project|Cross|Filter|NamedTableRead|NamedTableRead|");
        await Assert.That(bottomUpContext.ToString()).IsEqualTo("NamedTableRead|Filter|NamedTableRead|Cross|Project|");
    }

    [Test]
    public async Task DispatchersTreatReferencesAsLeavesWithoutVisitingTheirTargets()
    {
        Reference reference = new(0, CreateRelationTree(true));
        Cross relation = new(reference, reference);
        StringBuilderContext topDownContext = new();
        StringBuilderContext bottomUpContext = new();

        new RelTopDownDispatcher<StringBuilderContext, VoidOutput>(new RelationPrinter())
            .Dispatch(relation, topDownContext);
        new RelBottomUpDispatcher<StringBuilderContext, VoidOutput>(new RelationPrinter())
            .Dispatch(relation, bottomUpContext);

        await Assert.That(topDownContext.ToString()).IsEqualTo("Cross|Reference|Reference|");
        await Assert.That(bottomUpContext.ToString()).IsEqualTo("Reference|Reference|Cross|");
    }

    [Test]
    public async Task RelationEqualityIncludesNestedInputs()
    {
        Project first = CreateRelationTree(true);
        Project equivalent = CreateRelationTree(true);
        Project different = CreateRelationTree(false);

        await Assert.That(equivalent).IsEqualTo(first);
        await Assert.That(equivalent.GetHashCode()).IsEqualTo(first.GetHashCode());
        await Assert.That(different).IsNotEqualTo(first);
    }

    [Test]
    public async Task DispatchersBailOutWhenInteriorFilterIsFound()
    {
        IRel root = CreateRelationTree(true);
        BoolStringBuilderContext topDownContext = new();
        BoolStringBuilderContext bottomUpContext = new();

        await Assert.That(new FilterFindingTopDownDispatcher().Dispatch(root, topDownContext)).IsTrue();
        await Assert.That(new FilterFindingBottomUpDispatcher().Dispatch(root, bottomUpContext)).IsTrue();
        await Assert.That(topDownContext.ToString()).IsEqualTo("Project|Cross|Filter|");
        await Assert.That(bottomUpContext.ToString()).IsEqualTo("NamedTableRead|Filter|");
    }

    private static Project CreateRelationTree(bool condition)
    {
        NamedStruct schema = new(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]));
        NamedTableRead orders = new(schema, ["orders"], filter: null);
        NamedTableRead customers = new(schema, ["customers"], filter: null);
        Filter filter = new(orders, new BoolLiteral(condition));
        Cross cross = new(filter, customers);
        return new Project(cross, [new FieldReference(TypeFactory.REQUIRED.I64, 0)]);
    }

    private sealed class StringBuilderContext : NoOpContext<IRel, VoidOutput>
    {
        private readonly StringBuilder builder = new();

        public void Append(string value) => this.builder.Append(value).Append('|');

        public override string ToString() => this.builder.ToString();
    }

    private sealed class BoolStringBuilderContext : NoOpContext<IRel, bool>
    {
        private readonly StringBuilder builder = new();

        public void Append(string value) => this.builder.Append(value).Append('|');

        public override string ToString() => this.builder.ToString();
    }

    private sealed class RelationPrinter : DefaultRelVisitor<StringBuilderContext, VoidOutput>
    {
        public override VoidOutput Visit(IRel other, StringBuilderContext context)
        {
            throw new NotSupportedException($"Unable to print relation {other.GetType().Name}");
        }

        protected override VoidOutput DefaultVisit(IRel relation, StringBuilderContext context)
        {
            context.Append(relation.GetType().Name);
            return VoidOutput.Instance;
        }
    }

    private sealed class FilterFindingTopDownDispatcher
        : RelTopDownDispatcher<BoolStringBuilderContext, bool>
    {
        public FilterFindingTopDownDispatcher()
            : base(new FilterFinder())
        {
        }

        protected override bool ShouldBailOut(bool result, BoolStringBuilderContext context) => result;
    }

    private sealed class FilterFindingBottomUpDispatcher
        : RelBottomUpDispatcher<BoolStringBuilderContext, bool>
    {
        public FilterFindingBottomUpDispatcher()
            : base(new FilterFinder())
        {
        }

        protected override bool ShouldBailOut(bool result, BoolStringBuilderContext context) => result;
    }

    private sealed class FilterFinder : DefaultRelVisitor<BoolStringBuilderContext, bool>
    {
        public override bool Visit(Filter filter, BoolStringBuilderContext context)
        {
            context.Append(nameof(Filter));
            return true;
        }

        protected override bool DefaultVisit(IRel relation, BoolStringBuilderContext context)
        {
            context.Append(relation.GetType().Name);
            return false;
        }
    }
}
