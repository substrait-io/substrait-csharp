// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Extension;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using Substrait.Protobuf;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoRel = Substrait.Protobuf.Rel;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class ProtoToRelConverterTests
{
    private readonly ProtoToRelConverter converter = new(
        new ExtensionsDictionary.Builder().Build(),
        new ExtensionsCollection(),
        ExtensionsDictionary.StrictMode.OFF);

    [Test]
    public async Task ConvertsReadFilterProjectChain()
    {
        ProtoRel read = CreateNamedRead();
        ProtoRel filter = new()
        {
            Filter = new FilterRel
            {
                Input = read,
                Condition = new ProtoExpression { Literal = new ProtoExpression.Types.Literal { Boolean = true } },
                Common = new RelCommon(),
            },
        };
        ProtoRel project = new()
        {
            Project = new ProjectRel
            {
                Input = filter,
                Expressions = { new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I64 = 42 } } },
                Common = new RelCommon(),
            },
        };

        Project result = (Project)this.converter.ToRel(project);

        await Assert.That(result.Input).IsAssignableTo<Filter>();
        await Assert.That(((Filter)result.Input).Input).IsAssignableTo<NamedTableRead>();
        await Assert.That(result.RecordType.Fields.Count).IsEqualTo(2);
        await Assert.That(((Literal.I64Literal)result.Expressions[0]).Value).IsEqualTo(42L);
    }

    [Test]
    public async Task ConvertsScalarSubqueryThroughRelationConverter()
    {
        ProtoRel relation = new()
        {
            Project = new ProjectRel
            {
                Input = CreateNamedRead(),
                Expressions =
                {
                    new ProtoExpression
                    {
                        Subquery = new ProtoExpression.Types.Subquery
                        {
                            Scalar = new ProtoExpression.Types.Subquery.Types.Scalar { Input = CreateNamedRead() },
                        },
                    },
                },
                Common = new RelCommon { Emit = new RelCommon.Types.Emit { OutputMapping = { 1 } } },
            },
        };

        Project result = (Project)this.converter.ToRel(relation);

        await Assert.That(result.Expressions[0]).IsAssignableTo<Substrait.Core.Expression.Expression.ScalarSubquery>();
    }

    [Test]
    public async Task ConvertsSetComparisonSubqueryThroughRelationConverter()
    {
        ProtoRel relation = new()
        {
            Project = new ProjectRel
            {
                Input = CreateNamedRead(),
                Expressions =
                {
                    new ProtoExpression
                    {
                        Subquery = new ProtoExpression.Types.Subquery
                        {
                            SetComparison = new ProtoExpression.Types.Subquery.Types.SetComparison
                            {
                                Left = new ProtoExpression
                                {
                                    Literal = new ProtoExpression.Types.Literal { I64 = 2 },
                                },
                                ComparisonOp = ProtoExpression.Types.Subquery.Types.SetComparison.Types.ComparisonOp.Lt,
                                ReductionOp = ProtoExpression.Types.Subquery.Types.SetComparison.Types.ReductionOp.All,
                                Right = CreateNamedRead(),
                            },
                        },
                    },
                },
                Common = new RelCommon { Emit = new RelCommon.Types.Emit { OutputMapping = { 1 } } },
            },
        };

        Project result = (Project)this.converter.ToRel(relation);
        var comparison = (Substrait.Core.Expression.Expression.SetComparisonSubquery)result.Expressions[0];

        await Assert.That(comparison.Expression).IsEqualTo(new Literal.I64Literal(2));
        await Assert.That(comparison.Comparison).IsEqualTo(Substrait.Core.Expression.Expression.SetComparisonSubquery.ComparisonOp.LessThan);
        await Assert.That(comparison.Reduction).IsEqualTo(Substrait.Core.Expression.Expression.SetComparisonSubquery.ReductionOp.All);
        await Assert.That(comparison.Subquery).IsAssignableTo<NamedTableRead>();
    }

    [Test]
    [Arguments(3L, 0L)]
    [Arguments(-1L, 3L)]
    [Arguments(100L, 8L)]
    public async Task ConvertsFetch(long count, long offset)
    {
        ProtoRel relation = new()
        {
            Fetch = new FetchRel
            {
                Input = CreateNamedRead(),
                CountExpr = new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I64 = count } },
                OffsetExpr = new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I64 = offset } },
                Common = new RelCommon(),
            },
        };

        Fetch result = (Fetch)this.converter.ToRel(relation);

        await Assert.That(result.Count).IsEqualTo(new Literal.I64Literal(count));
        await Assert.That(result.Offset).IsEqualTo(new Literal.I64Literal(offset));
        await Assert.That(result.RecordType.Fields.Count).IsEqualTo(1);
        await Assert.That(result.RecordType.Fields[0]).IsEqualTo(TypeFactory.REQUIRED.I64);
    }

    [Test]
    public async Task ConvertsVirtualTableReadRows()
    {
        var nullableI32 = new ProtoType.Types.I32 { Nullability = ProtoType.Types.Nullability.Nullable };
        ProtoRel relation = new()
        {
            Read = new ReadRel
            {
                BaseSchema = new Substrait.Protobuf.NamedStruct
                {
                    Names = { "value" },
                    Struct = new ProtoType.Types.Struct
                    {
                        Types_ = { new ProtoType { I32 = nullableI32 } },
                        Nullability = ProtoType.Types.Nullability.Required,
                    },
                },
                VirtualTable = new ReadRel.Types.VirtualTable
                {
                    Expressions =
                    {
                        new ProtoExpression.Types.Nested.Types.Struct
                        {
                            Fields = { new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I32 = 10 } } },
                        },
                        new ProtoExpression.Types.Nested.Types.Struct
                        {
                            Fields =
                            {
                                new ProtoExpression
                                {
                                    Literal = new ProtoExpression.Types.Literal
                                    {
                                        Null = new ProtoType { I32 = nullableI32 },
                                        Nullable = true,
                                    },
                                },
                            },
                        },
                    },
                },
                Common = new RelCommon(),
            },
        };

        VirtualTableRead result = (VirtualTableRead)this.converter.ToRel(relation);

        await Assert.That(result.InitialSchema.Names[0]).IsEqualTo("value");
        await Assert.That(result.RecordType.Fields[0]).IsEqualTo(TypeFactory.NULLABLE.I32);
        await Assert.That(result.Rows.Count).IsEqualTo(2);
        await Assert.That(result.Rows[0].Fields[0]).IsEqualTo(new Literal.I32Literal(10));
        await Assert.That(result.Rows[1].Fields[0]).IsAssignableTo<Literal.NullLiteral>();
        await Assert.That(result.Rows[1].Fields[0].Type).IsEqualTo(TypeFactory.NULLABLE.I32);
    }

    [Test]
    [Arguments(SetRel.Types.SetOp.MinusPrimary, Set.SetOp.MinusPrimary, ProtoType.Types.Nullability.Nullable, ProtoType.Types.Nullability.Required, IType.NullableType.Nullable)]
    [Arguments(SetRel.Types.SetOp.IntersectionMultiset, Set.SetOp.IntersectionMultiset, ProtoType.Types.Nullability.Nullable, ProtoType.Types.Nullability.Required, IType.NullableType.Required)]
    [Arguments(SetRel.Types.SetOp.UnionAll, Set.SetOp.UnionAll, ProtoType.Types.Nullability.Required, ProtoType.Types.Nullability.Nullable, IType.NullableType.Nullable)]
    public async Task ConvertsSetOperations(
        SetRel.Types.SetOp protoOperation,
        Set.SetOp expectedOperation,
        ProtoType.Types.Nullability leftNullability,
        ProtoType.Types.Nullability rightNullability,
        IType.NullableType expectedNullability)
    {
        ProtoRel relation = new()
        {
            Set = new SetRel
            {
                Op = protoOperation,
                Inputs = { CreateNamedRead(leftNullability), CreateNamedRead(rightNullability) },
                Common = new RelCommon(),
            },
        };

        Set result = (Set)this.converter.ToRel(relation);

        await Assert.That(result.SetOperation).IsEqualTo(expectedOperation);
        await Assert.That(result.Inputs.Count).IsEqualTo(2);
        await Assert.That(result.RecordType.Fields[0].Nullable).IsEqualTo(expectedNullability);
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 1)]
    public async Task ConvertsScalarAndEmptyVectorAggregates(bool includeEmptyGrouping, int expectedGroupingCount)
    {
        AggregateRel aggregate = new()
        {
            Input = CreateNamedRead(),
            Measures = { CreateAggregateMeasure() },
            Common = new RelCommon(),
        };
        if (includeEmptyGrouping)
        {
            aggregate.Groupings.Add(new AggregateRel.Types.Grouping());
        }

        Aggregate result = (Aggregate)CreateAggregateConverter().ToRel(new ProtoRel { Aggregate = aggregate });

        await Assert.That(result.Groupings.Count).IsEqualTo(expectedGroupingCount);
        await Assert.That(result.Groupings.All(grouping => grouping.Expressions.Count == 0)).IsTrue();
        await Assert.That(result.GroupingExpressions.Count).IsEqualTo(0);
        await Assert.That(result.Measures.Count).IsEqualTo(1);
        await Assert.That(result.RecordType.Fields[0]).IsEqualTo(TypeFactory.REQUIRED.BOOL);
    }

    [Test]
    [Arguments(JoinRel.Types.JoinType.Inner, AbstractJoin.JoinType.Inner, "RR")]
    [Arguments(JoinRel.Types.JoinType.Outer, AbstractJoin.JoinType.Outer, "NN")]
    [Arguments(JoinRel.Types.JoinType.Left, AbstractJoin.JoinType.Left, "RN")]
    [Arguments(JoinRel.Types.JoinType.Right, AbstractJoin.JoinType.Right, "NR")]
    [Arguments(JoinRel.Types.JoinType.LeftSemi, AbstractJoin.JoinType.LeftSemi, "R")]
    [Arguments(JoinRel.Types.JoinType.LeftAnti, AbstractJoin.JoinType.LeftAnti, "R")]
    [Arguments(JoinRel.Types.JoinType.LeftSingle, AbstractJoin.JoinType.LeftSingle, "RN")]
    [Arguments(JoinRel.Types.JoinType.LeftMark, AbstractJoin.JoinType.LeftMark, "RN")]
    [Arguments(JoinRel.Types.JoinType.RightSemi, AbstractJoin.JoinType.RightSemi, "R")]
    [Arguments(JoinRel.Types.JoinType.RightAnti, AbstractJoin.JoinType.RightAnti, "R")]
    [Arguments(JoinRel.Types.JoinType.RightSingle, AbstractJoin.JoinType.RightSingle, "NR")]
    [Arguments(JoinRel.Types.JoinType.RightMark, AbstractJoin.JoinType.RightMark, "RN")]
    public async Task ConvertsLogicalJoinOutputShape(
        JoinRel.Types.JoinType protoType,
        AbstractJoin.JoinType expectedType,
        string expectedNullability)
    {
        ProtoRel relation = new()
        {
            Join = new JoinRel
            {
                Left = CreateNamedRead(),
                Right = CreateNamedRead(),
                Type = protoType,
                Common = new RelCommon(),
            },
        };

        Join result = (Join)this.converter.ToRel(relation);

        await Assert.That(result.Type).IsEqualTo(expectedType);
        await Assert.That(result.RecordType.Fields.Count).IsEqualTo(expectedNullability.Length);
        for (int index = 0; index < expectedNullability.Length; index++)
        {
            IType.NullableType expected = expectedNullability[index] == 'N'
                ? IType.NullableType.Nullable
                : IType.NullableType.Required;
            await Assert.That(result.RecordType.Fields[index].Nullable).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task RejectsUnspecifiedLogicalJoin()
    {
        ProtoRel relation = new()
        {
            Join = new JoinRel
            {
                Left = CreateNamedRead(),
                Right = CreateNamedRead(),
                Type = JoinRel.Types.JoinType.Unspecified,
                Common = new RelCommon(),
            },
        };

        await Assert.That(() => this.converter.ToRel(relation)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments(HashJoinRel.Types.JoinType.Inner, AbstractJoin.JoinType.Inner)]
    [Arguments(HashJoinRel.Types.JoinType.Outer, AbstractJoin.JoinType.Outer)]
    [Arguments(HashJoinRel.Types.JoinType.Left, AbstractJoin.JoinType.Left)]
    [Arguments(HashJoinRel.Types.JoinType.Right, AbstractJoin.JoinType.Right)]
    [Arguments(HashJoinRel.Types.JoinType.LeftSemi, AbstractJoin.JoinType.LeftSemi)]
    [Arguments(HashJoinRel.Types.JoinType.RightSemi, AbstractJoin.JoinType.RightSemi)]
    [Arguments(HashJoinRel.Types.JoinType.LeftAnti, AbstractJoin.JoinType.LeftAnti)]
    [Arguments(HashJoinRel.Types.JoinType.RightAnti, AbstractJoin.JoinType.RightAnti)]
    [Arguments(HashJoinRel.Types.JoinType.LeftSingle, AbstractJoin.JoinType.LeftSingle)]
    [Arguments(HashJoinRel.Types.JoinType.RightSingle, AbstractJoin.JoinType.RightSingle)]
    [Arguments(HashJoinRel.Types.JoinType.LeftMark, AbstractJoin.JoinType.LeftMark)]
    [Arguments(HashJoinRel.Types.JoinType.RightMark, AbstractJoin.JoinType.RightMark)]
    public async Task ConvertsHashJoinTypes(HashJoinRel.Types.JoinType protoType, AbstractJoin.JoinType expectedType)
    {
        HashJoin result = (HashJoin)this.converter.ToRel(CreateHashJoin(protoType));

        await Assert.That(result.Type).IsEqualTo(expectedType);
    }

    [Test]
    public async Task RejectsUnspecifiedHashJoinType()
    {
        await Assert.That(() => this.converter.ToRel(CreateHashJoin(HashJoinRel.Types.JoinType.Unspecified))).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments(HashJoinRel.Types.BuildInput.Unspecified, false)]
    [Arguments(HashJoinRel.Types.BuildInput.Left, true)]
    [Arguments(HashJoinRel.Types.BuildInput.Right, false)]
    public async Task ConvertsHashJoinBuildInput(HashJoinRel.Types.BuildInput buildInput, bool expectedBuildLeft)
    {
        HashJoin result = (HashJoin)this.converter.ToRel(CreateHashJoin(buildInput: buildInput));

        await Assert.That(result.BuildLeft).IsEqualTo(expectedBuildLeft);
        await Assert.That(result.Build).IsSameReferenceAs(expectedBuildLeft ? result.Left : result.Right);
        await Assert.That(result.Probe).IsSameReferenceAs(expectedBuildLeft ? result.Right : result.Left);
    }

    [Test]
    [Arguments(ComparisonJoinKey.Types.SimpleComparisonType.Eq, PhysicalJoin.ComparisonJoinKey.SimpleComparisonType.Eq)]
    [Arguments(ComparisonJoinKey.Types.SimpleComparisonType.IsNotDistinctFrom, PhysicalJoin.ComparisonJoinKey.SimpleComparisonType.IsNotDistinctFrom)]
    [Arguments(ComparisonJoinKey.Types.SimpleComparisonType.MightEqual, PhysicalJoin.ComparisonJoinKey.SimpleComparisonType.MightEqual)]
    public async Task ConvertsHashJoinSimpleComparisons(
        ComparisonJoinKey.Types.SimpleComparisonType protoComparison,
        PhysicalJoin.ComparisonJoinKey.SimpleComparisonType expectedComparison)
    {
        HashJoin result = (HashJoin)this.converter.ToRel(CreateHashJoin(comparison: protoComparison));

        await Assert.That(result.Keys[0].Comparison.Simple).IsEqualTo(expectedComparison);
    }

    [Test]
    public async Task RejectsUnspecifiedHashJoinSimpleComparison()
    {
        await Assert.That(() => this.converter.ToRel(
            CreateHashJoin(comparison: ComparisonJoinKey.Types.SimpleComparisonType.Unspecified))).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ConvertsAggregateJoinHashJoinAndExchange()
    {
        ProtoRel aggregate = new()
        {
            Aggregate = new AggregateRel
            {
                Input = CreateNamedRead(),
                GroupingExpressions = { new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I64 = 1 } } },
                Groupings = { new AggregateRel.Types.Grouping { ExpressionReferences = { 0 } } },
                Common = new RelCommon(),
            },
        };
        ProtoRel join = new()
        {
            Join = new JoinRel
            {
                Left = aggregate,
                Right = CreateNamedRead(),
                Type = JoinRel.Types.JoinType.Inner,
                Expression = new ProtoExpression { Literal = new ProtoExpression.Types.Literal { Boolean = true } },
                Common = new RelCommon(),
            },
        };
        ProtoRel hashJoin = new()
        {
            HashJoin = new HashJoinRel
            {
                Left = join,
                Right = CreateNamedRead(),
                Type = HashJoinRel.Types.JoinType.Left,
                BuildInput = HashJoinRel.Types.BuildInput.Left,
                Keys =
                {
                    new ComparisonJoinKey
                    {
                        Left = CreateFieldReference(0),
                        Right = CreateFieldReference(1),
                        Comparison = new ComparisonJoinKey.Types.ComparisonType
                        {
                            Simple = ComparisonJoinKey.Types.SimpleComparisonType.Eq,
                        },
                    },
                },
                Common = new RelCommon(),
            },
        };
        ProtoRel exchange = new()
        {
            Exchange = new ExchangeRel
            {
                Input = hashJoin,
                PartitionCount = 4,
                SingleTarget = new ExchangeRel.Types.SingleBucketExpression
                {
                    Expression = new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I64 = 7 } },
                },
                Common = new RelCommon(),
            },
        };

        SingleBucketExchange result = (SingleBucketExchange)this.converter.ToRel(exchange);

        await Assert.That(result.PartitionCount).IsEqualTo(4);
        HashJoin convertedHashJoin = (HashJoin)result.Input;
        await Assert.That(convertedHashJoin.BuildLeft).IsTrue();
        await Assert.That(convertedHashJoin.Keys.Count).IsEqualTo(1);
        Join convertedJoin = (Join)convertedHashJoin.Left;
        await Assert.That(convertedJoin.Left).IsAssignableTo<Aggregate>();

        ProtoRel serialized = new RelToProtoConverter().From(result);
        SingleBucketExchange roundTripped = (SingleBucketExchange)this.converter.ToRel(serialized);
        HashJoin roundTrippedHashJoin = (HashJoin)roundTripped.Input;

        await Assert.That(roundTripped).IsEqualTo(result);
        await Assert.That(roundTrippedHashJoin.BuildLeft).IsTrue();
    }

    [Test]
    public async Task RejectsAggregateWithUnusedGroupingExpression()
    {
        ProtoRel relation = new()
        {
            Aggregate = new AggregateRel
            {
                Input = CreateNamedRead(),
                GroupingExpressions =
                {
                    new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I64 = 1 } },
                    new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I64 = 2 } },
                },
                Groupings = { new AggregateRel.Types.Grouping { ExpressionReferences = { 0, 0 } } },
                Common = new RelCommon(),
            },
        };

        await Assert.That(() => this.converter.ToRel(relation)).ThrowsExactly<System.Runtime.Serialization.SerializationException>();
    }

    [Test]
    public async Task RoundTripsFoundationalRelationChain()
    {
        ProtoRel relation = new()
        {
            Project = new ProjectRel
            {
                Input = new ProtoRel
                {
                    Filter = new FilterRel
                    {
                        Input = CreateNamedRead(),
                        Condition = new ProtoExpression { Literal = new ProtoExpression.Types.Literal { Boolean = true } },
                        Common = new RelCommon(),
                    },
                },
                Expressions = { new ProtoExpression { Literal = new ProtoExpression.Types.Literal { I64 = 42 } } },
                Common = new RelCommon(),
            },
        };
        IRel original = this.converter.ToRel(relation);

        ProtoRel serialized = new RelToProtoConverter().From(original);
        IRel roundTripped = this.converter.ToRel(serialized);

        await Assert.That(roundTripped).IsEqualTo(original);
    }

    [Test]
    public async Task RoundTripsScalarSubquery()
    {
        ProtoRel relation = new()
        {
            Project = new ProjectRel
            {
                Input = CreateNamedRead(),
                Expressions =
                {
                    new ProtoExpression
                    {
                        Subquery = new ProtoExpression.Types.Subquery
                        {
                            Scalar = new ProtoExpression.Types.Subquery.Types.Scalar { Input = CreateNamedRead() },
                        },
                    },
                },
                Common = new RelCommon { Emit = new RelCommon.Types.Emit { OutputMapping = { 1 } } },
            },
        };
        IRel original = this.converter.ToRel(relation);

        ProtoRel serialized = new RelToProtoConverter().From(original);
        IRel roundTripped = this.converter.ToRel(serialized);

        await Assert.That(roundTripped).IsEqualTo(original);
    }

    private static ProtoExpression.Types.FieldReference CreateFieldReference(int field)
    {
        return new ProtoExpression.Types.FieldReference
        {
            DirectReference = new ProtoExpression.Types.ReferenceSegment
            {
                StructField = new ProtoExpression.Types.ReferenceSegment.Types.StructField { Field = field },
            },
            RootReference = new ProtoExpression.Types.FieldReference.Types.RootReference(),
        };
    }

    private static ProtoRel CreateHashJoin(
        HashJoinRel.Types.JoinType type = HashJoinRel.Types.JoinType.Inner,
        HashJoinRel.Types.BuildInput buildInput = HashJoinRel.Types.BuildInput.Right,
        ComparisonJoinKey.Types.SimpleComparisonType comparison = ComparisonJoinKey.Types.SimpleComparisonType.Eq)
    {
        return new ProtoRel
        {
            HashJoin = new HashJoinRel
            {
                Left = CreateNamedRead(),
                Right = CreateNamedRead(),
                Type = type,
                BuildInput = buildInput,
                Keys =
                {
                    new ComparisonJoinKey
                    {
                        Left = CreateFieldReference(0),
                        Right = CreateFieldReference(1),
                        Comparison = new ComparisonJoinKey.Types.ComparisonType { Simple = comparison },
                    },
                },
                Common = new RelCommon(),
            },
        };
    }

    private static AggregateRel.Types.Measure CreateAggregateMeasure()
    {
        return new AggregateRel.Types.Measure
        {
            Measure_ = new AggregateFunction
            {
                FunctionReference = 1,
                OutputType = new ProtoType
                {
                    Bool = new ProtoType.Types.Boolean { Nullability = ProtoType.Types.Nullability.Required },
                },
                Phase = AggregationPhase.InitialToResult,
                Invocation = AggregateFunction.Types.AggregationInvocation.All,
            },
        };
    }

    private static ProtoToRelConverter CreateAggregateConverter()
    {
        Substrait.Protobuf.Plan plan = new()
        {
            ExtensionUrns =
            {
                new SimpleExtensionURN { ExtensionUrnAnchor = 1, Urn = "extension:example:synthetic_aggregate" },
            },
            Extensions =
            {
                new SimpleExtensionDeclaration
                {
                    ExtensionFunction = new SimpleExtensionDeclaration.Types.ExtensionFunction
                    {
                        ExtensionUrnReference = 1,
                        FunctionAnchor = 1,
                        Name = "synthetic_aggregate",
                    },
                },
            },
        };
        return new ProtoToRelConverter(
            new ExtensionsDictionary.Builder(plan).Build(),
            new ExtensionsCollection(),
            ExtensionsDictionary.StrictMode.OFF);
    }

    private static ProtoRel CreateNamedRead()
    {
        return CreateNamedRead(ProtoType.Types.Nullability.Required);
    }

    private static ProtoRel CreateNamedRead(ProtoType.Types.Nullability nullability)
    {
        return new ProtoRel
        {
            Read = new ReadRel
            {
                BaseSchema = new Substrait.Protobuf.NamedStruct
                {
                    Names = { "value" },
                    Struct = new ProtoType.Types.Struct
                    {
                        Types_ =
                        {
                            new ProtoType
                            {
                                I64 = new ProtoType.Types.I64 { Nullability = nullability },
                            },
                        },
                        Nullability = ProtoType.Types.Nullability.Required,
                    },
                },
                NamedTable = new ReadRel.Types.NamedTable { Names = { "orders" } },
                Common = new RelCommon(),
            },
        };
    }
}
