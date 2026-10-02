// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Expression;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Protobuf;
using static Substrait.Tests.Core.RelationMetadataConversionTests;
using CorePlan = Substrait.Core.Plan.Plan;
using ProtoRel = Substrait.Protobuf.Rel;

namespace Substrait.Tests.Core;

[TestClass]
public sealed class RelationAnchorTests
{
    [TestMethod]
    public void AnchorsArePositiveUniqueAndUseTheFullUintRange()
    {
        NamedTableRead first = Anchored(1);
        NamedTableRead last = Anchored(uint.MaxValue);
        CorePlan plan = CorePlan.FromRelations(
            [new CorePlan.Root(first, []), new CorePlan.Relation(last)], Substrait.Core.Plan.Version.Current);
        Assert.AreEqual(plan, Decoder().FromBytes(new PlanToProtoConverter().From(plan).ToByteArray(), ExtensionsDictionary.StrictMode.OFF));
        Assert.ThrowsException<ArgumentException>(() => Plan(Anchored(0)));
        Assert.ThrowsException<ArgumentException>(() => Plan(new Cross(first, Anchored(1))));
        Assert.ThrowsException<ArgumentException>(() => CorePlan.FromRelations(
            [new CorePlan.Root(first, []), new CorePlan.Relation(first)], Substrait.Core.Plan.Version.Current));

        ProtoRel wire = WireRead(0);
        Assert.ThrowsException<SerializationException>(() => Decoder().From(WirePlan(wire), ExtensionsDictionary.StrictMode.OFF));
        wire = WireRead(1);
        Assert.ThrowsException<SerializationException>(() => Decoder().From(WirePlan(wire, wire.Clone()), ExtensionsDictionary.StrictMode.OFF));
    }

    [TestMethod]
    public void RepeatedInlineObjectsAndAnchoredDescendantsCountAsSeparateOccurrences()
    {
        NamedTableRead anchored = Anchored(7);
        Filter shared = new(anchored, new Literal.BoolLiteral(true));
        ArgumentException error = Assert.ThrowsException<ArgumentException>(() => Plan(new Cross(shared, shared)));
        StringAssert.Contains(error.Message, "duplicate relation anchor 7");
        StringAssert.Contains(error.Message, "relation occurrence");

        ProtoRel protoShared = new()
        {
            Filter = new() { Input = WireRead(7), Condition = new() { Literal = new() { Boolean = true } } },
        };
        ProtoRel cross = new() { Cross = new() { Left = protoShared, Right = protoShared } };
        Assert.ThrowsException<SerializationException>(() => Decoder().From(WirePlan(cross), ExtensionsDictionary.StrictMode.OFF));

        // Sharing only the common message must also count each defining relation.
        ProtoRel first = WireRead(7);
        ProtoRel second = WireRead(8);
        second.Read.Common = first.Read.Common;
        Assert.ThrowsException<SerializationException>(() => Decoder().From(WirePlan(first, second), ExtensionsDictionary.StrictMode.OFF));
        Assert.AreEqual(1, Plan(new Cross(Read(), Read())).Roots.Count);
    }

    [TestMethod]
    public void ExplicitReferencesDoNotRecountTheirTargetsOrCollideWithOrdinals()
    {
        PlanBuilder builder = new();
        Reference shared = builder.RegisterSubplan(Anchored(1));
        builder.AddRoot(new Cross(shared, shared), []);
        builder.AddRoot(shared, []);
        CorePlan plan = builder.Build();
        Assert.AreEqual(0, shared.SubtreeOrdinal);
        Assert.AreEqual(1U, shared.Target.Metadata.Common!.RelAnchor);
        Assert.AreEqual(plan, Decoder().FromBytes(new PlanToProtoConverter().From(plan).ToByteArray(), ExtensionsDictionary.StrictMode.OFF));

        ProtoRel forward = new() { Reference = new() { SubtreeOrdinal = 1 } };
        var wire = WirePlan(forward, WireRead(1), forward.Clone());
        IPlan converted = Decoder().From(wire, ExtensionsDictionary.StrictMode.OFF);
        Assert.AreSame(converted.Relations[1].Input, ((Reference)converted.Relations[0].Input).Target);
        Assert.AreEqual(wire, new PlanToProtoConverter().From(converted));
    }

    [TestMethod]
    public void RejectedBuilderRegistrationsDoNotConsumeOrdinalsOrReserveAnchors()
    {
        PlanBuilder builder = new();
        builder.RegisterSubplan(Anchored(7));
        Assert.ThrowsException<ArgumentException>(() => builder.AddRoot(new Cross(Anchored(8), Anchored(7)), []));
        Reference next = builder.RegisterSubplan(Anchored(8));
        Assert.AreEqual(1, next.SubtreeOrdinal);
        Assert.AreEqual(2, builder.Build().Relations.Count);
        Assert.ThrowsException<ArgumentException>(() => builder.RegisterSubplan(next.Target));
        Assert.AreEqual(2, builder.Build().Relations.Count);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void AnchorsInEverySubqueryKindParticipateInPlanWideValidation(int kind)
    {
        ProtoRel root = new()
        {
            Project = new()
            {
                Input = WireRead(2),
                Expressions = { PlanReferenceConversionTests.WireSubquery(kind, WireRead(1)) },
            },
        };
        IPlan valid = Decoder().From(WirePlan(root), ExtensionsDictionary.StrictMode.OFF);
        IRel nested = PlanReferenceConversionTests.GetSubquery(((Project)valid.Relations[0].Input).Expressions[0]);
        Assert.ThrowsException<ArgumentException>(() => CorePlan.FromRelations(
            [new CorePlan.Relation(valid.Relations[0].Input), new CorePlan.Root(nested, [])], Substrait.Core.Plan.Version.Current));
        Assert.ThrowsException<SerializationException>(() => Decoder().From(WirePlan(root, WireRead(1)), ExtensionsDictionary.StrictMode.OFF));
        root.Project.Expressions.Add(root.Project.Expressions[0]);
        Assert.ThrowsException<SerializationException>(() => Decoder().From(WirePlan(root), ExtensionsDictionary.StrictMode.OFF));
    }

    [TestMethod]
    public void SerializationRevalidatesCustomPlansAndErrorsIdentifyBothEntries()
    {
        var entries = new IPlan.IRelation[] { new CorePlan.Relation(Anchored(42)), new CorePlan.Root(Anchored(42), []) };
        ArgumentException error = Assert.ThrowsException<ArgumentException>(() => new PlanToProtoConverter().From(new UnvalidatedPlan(entries)));
        StringAssert.Contains(error.Message, "42");
        StringAssert.Contains(error.Message, "ordinal 0");
        StringAssert.Contains(error.Message, "ordinal 1");

        SerializationException wireError = Assert.ThrowsException<SerializationException>(() =>
            Decoder().From(WirePlan(WireRead(42), WireRead(42)), ExtensionsDictionary.StrictMode.OFF));
        StringAssert.Contains(wireError.Message, "ordinal 0");
        StringAssert.Contains(wireError.Message, "ordinal 1");
    }

    [TestMethod]
    public void DeepInlineValidationAndSerializationRemainIterative()
    {
        IRel input = Anchored(1);
        for (int index = 0; index < 10000; ++index)
        {
            input = new Filter(input, new Literal.BoolLiteral(true));
        }

        CorePlan plan = Plan(input);
        var wire = new PlanToProtoConverter().From(plan);
        IPlan converted = Decoder().From(wire, ExtensionsDictionary.StrictMode.OFF);
        Assert.AreEqual(plan, converted);
    }

    private static NamedTableRead Anchored(uint anchor) =>
        Read(new RelationMetadata(ReadOnlyRelCommon.FromProto(new RelCommon { RelAnchor = anchor })));

    private static ProtoRel WireRead(uint anchor)
    {
        ProtoRel wire = new RelToProtoConverter().From(Read());
        wire.Read.Common.RelAnchor = anchor;
        return wire;
    }

    private sealed class UnvalidatedPlan(IReadOnlyList<IPlan.IRelation> relations) : IPlan
    {
        public IReadOnlyList<IPlan.IRelation> Relations => relations;

        public IReadOnlyList<IPlan.IRoot> Roots => this.Relations.OfType<IPlan.IRoot>().ToArray();

        public IVersion Version => Substrait.Core.Plan.Version.Current;
    }
}
