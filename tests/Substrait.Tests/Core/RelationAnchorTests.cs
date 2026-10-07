// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.Serialization;
using Google.Protobuf;
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

public sealed class RelationAnchorTests
{
    private static readonly Action<uint?, Dictionary<uint, string>, string, Func<string, Exception>> RegisterAnchor =
        typeof(ProtoToPlanConverter).Assembly.GetType("Substrait.Core.Plan.PlanValidation")!
            .GetMethod("RegisterAnchor", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Action<uint?, Dictionary<uint, string>, string, Func<string, Exception>>>();

    [Test]
    public async Task SuccessfulAnchorRegistrationUsesOneLookupAndPreservesDuplicateDiagnostics()
    {
        CountingAnchorComparer comparer = new();
        Dictionary<uint, string> anchors = new(4, comparer);
        RegisterAnchor(7, anchors, "first occurrence", message => new SerializationException(message));
        await Assert.That(comparer.HashCalls).IsEqualTo(1);
        RegisterAnchor(8, anchors, "second occurrence", message => new SerializationException(message));
        await Assert.That(comparer.HashCalls).IsEqualTo(2);

        SerializationException error = await Assert.That(() =>
            RegisterAnchor(7, anchors, "duplicate occurrence", message => new SerializationException(message))).ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("duplicate relation anchor 7");
        await Assert.That(error.Message).Contains("first occurrence");
        await Assert.That(error.Message).Contains("duplicate occurrence");
        await Assert.That(anchors.Count).IsEqualTo(2);
        await Assert.That(anchors[7]).IsEqualTo("first occurrence");
    }

    [Test]
    public async Task AbsentAndZeroAnchorsDoNotProbeOrModifyTheDictionary()
    {
        CountingAnchorComparer comparer = new();
        Dictionary<uint, string> anchors = new(4, comparer);
        RegisterAnchor(null, anchors, "absent", message => new SerializationException(message));
        await Assert.That(() =>
            RegisterAnchor(0, anchors, "zero", message => new SerializationException(message))).ThrowsExactly<SerializationException>();
        await Assert.That(comparer.HashCalls).IsEqualTo(0);
        await Assert.That(anchors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AnchorsArePositiveUniqueAndUseTheFullUintRange()
    {
        NamedTableRead first = Anchored(1);
        NamedTableRead last = Anchored(uint.MaxValue);
        CorePlan plan = CorePlan.FromRelations(
            [new CorePlan.Root(first, []), new CorePlan.Relation(last)], Substrait.Core.Plan.Version.Current);
        await Assert.That(Decoder().FromBytes(new PlanToProtoConverter().From(plan).ToByteArray(), ExtensionsDictionary.StrictMode.OFF)).IsEqualTo(plan);
        await Assert.That(() => Plan(Anchored(0))).ThrowsExactly<ArgumentException>();
        await Assert.That(() => Plan(new Cross(first, Anchored(1)))).ThrowsExactly<ArgumentException>();
        await Assert.That(() => CorePlan.FromRelations(
            [new CorePlan.Root(first, []), new CorePlan.Relation(first)], Substrait.Core.Plan.Version.Current)).ThrowsExactly<ArgumentException>();

        ProtoRel wire = WireRead(0);
        await Assert.That(() => Decoder().From(WirePlan(wire), ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
        wire = WireRead(1);
        await Assert.That(() => Decoder().From(WirePlan(wire, wire.Clone()), ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task RepeatedInlineObjectsAndAnchoredDescendantsCountAsSeparateOccurrences()
    {
        NamedTableRead anchored = Anchored(7);
        Filter shared = new(anchored, new Literal.BoolLiteral(true));
        ArgumentException error = await Assert.That(() => Plan(new Cross(shared, shared))).ThrowsExactly<ArgumentException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("duplicate relation anchor 7");
        await Assert.That(error.Message).Contains("relation occurrence");

        ProtoRel protoShared = new()
        {
            Filter = new() { Input = WireRead(7), Condition = new() { Literal = new() { Boolean = true } } },
        };
        ProtoRel cross = new() { Cross = new() { Left = protoShared, Right = protoShared } };
        await Assert.That(() => Decoder().From(WirePlan(cross), ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();

        // Sharing only the common message must also count each defining relation.
        ProtoRel first = WireRead(7);
        ProtoRel second = WireRead(8);
        second.Read.Common = first.Read.Common;
        await Assert.That(() => Decoder().From(WirePlan(first, second), ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
        await Assert.That(Plan(new Cross(Read(), Read())).Roots.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExplicitReferencesDoNotRecountTheirTargetsOrCollideWithOrdinals()
    {
        PlanBuilder builder = new();
        Reference shared = builder.RegisterSubplan(Anchored(1));
        builder.AddRoot(new Cross(shared, shared), []);
        builder.AddRoot(shared, []);
        CorePlan plan = builder.Build();
        await Assert.That(shared.SubtreeOrdinal).IsEqualTo(0);
        await Assert.That(shared.Target.Metadata.Common!.RelAnchor).IsEqualTo(1U);
        await Assert.That(Decoder().FromBytes(new PlanToProtoConverter().From(plan).ToByteArray(), ExtensionsDictionary.StrictMode.OFF)).IsEqualTo(plan);

        ProtoRel forward = new() { Reference = new() { SubtreeOrdinal = 1 } };
        var wire = WirePlan(forward, WireRead(1), forward.Clone());
        IPlan converted = Decoder().From(wire, ExtensionsDictionary.StrictMode.OFF);
        await Assert.That(((Reference)converted.Relations[0].Input).Target).IsSameReferenceAs(converted.Relations[1].Input);
        await Assert.That(new PlanToProtoConverter().From(converted)).IsEqualTo(wire);
    }

    [Test]
    public async Task RejectedBuilderRegistrationsDoNotConsumeOrdinalsOrReserveAnchors()
    {
        PlanBuilder builder = new();
        builder.RegisterSubplan(Anchored(7));
        await Assert.That(() => builder.AddRoot(new Cross(Anchored(8), Anchored(7)), [])).ThrowsExactly<ArgumentException>();
        Reference next = builder.RegisterSubplan(Anchored(8));
        await Assert.That(next.SubtreeOrdinal).IsEqualTo(1);
        await Assert.That(builder.Build().Relations.Count).IsEqualTo(2);
        await Assert.That(() => builder.RegisterSubplan(next.Target)).ThrowsExactly<ArgumentException>();
        await Assert.That(builder.Build().Relations.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FailedAnchorBatchRollsBackNewKeysWithoutRemovingExistingKeys()
    {
        PlanBuilder builder = new();
        builder.RegisterSubplan(Anchored(7));
        builder.RegisterSubplan(Anchored(11));
        IPlan before = builder.Build();

        // The right-hand inputs are visited first, inserting 9 and 8 before colliding with 11.
        Cross candidate = new(new Cross(Anchored(7), Anchored(11)), new Cross(Anchored(8), Anchored(9)));
        ArgumentException error = await Assert.That(() => builder.AddRoot(candidate, [])).ThrowsExactly<ArgumentException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("duplicate relation anchor 11");
        await Assert.That(error.Message).Contains("ordinal 1");
        await Assert.That(error.Message).Contains("ordinal 2");
        await Assert.That<IPlan>(builder.Build()).IsEqualTo(before);

        await Assert.That(builder.RegisterSubplan(Anchored(8)).SubtreeOrdinal).IsEqualTo(2);
        await Assert.That(builder.RegisterSubplan(Anchored(9)).SubtreeOrdinal).IsEqualTo(3);
        await Assert.That(() => builder.RegisterSubplan(Anchored(7))).ThrowsExactly<ArgumentException>();
        await Assert.That(() => builder.RegisterSubplan(Anchored(11))).ThrowsExactly<ArgumentException>();
        await Assert.That(builder.Build().Relations.Count).IsEqualTo(4);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task AnchorsInEverySubqueryKindParticipateInPlanWideValidation(int kind)
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
        await Assert.That(() => CorePlan.FromRelations(
            [new CorePlan.Relation(valid.Relations[0].Input), new CorePlan.Root(nested, [])], Substrait.Core.Plan.Version.Current)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => Decoder().From(WirePlan(root, WireRead(1)), ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
        root.Project.Expressions.Add(root.Project.Expressions[0]);
        await Assert.That(() => Decoder().From(WirePlan(root), ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task SerializationRevalidatesCustomPlansAndErrorsIdentifyBothEntries()
    {
        var entries = new IPlan.IRelation[] { new CorePlan.Relation(Anchored(42)), new CorePlan.Root(Anchored(42), []) };
        ArgumentException error = await Assert.That(() => new PlanToProtoConverter().From(new UnvalidatedPlan(entries))).ThrowsExactly<ArgumentException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("42");
        await Assert.That(error.Message).Contains("ordinal 0");
        await Assert.That(error.Message).Contains("ordinal 1");

        SerializationException wireError = await Assert.That(() =>
            Decoder().From(WirePlan(WireRead(42), WireRead(42)), ExtensionsDictionary.StrictMode.OFF)).ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(wireError.Message).Contains("ordinal 0");
        await Assert.That(wireError.Message).Contains("ordinal 1");
    }

    [Test]
    public async Task DeepInlineValidationAndSerializationRemainIterative()
    {
        IRel input = Anchored(1);
        for (int index = 0; index < 10000; ++index)
        {
            input = new Filter(input, new Literal.BoolLiteral(true));
        }

        CorePlan plan = Plan(input);
        var wire = new PlanToProtoConverter().From(plan);
        IPlan converted = Decoder().From(wire, ExtensionsDictionary.StrictMode.OFF);
        await Assert.That(converted).IsEqualTo(plan);
    }

    private static NamedTableRead Anchored(uint anchor) =>
        Read(new RelationMetadata(ReadOnlyRelCommon.FromProto(new RelCommon { RelAnchor = anchor })));

    private static ProtoRel WireRead(uint anchor)
    {
        ProtoRel wire = new RelToProtoConverter().From(Read());
        wire.Read.Common.RelAnchor = anchor;
        return wire;
    }

    private sealed class CountingAnchorComparer : IEqualityComparer<uint>
    {
        internal int HashCalls { get; private set; }

        public bool Equals(uint left, uint right) => left == right;

        public int GetHashCode(uint value)
        {
            ++this.HashCalls;
            return value.GetHashCode();
        }
    }

    private sealed class UnvalidatedPlan(IReadOnlyList<IPlan.IRelation> relations) : IPlan
    {
        public IReadOnlyList<IPlan.IRelation> Relations => relations;

        public IReadOnlyList<IPlan.IRoot> Roots => this.Relations.OfType<IPlan.IRoot>().ToArray();

        public IVersion Version => Substrait.Core.Plan.Version.Current;

        public PlanMetadata Metadata => PlanMetadata.Empty;
    }
}
