// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Plan.Converters;
using Substrait.Protobuf;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoLiteral = Substrait.Protobuf.Expression.Types.Literal;
using ProtoMask = Substrait.Protobuf.Expression.Types.MaskExpression;
using ProtoOuterReference = Substrait.Protobuf.Expression.Types.FieldReference.Types.OuterReference;
using ProtoReferenceSegment = Substrait.Protobuf.Expression.Types.ReferenceSegment;
using ProtoRel = Substrait.Protobuf.Rel;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

[TestClass]
public sealed class ProtoPlanDependenciesTests
{
    private static readonly ProtoRel.RelTypeOneofCase[] SupportedRelations =
    [
        ProtoRel.RelTypeOneofCase.Read, ProtoRel.RelTypeOneofCase.Filter,
        ProtoRel.RelTypeOneofCase.Project, ProtoRel.RelTypeOneofCase.Fetch,
        ProtoRel.RelTypeOneofCase.Sort, ProtoRel.RelTypeOneofCase.Aggregate,
        ProtoRel.RelTypeOneofCase.Cross, ProtoRel.RelTypeOneofCase.Join,
        ProtoRel.RelTypeOneofCase.HashJoin, ProtoRel.RelTypeOneofCase.Set,
        ProtoRel.RelTypeOneofCase.Exchange, ProtoRel.RelTypeOneofCase.Reference,
    ];

    private static readonly ProtoExpression.RexTypeOneofCase[] SupportedExpressions =
    [
        ProtoExpression.RexTypeOneofCase.Literal, ProtoExpression.RexTypeOneofCase.Selection,
        ProtoExpression.RexTypeOneofCase.ScalarFunction, ProtoExpression.RexTypeOneofCase.Cast,
        ProtoExpression.RexTypeOneofCase.IfThen, ProtoExpression.RexTypeOneofCase.Subquery,
    ];

    private static readonly HashSet<MessageDescriptor> RelevantMessages =
    [
        ProtoRel.Descriptor, ProtoExpression.Descriptor, RelCommon.Descriptor,
        ProtoOuterReference.Descriptor, ProtoType.Descriptor, ProtoType.Types.Struct.Descriptor,
        ProtoType.Types.List.Descriptor, ProtoType.Types.Map.Descriptor,
        ProtoReferenceSegment.Descriptor, ProtoMask.Descriptor,
    ];

    private static readonly Func<ProtoRel, int, int, Dictionary<uint, string>, IReadOnlyList<int>> Find =
        typeof(ProtoToPlanConverter).Assembly.GetType("Substrait.Core.Plan.Converters.ProtoPlanDependencies")!
            .GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<ProtoRel, int, int, Dictionary<uint, string>, IReadOnlyList<int>>>();

    private static readonly Func<IMessage, IEnumerable<IMessage?>> Children =
        typeof(ProtoToPlanConverter).Assembly.GetType("Substrait.Core.Plan.Converters.ProtoPlanDependencies")!
            .GetMethod("Children", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<IMessage, IEnumerable<IMessage?>>>();

    public static IEnumerable<object[]> ChildPaths()
    {
        foreach (var variant in SupportedRelations)
        {
            FieldDescriptor field = ProtoRel.Descriptor.FindFieldByNumber((int)variant);
            foreach (FieldDescriptor[] path in RelevantPaths(field.MessageType, []))
            {
                yield return [field.FullName + "." + string.Join(".", path.Select(part => part.Name)), false, new[] { field }.Concat(path).ToArray()];
            }
        }

        foreach (var variant in SupportedExpressions)
        {
            FieldDescriptor field = ProtoExpression.Descriptor.FindFieldByNumber((int)variant);
            foreach (FieldDescriptor[] path in RelevantPaths(field.MessageType, []))
            {
                yield return [field.FullName + "." + string.Join(".", path.Select(part => part.Name)), true, new[] { field }.Concat(path).ToArray()];
            }
        }
    }

    [DataTestMethod]
    [DynamicData(nameof(ChildPaths), DynamicDataSourceType.Method)]
    public void VisitsEveryQueryAndRecursiveField(string name, bool expressionRoot, FieldDescriptor[] path)
    {
        MessageDescriptor target = path[^1].MessageType;
        MessageDescriptor root = expressionRoot ? ProtoExpression.Descriptor : ProtoRel.Descriptor;
        ProtoRel Wrap(IMessage leaf) => AsRelation(CreatePath(root, path, leaf));

        if (target == ProtoRel.Descriptor || target == ProtoExpression.Descriptor)
        {
            ProtoRel reference = new() { Reference = new() { SubtreeOrdinal = 1 } };
            ProtoRel relation = Wrap(target == ProtoRel.Descriptor ? reference : Subquery(reference));
            IReadOnlyList<int> dependencies = Find(relation, 0, 2, []);
            Assert.AreEqual(1, dependencies.Count, name);
            Assert.AreEqual(1, dependencies[0], name);
            Assert.ThrowsException<SerializationException>(() => Find(relation, 0, 1, []), name);

            ProtoRel anchored = new() { Read = new() { Common = new() { RelAnchor = 7 } } };
            relation = Wrap(target == ProtoRel.Descriptor ? anchored : Subquery(anchored));
            Dictionary<uint, string> anchors = [];
            Assert.AreEqual(0, Find(relation, 0, 1, anchors).Count, name);
            Assert.IsTrue(anchors.ContainsKey(7), name);
            Assert.ThrowsException<SerializationException>(() => Find(relation, 1, 2, anchors), name);
        }
        else if (target == RelCommon.Descriptor)
        {
            Assert.ThrowsException<SerializationException>(() => Find(Wrap(new RelCommon { RelAnchor = 0 }), 0, 1, []), name);
            Assert.ThrowsException<SerializationException>(() =>
                Find(Wrap(new RelCommon { RelAnchor = 7 }), 0, 1, new() { [7] = "another occurrence" }), name);
        }
        else if (target == ProtoOuterReference.Descriptor)
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                Find(Wrap(new ProtoOuterReference { RelReference = 7 }), 0, 1, []), name);
            Assert.ThrowsException<SerializationException>(() =>
                Find(Wrap(new ProtoOuterReference()), 0, 1, []), name);
        }
        else
        {
            var (cyclePath, ancestor) = CyclicPaths(target, [target]).First();
            IMessage cycle = CreateCycle(target, cyclePath, ancestor);
            SerializationException error = Assert.ThrowsException<SerializationException>(() => Find(Wrap(cycle), 0, 1, []), name);
            StringAssert.Contains(error.Message, "cycle");
        }
    }

    public static IEnumerable<object[]> RecursivePaths()
    {
        MessageDescriptor[] roots = [ProtoType.Descriptor, ProtoLiteral.Descriptor, ProtoReferenceSegment.Descriptor, ProtoMask.Descriptor];
        for (int kind = 0; kind < roots.Length; ++kind)
        {
            foreach (var (path, ancestor) in CyclicPaths(roots[kind], [roots[kind]]))
            {
                yield return [string.Join(".", path.Select(field => field.FullName)), kind, path, ancestor];
            }
        }
    }

    [DataTestMethod]
    [DynamicData(nameof(RecursivePaths), DynamicDataSourceType.Method)]
    public void RetainsEveryRecursiveSchemaEdge(string name, int kind, FieldDescriptor[] path, int ancestor)
    {
        IMessage cycle = CreateCycle(path[0].ContainingType, path, ancestor);
        ProtoRel relation = kind switch
        {
            0 => new() { Read = new() { BaseSchema = new() { Struct = new() { Types_ = { (ProtoType)cycle } } } } },
            1 => Project(new ProtoExpression { Literal = (ProtoLiteral)cycle }),
            2 => Project(new ProtoExpression { Selection = new() { DirectReference = (ProtoReferenceSegment)cycle } }),
            3 => new() { Read = new() { Projection = (ProtoMask)cycle } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        SerializationException error = Assert.ThrowsException<SerializationException>(() => Find(relation, 0, 1, []), name);
        StringAssert.Contains(error.Message, "cycle");
    }

    [TestMethod]
    public void RejectsRelationAndExpressionObjectCyclesButAllowsInlineSharing()
    {
        ProtoRel relation = new() { Filter = new() };
        relation.Filter.Input = relation;
        Assert.ThrowsException<SerializationException>(() => Find(relation, 0, 1, []));

        ProtoExpression expression = new() { Cast = new() };
        expression.Cast.Input = expression;
        Assert.ThrowsException<SerializationException>(() => Find(Project(expression), 0, 1, []));

        ProtoRel shared = new() { Read = new() { Common = new() } };
        ProtoRel cross = new() { Cross = new() { Left = shared, Right = shared } };
        Assert.AreEqual(0, Find(cross, 0, 1, []).Count);
        shared.Read.Common.RelAnchor = 7;
        Assert.ThrowsException<SerializationException>(() => Find(cross, 0, 1, []));
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public void SubqueryOperandsKeepTheirEnclosingCorrelationDepth(int kind)
    {
#pragma warning disable CS0612 // Exercise the supported legacy correlation representation.
        ProtoExpression outer = new() { Selection = new() { OuterReference = new() { StepsOut = 1 } } };
#pragma warning restore CS0612
        ProtoExpression subquery = PlanReferenceConversionTests.WireSubquery(kind, new ProtoRel { Read = new() });
        if (kind == 1)
        {
            subquery.Subquery.InPredicate.Needles.Clear();
            subquery.Subquery.InPredicate.Needles.Add(outer);
        }
        else
        {
            subquery.Subquery.SetComparison.Left = outer;
        }

        Assert.ThrowsException<SerializationException>(() => Find(Project(subquery), 0, 1, []));
        Assert.AreEqual(0, Find(Project(Subquery(Project(subquery))), 0, 1, []).Count);

        // The same expression must also be checked when encountered at a shallower depth.
        Assert.ThrowsException<SerializationException>(() =>
            Find(Project(subquery, Subquery(Project(subquery))), 0, 1, []));
    }

    [TestMethod]
    public void UnsupportedVariantsFailExplicitly()
    {
        foreach (FieldDescriptor field in MessageFields(ProtoRel.Descriptor))
        {
            if (!SupportedRelations.Contains((ProtoRel.RelTypeOneofCase)field.FieldNumber))
            {
                ProtoRel relation = (ProtoRel)CreatePath(ProtoRel.Descriptor, [field], Empty(field.MessageType));
                Assert.ThrowsException<NotImplementedException>(() => Find(relation, 0, 1, []), field.FullName);
            }
        }

        foreach (FieldDescriptor field in MessageFields(ProtoExpression.Descriptor))
        {
            if (!SupportedExpressions.Contains((ProtoExpression.RexTypeOneofCase)field.FieldNumber))
            {
                ProtoExpression expression = (ProtoExpression)CreatePath(ProtoExpression.Descriptor, [field], Empty(field.MessageType));
                Assert.ThrowsException<NotImplementedException>(() => Find(Project(expression), 0, 1, []), field.FullName);
            }
        }

        Assert.ThrowsException<SerializationException>(() => Find(new ProtoRel(), 0, 1, []));
        Assert.ThrowsException<NotImplementedException>(() => Find(Project(new ProtoExpression()), 0, 1, []));
        Assert.ThrowsException<NotImplementedException>(() => Find(Project(new ProtoExpression { Subquery = new() }), 0, 1, []));
    }

    [TestMethod]
    public void MetadataPayloadSizeDoesNotIncreaseTraversalWork()
    {
        ProtoRel relation = new() { Read = new() { Common = new() { RelAnchor = 7 } } };
        Assert.AreEqual(2, CountVisits(relation));
        relation.Read.Common.Hint = MetadataFacadeTests.CreateCommon().Hint;
        for (int index = 0; index < 1000; ++index)
        {
            relation.Read.Common.Hint.SavedComputations.Add(new RelCommon.Types.Hint.Types.SavedComputation());
        }

        Assert.AreEqual(2, CountVisits(relation));
        Dictionary<uint, string> anchors = [];
        Assert.AreEqual(0, Find(relation, 0, 1, anchors).Count);
        Assert.IsTrue(anchors.ContainsKey(7));

        foreach (FieldDescriptor field in MessageFields(RelCommon.Descriptor))
        {
            AssertOpaque(field.MessageType, []);
        }
    }

    private static IEnumerable<FieldDescriptor[]> RelevantPaths(MessageDescriptor descriptor, HashSet<MessageDescriptor> active)
    {
        if (!active.Add(descriptor))
        {
            yield break;
        }

        foreach (FieldDescriptor field in MessageFields(descriptor))
        {
            if (RelevantMessages.Contains(field.MessageType))
            {
                yield return [field];
            }
            else
            {
                foreach (FieldDescriptor[] path in RelevantPaths(field.MessageType, active))
                {
                    yield return new[] { field }.Concat(path).ToArray();
                }
            }
        }

        active.Remove(descriptor);
    }

    private static IEnumerable<(FieldDescriptor[] Path, int Ancestor)> CyclicPaths(
        MessageDescriptor descriptor, List<MessageDescriptor> active)
    {
        foreach (FieldDescriptor field in MessageFields(descriptor))
        {
            int ancestor = active.IndexOf(field.MessageType);
            if (ancestor >= 0)
            {
                yield return ([field], ancestor);
            }
            else
            {
                active.Add(field.MessageType);
                foreach (var (path, target) in CyclicPaths(field.MessageType, active))
                {
                    yield return (new[] { field }.Concat(path).ToArray(), target);
                }

                active.RemoveAt(active.Count - 1);
            }
        }
    }

    private static IMessage CreatePath(MessageDescriptor descriptor, FieldDescriptor[] path, IMessage leaf)
    {
        IMessage root = Empty(descriptor);
        IMessage current = root;
        for (int index = 0; index < path.Length; ++index)
        {
            IMessage child = index == path.Length - 1 ? leaf : Empty(path[index].MessageType);
            SetChild(current, path[index], child);
            current = child;
        }

        return root;
    }

    private static IMessage CreateCycle(MessageDescriptor descriptor, FieldDescriptor[] path, int ancestor)
    {
        List<IMessage> messages = [Empty(descriptor)];
        for (int index = 0; index < path.Length; ++index)
        {
            IMessage child = index == path.Length - 1 ? messages[ancestor] : Empty(path[index].MessageType);
            SetChild(messages[^1], path[index], child);
            messages.Add(child);
        }

        return messages[0];
    }

    private static void SetChild(IMessage parent, FieldDescriptor field, IMessage child)
    {
        if (field.IsRepeated)
        {
            ((IList)field.Accessor.GetValue(parent)).Add(child);
        }
        else
        {
            field.Accessor.SetValue(parent, child);
        }
    }

    private static IMessage Empty(MessageDescriptor descriptor) => descriptor.Parser.ParseFrom(Array.Empty<byte>());

    private static IEnumerable<FieldDescriptor> MessageFields(MessageDescriptor descriptor) =>
        descriptor.Fields.InFieldNumberOrder().Where(field => field.FieldType == FieldType.Message);

    private static ProtoRel AsRelation(IMessage message) =>
        message is ProtoRel relation ? relation : Project((ProtoExpression)message);

    private static ProtoRel Project(params ProtoExpression[] expressions) =>
        new() { Project = new() { Expressions = { expressions } } };

    private static ProtoExpression Subquery(ProtoRel input) => new() { Subquery = new() { Scalar = new() { Input = input } } };

    private static int CountVisits(IMessage root)
    {
        Stack<IMessage> stack = new();
        stack.Push(root);
        int count = 0;
        while (stack.Count > 0)
        {
            ++count;
            foreach (IMessage? child in Children(stack.Pop()))
            {
                if (child is not null)
                {
                    stack.Push(child);
                }
            }
        }

        return count;
    }

    private static void AssertOpaque(MessageDescriptor descriptor, HashSet<MessageDescriptor> active)
    {
        Assert.IsFalse(RelevantMessages.Contains(descriptor), descriptor.FullName);
        Assert.IsTrue(active.Add(descriptor), descriptor.FullName);
        foreach (FieldDescriptor field in MessageFields(descriptor))
        {
            AssertOpaque(field.MessageType, active);
        }

        active.Remove(descriptor);
    }
}
