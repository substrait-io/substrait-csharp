# Contributing

## Prerequisites

Install the .NET 10 SDK and Git with submodule support. The repository's
`global.json` pins the required SDK feature band.

Clone the repository and its pinned Substrait specification:

```shell
git clone --recurse-submodules https://github.com/substrait-io/substrait-csharp.git
cd substrait-csharp
```

For an existing clone, initialize all submodules:

```shell
git submodule update --init --recursive
```

## Restore, format, build, and test

Run the same validation used by continuous integration:

```shell
dotnet restore Substrait.sln
dotnet format Substrait.sln --verify-no-changes --no-restore
dotnet build Substrait.sln --configuration Release --no-restore
dotnet test Substrait.sln --configuration Release --no-build
```

## Package validation

Continuous integration creates a run-scoped preview package, validates its
metadata, symbols, Source Link mappings, and SPDX SBOM, and tests the package
from standalone consumers. It uploads the artifacts for review but does not
publish them. See [Preview package status](docs/preview-package.md) for the
current compatibility scope.

For local package validation, pack the project and run
`eng/Validate-Package.ps1` with the package, symbols package, and expected
version. The CI workflow is the authoritative reference for the complete
commands and cross-platform package-consumer matrix.

## Updating Substrait

`third_party/substrait` pins the upstream specification used to generate
protobuf and type-parser code. Check out a reviewed upstream revision in that
directory, regenerate affected sources as appropriate, and commit the submodule
pointer with the resulting changes. Keep fixture provenance requirements in
`tests/README.md` in mind when adding or updating test data.

## Public API changes

The Public API analyzer tracks the package surface in
`src/Substrait/PublicAPI.Shipped.txt` and
`src/Substrait/PublicAPI.Unshipped.txt`. Add new APIs to the unshipped baseline.
Do not remove or change shipped APIs without explicitly documenting and
reviewing the breaking change. Include tests and documentation for public API
changes.

## Pull requests

Use the pull request template and keep each change focused. Explain its
motivation, compatibility and public API impact, validation performed, and any
package, generated-code, or submodule considerations. Open an issue before
starting significant changes so the approach can be discussed.
