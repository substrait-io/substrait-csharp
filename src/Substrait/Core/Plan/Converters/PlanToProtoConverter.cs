// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;
using Substrait.Core.Extension;
using Substrait.Core.Metadata;
using Substrait.Core.Relation;
using Substrait.Core.Relation.Converters;
using Substrait.Core.Type;
using Substrait.Tools.Visitor;
using ProtoExpression = Substrait.Protobuf.Expression;
using ProtoRel = Substrait.Protobuf.Rel;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Core.Plan.Converters;

/// <summary>
/// Converts internal plans to protobuf plans.
/// </summary>
public class PlanToProtoConverter
{
    /// <summary>
    /// Converts an internal plan to protobuf.
    /// </summary>
    /// <param name="plan">The internal plan.</param>
    /// <returns>The protobuf plan.</returns>
    public Protobuf.Plan From(IPlan plan) => this.FromCore(plan, maxInlineRelExpressionDepth: null);

    /// <summary>Exports a plan with bounded inline relation/expression depth using detached subtrees.</summary>
    /// <param name="plan">The internal plan.</param>
    /// <param name="maxInlineRelExpressionDepth">Positive maximum number of concrete relation and expression nodes on an inline path.
    /// Each chunk starts at depth one; detached ordinal placeholders do not count.
    /// Retained detached tables, types, literal internals, and opaque metadata are not bounded.</param>
    /// <returns>The protobuf plan, with new detached entries appended after any retained entries.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The depth is less than one.</exception>
    /// <remarks>This is not a protobuf parser recursion limit. Output is deterministic but does not minimize the number of detached entries.</remarks>
    public Protobuf.Plan From(IPlan plan, int maxInlineRelExpressionDepth)
    {
        if (maxInlineRelExpressionDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInlineRelExpressionDepth), "Inline relation/expression depth must be positive.");
        }

        return this.FromCore(plan, maxInlineRelExpressionDepth);
    }

    private Protobuf.Plan FromCore(IPlan plan, int? maxInlineRelExpressionDepth)
    {
        if (plan.Relations.Count == 0)
        {
            throw new ArgumentException("Plan must contain at least one relation.");
        }

        PlanValidation.Validate(plan.Relations);
        PlanMetadata metadata = plan.Metadata ?? throw new ArgumentException("Plan metadata must not be null.", nameof(plan));
        var context = new ConverterContext(metadata) { PlanRelations = plan.Relations };
        if (maxInlineRelExpressionDepth is int maxDepth)
        {
            context.Export = new ProtoExportContext(context, maxDepth);
        }

        var relationConverter = new RelToProtoConverter();
        var result = new Protobuf.Plan
        {
            AdvancedExtensions = metadata.AdvancedExtensions?.ToProto(),
            Version = new Protobuf.Version
            {
                MajorNumber = plan.Version.MajorNumber,
                MinorNumber = plan.Version.MinorNumber,
                PatchNumber = plan.Version.PatchNumber,
                GitHash = plan.Version.GitHash,
                Producer = plan.Version.Producer,
            },
        };
        result.ExpectedTypeUrls.AddRange(metadata.ExpectedTypeUrls);
        result.Relations.AddRange(plan.Relations.Select(entry =>
        {
            Protobuf.PlanRel output = entry.Metadata.ToProto();
            ProtoRel input = context.Export is null
                ? relationConverter.From(entry.Input, context)
                : context.Export.Convert(entry.Input, output, relationConverter);
            if (entry is IPlan.IRoot root)
            {
                output.Root = new Protobuf.RelRoot
                {
                    Input = input,
                    Names = { root.Names },
                };
            }
            else
            {
                output.Rel = input;
            }

            return output;
        }));

        context.ExtensionsCollector.WriteTo(result);

        return result;
    }

    /// <summary>
    /// Stores intermediate converter outputs and collected extensions.
    /// </summary>
    public class ConverterContext : IContext<IRel, ProtoRel>, IContext<IExpression, ProtoExpression>, IContext<IType, ProtoType>
    {
        private readonly ExtensionsCollector.Builder extensions;
        private readonly Context<IExpression, ProtoExpression> expressions = new();
        private readonly Context<IRel, ProtoRel> relations = new();
        private readonly Context<IType, ProtoType> types = new();

        /// <summary>Initializes a context with fresh extension anchor namespaces.</summary>
        public ConverterContext()
            : this(PlanMetadata.Empty)
        {
        }

        internal ConverterContext(PlanMetadata metadata)
        {
            this.extensions = new(metadata.ExtensionUrns, metadata.Extensions);
        }

        internal IReadOnlyList<IPlan.IRelation>? PlanRelations { get; init; }

        internal ProtoExportContext? Export { get; set; }

        internal void ValidateReference(Reference reference)
        {
            if (this.PlanRelations is null)
            {
                throw new InvalidOperationException($"Reference ordinal {reference.SubtreeOrdinal} requires a plan reference context. Convert the containing plan instead.");
            }

            PlanValidation.ValidateBinding(reference, this.PlanRelations);
        }

        /// <summary>Gets the collected extension declarations.</summary>
        public ExtensionsCollector ExtensionsCollector => this.extensions.Build();

        /// <summary>Collects an extension and returns its anchor.</summary>
        /// <returns>The extension anchor, from 0 through <see cref="int.MaxValue"/>.</returns>
        /// <exception cref="OverflowException">
        /// The extension anchor exceeds <see cref="int.MaxValue"/>.
        /// </exception>
        /// <remarks>Full-plan conversion uses unsigned anchors and is not limited by this signed return type.</remarks>
        public int AddExtension(ExtensionsCollector.ExtensionType type, string uri, string name) =>
            this.extensions.Collect(type, uri, name);

        internal uint AddExtensionReference(ExtensionsCollector.ExtensionType type, string uri, string name) =>
            this.extensions.CollectReference(type, uri, name);

        /// <inheritdoc/>
        public ProtoRel GetOutput(IRel node) => this.Export is null ? this.relations.GetOutput(node) : this.Export.GetOutput(node);

        /// <inheritdoc/>
        public void AddOutput(IRel node, ProtoRel output) => this.relations.AddOutput(node, output);

        /// <inheritdoc/>
        public void RemoveOutput(IRel node) => this.relations.RemoveOutput(node);

        /// <inheritdoc/>
        public ProtoExpression GetOutput(IExpression node) => this.Export is null ? this.expressions.GetOutput(node) : this.Export.GetOutput(node);

        /// <inheritdoc/>
        public void AddOutput(IExpression node, ProtoExpression output) => this.expressions.AddOutput(node, output);

        /// <inheritdoc/>
        public void RemoveOutput(IExpression node) => this.expressions.RemoveOutput(node);

        /// <inheritdoc/>
        public ProtoType GetOutput(IType node) => this.types.GetOutput(node);

        /// <inheritdoc/>
        public void AddOutput(IType node, ProtoType output) => this.types.AddOutput(node, output);

        /// <inheritdoc/>
        public void RemoveOutput(IType node) => this.types.RemoveOutput(node);
    }
}
