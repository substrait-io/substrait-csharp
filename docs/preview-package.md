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
- Generated protobuf bindings and embedded extension definitions correspond to
  the Substrait v0.73.0 source pinned by the repository submodule.
- Both targets depend on `Antlr4.Runtime.Standard`, `Google.Protobuf`, and
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