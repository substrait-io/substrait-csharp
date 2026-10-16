// SPDX-License-Identifier: Apache-2.0

using System.Runtime.Serialization;
using Google.Protobuf;
using Substrait.Core.Extension.Types;
using Substrait.Core.Type;
using Substrait.Core.Type.Converters;
using Substrait.Tools;
using TUnit.Assertions.Enums;
using NullableType = Substrait.Core.Type.IType.NullableType;
using ProtoType = Substrait.Protobuf.Type;

namespace Substrait.Tests.Core;

public sealed class ListAndMapTypeTests
{
    [Test]
    public async Task ListRoundTripsThroughProto()
    {
        IType list = TypeFactory.NULLABLE.List(TypeFactory.REQUIRED.I32);
        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(list);

        await Assert.That(proto.List.Nullability).IsEqualTo(ProtoType.Types.Nullability.Nullable);
        await Assert.That(proto.List.Type.I32).IsNotNull();

        foreach (var roundTripped in new[] { proto, ProtoType.Parser.ParseFrom(proto.ToByteArray()), ProtoType.Parser.ParseJson(proto.ToString()) })
        {
            IType actual = import.From(roundTripped);
            await Assert.That(actual).IsEqualTo(list);
            await Assert.That(actual.Equals(list, ITypeComparison.Strict)).IsTrue();
            await Assert.That(actual.GetHashCode()).IsEqualTo(list.GetHashCode());
            await Assert.That(export.From(actual)).IsEqualTo(proto);
        }
    }

    [Test]
    public async Task MapRoundTripsThroughProtoAndVisitsKeyBeforeValue()
    {
        IType map = TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, TypeFactory.NULLABLE.I64);
        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(map);

        await Assert.That(proto.Map.Nullability).IsEqualTo(ProtoType.Types.Nullability.Required);
        await Assert.That(proto.Map.Key.String).IsNotNull();
        await Assert.That(proto.Map.Value.I64.Nullability).IsEqualTo(ProtoType.Types.Nullability.Nullable);

        // InputNodes exposes [Key, Value], matching upstream's key-before-value visiting order.
        await Assert.That(map.InputNodes.ToList()).IsEquivalentTo(
            [((ParameterizedType.Map)map).KeyType, ((ParameterizedType.Map)map).ValueType], CollectionOrdering.Matching);

        foreach (var roundTripped in new[] { proto, ProtoType.Parser.ParseFrom(proto.ToByteArray()), ProtoType.Parser.ParseJson(proto.ToString()) })
        {
            IType actual = import.From(roundTripped);
            await Assert.That(actual).IsEqualTo(map);
            await Assert.That(actual.Equals(map, ITypeComparison.Strict)).IsTrue();
            await Assert.That(actual.GetHashCode()).IsEqualTo(map.GetHashCode());
            await Assert.That(export.From(actual)).IsEqualTo(proto);
        }
    }

    [Test]
    public async Task NestedStructListAndMapCombinationsRoundTrip()
    {
        // list<map<string, struct<i32, decimal<10,2>>>>, with an empty struct thrown in.
        IType innerStruct = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I32, TypeFactory.NULLABLE.Decimal(10, 2)]);
        IType map = TypeFactory.NULLABLE.Map(TypeFactory.REQUIRED.STR, innerStruct);
        IType list = TypeFactory.REQUIRED.List(map);
        IType withEmptyStruct = TypeFactory.REQUIRED.Struct([list, TypeFactory.REQUIRED.Struct([])]);

        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(withEmptyStruct);
        IType actual = import.From(proto);

        await Assert.That(actual).IsEqualTo(withEmptyStruct);
        await Assert.That(actual.Equals(withEmptyStruct, ITypeComparison.Strict)).IsTrue();
        await Assert.That(export.From(actual)).IsEqualTo(proto);
        await Assert.That(proto.Struct.Types_[0].List.Type.Map.Value.Struct.Types_.Count).IsEqualTo(2);
        await Assert.That(proto.Struct.Types_[1].Struct.Types_.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DeeplyNestedListOfListsRoundTripsWithoutRecursion()
    {
        IType deep = TypeFactory.REQUIRED.I32;
        for (int i = 0; i < 64; ++i)
        {
            deep = TypeFactory.REQUIRED.List(deep);
        }

        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();

        ProtoType proto = export.From(deep);
        IType actual = import.From(proto);

        await Assert.That(actual).IsEqualTo(deep);
    }

    [Test]
    public async Task DeeplyAlternatingListAndMapRoundTripsAndNamesWithoutStackOverflow()
    {
        // 1,500 alternating list/map levels around a leaf struct: deep enough that a recursive
        // traversal overflows the CLR call stack on .NET 8, even though each individual level is
        // trivial. Both the proto round trip and GetFieldNames must use an explicit stack instead.
        IType deep = TypeFactory.REQUIRED.Struct([TypeFactory.NULLABLE.I64]);
        for (int depth = 0; depth < 1500; ++depth)
        {
            deep = depth % 2 == 0
                ? TypeFactory.REQUIRED.List(deep)
                : TypeFactory.NULLABLE.Map(TypeFactory.REQUIRED.STR, deep);
        }

        var export = new TypeToProtoConverter();
        var import = new ProtoToTypeConverter();
        ProtoType proto = export.From(deep);
        IType actual = import.From(proto);
        await Assert.That(actual).IsEqualTo(deep);

        var schema = new NamedStruct(["outer", "leaf"], TypeFactory.REQUIRED.Struct([actual]));
        List<(IType Field, string Name)> fields = schema.GetFieldNames().ToList();
        await Assert.That(fields.Select(f => f.Name).ToList()).IsEquivalentTo(["outer", "leaf"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task RejectsListAndMapWithUnsetChildTypes()
    {
        ProtoType listMissingElement = new() { List = new ProtoType.Types.List { Nullability = ProtoType.Types.Nullability.Required } };
        ProtoType mapMissingKey = new()
        {
            Map = new ProtoType.Types.Map
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Value = new ProtoType { I32 = new ProtoType.Types.I32 { Nullability = ProtoType.Types.Nullability.Required } },
            },
        };
        ProtoType mapMissingValue = new()
        {
            Map = new ProtoType.Types.Map
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Key = new ProtoType { I32 = new ProtoType.Types.I32 { Nullability = ProtoType.Types.Nullability.Required } },
            },
        };
        ProtoToTypeConverter converter = new();

        await Assert.That(() => converter.From(listMissingElement)).ThrowsExactly<SerializationException>();
        await Assert.That(() => converter.From(mapMissingKey)).ThrowsExactly<SerializationException>();
        await Assert.That(() => converter.From(mapMissingValue)).ThrowsExactly<SerializationException>();

        // Nested under a struct, the error names the full path - including the enclosing struct
        // field - so the failure is locatable even without a user-defined type ancestor.
        ProtoType nested = new()
        {
            Struct = new ProtoType.Types.Struct { Nullability = ProtoType.Types.Nullability.Required, Types_ = { listMissingElement } },
        };
        SerializationException error = await Assert.That(() => converter.From(nested)).ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("struct field 0");
        await Assert.That(error.Message).Contains("list element");
    }

    [Test]
    public async Task RejectsPresentButEmptyChildTypesWithTheirPath()
    {
        // The child message is present (not null) but has no kind set - distinct from an absent
        // child message, and from a legitimate top-level unset type.
        ProtoType emptyElement = new() { List = new ProtoType.Types.List { Type = new ProtoType(), Nullability = ProtoType.Types.Nullability.Required } };
        ProtoType emptyKey = new()
        {
            Map = new ProtoType.Types.Map
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Key = new ProtoType(),
                Value = new ProtoType { I32 = new ProtoType.Types.I32 { Nullability = ProtoType.Types.Nullability.Required } },
            },
        };
        ProtoType emptyValue = new()
        {
            Map = new ProtoType.Types.Map
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Key = new ProtoType { I32 = new ProtoType.Types.I32 { Nullability = ProtoType.Types.Nullability.Required } },
                Value = new ProtoType(),
            },
        };
        ProtoToTypeConverter converter = new();

        await Assert.That(() => converter.From(emptyElement)).ThrowsExactly<SerializationException>();
        await Assert.That(() => converter.From(emptyKey)).ThrowsExactly<SerializationException>();
        await Assert.That(() => converter.From(emptyValue)).ThrowsExactly<SerializationException>();

        // Nested under an ordinary struct and list (no user-defined type involved), the error still
        // names the full enclosing path.
        ProtoType nested = new()
        {
            Struct = new ProtoType.Types.Struct
            {
                Nullability = ProtoType.Types.Nullability.Required,
                Types_ = { new ProtoType { List = new ProtoType.Types.List { Type = emptyKey, Nullability = ProtoType.Types.Nullability.Required } } },
            },
        };
        SerializationException error = await Assert.That(() => converter.From(nested)).ThrowsExactly<SerializationException>().And.IsNotNull();
        await Assert.That(error.Message).Contains("struct field 0");
        await Assert.That(error.Message).Contains("list element");
        await Assert.That(error.Message).Contains("map key");
    }

    [Test]
    public async Task RewritesNullabilityWithoutChangingChildTypes()
    {
        IType elementType = TypeFactory.NULLABLE.I32;
        IType list = TypeFactory.REQUIRED.List(elementType);
        IType rewritten = TypeFactory.NULLABLE.ResolveTypeWithNullability(list);

        await Assert.That(rewritten).IsEqualTo(TypeFactory.NULLABLE.List(elementType));
        await Assert.That(((ParameterizedType.List)rewritten).ElementType).IsEqualTo(elementType);

        IType keyType = TypeFactory.REQUIRED.STR;
        IType valueType = TypeFactory.NULLABLE.I64;
        IType map = TypeFactory.REQUIRED.Map(keyType, valueType);
        IType rewrittenMap = TypeFactory.NULLABLE.ResolveTypeWithNullability(map);

        await Assert.That(rewrittenMap).IsEqualTo(TypeFactory.NULLABLE.Map(keyType, valueType));
        await Assert.That(((ParameterizedType.Map)rewrittenMap).KeyType).IsEqualTo(keyType);
        await Assert.That(((ParameterizedType.Map)rewrittenMap).ValueType).IsEqualTo(valueType);
    }

    [Test]
    public async Task EqualityDistinguishesListAndMapByChildTypesNullabilityAndVariation()
    {
        var listVariation = new TypeVariationImpl("/types.yaml", "list", "custom", string.Empty, FunctionBehavior.INHERITS);
        IType list = TypeFactory.REQUIRED.List(TypeFactory.REQUIRED.I32);
        IType sameList = TypeFactory.REQUIRED.List(TypeFactory.REQUIRED.I32);
        IType differentElement = TypeFactory.REQUIRED.List(TypeFactory.NULLABLE.I32);
        IType differentNullability = TypeFactory.NULLABLE.List(TypeFactory.REQUIRED.I32);
        IType differentVariation = new ParameterizedType.List(TypeFactory.REQUIRED.I32, NullableType.Required, listVariation);

        await Assert.That(sameList).IsEqualTo(list);
        await Assert.That(sameList.GetHashCode()).IsEqualTo(list.GetHashCode());
        await Assert.That(list).IsNotEqualTo(differentElement);
        await Assert.That(list).IsNotEqualTo(differentNullability);

        // Direct Equals/GetHashCode ignore type variation (matching Struct/Decimal/etc.); only the
        // flexible ITypeComparison-based comparison accounts for it.
        await Assert.That(list.Equals(differentVariation, ITypeComparison.Strict)).IsFalse();
        await Assert.That(list.Equals(differentVariation, ITypeComparison.IgnoreTypeVariation)).IsTrue();

        var mapVariation = new TypeVariationImpl("/types.yaml", "map", "custom", string.Empty, FunctionBehavior.INHERITS);
        IType map = TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, TypeFactory.REQUIRED.I32);
        IType sameMap = TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, TypeFactory.REQUIRED.I32);
        IType differentKey = TypeFactory.REQUIRED.Map(TypeFactory.NULLABLE.STR, TypeFactory.REQUIRED.I32);
        IType differentValue = TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, TypeFactory.NULLABLE.I32);
        IType differentMapVariation = new ParameterizedType.Map(TypeFactory.REQUIRED.STR, TypeFactory.REQUIRED.I32, NullableType.Required, mapVariation);

        await Assert.That(sameMap).IsEqualTo(map);
        await Assert.That(sameMap.GetHashCode()).IsEqualTo(map.GetHashCode());
        await Assert.That(map).IsNotEqualTo(differentKey);
        await Assert.That(map).IsNotEqualTo(differentValue);
        await Assert.That(map.Equals(differentMapVariation, ITypeComparison.Strict)).IsFalse();
        await Assert.That(map.Equals(differentMapVariation, ITypeComparison.IgnoreTypeVariation)).IsTrue();

        // A list and a map are never equal to each other, regardless of comparison mode.
        await Assert.That(list.NodeEquals(map, ITypeComparison.IgnoreNullability)).IsFalse();
    }

    [Test]
    public async Task DefaultVisitorsFallBackForListAndMap()
    {
        var visitor = new RecordingDefaultVisitor();
        ParameterizedType.List list = TypeFactory.REQUIRED.List(TypeFactory.REQUIRED.I32);
        ParameterizedType.Map map = TypeFactory.REQUIRED.Map(TypeFactory.REQUIRED.STR, TypeFactory.REQUIRED.I32);

        list.Accept(visitor, 0);
        map.Accept(visitor, 0);

        await Assert.That(visitor.VisitedTypes).IsEquivalentTo(new IType[] { list, map });
    }

    [Test]
    public async Task GetFieldNamesSkipsListAndMapButPairsStructsReachedThroughThemWithNames()
    {
        // struct<i64, list<i64>, map<i64, i64>, i64>: the list/map elements are plain i64, so they
        // contribute no pairs of their own - only the 4 outer fields do.
        IType i64 = TypeFactory.REQUIRED.I64;
        IType listOfI64 = TypeFactory.REQUIRED.List(i64);
        IType mapOfI64 = TypeFactory.REQUIRED.Map(i64, i64);
        var simple = new NamedStruct(["a", "b", "c", "d"], TypeFactory.REQUIRED.Struct([i64, listOfI64, mapOfI64, i64]));
        await Assert.That(simple.GetFieldNames().ToList()).IsEquivalentTo(
            [(i64, "a"), (listOfI64, "b"), (mapOfI64, "c"), (i64, "d")], CollectionOrdering.Matching);

        // struct<struct<i64, i64>, i64>: the first field is itself a struct, so it is paired with its
        // own name ("a") and then its 2 inner fields are paired with the next 2 ("b", "c"); the
        // trailing i64 is paired with the last ("d").
        IType innerStruct = TypeFactory.REQUIRED.Struct([i64, i64]);
        var nested = new NamedStruct(["a", "b", "c", "d"], TypeFactory.REQUIRED.Struct([innerStruct, i64]));
        await Assert.That(nested.GetFieldNames().ToList()).IsEquivalentTo(
            [(innerStruct, "a"), (i64, "b"), (i64, "c"), (i64, "d")], CollectionOrdering.Matching);

        // struct<list<struct<i64, i64>>>: the one field (the list itself) is paired with its own name
        // ("a"); the list contributes no further pair of its own, but the struct it transparently
        // contains still pairs its 2 fields ("b", "c").
        IType listOfStruct = TypeFactory.REQUIRED.List(innerStruct);
        var structInList = new NamedStruct(["a", "b", "c"], TypeFactory.REQUIRED.Struct([listOfStruct]));
        await Assert.That(structInList.GetFieldNames().ToList()).IsEquivalentTo(
            [(listOfStruct, "a"), (i64, "b"), (i64, "c")], CollectionOrdering.Matching);

        // struct<map<struct<str, str>, struct<i64, i64, i64>>>: the one field (the map itself) is
        // paired with its own name ("a"); the key struct's 2 fields are then paired with names before
        // the value struct's 3 fields. Key and value use distinct leaf types (str vs i64) so that a
        // key/value traversal-order bug would show up as the wrong type at the wrong name, not just
        // the wrong count.
        IType str = TypeFactory.REQUIRED.STR;
        IType keyStruct = TypeFactory.REQUIRED.Struct([str, str]);
        IType valueStruct = TypeFactory.REQUIRED.Struct([i64, i64, i64]);
        IType mapOfStructs = TypeFactory.REQUIRED.Map(keyStruct, valueStruct);
        var structInMap = new NamedStruct(["a", "b", "c", "d", "e", "f"], TypeFactory.REQUIRED.Struct([mapOfStructs]));
        await Assert.That(structInMap.GetFieldNames().ToList()).IsEquivalentTo(
            [(mapOfStructs, "a"), (str, "b"), (str, "c"), (i64, "d"), (i64, "e"), (i64, "f")], CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetFieldNamesRejectsMismatchedNameCounts()
    {
        var struct_ = TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64, TypeFactory.REQUIRED.I64]);

        var tooFew = new NamedStruct(["a"], struct_);
        await Assert.That(() => tooFew.GetFieldNames().ToList()).ThrowsExactly<ArgumentException>();

        var tooMany = new NamedStruct(["a", "b", "c"], struct_);
        await Assert.That(() => tooMany.GetFieldNames().ToList()).ThrowsExactly<ArgumentException>();
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
