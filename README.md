<!--
Copyright (c) Microsoft Corporation
SPDX-License-Identifier: Apache-2.0
-->

# substrait-csharp

Experimental C# bindings for [substrait](https://substrait.io).

## Development

The repository requires a .NET 10 SDK. The checked-in `global.json` selects a
compatible installed SDK.

Clone the repository:

```shell
git clone https://github.com/substrait-io/substrait-csharp.git
```

Restore, build, and test the solution from the repository root:

```shell
dotnet restore Substrait.sln
dotnet build Substrait.sln --configuration Release --no-restore
dotnet test Substrait.sln --configuration Release --no-build
```

Create the NuGet package locally with:

```shell
dotnet pack src/Substrait/Substrait.csproj --configuration Release --output artifacts/packages
```

The package ID and managed assembly name are `Substrait.Net`, and
public namespaces remain under `Substrait`.

## Preview package validation

Pull-request and branch continuous integration creates a run-scoped prerelease package, validates its
NuGet metadata and Source Link mappings, restores it into a standalone consumer,
and generates an SPDX 2.2 SBOM. The `.nupkg`, `.snupkg`, and SBOM are uploaded as
workflow artifacts for review. Tagged releases publish to NuGet.org through
trusted publishing; see [RELEASING.md](RELEASING.md).

The package consumer has no project reference to the library. To exercise it
locally after packing a preview version, restore from the package output plus a
public source and then run it:

```shell
dotnet restore tests/PackageSmokeTest/PackageSmokeTest.csproj -p:SmokeTestPackageVersion=0.1.0-preview.1 --configfile tests/PackageSmokeTest/NuGet.Config --no-cache
dotnet run --project tests/PackageSmokeTest/PackageSmokeTest.csproj --configuration Release --no-restore -p:SmokeTestPackageVersion=0.1.0-preview.1
```

When local network policy blocks public package downloads, the Windows and Linux
CI jobs are the authoritative package restore and consumer validation.

See [Preview package status](docs/preview-package.md) for the API stability and
compatibility scope of the package artifacts.

The package and assembly identity remain provisional until the first package
preview.

The specification is supplied by `Substrait.Net.Protobuf`,
`Substrait.Net.Antlr`, and `Substrait.Net.Extensions` NuGet packages, currently
version **0.104.0**. No submodule, protobuf compiler, or Java/ANTLR generation
tool is needed. Versions are pinned in `Directory.Packages.props`; see
[Updating Substrait](CONTRIBUTING.md#updating-substrait) for upgrade instructions
and [compatibility notes](docs/preview-package.md#migration-to-specification-packages)
for changes from the previously bundled v0.73.0 specification.

## Conversion and serialization

Use `ProtoToPlanConverter` and `PlanToProtoConverter` to convert between
generated protobuf plans and the immutable internal representation. Extension
references can be resolved strictly or selectively with
`ExtensionsDictionary.StrictMode`; non-strict conversion preserves unresolved
function references but cannot attach their declarations.

`FileUtils` reads and writes protobuf binary and protobuf JSON plan files.
Converting a plan does not add nondeterministic metadata, so repeated protobuf
serialization of the same internal plan produces the same bytes.

Plans can contain ordered mixtures of roots and reusable non-root relations.
Use `PlanBuilder` to assign reference ordinals and safely share a registered
subplan across outputs:

```csharp
using Substrait.Core.Expression;
using Substrait.Core.Plan;
using Substrait.Core.Plan.Converters;
using Substrait.Core.Relation;
using Substrait.Core.Type;

var schema = new NamedStruct(
    ["order_id"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]));
var orders = new NamedTableRead(schema, ["orders"], filter: null);

var builder = new PlanBuilder();
Reference sharedOrders = builder.RegisterSubplan(orders); // Entry 0, not an output.
builder.AddRoot(sharedOrders, ["all_order_ids"]);         // Entry 1.
builder.AddRoot(
    new Project(sharedOrders, [new Literal.I64Literal(1)]),
    ["order_id", "marker"]);                             // Entry 2.

Plan plan = builder.Build();
Substrait.Protobuf.Plan protobuf = new PlanToProtoConverter().From(plan);
IPlan roundTrip = new ProtoToPlanConverter().From(protobuf);
```

`IPlan.Relations` is the authoritative ordered list, including non-root entries;
`Roots` is only its root projection. References point at entry ordinals, not
root indexes, and each registration creates a new entry without deduplication.
`Build()` returns an immutable snapshot; later registrations do not change
earlier plans. References returned by a builder belong to that builder.

Empty plans remain constructible and deserializable, but cannot be serialized.
See [multi-relation API migration](docs/preview-package.md#multi-relation-plans-and-references)
for ordering, advanced composition, validation, and correlation limitations.

## Contributing

Here are some ways you can contribute to the substrait-csharp project:

* Submit PRs to fix bugs or add new features.
* Review currently [open PRs](https://github.com/substrait-io/substrait-csharp/pulls).
* Provide feedback and report bugs related to the software or the documentation.
* Enhance our design documents, examples, tutorials, and overall documentation.

To get started, read the [contribution guide](CONTRIBUTING.md), then take a look
at the [issues](https://github.com/substrait-io/substrait-csharp/issues) and
leave a comment if any of them interest you.

If you plan to make significant changes, open an
[issue](https://github.com/substrait-io/substrait-csharp/issues) to discuss them
with the substrait-csharp community first.
This helps ensure that your contributions align with the project's goals and avoids duplicating efforts.

## Contributor License Agreement

Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

## Code of Conduct

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## License

See the [LICENSE](LICENSE) file for more details.
