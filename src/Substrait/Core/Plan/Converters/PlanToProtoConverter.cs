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
    public Protobuf.Plan From(IPlan plan)
    {
        if (plan.Relations.Count == 0)
        {
            throw new ArgumentException("Plan must contain at least one relation.");
        }

        PlanValidation.Validate(plan.Relations);
        PlanMetadata metadata = plan.Metadata ?? throw new ArgumentException("Plan metadata must not be null.", nameof(plan));
        var context = new ConverterContext { PlanRelations = plan.Relations };
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
        result.Relations.AddRange(plan.Relations.Select(entry => entry is IPlan.IRoot root
            ? new Protobuf.PlanRel
            {
                Root = new Protobuf.RelRoot
                {
                    Input = relationConverter.From(root.Input, context),
                    Names = { root.Names },
                },
            }
            : new Protobuf.PlanRel { Rel = relationConverter.From(entry.Input, context) }));

        ExtensionsCollector collected = context.ExtensionsCollector;
        result.ExtensionUrns.AddRange(collected.ExtensionUris.Select((uri, index) => new Protobuf.SimpleExtensionURN
        {
            Urn = uri,
            ExtensionUrnAnchor = (uint)index + 1,
        }));

#if NET5_0_OR_GREATER
        var nextAnchor = Enum.GetValues<ExtensionsCollector.ExtensionType>()
            .ToDictionary(type => type, _ => 0U);
#else
        var nextAnchor = Enum.GetValues(typeof(ExtensionsCollector.ExtensionType))
            .Cast<ExtensionsCollector.ExtensionType>()
            .ToDictionary(type => type, _ => 0U);
#endif
        result.Extensions.AddRange(collected.Extensions.Select(extension =>
        {
            uint anchor = nextAnchor[extension.Type]++;
            uint uriReference = (uint)extension.ExtensionUriReference + 1;
            return extension.Type switch
            {
                ExtensionsCollector.ExtensionType.TypeVariation => new Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionTypeVariation = new()
                    {
                        TypeVariationAnchor = anchor + 1,
                        ExtensionUrnReference = uriReference,
                        Name = extension.Name,
                    },
                },
                ExtensionsCollector.ExtensionType.Function => new Protobuf.SimpleExtensionDeclaration
                {
                    ExtensionFunction = new()
                    {
                        FunctionAnchor = anchor,
                        ExtensionUrnReference = uriReference,
                        Name = extension.Name,
                    },
                },
                _ => throw new NotImplementedException($"Extension type {extension.Type} is not supported."),
            };
        }));

        return result;
    }

    /// <summary>
    /// Stores intermediate converter outputs and collected extensions.
    /// </summary>
    public class ConverterContext : IContext<IRel, ProtoRel>, IContext<IExpression, ProtoExpression>, IContext<IType, ProtoType>
    {
        private readonly ExtensionsCollector.Builder extensions = new();
        private readonly Context<IExpression, ProtoExpression> expressions = new();
        private readonly Context<IRel, ProtoRel> relations = new();
        private readonly Context<IType, ProtoType> types = new();

        internal IReadOnlyList<IPlan.IRelation>? PlanRelations { get; init; }

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
        public int AddExtension(ExtensionsCollector.ExtensionType type, string uri, string name) =>
            this.extensions.Collect(type, uri, name);

        /// <inheritdoc/>
        public ProtoRel GetOutput(IRel node) => this.relations.GetOutput(node);

        /// <inheritdoc/>
        public void AddOutput(IRel node, ProtoRel output) => this.relations.AddOutput(node, output);

        /// <inheritdoc/>
        public void RemoveOutput(IRel node) => this.relations.RemoveOutput(node);

        /// <inheritdoc/>
        public ProtoExpression GetOutput(IExpression node) => this.expressions.GetOutput(node);

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
