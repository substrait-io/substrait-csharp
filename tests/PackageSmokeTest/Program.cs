// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Substrait.Core.Extension;
using Substrait.Core.Extension.Functions;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;
using Substrait.Protobuf;
using Substrait.Tools;

Substrait.Protobuf.Type protoType = new()
{
    I64 = new Substrait.Protobuf.Type.Types.I64
    {
        Nullability = Substrait.Protobuf.Type.Types.Nullability.Required,
    },
};

byte[] serialized = protoType.ToByteArray();
Substrait.Protobuf.Type parsed = Substrait.Protobuf.Type.Parser.ParseFrom(serialized);

if (parsed.KindCase != Substrait.Protobuf.Type.KindOneofCase.I64 ||
    TypeFactory.REQUIRED.I64.Nullable != IType.NullableType.Required)
{
    return 1;
}

if (!TypeFactory.REQUIRED.I64.Equals(TypeExpressionParser.Parse("i64")))
{
    throw new InvalidOperationException("The packaged type parser did not produce the expected type.");
}

ExtensionsCollection extensions = ExtensionUtils.LoadDefaults();
if (!extensions.TryGetScalarFunction(
    new FunctionImplAnchor("extension:io.substrait:functions_arithmetic", "add:i64_i64"),
    ExtensionsDictionary.StrictMode.STRICT,
    out _))
{
    throw new InvalidOperationException("The packaged arithmetic extension could not be resolved.");
}

NamedTableRead read = new(
    new Substrait.Core.Type.NamedStruct(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64])),
    ["orders"],
    null);
PlanBuilder builder = new();
Reference shared = builder.RegisterSubplan(read);
builder.AddRoot(shared, ["first"]);
builder.AddRoot(shared, ["second"]);
IPlan plan = builder.Build();
Substrait.Protobuf.Plan wire = new PlanToProtoConverter().From(plan);
IPlan roundTrip = new ProtoToPlanConverter().From(Substrait.Protobuf.Plan.Parser.ParseFrom(wire.ToByteArray()));

if (!plan.Equals(roundTrip) ||
    roundTrip.Relations.Count != 3 ||
    roundTrip.Roots.Count != 2 ||
    !ReferenceEquals(roundTrip.Relations[0].Input, ((Reference)roundTrip.Roots[0].Input).Target) ||
    !ReferenceEquals(((Reference)roundTrip.Roots[0].Input).Target, ((Reference)roundTrip.Roots[1].Input).Target))
{
    throw new InvalidOperationException("The packaged multi-relation plan API did not preserve shared references.");
}

return 0;
