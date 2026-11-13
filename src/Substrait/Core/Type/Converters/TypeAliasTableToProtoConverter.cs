// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Plan.Converters;
using ProtoTypeAlias = Substrait.Protobuf.TypeAlias;

namespace Substrait.Core.Type.Converters;

/// <summary>
/// Converts a <see cref="TypeAliasTable"/> to protobuf type-alias declarations (<c>Plan.type_aliases</c>).
/// </summary>
public sealed class TypeAliasTableToProtoConverter
{
    private readonly TypeToProtoConverter typeConverter;

    /// <summary>Initializes a converter using a new type converter.</summary>
    public TypeAliasTableToProtoConverter()
        : this(new TypeToProtoConverter())
    {
    }

    /// <summary>Initializes a converter whose aliased types share the given type converter.</summary>
    public TypeAliasTableToProtoConverter(TypeToProtoConverter typeConverter)
    {
        this.typeConverter = typeConverter;
    }

    /// <summary>Converts a table using a new conversion context.</summary>
    public IReadOnlyList<ProtoTypeAlias> From(TypeAliasTable table) => this.From(table, new PlanToProtoConverter.ConverterContext());

    /// <summary>
    /// Converts a table using a shared conversion context, so extension anchors referenced inside
    /// alias definitions are collected into the same plan-wide extension declaration table as the
    /// rest of the plan.
    /// </summary>
    public IReadOnlyList<ProtoTypeAlias> From(TypeAliasTable table, PlanToProtoConverter.ConverterContext context)
    {
        var result = new List<ProtoTypeAlias>(table.Count);
        foreach (KeyValuePair<uint, IType> entry in table.Definitions)
        {
            result.Add(new ProtoTypeAlias
            {
                TypeAliasAnchor = entry.Key,
                Type = this.typeConverter.From(entry.Value, context),
            });
        }

        return result;
    }
}
