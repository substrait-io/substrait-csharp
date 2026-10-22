// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Core.Expression;
using Substrait.Core.Expression.Converters;
using Substrait.Core.Extension;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Protobuf;
using CoreExpression = Substrait.Core.Expression.Expression;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoRel = Substrait.Protobuf.Rel;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class ProtoConversionTraversalTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeepMixedSubqueriesConvertWithoutClrRecursion(bool expressionRoot)
    {
        const int depth = 10000;
        ProtoRel input = Read();
        for (int index = 0; index < depth; ++index)
        {
            input = ScalarProject(Cast(Scalar(input)));
        }

        IRel current = expressionRoot
            ? ((CoreExpression.ScalarSubquery)Expressions().From(Scalar(input))).Subquery
            : Relations().ToRel(input);
        await Assert.That(current.RecordType.Fields[0]).IsEqualTo(TypeFactory.REQUIRED.I64);
        for (int index = 0; index < depth; ++index)
        {
            var project = (Project)current;
            current = ((CoreExpression.ScalarSubquery)((CoreExpression.Cast)project.Expressions[0]).Input).Subquery;
        }

        await Assert.That(current).IsAssignableTo<NamedTableRead>();
    }

    [Test]
    public async Task DeepExpressionAndRelationChainsShareTheTraversal()
    {
        const int depth = 10000;
        ProtoRel input = Read();
        for (int index = 0; index < depth; ++index)
        {
            input = new() { Filter = new() { Input = input, Condition = Boolean() } };
        }

        ProtoExpression expression = Scalar(input);
        for (int index = 0; index < depth; ++index)
        {
            expression = Cast(expression);
        }

        IExpression converted = ((Project)Relations().ToRel(ScalarProject(expression))).Expressions[0];
        for (int index = 0; index < depth; ++index)
        {
            converted = ((CoreExpression.Cast)converted).Input;
        }

        IRel relation = ((CoreExpression.ScalarSubquery)converted).Subquery;
        for (int index = 0; index < depth; ++index)
        {
            relation = ((Filter)relation).Input;
        }

        await Assert.That(relation).IsAssignableTo<NamedTableRead>();
    }

    [Test]
    public async Task WideSiblingsKeepRelationAndExpressionResultOffsets()
    {
        const int width = 256;
        ProtoRel input = new() { Set = new() { Op = SetRel.Types.SetOp.UnionAll } };
        for (int index = 0; index < width; ++index)
        {
            input.Set.Inputs.Add(ScalarProject(Scalar(ScalarProject(Integer(index)))));
        }

        var result = (Set)Relations().ToRel(input);
        await Assert.That(result.Inputs.Count).IsEqualTo(width);
        for (int index = 0; index < width; ++index)
        {
            var subquery = (CoreExpression.ScalarSubquery)((Project)result.Inputs[index]).Expressions[0];
            await Assert.That(((Literal.I64Literal)((Project)subquery.Subquery).Expressions[0]).Value).IsEqualTo((long)index);
        }

        ProtoRel project = new() { Project = new() { Input = Read() } };
        for (int index = 0; index < width; ++index)
        {
            project.Project.Expressions.Add(Cast(Scalar(ScalarProject(Integer(index)))));
        }

        var expressions = (Project)Relations().ToRel(project);
        await Assert.That(expressions.Expressions.Count).IsEqualTo(width);
        for (int index = 0; index < width; ++index)
        {
            var subquery = (CoreExpression.ScalarSubquery)((CoreExpression.Cast)expressions.Expressions[index]).Input;
            await Assert.That(((Literal.I64Literal)((Project)subquery.Subquery).Expressions[0]).Value).IsEqualTo((long)index);
        }
    }

    [Test]
    public async Task ReusedWireNodesAreConvertedInTheirOwnSchemaContext()
    {
        ProtoExpression shared = Field();
        ProtoRel input = new()
        {
            Cross = new()
            {
                Left = ScalarProject(shared),
                Right = ScalarProject(shared, Read(StringType())),
            },
        };
        var result = (Cross)Relations().ToRel(input);
        var left = (Project)result.Left;
        var right = (Project)result.Right;
        await Assert.That(left.Expressions[0].Type).IsEqualTo(left.Input.RecordType.Fields[0]);
        await Assert.That(right.Expressions[0].Type).IsEqualTo(right.Input.RecordType.Fields[0]);
        await Assert.That(left.Expressions[0].Type).IsNotEqualTo(right.Expressions[0].Type);
        await Assert.That(left.Expressions[0]).IsNotSameReferenceAs(right.Expressions[0]);
    }

    [Test]
    [Arguments("in")]
    [Arguments("comparison")]
    public async Task SubqueryOperandsUseCallerSchemasAndInnerRelationsUseNestedScopes(string kind)
    {
        ProtoRel inner = ScalarProject(Field(outerLevels: 1), Read(StringType()));
        ProtoExpression expression = kind == "in"
            ? new()
            {
                Subquery = new()
                {
                    InPredicate = new() { Haystack = inner, Needles = { Field(), Scalar(ScalarProject(Integer(9))) } },
                },
            }
            : new()
            {
                Subquery = new()
                {
                    SetComparison = new()
                    {
                        Left = Field(),
                        Right = inner,
                        ComparisonOp = ProtoExpression.Types.Subquery.Types.SetComparison.Types.ComparisonOp.Eq,
                        ReductionOp = ProtoExpression.Types.Subquery.Types.SetComparison.Types.ReductionOp.Any,
                    },
                },
            };
        IExpression result = ((Project)Relations().ToRel(ScalarProject(expression))).Expressions[0];
        IExpression operand = result is CoreExpression.InPredicateSubquery predicate
            ? predicate.Values[0] : ((CoreExpression.SetComparisonSubquery)result).Expression;
        IRel relation = result is CoreExpression.InPredicateSubquery inPredicate
            ? inPredicate.Subquery : ((CoreExpression.SetComparisonSubquery)result).Subquery;
        await Assert.That(operand.Type).IsEqualTo(TypeFactory.REQUIRED.I64);
        var reference = (FieldReference)((Project)relation).Expressions[0];
        await Assert.That(reference.SubqueryLevels).IsEqualTo(1);
        await Assert.That(reference.Type).IsEqualTo(TypeFactory.REQUIRED.I64);
    }

    [Test]
    public async Task NestedCorrelationAndIfThenOrderArePreserved()
    {
        ProtoRel inner = ScalarProject(Field(outerLevels: 2), Read(BoolType()));
        ProtoRel middle = ScalarProject(Scalar(inner), Read(StringType()));
        ProtoExpression choice = new()
        {
            IfThen = new()
            {
                Ifs =
                {
                    new ProtoExpression.Types.IfThen.Types.IfClause { If = Boolean(), Then = Scalar(middle) },
                    new ProtoExpression.Types.IfThen.Types.IfClause { If = Boolean(false), Then = Integer(22) },
                },
                Else = Integer(33),
            },
        };
        var conditional = (CoreExpression.IfThen)((Project)Relations().ToRel(ScalarProject(choice))).Expressions[0];
        var first = (CoreExpression.ScalarSubquery)conditional.IfClauses[0].Then;
        var second = (CoreExpression.ScalarSubquery)((Project)first.Subquery).Expressions[0];
        var reference = (FieldReference)((Project)second.Subquery).Expressions[0];
        await Assert.That(reference.SubqueryLevels).IsEqualTo(2);
        await Assert.That(reference.Type).IsEqualTo(TypeFactory.REQUIRED.I64);
        await Assert.That(((Literal.BoolLiteral)conditional.IfClauses[0].Condition).Value).IsTrue();
        await Assert.That(((Literal.BoolLiteral)conditional.IfClauses[1].Condition).Value).IsFalse();
        await Assert.That(((Literal.I64Literal)conditional.IfClauses[1].Then).Value).IsEqualTo(22L);
        await Assert.That(((Literal.I64Literal)conditional.ElseClause).Value).IsEqualTo(33L);
    }

    [Test]
    public async Task VirtualRowsAndReadFilterDoNotConsumeEachOthersResults()
    {
        ProtoRel wire = Read();
        wire.Read.BaseSchema.Names.Add("second");
        wire.Read.BaseSchema.Struct.Types_.Add(I64());
        wire.Read.Filter = Boolean();
        wire.Read.VirtualTable = new()
        {
            Expressions =
            {
                new ProtoExpression.Types.Nested.Types.Struct { Fields = { Integer(11), Scalar(ScalarProject(Integer(12))) } },
                new ProtoExpression.Types.Nested.Types.Struct { Fields = { Cast(Integer(21)), Integer(22) } },
            },
        };
        var result = (VirtualTableRead)Relations().ToRel(wire);
        await Assert.That(((Literal.BoolLiteral)result.Filter!).Value).IsTrue();
        await Assert.That(((Literal.I64Literal)result.Rows[0].Fields[0]).Value).IsEqualTo(11L);
        var subquery = (CoreExpression.ScalarSubquery)result.Rows[0].Fields[1];
        await Assert.That(((Literal.I64Literal)((Project)subquery.Subquery).Expressions[0]).Value).IsEqualTo(12L);
        await Assert.That(((Literal.I64Literal)((CoreExpression.Cast)result.Rows[1].Fields[0]).Input).Value).IsEqualTo(21L);
        await Assert.That(((Literal.I64Literal)result.Rows[1].Fields[1]).Value).IsEqualTo(22L);

        wire.Read.VirtualTable.Expressions[0].Fields[0] = Field(outerLevels: 1);
        await Assert.That(() => Relations().ToRel(wire, [TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])]))
            .ThrowsExactly<SerializationException>();
    }

    [Test]
    public async Task AggregateGroupingValuesTypeArgumentsAndFiltersKeepTheirOrder()
    {
        Plan plan = new()
        {
            ExtensionUrns = { new SimpleExtensionURN { ExtensionUrnAnchor = 1, Urn = "extension:example:aggregate" } },
            Extensions =
            {
                new SimpleExtensionDeclaration
                {
                    ExtensionFunction = new() { FunctionAnchor = 0, ExtensionUrnReference = 1, Name = "test:i64" },
                },
            },
        };
        ProtoRel wire = new()
        {
            Aggregate = new()
            {
                Input = Read(),
                GroupingExpressions = { Integer(10) },
                Groupings = { new AggregateRel.Types.Grouping { ExpressionReferences = { 0 } } },
                Measures = { Measure(11, true), Measure(21, false) },
            },
        };
        var converter = new ProtoToRelConverter(new ExtensionsDictionary.Builder(plan).Build(), new ExtensionsCollection(), ExtensionsDictionary.StrictMode.OFF);
        var result = (Aggregate)converter.ToRel(wire);
        await Assert.That(((Literal.I64Literal)result.GroupingExpressions[0]).Value).IsEqualTo(10L);
        for (int index = 0; index < 2; ++index)
        {
            Aggregate.Measure measure = result.Measures[index];
            await Assert.That(((Literal.I64Literal)measure.Function.Arguments[0]).Value).IsEqualTo(11L + (index * 10));
            await Assert.That(measure.Function.Arguments[1]).IsEqualTo((IFunctionArg)TypeFactory.REQUIRED.I64);
            var subquery = (CoreExpression.ScalarSubquery)measure.Function.Arguments[2];
            await Assert.That(((Literal.I64Literal)((Project)subquery.Subquery).Expressions[0]).Value).IsEqualTo(12L + (index * 10));
            await Assert.That(((Literal.BoolLiteral)measure.PreMeasureFilter!).Value).IsEqualTo(index == 0);
        }
    }

    [Test]
    public async Task StandaloneExpressionSubqueriesPreserveSchemaErrorsAndConvertersRemainReusable()
    {
        ProtoRel invalid = ScalarProject(Integer(1), new ProtoRel { ExtensionLeaf = new() });
        ProtoToExpressionConverter expressions = Expressions();
        await Assert.That(() => expressions.From(Scalar(invalid))).ThrowsExactly<SerializationException>();
        await Assert.That(() => expressions.From(new ProtoExpression { Subquery = new() })).ThrowsExactly<NotImplementedException>();
        await Assert.That(((Literal.I64Literal)expressions.From(Integer(7))).Value).IsEqualTo(7L);
        await Assert.That(expressions.From(Scalar(Read()))).IsAssignableTo<CoreExpression.ScalarSubquery>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmptyExpressionListsDoNotForceUnresolvedInputSchemas(bool sort)
    {
        ProtoRel unresolved = new() { ExtensionLeaf = new() };
        ProtoRel wire = sort
            ? new() { Sort = new() { Input = unresolved } }
            : new() { Project = new() { Input = unresolved } };
        IRel result = Relations().ToRel(wire);
        await Assert.That(result.Inputs[0]).IsAssignableTo<ExtensionLeaf>();
    }

    [Test]
    public async Task ShallowPlansKeepBinaryAndJsonSerializationUnchanged()
    {
        Plan wire = new()
        {
            Version = new() { MinorNumber = 107, Producer = "traversal-tests" },
            Relations =
            {
                new PlanRel
                {
                    Root = new()
                    {
                        Input = ScalarProject(Cast(Scalar(ScalarProject(Integer(7))))),
                        Names = { "value" },
                    },
                },
            },
        };
        byte[] original = wire.ToByteArray();
        var reader = new ProtoToPlanConverter(new ExtensionsCollection());
        var imported = reader.From(wire);
        Plan exported = new PlanToProtoConverter().From(imported);
        await Assert.That(exported.ToByteArray().SequenceEqual(original)).IsTrue();
        await Assert.That(reader.FromBytes(exported.ToByteArray())).IsEqualTo(imported);
        await Assert.That(reader.FromJson(JsonFormatter.Default.Format(exported))).IsEqualTo(imported);
        await Assert.That(wire.ToByteArray().SequenceEqual(original)).IsTrue();
    }

    [Test]
    public async Task OneConverterCanRunIndependentTraversalsConcurrently()
    {
        ProtoToRelConverter converter = Relations();
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
            (Project)converter.ToRel(ScalarProject(Scalar(ScalarProject(Integer(index))))))));
        for (int index = 0; index < results.Length; ++index)
        {
            var subquery = (CoreExpression.ScalarSubquery)results[index].Expressions[0];
            await Assert.That(((Literal.I64Literal)((Project)subquery.Subquery).Expressions[0]).Value).IsEqualTo((long)index);
        }
    }

    private static ProtoToRelConverter Relations() => new(new ExtensionsDictionary.Builder().Build(), new ExtensionsCollection());

    private static ProtoToExpressionConverter Expressions()
    {
        var lookup = new ExtensionsDictionary.Builder().Build();
        var extensions = new ExtensionsCollection();
        return new(lookup, extensions, new ProtoToTypeConverter(lookup, extensions), new ProtoToRelConverter(lookup, extensions));
    }

    private static ProtoRel Read(ProtoType? type = null) => new()
    {
        Read = new()
        {
            BaseSchema = new()
            {
                Names = { "value" },
                Struct = new() { Nullability = ProtoType.Types.Nullability.Required, Types_ = { type ?? I64() } },
            },
            NamedTable = new() { Names = { "items" } },
        },
    };

    private static ProtoRel ScalarProject(ProtoExpression expression, ProtoRel? input = null) => new()
    {
        Project = new()
        {
            Input = input ?? Read(),
            Expressions = { expression },
            Common = new() { Emit = new() { OutputMapping = { 1 } } },
        },
    };

    private static ProtoType I64() => new() { I64 = new() { Nullability = ProtoType.Types.Nullability.Required } };
    private static ProtoType StringType() => new() { String = new() { Nullability = ProtoType.Types.Nullability.Required } };
    private static ProtoType BoolType() => new() { Bool = new() { Nullability = ProtoType.Types.Nullability.Required } };
    private static ProtoExpression Integer(long value) => new() { Literal = new() { I64 = value } };
    private static ProtoExpression Boolean(bool value = true) => new() { Literal = new() { Boolean = value } };
    private static ProtoExpression Scalar(ProtoRel input) => new() { Subquery = new() { Scalar = new() { Input = input } } };
    private static ProtoExpression Cast(ProtoExpression input) => new()
    {
        Cast = new()
        {
            Input = input,
            Type = I64(),
            FailureBehavior = ProtoExpression.Types.Cast.Types.FailureBehavior.ThrowException,
        },
    };

    private static ProtoExpression Field(uint outerLevels = 0)
    {
        var reference = new ProtoExpression.Types.FieldReference
        {
            DirectReference = new() { StructField = new() { Field = 0 } },
        };
        if (outerLevels == 0)
        {
            reference.RootReference = new();
        }
        else
        {
#pragma warning disable CS0612
            reference.OuterReference = new() { StepsOut = outerLevels };
#pragma warning restore CS0612
        }

        return new() { Selection = reference };
    }

    private static AggregateRel.Types.Measure Measure(long value, bool filter) => new()
    {
        Measure_ = new()
        {
            FunctionReference = 0,
            Arguments =
            {
                new FunctionArgument { Value = Integer(value) },
                new FunctionArgument { Type = I64() },
                new FunctionArgument { Value = Scalar(ScalarProject(Integer(value + 1))) },
            },
            OutputType = I64(),
            Phase = AggregationPhase.InitialToResult,
            Invocation = AggregateFunction.Types.AggregationInvocation.All,
        },
        Filter = Boolean(filter),
    };
}
