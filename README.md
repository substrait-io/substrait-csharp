<!--
{% comment %}
Copyright (c) Microsoft Corporation.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
{% endcomment %}
-->

# substrait-csharp

Experimental C# bindings for [substrait](https://substrait.io).

## Development

The repository requires a .NET 10 SDK. The checked-in `global.json` selects a
compatible installed SDK.

Clone the repository with its pinned Substrait specification sources:

```shell
git clone --recurse-submodules https://github.com/substrait-io/substrait-csharp.git
```

For an existing clone, initialize the submodule with:

```shell
git submodule update --init --recursive
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

## Preview package validation

Continuous integration creates a run-scoped prerelease package, validates its
NuGet metadata and Source Link mappings, restores it into a standalone consumer,
and generates an SPDX 2.2 SBOM. The `.nupkg`, `.snupkg`, and SBOM are uploaded as
workflow artifacts for review. The workflow does not publish to NuGet.org or any
other package feed.

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

The `third_party/substrait` submodule pins the upstream specification used to
generate protobuf and type-parser code. To upgrade it, check out the desired
upstream release in that directory and commit the updated submodule pointer.

## Conversion and serialization

Use `ProtoToPlanConverter` and `PlanToProtoConverter` to convert between
generated protobuf plans and the immutable internal representation. Extension
references can be resolved strictly or selectively with
`ExtensionsDictionary.StrictMode`; non-strict conversion preserves unresolved
function references but cannot attach their declarations.

`FileUtils` reads and writes protobuf binary and protobuf JSON plan files.
Converting a plan does not add nondeterministic metadata, so repeated protobuf
serialization of the same internal plan produces the same bytes.

Plan conversion currently supports exactly one relation root. Empty internal
plans and plans with multiple roots cannot be serialized, and protobuf plans
with multiple relations cannot be converted to the internal representation.

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
