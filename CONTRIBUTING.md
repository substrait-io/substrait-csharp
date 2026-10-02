# Contributing

## Prerequisites

Install the .NET 10 SDK and Git. The repository's
`global.json` pins the required SDK feature band.

Clone the repository:

```shell
git clone https://github.com/substrait-io/substrait-csharp.git
cd substrait-csharp
```

NuGet restore supplies the specification's generated protobuf bindings, ANTLR
parsers, and extension definitions. No local code generation is required.

## Restore, format, build, and test

Run the same validation used by continuous integration:

```shell
dotnet restore Substrait.sln
dotnet run --project tools/Substrait.MetadataGenerator --configuration Release --no-restore -- --check
dotnet format Substrait.sln --verify-no-changes --no-restore
dotnet build Substrait.sln --configuration Release --no-restore
dotnet test Substrait.sln --configuration Release --no-build
```

## Package validation

Continuous integration creates a run-scoped preview package, validates its
metadata, symbols, Source Link mappings, and SPDX SBOM, and tests the package
from standalone consumers. Pull-request and branch builds upload the artifacts
for review but do not publish them. Tagged releases publish through NuGet trusted
publishing as described in [RELEASING.md](RELEASING.md). See
[Preview package status](docs/preview-package.md) for the current compatibility
scope.

For local package validation, pack the project and run
`eng/Validate-Package.ps1` with the package, symbols package, and expected
version. The CI workflow is the authoritative reference for the complete
commands and cross-platform package-consumer matrix.

## Updating Substrait

Update `Substrait.Net.Protobuf`, `Substrait.Net.Antlr`, and
`Substrait.Net.Extensions` together in `Directory.Packages.props` to the same
reviewed specification release. Align direct runtime dependencies with the
packages' minimum versions. Update the package dependency expectations in
`eng/Validate-Package.ps1` and the related tests and compatibility documentation.
`CurrentVersion` automatically reads the specification version from the
`Substrait.Net.Protobuf` assembly metadata; no source generation or manual
version update is needed. Its Git hash comes from the package's `SubstraitGitHash`
assembly metadata when available; older packages without that metadata leave it
empty.

Regenerate the read-only metadata facades as described below when updating the
protobuf package. Review the generated diff and update the public API baseline
explicitly; regeneration does not approve new public APIs.

Run the solution tests and standalone package smoke tests: upstream releases
can change generated APIs, grammar visitors, and extension YAML. Keep fixture
provenance requirements in `tests/README.md` in mind when adding or updating test
data.

## Generated metadata facades

The checked-in files under `src/Substrait/Core/Metadata/Generated` are produced
by `tools/Substrait.MetadataGenerator`. Do not edit them manually. The generator
references the same centrally pinned protobuf package as the library and starts
from the `RelCommon` and `AdvancedExtension` descriptors, following only their
message-field dependencies. It does not reference the core library or run during
normal library compilation.

```shell
dotnet run --project tools/Substrait.MetadataGenerator -- --write
dotnet run --project tools/Substrait.MetadataGenerator -- --check
```

`--write` updates missing or stale generated files. `--check` verifies exact,
deterministic output without modifying files and fails on missing, stale, or
unexpected files. Both commands reject unexpected files in the generated
directory; explicitly remove obsolete generated files after reviewing a schema
update. Unsupported field shapes fail generation rather than being omitted.
Generator tests target .NET 10; facade tests run on both .NET 8 and .NET 10.

Generation uses protobuf field descriptors and verified CLR names. Emitted
properties access typed members directly, with no runtime reflection. Public
imports and exports copy mutable protobuf data. Internal `FromOwnedProto`
factories must only receive privately owned messages whose entire mutable graph
will never subsequently be modified or exposed. Nested facades share that owned
graph without further cloning. See [metadata facades](docs/metadata-facades.md)
for the public API and its scope.

## Public API changes

The Public API analyzer tracks the package surface in
`src/Substrait/PublicAPI.Shipped.txt` and
`src/Substrait/PublicAPI.Unshipped.txt`. Add new APIs to the unshipped baseline.
Do not remove or change shipped APIs without explicitly documenting and
reviewing the breaking change. Include tests and documentation for public API
changes.

## License headers and attribution

Use an SPDX-only header for independently written new C# files:

```csharp
// SPDX-License-Identifier: Apache-2.0
```

Preserve existing copyright notices in donated code and in files copied,
extracted, moved, or adapted from it. A new filename or a later commit does not
establish independent authorship. Check the source and PR history before
changing attribution; do not add Microsoft attribution solely to match nearby
files.

The formatter intentionally does not enforce a single file-header template:
both SPDX-only headers and headers retaining inherited copyright notices are
valid. Review header changes for provenance rather than normalizing all files
to one form.

## Pull requests

Use the pull request template and keep each change focused. Explain its
motivation, compatibility and public API impact, validation performed, and any
package or upstream specification compatibility considerations. Open an issue before
starting significant changes so the approach can be discussed.

Pull requests are squash-merged. Use Conventional Commit syntax in the pull
request title because semantic-release derives package versions and release
notes from the resulting commit on `main`.
