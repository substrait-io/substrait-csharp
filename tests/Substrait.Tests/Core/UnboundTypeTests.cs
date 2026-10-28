// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class UnboundTypeTests
{
    [Test]
    public async Task RoundTripsThroughProto()
    {
        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(UnboundType.Instance);
        await Assert.That(proto.KindCase).IsEqualTo(ProtoType.KindOneofCase.Unbound);

        foreach (var roundTripped in new[] { proto, ProtoType.Parser.ParseFrom(proto.ToByteArray()), ProtoType.Parser.ParseJson(proto.ToString()) })
        {
            IType actual = import.From(roundTripped);
            await Assert.That(actual).IsSameReferenceAs(UnboundType.Instance);
            await Assert.That(actual.Equals(UnboundType.Instance, ITypeComparison.Strict)).IsTrue();
            await Assert.That(export.From(actual)).IsEqualTo(proto);
        }
    }

    [Test]
    public async Task RemainsDistinguishableFromUnsetTypeKind()
    {
        // An entirely unset Type.kind at the root is not the same as an explicit Unbound: the
        // dispatcher has nothing at all to dispatch on.
        ProtoToTypeConverter converter = new();
        await Assert.That(() => converter.From(new ProtoType())).ThrowsExactly<NotImplementedException>();
    }

    [Test]
    public async Task RoundTripsNestedInsideStruct()
    {
        ParameterizedType.Struct type = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, UnboundType.Instance]);

        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();
        ProtoType proto = export.From(type);
        IType actual = import.From(proto);

        await Assert.That(actual).IsEqualTo(type);
        await Assert.That(export.From(actual)).IsEqualTo(proto);

        var actualStruct = (ParameterizedType.Struct)actual;
        await Assert.That(actualStruct.Fields[1]).IsSameReferenceAs(UnboundType.Instance);
    }

    [Test]
    public async Task NullableThrows()
    {
        await Assert.That(() => UnboundType.Instance.Nullable).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task TypeVariationIsAlwaysNull()
    {
        await Assert.That(UnboundType.Instance.TypeVariation).IsNull();
    }

    [Test]
    public async Task NullabilityRewritingThrowsRatherThanSilentlyCoercing()
    {
        // The unbound type has no nullability to rewrite, so this must fail loudly rather than
        // silently binding it into a concrete nullable/required type.
        await Assert.That(() => TypeFactory.REQUIRED.ResolveTypeWithNullability(UnboundType.Instance)).ThrowsExactly<NotSupportedException>();
        await Assert.That(() => TypeFactory.NULLABLE.ResolveTypeWithNullability(UnboundType.Instance)).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task EqualityAndHashingTreatEveryInstanceAsEqual()
    {
        await Assert.That(UnboundType.Instance).IsEqualTo(UnboundType.Instance);
        await Assert.That(UnboundType.Instance.GetHashCode()).IsEqualTo(UnboundType.Instance.GetHashCode());
        await Assert.That(UnboundType.Instance.NodeEquals(UnboundType.Instance, ITypeComparison.Strict)).IsTrue();
        await Assert.That(UnboundType.Instance.Equals((object)TypeFactory.REQUIRED.I64)).IsFalse();
    }

    [Test]
    public async Task SharedComparerHashesUnboundWithoutReadingNullability()
    {
        // TypeUtils.ITypeEqualityComparer's hashing must not read Nullable for the unbound marker,
        // standalone or nested, even when the comparison mode includes nullability.
        foreach (ITypeComparison comparison in new[] { ITypeComparison.Strict, ITypeComparison.IgnoreNullability })
        {
            var comparer = TypeUtils.ITypeEqualityComparer.Of(comparison);

            var standalone = new HashSet<IType>(comparer) { UnboundType.Instance };
            await Assert.That(standalone.Contains(UnboundType.Instance)).IsTrue();

            IType nested = TypeFactory.REQUIRED.Struct([UnboundType.Instance]);
            var nestedSet = new HashSet<IType>(comparer) { nested };
            await Assert.That(nestedSet.Contains(nested)).IsTrue();
        }
    }

    [Test]
    public async Task HasNoInputNodes()
    {
        await Assert.That(UnboundType.Instance.InputNodes).IsEmpty();
    }

    [Test]
    public async Task DoesNotBindOrExecute()
    {
        // The unbound type is a placeholder only, never treated as an executable concrete type.
        await Assert.That(UnboundType.Instance.ToTypeString()).IsEqualTo("unbound");
        await Assert.That(UnboundType.Instance.ShortTypeName).IsEqualTo("unbound");
        await Assert.That(UnboundType.Instance.TypeName).IsEqualTo("unbound");
    }

    [Test]
    public async Task DefaultVisitorFallsBackForUnbound()
    {
        var visitor = new RecordingDefaultVisitor();

        UnboundType.Instance.Accept(visitor, 0);

        await Assert.That(visitor.VisitedTypes.Count).IsEqualTo(1);
        await Assert.That(visitor.VisitedTypes[0]).IsSameReferenceAs(UnboundType.Instance);
    }

    [Test]
    public async Task VisitorOverloadIsBackwardCompatible()
    {
        var overload = typeof(TypeVisitor<object, object>).GetMethod("Visit", [typeof(UnboundType), typeof(object)])!;
        await Assert.That(overload.IsVirtual).IsTrue();
        await Assert.That(overload.IsAbstract).IsFalse();
    }

    private sealed class RecordingDefaultVisitor : DefaultTypeVisitor<int, int>
    {
        public List<IType> VisitedTypes { get; } = [];

        protected override int DefaultVisit(IType type, int context)
        {
            this.VisitedTypes.Add(type);
            return context;
        }
    }
}
