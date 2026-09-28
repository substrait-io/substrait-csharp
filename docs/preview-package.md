# Preview package status

`Substrait.Net` 0.1 previews are evaluation packages. Continuous
integration produces `.nupkg` and `.snupkg` workflow artifacts, but does not
publish them to a package feed.

## API baseline

The Roslyn Public API analyzer records the current surface in
`src/Substrait/PublicAPI.Unshipped.txt`. `PublicAPI.Shipped.txt` remains empty for
the first preview, so no API is declared stable yet. Moving APIs into the shipped
baseline requires a separate review after package-consumer feedback. Until then,
preview releases may contain breaking API changes.

## Compatibility scope

- The package targets `net10.0`, `net8.0`, and `netstandard2.0`. .NET 9
  consumers use the compatible `net8.0` package asset.
- Generated protobuf bindings, ANTLR parsers, and embedded extension definitions
  come from `Substrait.Net.Protobuf`, `Substrait.Net.Antlr`, and
  `Substrait.Net.Extensions` version 0.104.0.
- All targets also depend on `Antlr4.Runtime.Standard`, `Google.Protobuf`, and
  `YamlDotNet`. The `netstandard2.0` target also uses `IndexRange`,
  `Microsoft.Bcl.HashCode`, `System.Collections.Immutable`, and `System.Memory`
  compatibility packages.
- Product-specific integrations, schemas, and test assets remain outside the
  public package.

Package validation covers metadata, dependencies, license expression, README,
symbols, Source Link, an SPDX 2.2 SBOM, and standalone package consumers on
.NET 8, .NET 9, and .NET 10 across Windows, Linux, and macOS. Windows also
executes the `netstandard2.0` package asset from a .NET Framework 4.6.2
consumer. When organization policy blocks public package downloads on developer
machines, the hosted CI jobs are the authoritative clean-restore result.

## Remaining gate

Behavioral comparison with downstream consumers using an approved compatibility
corpus is still required before package adoption. Existing non-public fixture
collections cannot be copied into this repository without the provenance review
described in `tests/README.md`.

## Migration to specification packages

Replacing the v0.73.0 submodule with the 0.104.0 packages changes the preview API
and wire compatibility:

- Generated protobuf types retain the `Substrait.Protobuf` namespace but move
  from `Substrait.Net.dll` to `Substrait.Net.Protobuf.dll`. Consumers must
  recompile. The packages flow transitively from `Substrait.Net`.
- ANTLR types now use `Substrait.Antlr.SubstraitType` in
  `Substrait.Net.Antlr.dll`, replacing `Substrait.Antlr.Type`.
- Plans use `ExtensionUrns`, `SimpleExtensionURN`, and
  `ExtensionUrnReference`. URI-only plans must be migrated before conversion;
  the new protobuf package no longer exposes the removed URI fields. Internal
  API names such as `Uri` and `ExtensionUris` are retained, but their values
  must now be extension URNs when serializing plans.
- Default extensions use their declared namespaces, such as
  `extension:io.substrait:functions_arithmetic`, rather than
  `/functions_arithmetic.yaml`. The same nine function families are loaded.
  The old `type_variations.yaml` catalog is no longer published upstream, so
  default type variations are empty. Custom type variations can still be loaded
  explicitly. Function implementation deprecation metadata is preserved.
- The existing microsecond `Time` model is serialized as `precision_time<6>`;
  other time precisions are rejected rather than rescaled or truncated.
  Day intervals use explicit precision 0 (whole seconds); fractional or
  unspecified-precision interval types are rejected.
- Offset-based outer references remain supported using the deprecated
  `steps_out` representation. Relation-anchor outer references are explicitly
  unsupported by the internal model.

The package upgrade does not imply support for every newer specification
feature in the internal representation. Unsupported grammar constructs and
protobuf variants continue to report errors.