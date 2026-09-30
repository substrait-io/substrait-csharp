# Releasing substrait-csharp

Substrait C# releases use the same independent, weekly release model as the
Substrait Java and Python projects. The NuGet package version describes this
library; it does not mirror the version of the Substrait specification.

The specification version is pinned by the `Substrait.Net.Protobuf`,
`Substrait.Net.Antlr`, and `Substrait.Net.Extensions` dependencies in
`Directory.Packages.props`. These packages must use the same specification
release. A spec update is reviewed and merged as a normal dependency change,
and may cause a new C# release when it changes package behavior.

## One-time setup

Complete these steps before creating the first release tag.

1. Create or select the Substrait organization on NuGet.org and add at least two
   maintainers. The organization will own the `Substrait.Net` package.
2. In the NuGet.org account that will perform publishing, add a GitHub trusted
   publishing policy with these values:
   - owner: the Substrait NuGet organization
   - repository owner: `substrait-io`
   - repository: `substrait-csharp`
   - workflow file: `release.yml`
   - environment: `nuget.org`
   - package scope: exactly `Substrait.Net`, allowing new packages and new versions
3. Create a GitHub environment named `nuget.org`. Restrict deployments to tags
   matching `v*.*.*`, add required reviewers, and define the environment secret
   `NUGET_USER` as the NuGet.org profile name associated with the policy. Use the
   profile name, not an email address.
4. Make the Substrait release GitHub App available to this repository through
   the organization secrets `APP_CLIENT_ID` and `APP_PRIVATE_KEY`. The App needs
   repository Contents read/write permission so semantic-release can create tags
   and GitHub releases. Using the App token is required because tags created by
   the default `GITHUB_TOKEN` do not start another workflow.
5. Protect the `main` branch and require CI before merge.

No persistent NuGet API key is stored. `NuGet/login` exchanges the workflow's
GitHub OIDC token for a one-hour API key immediately before publishing.

## First release

Semantic-release intentionally waits for an initial `v0.1.0` tag, preventing an
untagged repository from defaulting to `1.0.0`. After the one-time setup and the
release workflow are on `main`, create and push an annotated `v0.1.0` tag from a
reviewed commit. The tag starts the Release and Publish workflow, which rebuilds,
tests, validates, and publishes `Substrait.Net.0.1.0.nupkg` and its symbol package.

Confirm the package, repository metadata, and symbols on NuGet.org before
enabling the weekly Semantic Release schedule.

## Normal releases

The Semantic Release workflow runs Sundays at 02:00 UTC and can also be started
manually. It analyzes Conventional Commits merged since the previous release,
creates a `vX.Y.Z` tag and GitHub release, and lets the tag-triggered workflow
publish the corresponding NuGet package.

- `fix:` produces a patch release.
- `feat:` produces a minor release.
- A breaking change produces a minor release while the project is pre-1.0.
- Other commit types do not publish a release by default.

NuGet packages are immutable. Never move or recreate a release tag, and do not
use `--skip-duplicate` to conceal a conflicting package build.