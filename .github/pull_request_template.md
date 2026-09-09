Thank you for submitting a pull request to the Substrait project. Please keep
your description clear and concise for reviewers.

## Change Summary [REQUIRED]

Summarize the change. The pull request title and summary should follow
[Conventional Commits](https://www.conventionalcommits.org/).

## Motivation [REQUIRED]

Link the motivating issue or explain the problem this change addresses.

## Reviewer Context [OPTIONAL]

Describe important invariants, design decisions, or non-obvious constraints.

## Validation [REQUIRED]

- [ ] `dotnet format Substrait.sln --verify-no-changes --no-restore`
- [ ] `dotnet build Substrait.sln --configuration Release --no-restore`
- [ ] `dotnet test Substrait.sln --configuration Release --no-build`

## Public API and compatibility [REQUIRED]

Describe any public API, serialization, package, or compatibility impact. Write
`None` if there is no impact.

## Breaking changes [REQUIRED]

Describe breaking changes using `BREAKING CHANGE:` footers, or write `None`.
