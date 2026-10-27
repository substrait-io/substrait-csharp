// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Substrait.Core.Extension.Types;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using NullableType = Substrait.Core.Type.IType.NullableType;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class AliasReferenceTests
{
    private const string Urn = "extension:example:alias-reference-types";

    [Test]
    public async Task RoundTripsRequiredReference()
    {
        ParameterizedType.AliasReference type = TypeFactory.REQUIRED.AliasReference(5);
        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(type);
        await Assert.That(proto.Alias.TypeAliasReference_).IsEqualTo((uint)5);
        await Assert.That(proto.Alias.Nullability).IsEqualTo(ProtoType.Types.Nullability.Required);

        foreach (var roundTripped in new[] { proto, ProtoType.Parser.ParseFrom(proto.ToByteArray()), ProtoType.Parser.ParseJson(proto.ToString()) })
        {
            IType actual = import.From(roundTripped);
            await Assert.That(actual).IsEqualTo(type);
            await Assert.That(actual.Equals(type, ITypeComparison.Strict)).IsTrue();
            await Assert.That(actual.GetHashCode()).IsEqualTo(type.GetHashCode());
            await Assert.That(export.From(actual)).IsEqualTo(proto);
        }
    }

    [Test]
    public async Task RoundTripsNullableReference()
    {
        ParameterizedType.AliasReference type = TypeFactory.NULLABLE.AliasReference(5);
        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(type);
        await Assert.That(proto.Alias.Nullability).IsEqualTo(ProtoType.Types.Nullability.Nullable);

        IType actual = import.From(proto);
        await Assert.That(actual).IsEqualTo(type);
        await Assert.That(((ParameterizedType.AliasReference)actual).Anchor).IsEqualTo((uint)5);
    }

    [Test]
    public async Task RoundTripsAnchorZero()
    {
        // Anchor 0 is a valid plan-local anchor upstream explicitly permits, not a stand-in for
        // "unset" - it must round-trip and compare identically to any other anchor value.
        ParameterizedType.AliasReference type = TypeFactory.REQUIRED.AliasReference(0);
        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(type);
        await Assert.That(proto.Alias.TypeAliasReference_).IsEqualTo((uint)0);

        IType actual = import.From(proto);
        await Assert.That(actual).IsEqualTo(type);
        await Assert.That(((ParameterizedType.AliasReference)actual).Anchor).IsEqualTo((uint)0);
    }

    [Test]
    public async Task RoundTripsMaxUnsignedAnchor()
    {
        ParameterizedType.AliasReference type = TypeFactory.REQUIRED.AliasReference(uint.MaxValue);
        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(type);
        await Assert.That(proto.Alias.TypeAliasReference_).IsEqualTo(uint.MaxValue);

        IType actual = import.From(proto);
        await Assert.That(actual).IsEqualTo(type);
    }

    [Test]
    public async Task RoundTripsReferenceNestedInStruct()
    {
        ParameterizedType.AliasReference reference = TypeFactory.NULLABLE.AliasReference(7);
        ParameterizedType.Struct type = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, reference]);

        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();
        ProtoType proto = export.From(type);
        IType actual = import.From(proto);

        await Assert.That(actual).IsEqualTo(type);
        await Assert.That(export.From(actual)).IsEqualTo(proto);
    }

    [Test]
    public async Task RejectsUnspecifiedNullability()
    {
        ProtoType unspecified = new()
        {
            Alias = new ProtoType.Types.TypeAliasReference
            {
                TypeAliasReference_ = 3,
                Nullability = ProtoType.Types.Nullability.Unspecified,
            },
        };
        ProtoToTypeConverter converter = new();

        await Assert.That(() => converter.From(unspecified)).ThrowsExactly<NotImplementedException>();
    }

    [Test]
    public async Task RejectsNonNullTypeVariationAtConstruction()
    {
        var variation = new TypeVariationImpl(Urn, "alias", "custom", string.Empty, FunctionBehavior.INHERITS);

        ArgumentException error = await Assert.That(() =>
            new ParameterizedType.AliasReference(5, NullableType.Required, variation))
            .ThrowsExactly<ArgumentException>().And.IsNotNull();
        await Assert.That(error.ParamName).IsEqualTo("typeVariation");

        await Assert.That(() => TypeFactory.REQUIRED.AliasReference(5, variation)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ResolveTypeWithNullabilityRejectsNonNullVariationButPreservesAnchor()
    {
        var variation = new TypeVariationImpl(Urn, "alias", "custom", string.Empty, FunctionBehavior.INHERITS);
        ParameterizedType.AliasReference type = TypeFactory.REQUIRED.AliasReference(5);

        await Assert.That(() => TypeFactory.NULLABLE.ResolveTypeWithNullability(type, variation)).ThrowsExactly<ArgumentException>();

        // No-op: same nullability requested, no variation requested -> same instance.
        await Assert.That(TypeFactory.REQUIRED.ResolveTypeWithNullability(type)).IsSameReferenceAs(type);

        var nullableReference = (ParameterizedType.AliasReference)TypeFactory.NULLABLE.ResolveTypeWithNullability(type);
        await Assert.That(nullableReference.Nullable).IsEqualTo(NullableType.Nullable);
        await Assert.That(nullableReference.Anchor).IsEqualTo((uint)5);
        await Assert.That(nullableReference.TypeVariation).IsNull();
    }

    [Test]
    public async Task EqualityAndHashingDistinguishReferencesByAnchorAndNullability()
    {
        ParameterizedType.AliasReference type = TypeFactory.REQUIRED.AliasReference(5);
        ParameterizedType.AliasReference same = TypeFactory.REQUIRED.AliasReference(5);
        ParameterizedType.AliasReference differentAnchor = TypeFactory.REQUIRED.AliasReference(6);
        ParameterizedType.AliasReference differentNullability = TypeFactory.NULLABLE.AliasReference(5);

        await Assert.That(same).IsEqualTo(type);
        await Assert.That(same.GetHashCode()).IsEqualTo(type.GetHashCode());
        await Assert.That(type).IsNotEqualTo(differentAnchor);
        await Assert.That(type).IsNotEqualTo(differentNullability);
    }

    [Test]
    public async Task DefaultVisitorFallsBackForAliasReference()
    {
        var visitor = new RecordingDefaultVisitor();
        ParameterizedType.AliasReference type = TypeFactory.REQUIRED.AliasReference(5);

        type.Accept(visitor, 0);

        await Assert.That(visitor.VisitedTypes).IsEquivalentTo(new IType[] { type });
    }

    [Test]
    public async Task VisitorOverloadIsBackwardCompatible()
    {
        var overload = typeof(TypeVisitor<object, object>).GetMethod("Visit", [typeof(ParameterizedType.AliasReference), typeof(object)])!;
        await Assert.That(overload.IsVirtual).IsTrue();
        await Assert.That(overload.IsAbstract).IsFalse();
    }

    [Test]
    public async Task DoesNotFollowOrResolveTheReferencedAlias()
    {
        // A standalone AliasReference carries only the anchor and nullability: it has no children to
        // traverse, and the converter never consults an alias table to expand it into a concrete type.
        ParameterizedType.AliasReference type = TypeFactory.REQUIRED.AliasReference(5);
        await Assert.That(type.InputNodes).IsEmpty();
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
