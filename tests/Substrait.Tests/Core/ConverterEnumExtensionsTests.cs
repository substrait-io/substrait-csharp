// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using static Substrait.Core.Expression.AggregateFunctionInvocation;
using static Substrait.Core.Relation.AbstractJoin;
using static Substrait.Core.Relation.Set;
using ExpressionEnums = Substrait.Core.Expression.Converters.EnumExtensions;
using ProtoAggregationPhase = Substrait.Protobuf.AggregationPhase;
using ProtoHashJoinType = Substrait.Protobuf.HashJoinRel.Types.JoinType;
using ProtoJoinType = Substrait.Protobuf.JoinRel.Types.JoinType;
using ProtoSetOp = Substrait.Protobuf.SetRel.Types.SetOp;
using RelationEnums = Substrait.Core.Relation.Converters.EnumExtensions;

namespace Substrait.Tests.Core;

public sealed class ConverterEnumExtensionsTests
{
    [Test]
    public async Task ExpressionEnumsRoundTripByValue()
    {
        ProtoAggregationPhase proto = ExpressionEnums.ToProto(AggregationPhase.IntermediateToResult);

        await Assert.That(proto).IsEqualTo(ProtoAggregationPhase.IntermediateToResult);
        await Assert.That(ExpressionEnums.FromProto(proto)).IsEqualTo(AggregationPhase.IntermediateToResult);
    }

    [Test]
    public async Task RelationEnumsRoundTripByValueAndName()
    {
        ProtoJoinType joinProto = RelationEnums.ToProto(JoinType.Left);
        ProtoHashJoinType hashJoinProto = RelationEnums.ToHashJoinProto(JoinType.Left);
        ProtoSetOp setProto = RelationEnums.ToProto(SetOp.UnionDistinct);

        await Assert.That(RelationEnums.FromProto(joinProto)).IsEqualTo(JoinType.Left);
        await Assert.That(RelationEnums.FromProto(hashJoinProto)).IsEqualTo(JoinType.Left);
        await Assert.That(RelationEnums.FromProto(setProto)).IsEqualTo(SetOp.UnionDistinct);
    }

    [Test]
    public async Task RelationEnumsRejectUnspecifiedValues()
    {
        await Assert.That(() => RelationEnums.FromProto(ProtoJoinType.Unspecified)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => RelationEnums.FromProto(ProtoHashJoinType.Unspecified)).ThrowsExactly<ArgumentException>();
        await Assert.That(() => RelationEnums.FromProto(ProtoSetOp.Unspecified)).ThrowsExactly<ArgumentException>();
    }
}
