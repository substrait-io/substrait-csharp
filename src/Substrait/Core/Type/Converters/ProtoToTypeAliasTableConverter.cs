// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Runtime.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoType = Substrait.Protobuf.Type;
using ProtoTypeAlias = Substrait.Protobuf.TypeAlias;

namespace Substrait.Core.Type.Converters;

/// <summary>
/// Converts protobuf type-alias declarations (<c>Plan.type_aliases</c>) to a validated
/// <see cref="TypeAliasTable"/>.
/// </summary>
public sealed class ProtoToTypeAliasTableConverter
{
    private readonly ProtoToTypeConverter typeConverter;

    /// <summary>Initializes a converter using the standard extensions.</summary>
    public ProtoToTypeAliasTableConverter()
        : this(new ProtoToTypeConverter())
    {
    }

    /// <summary>Initializes a converter whose aliased types share the given type converter's extension lookup.</summary>
    public ProtoToTypeAliasTableConverter(ProtoToTypeConverter typeConverter)
    {
        this.typeConverter = typeConverter;
    }

    /// <summary>Converts protobuf type-alias declarations to a validated alias table.</summary>
    /// <param name="aliases">The protobuf type-alias declarations, in any order.</param>
    /// <returns>The validated, immutable alias table.</returns>
    /// <exception cref="SerializationException">An entry's aliased type is unset, an entry's aliased
    /// type is otherwise malformed, or the resulting table is invalid: a duplicate anchor, a missing
    /// target, a direct alias-to-alias definition, a non-concrete definition, or a circular alias
    /// dependency.</exception>
    public TypeAliasTable From(IEnumerable<ProtoTypeAlias> aliases)
    {
        var builder = new TypeAliasTable.Builder();
        foreach (ProtoTypeAlias alias in aliases)
        {
            if (alias.Type is null || alias.Type.KindCase == ProtoType.KindOneofCase.None)
            {
                throw new SerializationException($"Alias anchor {alias.TypeAliasAnchor}: the aliased type is unset.");
            }

            IType definition;
            try
            {
                definition = this.typeConverter.From(WithSpecifiedRootNullability(alias.Type));
            }
            catch (Exception error) when (error is SerializationException or ArgumentException or NotImplementedException or NotSupportedException)
            {
                throw new SerializationException($"Alias anchor {alias.TypeAliasAnchor}: {error.Message}", error);
            }

            try
            {
                builder.Add(alias.TypeAliasAnchor, definition);
            }
            catch (ArgumentException error)
            {
                throw new SerializationException(error.Message, error);
            }
        }

        try
        {
            return builder.Build();
        }
        catch (ArgumentException error)
        {
            throw new SerializationException(error.Message, error);
        }
    }

    private static ProtoType WithSpecifiedRootNullability(ProtoType type)
    {
        FieldDescriptor kind = ProtoType.Descriptor.FindFieldByNumber((int)type.KindCase);
        var original = (IMessage)kind.Accessor.GetValue(type);
        FieldDescriptor? nullability = original.Descriptor.FindFieldByName("nullability");
        if (nullability is null || nullability.Accessor.GetValue(original) is not ProtoType.Types.Nullability.Unspecified)
        {
            return type;
        }

        IMessage root = original.Descriptor.Parser.ParseFrom(ByteString.Empty);
        foreach (FieldDescriptor field in original.Descriptor.Fields.InDeclarationOrder())
        {
            if (field.IsRepeated)
            {
                var target = (IList)field.Accessor.GetValue(root);
                foreach (object item in (IList)field.Accessor.GetValue(original))
                {
                    target.Add(item);
                }
            }
            else if (!field.HasPresence || field.Accessor.HasValue(original))
            {
                field.Accessor.SetValue(root, field.Accessor.GetValue(original));
            }
        }

        nullability.Accessor.SetValue(root, ProtoType.Types.Nullability.Required);
        var result = new ProtoType();
        kind.Accessor.SetValue(result, root);
        return result;
    }
}
