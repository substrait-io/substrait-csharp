// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Expression;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using Substrait.Protobuf;
using Substrait.Tools;
using CoreExpression = Substrait.Core.Expression.Expression;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoPlan = Substrait.Protobuf.Plan;
using ProtoRel = Substrait.Protobuf.Rel;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class DetachedPlanTests
{
    private static readonly string[] ImportFormats = ["object", "bytes", "stream", "file", "json"];

    [Test]
    [Arguments("expressions", false)]
    [Arguments("expressions", true)]
    [Arguments("relations", false)]
    [Arguments("relations", true)]
    [Arguments("mixed", false)]
    [Arguments("mixed", true)]
    public async Task DetachedPlansRoundTripThroughEveryImport(string kind, bool nonRoot)
    {
        ProtoPlan wire = DetachedPlan(kind, nonRoot);
        byte[] originalBytes = wire.ToByteArray();
        IPlan expected = Reader().From(WirePlan(InlineTree(), nonRoot));
        foreach (string format in ImportFormats)
        {
            foreach (var mode in new[] { ExtensionsDictionary.StrictMode.STRICT, ExtensionsDictionary.StrictMode.OFF })
            {
                IPlan imported = await Import(wire, format, mode);
                await Assert.That(imported.Relations[0].Input).IsEqualTo(expected.Relations[0].Input).Because(format);
                await Assert.That(imported.Roots.Count).IsEqualTo(expected.Roots.Count);
                if (!nonRoot)
                {
                    await Assert.That(imported.Roots[0].Names.SequenceEqual(expected.Roots[0].Names)).IsTrue();
                }

                ProtoPlan exported = new PlanToProtoConverter().From(imported);
                await Assert.That(exported.Relations[0].DetachedExpressions.SequenceEqual(wire.Relations[0].DetachedExpressions)).IsTrue();
                await Assert.That(exported.Relations[0].DetachedRels.SequenceEqual(wire.Relations[0].DetachedRels)).IsTrue();
                await Assert.That(Reader().FromBytes(exported.ToByteArray())).IsEqualTo(imported);
                await Assert.That(Reader().FromJson(JsonFormatter.Default.Format(exported))).IsEqualTo(imported);
                await Assert.That(new PlanToProtoConverter().From(imported).ToByteArray().SequenceEqual(exported.ToByteArray())).IsTrue();
            }
        }

        await Assert.That(wire.ToByteArray().SequenceEqual(originalBytes)).IsTrue();
    }

    [Test]
    public async Task BinaryAndJsonFilesPreserveDetachedEncoding()
    {
        ProtoPlan wire = DetachedPlan("mixed", nonRoot: false);
        foreach (var format in new[] { FileUtils.FileType.Protobuf, FileUtils.FileType.Json })
        {
            string path = Path.Combine(Path.GetTempPath(), $"detached-plan-{Guid.NewGuid():N}");
            try
            {
                FileUtils.WritePlan(wire, path, format);
                ProtoPlan parsed = FileUtils.FetchPlan(path, format);
                await Assert.That(parsed).IsEqualTo(wire);
                await Assert.That(Reader().From(parsed).Relations[0].Input).IsEqualTo(Reader().From(WirePlan(InlineTree())).Relations[0].Input);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task DetachedOrdinalsAreLocalAndPlanDependenciesAreStillResolved()
    {
        ProtoPlan wire = WirePlan(RelOrdinal(0));
        wire.Relations[0].DetachedRels.Add(new ProtoRel { Reference = new() { SubtreeOrdinal = 1 } });
        wire.Relations.Add(new PlanRel { Rel = RelOrdinal(0), DetachedRels = { Read() } });
        IPlan imported = Reader().FromBytes(wire.ToByteArray());
        var reference = (Reference)imported.Relations[0].Input;
        await Assert.That(reference.SubtreeOrdinal).IsEqualTo(1);
        await Assert.That(reference.Target).IsSameReferenceAs(imported.Relations[1].Input);
        await Assert.That(reference.RecordType.Fields[0]).IsEqualTo(TypeFactory.REQUIRED.I64);

        wire.Relations[0].DetachedRels.Clear();
        SerializationException error = await Assert.That(() => Reader().From(wire)).ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("Plan relation ordinal 0");
        await Assert.That(error.Message).Contains("detached relation ordinal 0");
    }

    [Test]
    public async Task DetachedSubqueriesPreserveTheirUseSiteCorrelation()
    {
        ProtoPlan wire = WirePlan(Project(Read(), ExpressionOrdinal(0)));
        wire.Relations[0].DetachedExpressions.Add(Scalar(RelOrdinal(0)));
        wire.Relations[0].DetachedExpressions.Add(OuterField(1));
        wire.Relations[0].DetachedRels.Add(new ProtoRel
        {
            Project = new()
            {
                Input = Read(),
                Expressions = { ExpressionOrdinal(1) },
                Common = new() { Emit = new() { OutputMapping = { 1 } } },
            },
        });

        foreach (string format in ImportFormats)
        {
            IPlan imported = await Import(wire, format);
            var outer = (Project)imported.Relations[0].Input;
            var subquery = (CoreExpression.ScalarSubquery)outer.Expressions[0];
            var inner = (Project)subquery.Subquery;
            var reference = (FieldReference)inner.Expressions[0];
            await Assert.That(reference.SubqueryLevels).IsEqualTo(1);
            await Assert.That(reference.Type).IsEqualTo(TypeFactory.REQUIRED.I64);
            await Assert.That(Reader().FromBytes(new PlanToProtoConverter().From(imported).ToByteArray())).IsEqualTo(imported);
        }

        wire.Relations[0].DetachedExpressions[1] = OuterField(2);
        await Assert.That(() => Reader().From(wire)).ThrowsExactly<SerializationException>();
    }

    [Test]
    [Arguments("scalar")]
    [Arguments("in")]
    [Arguments("set-predicate")]
    [Arguments("set-comparison")]
    public async Task AllSubqueryKindsResolveDetachedInputsAndOperands(string kind)
    {
        ProtoExpression expression = kind switch
        {
            "scalar" => Scalar(RelOrdinal(0)),
            "in" => new() { Subquery = new() { InPredicate = new() { Haystack = RelOrdinal(0), Needles = { ExpressionOrdinal(1) } } } },
            "set-predicate" => new()
            {
                Subquery = new()
                {
                    SetPredicate = new()
                    {
                        Tuples = RelOrdinal(0),
                        PredicateOp = ProtoExpression.Types.Subquery.Types.SetPredicate.Types.PredicateOp.Exists,
                    },
                },
            },
            "set-comparison" => new()
            {
                Subquery = new()
                {
                    SetComparison = new()
                    {
                        Left = ExpressionOrdinal(1),
                        Right = RelOrdinal(0),
                        ComparisonOp = ProtoExpression.Types.Subquery.Types.SetComparison.Types.ComparisonOp.Eq,
                        ReductionOp = ProtoExpression.Types.Subquery.Types.SetComparison.Types.ReductionOp.Any,
                    },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown subquery kind."),
        };
        ProtoPlan wire = WirePlan(Project(Read(), ExpressionOrdinal(0)));
        wire.Relations[0].DetachedExpressions.Add(expression);
        wire.Relations[0].DetachedExpressions.Add(Integer(7));
        wire.Relations[0].DetachedRels.Add(Read());
        foreach (string format in ImportFormats)
        {
            IPlan imported = await Import(wire, format);
            ProtoPlan exported = new PlanToProtoConverter().From(imported);
            await Assert.That(Reader().FromBytes(exported.ToByteArray())).IsEqualTo(imported);
            await Assert.That(exported.Relations[0].Root.Input.Project.Expressions[0].RexTypeCase).IsEqualTo(ProtoExpression.RexTypeOneofCase.Subquery);
        }
    }

    [Test]
    [Arguments("expression-range")]
    [Arguments("relation-range")]
    [Arguments("expression-max")]
    [Arguments("relation-max")]
    [Arguments("expression-alias")]
    [Arguments("relation-alias")]
    [Arguments("expression-unset")]
    [Arguments("relation-unset")]
    [Arguments("expression-duplicate")]
    [Arguments("relation-duplicate")]
    [Arguments("expression-cycle")]
    [Arguments("relation-cycle")]
    [Arguments("mixed-cycle")]
    [Arguments("plan-cycle")]
    [Arguments("duplicate-anchor")]
    public async Task InvalidReachableTablesFailAcrossImportEntryPoints(string failure)
    {
        ProtoPlan wire = InvalidPlan(failure);
        foreach (string format in ImportFormats)
        {
            foreach (var mode in new[] { ExtensionsDictionary.StrictMode.STRICT, ExtensionsDictionary.StrictMode.OFF })
            {
                SerializationException error = await Assert.That(async () => await Import(wire, format, mode))
                    .ThrowsExactly<SerializationException>().And.IsNotNull();
                await Assert.That(error.Message).Contains("ordinal 0").Because($"{failure}, {format}");
            }
        }
    }

    [Test]
    public async Task UnreachableEntriesAreRetainedWithoutSemanticValidation()
    {
        ProtoPlan wire = WirePlan(Read());
        wire.Relations[0].DetachedExpressions.Add(new ProtoExpression());
        wire.Relations[0].DetachedExpressions.Add(ExpressionOrdinal(uint.MaxValue));
        wire.Relations[0].DetachedRels.Add(new ProtoRel());
        wire.Relations[0].DetachedRels.Add(RelOrdinal(uint.MaxValue));
        wire.Relations[0].DetachedRels.Add(new ProtoRel { Reference = new() { SubtreeOrdinal = -1 } });
        foreach (string format in ImportFormats)
        {
            IPlan imported = await Import(wire, format);
            await Assert.That(imported.Relations.Count).IsEqualTo(1);
            await Assert.That(imported.Relations[0].Input).IsAssignableTo<NamedTableRead>();
            await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(wire);
        }
    }

    [Test]
    public async Task UnreachableDetachedAnchorsDoNotConflictWithLogicalTrees()
    {
        ProtoPlan wire = WirePlan(Read());
        wire.Relations[0].DetachedRels.Add(Read());
        wire.Relations[0].DetachedRels[0].Read.Common.RelAnchor = 9;
        wire.Relations.Add(new PlanRel { Rel = Read() });
        wire.Relations[1].Rel.Read.Common.RelAnchor = 9;
        IPlan imported = Reader().FromBytes(wire.ToByteArray());
        await Assert.That(imported.Relations.Count).IsEqualTo(2);
        await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(wire);
    }

    [Test]
    public async Task OpaquePayloadsKeepTheirTablesAndOriginalOrdinals()
    {
        ProtoPlan wire = DetachedPlan("mixed", nonRoot: false);
        PlanRel entry = wire.Relations[0];
        entry.DetachedExpressions.Add(Scalar(RelOrdinal(2)));
        entry.DetachedRels.Add(Read());
        entry.Root.Input.Project.AdvancedExtension = new()
        {
            Enhancement = Any.Pack(ExpressionOrdinal(2)),
            Optimization = { Any.Pack(RelOrdinal(2)) },
        };
        TypeRegistry registry = TypeRegistry.FromMessages(ProtoExpression.Descriptor, ProtoRel.Descriptor);
        JsonFormatter formatter = new(JsonFormatter.Settings.Default.WithTypeRegistry(registry));
        JsonParser parser = new(JsonParser.Settings.Default.WithTypeRegistry(registry));
        IPlan imported = Reader().From(wire);
        IPlan fromBytes = Reader().FromBytes(wire.ToByteArray());
        IPlan fromJson = Reader().FromJson(formatter.Format(wire), parser: parser);
        ProtoPlan expected = new PlanToProtoConverter().From(imported);
        await Assert.That(fromBytes).IsEqualTo(imported);
        await Assert.That(fromJson).IsEqualTo(imported);
        await Assert.That(expected.Relations[0].DetachedExpressions[2]).IsEqualTo(entry.DetachedExpressions[2]);
        await Assert.That(expected.Relations[0].DetachedRels[2]).IsEqualTo(entry.DetachedRels[2]);
        await Assert.That(expected.Relations[0].Root.Input.Project.AdvancedExtension).IsEqualTo(entry.Root.Input.Project.AdvancedExtension);
        await Assert.That(expected.Relations[0].Root.Input.Project.AdvancedExtension.Enhancement.Unpack<ProtoExpression>().DetachedExpressionOrdinal).IsEqualTo(2U);

        entry.DetachedExpressions.Clear();
        entry.DetachedRels[2].Read.NamedTable.Names.Clear();
        entry.Root.Input.Project.AdvancedExtension.Enhancement.Value = ByteString.Empty;
        PlanRel metadataCopy = imported.Relations[0].Metadata.ToProto();
        metadataCopy.DetachedRels.Clear();
        metadataCopy.DetachedExpressions[2].Subquery.Scalar.Input = Read();
        await Assert.That(new PlanToProtoConverter().From(imported)).IsEqualTo(expected);

        var root = imported.Roots[0];
        var builder = new PlanBuilder(imported.Metadata, imported.Version);
        builder.AddRoot(root.Metadata, root.Input, root.Names);
        await Assert.That(new PlanToProtoConverter().From(builder.Build())).IsEqualTo(expected);
        expected.Relations[0].DetachedExpressions.Clear();
        await Assert.That(imported.Relations[0].Metadata.DetachedExpressionCount).IsEqualTo(3);
    }

    [Test]
    public async Task EncodingMetadataSnapshotsTablesAndSurvivesEntryCopies()
    {
        ProtoPlan wire = DetachedPlan("mixed", nonRoot: true);
        PlanRel source = wire.Relations[0];
        PlanRelationMetadata metadata = PlanRelationMetadata.FromProto(source);
        PlanRelationMetadata equal = PlanRelationMetadata.FromProto(source);
        await Assert.That(metadata).IsEqualTo(equal);
        await Assert.That(metadata.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(metadata.DetachedExpressionCount).IsEqualTo(2);
        await Assert.That(metadata.DetachedRelCount).IsEqualTo(2);
        await Assert.That(metadata.ToProto().RelTypeCase).IsEqualTo(PlanRel.RelTypeOneofCase.None);
        source.DetachedExpressions[1].Literal.I64 = 999;
        source.DetachedRels.Clear();
        await Assert.That(metadata).IsEqualTo(equal);

        IPlan imported = Reader().From(DetachedPlan("mixed", nonRoot: true));
        var builder = new PlanBuilder(imported.Metadata, imported.Version);
        builder.RegisterSubplan(imported.Relations[0].Metadata, imported.Relations[0].Input);
        IPlan rebuilt = builder.Build();
        await Assert.That(rebuilt).IsEqualTo(imported);
        IPlan copied = Substrait.Core.Plan.Plan.FromRelations(imported.Metadata, imported.Relations, imported.Version);
        await Assert.That(copied).IsEqualTo(imported);
        await Assert.That(PlanRelationMetadata.FromProto(new PlanRel())).IsSameReferenceAs(PlanRelationMetadata.Empty);
        await Assert.That(() => PlanRelationMetadata.FromProto(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task UnknownPayloadsAndUnknownDetachedFieldsSurviveBinaryRoundTrip()
    {
        ProtoPlan wire = WirePlan(Read());
        // Unknown field 100 (varint) supplements an otherwise ordinary expression.
        ProtoExpression expression = Integer(7);
        expression.MergeFrom(new byte[] { 0xa0, 0x06, 0x01 });
        wire.Relations[0].DetachedExpressions.Add(expression);
        wire.Relations[0].Root.Input.Read.AdvancedExtension = new()
        {
            Enhancement = new Any
            {
                TypeUrl = "example.invalid/opaque-expression-reference",
                Value = ExpressionOrdinal(0).ToByteString(),
            },
        };
        IPlan imported = Reader().FromBytes(wire.ToByteArray());
        await Assert.That(new PlanToProtoConverter().From(imported).ToByteArray().SequenceEqual(wire.ToByteArray())).IsTrue();
    }

    [Test]
    public async Task ExtensionRelationDetailsRetainTheirContainerReferences()
    {
        ProtoPlan wire = WirePlan(new ProtoRel
        {
            ExtensionSingle = new()
            {
                Input = RelOrdinal(0),
                Detail = Any.Pack(ExpressionOrdinal(0)),
                Common = new() { Direct = new() },
            },
        });
        wire.Relations[0].DetachedExpressions.Add(Integer(7));
        wire.Relations[0].DetachedRels.Add(Read());
        IPlan imported = Reader().FromBytes(wire.ToByteArray());
        ProtoPlan exported = new PlanToProtoConverter().From(imported);
        await Assert.That(exported.Relations[0].Root.Input.ExtensionSingle.Detail).IsEqualTo(wire.Relations[0].Root.Input.ExtensionSingle.Detail);
        await Assert.That(exported.Relations[0].DetachedExpressions[0]).IsEqualTo(Integer(7));
        await Assert.That(exported.Relations[0].DetachedRels[0]).IsEqualTo(Read());
        await Assert.That(Reader().FromBytes(exported.ToByteArray())).IsEqualTo(imported);
    }

    [Test]
    public async Task DetachedEncodingOvercomesTheDefaultParserNestingLimit()
    {
        const int depth = 256;
        ProtoExpression inline = Integer(7);
        ProtoPlan detached = WirePlan(Project(Read(), ExpressionOrdinal(0)));
        for (int index = 0; index < depth; ++index)
        {
            inline = Cast(inline);
            detached.Relations[0].DetachedExpressions.Add(Cast(index == depth - 1 ? Integer(7) : ExpressionOrdinal((uint)index + 1)));
        }

        ProtoPlan nested = WirePlan(Project(Read(), inline));
        await Assert.That(() => Reader().FromBytes(nested.ToByteArray())).ThrowsExactly<InvalidProtocolBufferException>();
        await Assert.That(() => Reader().FromJson(JsonFormatter.Default.Format(nested))).ThrowsExactly<InvalidProtocolBufferException>();
        IPlan fromBytes = Reader().FromBytes(detached.ToByteArray());
        IPlan fromJson = Reader().FromJson(JsonFormatter.Default.Format(detached));
        await Assert.That(fromBytes.Relations[0].Input).IsEqualTo(Reader().From(nested).Relations[0].Input);
        await Assert.That(fromJson).IsEqualTo(fromBytes);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeepDetachedChainsImportWithoutRaisingParserLimits(bool expressions)
    {
        const int depth = 10000;
        ProtoPlan wire = WirePlan(expressions ? Project(Read(), ExpressionOrdinal(0)) : RelOrdinal(0));
        PlanRel entry = wire.Relations[0];
        for (int index = 0; index < depth; ++index)
        {
            if (expressions)
            {
                entry.DetachedExpressions.Add(Cast(index == depth - 1 ? Integer(7) : ExpressionOrdinal((uint)index + 1)));
            }
            else
            {
                entry.DetachedRels.Add(Filter(index == depth - 1 ? Read() : RelOrdinal((uint)index + 1), Boolean()));
            }
        }

        foreach (string format in new[] { "object", "bytes", "json" })
        {
            IPlan imported = await Import(wire, format);
            if (expressions)
            {
                IExpression current = ((Project)imported.Relations[0].Input).Expressions[0];
                for (int index = 0; index < depth; ++index)
                {
                    current = ((CoreExpression.Cast)current).Input;
                }

                await Assert.That(((Literal.I64Literal)current).Value).IsEqualTo(7L);
            }
            else
            {
                IRel current = imported.Relations[0].Input;
                for (int index = 0; index < depth; ++index)
                {
                    current = ((Filter)current).Input;
                }

                await Assert.That(current).IsAssignableTo<NamedTableRead>();
            }
        }
    }

    [Test]
    public async Task DeepMixedSubqueriesImportWithoutConverterRecursion()
    {
        const int depth = 10000;
        ProtoPlan wire = WirePlan(RelOrdinal(0));
        PlanRel entry = wire.Relations[0];
        for (int index = 0; index < depth; ++index)
        {
            ProtoRel project = Project(Read(), ExpressionOrdinal((uint)index));
            project.Project.Common = new() { Emit = new() { OutputMapping = { 1 } } };
            entry.DetachedRels.Add(project);
            entry.DetachedExpressions.Add(Scalar(RelOrdinal((uint)index + 1)));
        }

        entry.DetachedRels.Add(Read());
        foreach (string format in new[] { "bytes", "json" })
        {
            IPlan imported = await Import(wire, format);
            IRel current = imported.Relations[0].Input;
            for (int index = 0; index < depth; ++index)
            {
                current = ((CoreExpression.ScalarSubquery)((Project)current).Expressions[0]).Subquery;
            }

            await Assert.That(current).IsAssignableTo<NamedTableRead>();
            await Assert.That(imported.Relations[0].Input.RecordType.Fields[0]).IsEqualTo(TypeFactory.REQUIRED.I64);
        }
    }

    [Test]
    public async Task WideDetachedSiblingsKeepRelationAndExpressionResultsSeparate()
    {
        const int width = 128;
        ProtoPlan wire = WirePlan(new ProtoRel { Set = new() { Op = SetRel.Types.SetOp.UnionAll } });
        PlanRel entry = wire.Relations[0];
        for (int index = 0; index < width; ++index)
        {
            entry.Root.Input.Set.Inputs.Add(RelOrdinal((uint)index));
            entry.DetachedRels.Add(Project(Read(), ExpressionOrdinal((uint)index)));
            ProtoRel subquery = Project(Read(), Integer(index));
            subquery.Project.Common = new() { Emit = new() { OutputMapping = { 1 } } };
            entry.DetachedExpressions.Add(Scalar(subquery));
        }

        foreach (string format in new[] { "object", "bytes", "json" })
        {
            IPlan imported = await Import(wire, format);
            var set = (Set)imported.Relations[0].Input;
            await Assert.That(set.Inputs.Count).IsEqualTo(width);
            for (int index = 0; index < width; ++index)
            {
                var expression = (CoreExpression.ScalarSubquery)((Project)set.Inputs[index]).Expressions[0];
                var value = (Literal.I64Literal)((Project)expression.Subquery).Expressions[0];
                await Assert.That(value.Value).IsEqualTo((long)index);
            }
        }
    }

    [Test]
    public async Task SharedDetachedProtobufObjectsUseTheirOwnContainerSchemas()
    {
        ProtoExpression field = new()
        {
            Selection = new()
            {
                DirectReference = new() { StructField = new() { Field = 0 } },
                RootReference = new(),
            },
        };
        ProtoPlan wire = WirePlan(Project(Read(), ExpressionOrdinal(0)));
        wire.Relations[0].DetachedExpressions.Add(field);
        ProtoRel booleanRead = Read();
        booleanRead.Read.BaseSchema.Struct.Types_[0] = new()
        {
            Bool = new() { Nullability = ProtoType.Types.Nullability.Required },
        };
        wire.Relations.Add(new PlanRel
        {
            Rel = Project(booleanRead, ExpressionOrdinal(0)),
            DetachedExpressions = { field },
        });

        foreach (string format in new[] { "object", "bytes", "json" })
        {
            IPlan imported = await Import(wire, format);
            await Assert.That(((Project)imported.Relations[0].Input).Expressions[0].Type).IsEqualTo(TypeFactory.REQUIRED.I64);
            await Assert.That(((Project)imported.Relations[1].Input).Expressions[0].Type).IsEqualTo(TypeFactory.REQUIRED.BOOL);
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DetachedFetchBoundsPreserveProtocolDefaults(bool count, bool offset)
    {
        ProtoPlan wire = WirePlan(new ProtoRel
        {
            Fetch = new()
            {
                Input = RelOrdinal(0),
                CountExpr = count ? ExpressionOrdinal(0) : null,
                OffsetExpr = offset ? ExpressionOrdinal(1) : null,
            },
        });
        wire.Relations[0].DetachedRels.Add(Read());
        wire.Relations[0].DetachedExpressions.Add(Integer(3));
        wire.Relations[0].DetachedExpressions.Add(Integer(7));
        IExpression expectedCount = count ? new Literal.I64Literal(3) : new Literal.NullLiteral(TypeFactory.NULLABLE.I64);
        foreach (string format in new[] { "object", "bytes", "json" })
        {
            IPlan imported = await Import(wire, format);
            var fetch = (Fetch)imported.Relations[0].Input;
            await Assert.That(fetch.Count).IsEqualTo(expectedCount);
            await Assert.That(fetch.Offset).IsEqualTo(new Literal.I64Literal(offset ? 7 : 0));
            ProtoPlan exported = new PlanToProtoConverter().From(imported);
            await Assert.That(Reader().FromBytes(exported.ToByteArray())).IsEqualTo(imported);
            await Assert.That(Reader().FromJson(JsonFormatter.Default.Format(exported))).IsEqualTo(imported);
        }
    }

    [Test]
    public async Task ReaderReuseAndConcurrentImportsDoNotLeakDetachedContexts()
    {
        ProtoToPlanConverter reader = Reader();
        await Assert.That(() => reader.From(InvalidPlan("mixed-cycle"))).ThrowsExactly<SerializationException>();
        IPlan[] results = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            ProtoPlan wire = WirePlan(Project(Read(), ExpressionOrdinal(0)));
            wire.Relations[0].DetachedExpressions.Add(Integer(index));
            return reader.FromBytes(wire.ToByteArray());
        })));
        for (int index = 0; index < results.Length; ++index)
        {
            var value = (Literal.I64Literal)((Project)results[index].Relations[0].Input).Expressions[0];
            await Assert.That(value.Value).IsEqualTo((long)index);
        }

        await Assert.That(() => reader.From(WirePlan(Project(Read(), ExpressionOrdinal(0))))).ThrowsExactly<SerializationException>();
        await Assert.That(reader.From(WirePlan(Read())).Relations[0].Input).IsAssignableTo<NamedTableRead>();
    }

    [Test]
    public async Task StandaloneConvertersRejectDetachedOrdinalsWithoutContainerContext()
    {
        var relations = new ProtoToRelConverter(new ExtensionsDictionary.Builder().Build(), new ExtensionsCollection());
        var expressions = new Substrait.Core.Expression.Converters.ProtoToExpressionConverter(
            new ExtensionsDictionary.Builder().Build(), new ExtensionsCollection(),
            new Substrait.Core.Type.Converters.ProtoToTypeConverter(new ExtensionsDictionary.Builder().Build(), new ExtensionsCollection()));
        await Assert.That(() => relations.ToRel(RelOrdinal(0))).ThrowsExactly<SerializationException>();
        await Assert.That(() => expressions.From(ExpressionOrdinal(0))).ThrowsExactly<SerializationException>();
    }

    private static ProtoToPlanConverter Reader() => new(new ExtensionsCollection());

    private static async Task<IPlan> Import(
        ProtoPlan wire, string format,
        ExtensionsDictionary.StrictMode mode = ExtensionsDictionary.StrictMode.STRICT)
    {
        ProtoToPlanConverter reader = Reader();
        switch (format)
        {
            case "object":
                return reader.From(wire, mode);
            case "bytes":
                return reader.FromBytes(wire.ToByteArray(), mode);
            case "json":
                return reader.FromJson(JsonFormatter.Default.Format(wire), mode);
            case "stream":
                using (var stream = new MemoryStream(wire.ToByteArray()))
                {
                    try
                    {
                        return reader.FromStream(stream, mode);
                    }
                    finally
                    {
                        await Assert.That(stream.CanRead).IsTrue();
                    }
                }
            case "file":
                string path = Path.Combine(Path.GetTempPath(), $"detached-import-{Guid.NewGuid():N}.pb");
                try
                {
                    File.WriteAllBytes(path, wire.ToByteArray());
                    return reader.FromFile(path, mode);
                }
                finally
                {
                    using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        await Assert.That(exclusive.CanWrite).IsTrue();
                    }

                    File.Delete(path);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown import format.");
        }
    }

    private static ProtoPlan DetachedPlan(string kind, bool nonRoot)
    {
        bool expressions = kind is "expressions" or "mixed";
        bool relations = kind is "relations" or "mixed";
        ProtoRel filter = Filter(relations ? RelOrdinal(0) : Read(), expressions ? ExpressionOrdinal(0) : Boolean());
        ProtoPlan wire = WirePlan(Project(relations ? RelOrdinal(1) : filter, Cast(expressions ? ExpressionOrdinal(1) : Integer(7))), nonRoot);
        if (expressions)
        {
            wire.Relations[0].DetachedExpressions.Add(Boolean());
            wire.Relations[0].DetachedExpressions.Add(Integer(7));
        }

        if (relations)
        {
            wire.Relations[0].DetachedRels.Add(Read());
            wire.Relations[0].DetachedRels.Add(filter);
        }

        return wire;
    }

    private static ProtoPlan InvalidPlan(string failure)
    {
        bool expression = failure.StartsWith("expression", StringComparison.Ordinal);
        ProtoPlan wire = WirePlan(expression ? Project(Read(), ExpressionOrdinal(0)) : RelOrdinal(0));
        PlanRel entry = wire.Relations[0];
        switch (failure)
        {
            case "expression-range":
            case "relation-range":
                break;
            case "expression-max":
                entry.Root.Input.Project.Expressions[0] = ExpressionOrdinal(uint.MaxValue);
                break;
            case "relation-max":
                entry.Root.Input = RelOrdinal(uint.MaxValue);
                break;
            case "expression-alias":
                entry.DetachedExpressions.Add(ExpressionOrdinal(1));
                entry.DetachedExpressions.Add(Integer(1));
                break;
            case "relation-alias":
                entry.DetachedRels.Add(RelOrdinal(1));
                entry.DetachedRels.Add(Read());
                break;
            case "expression-unset":
                entry.DetachedExpressions.Add(new ProtoExpression());
                break;
            case "relation-unset":
                entry.DetachedRels.Add(new ProtoRel());
                break;
            case "expression-duplicate":
                entry.DetachedExpressions.Add(Integer(1));
                entry.Root.Input.Project.Expressions.Add(ExpressionOrdinal(0));
                break;
            case "relation-duplicate":
                entry.DetachedRels.Add(Read());
                entry.Root.Input = new ProtoRel { Cross = new() { Left = RelOrdinal(0), Right = RelOrdinal(0) } };
                break;
            case "expression-cycle":
                entry.DetachedExpressions.Add(Cast(ExpressionOrdinal(0)));
                break;
            case "relation-cycle":
                entry.DetachedRels.Add(Filter(RelOrdinal(0), Boolean()));
                break;
            case "mixed-cycle":
                entry.DetachedRels.Add(Project(Read(), ExpressionOrdinal(0)));
                entry.DetachedExpressions.Add(Scalar(RelOrdinal(0)));
                break;
            case "plan-cycle":
                entry.DetachedRels.Add(new ProtoRel { Reference = new() { SubtreeOrdinal = 0 } });
                break;
            case "duplicate-anchor":
                ProtoRel read = Read();
                read.Read.Common.RelAnchor = 1;
                entry.DetachedRels.Add(read);
                entry.Root.Input = new ProtoRel { Cross = new() { Left = RelOrdinal(0), Right = read.Clone() } };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown failure case.");
        }

        return wire;
    }

    private static ProtoPlan WirePlan(ProtoRel input, bool nonRoot = false) => new()
    {
        Version = new() { MinorNumber = 107, Producer = "detached-plan-tests" },
        Relations = { nonRoot ? new PlanRel { Rel = input } : new PlanRel { Root = new() { Input = input, Names = { "value", "computed" } } } },
    };

    private static ProtoRel InlineTree() => Project(Filter(Read(), Boolean()), Cast(Integer(7)));

    private static ProtoRel Read() => new RelToProtoConverter().From(new NamedTableRead(
        new Substrait.Core.Type.NamedStruct(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])), ["items"], filter: null));

    private static ProtoRel Filter(ProtoRel input, ProtoExpression condition) => new()
    {
        Filter = new() { Input = input, Condition = condition, Common = new() { Direct = new() } },
    };

    private static ProtoRel Project(ProtoRel input, ProtoExpression expression) => new()
    {
        Project = new() { Input = input, Expressions = { expression }, Common = new() { Direct = new() } },
    };

    private static ProtoRel RelOrdinal(uint index) => new() { DetachedRelOrdinal = index };
    private static ProtoExpression ExpressionOrdinal(uint index) => new() { DetachedExpressionOrdinal = index };
    private static ProtoExpression Boolean() => new() { Literal = new() { Boolean = true } };
    private static ProtoExpression Integer(long value) => new() { Literal = new() { I64 = value } };
    private static ProtoExpression Cast(ProtoExpression input) => new()
    {
        Cast = new()
        {
            Input = input,
            Type = new() { I64 = new() { Nullability = ProtoType.Types.Nullability.Required } },
            FailureBehavior = ProtoExpression.Types.Cast.Types.FailureBehavior.ThrowException,
        },
    };

    private static ProtoExpression Scalar(ProtoRel input) => new() { Subquery = new() { Scalar = new() { Input = input } } };

    private static ProtoExpression OuterField(uint levels) => new()
    {
        Selection = new()
        {
            DirectReference = new() { StructField = new() { Field = 0 } },
#pragma warning disable CS0612
            OuterReference = new() { StepsOut = levels },
#pragma warning restore CS0612
        },
    };
}
