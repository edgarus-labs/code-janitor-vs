# Development Guide

## Requirements

Development requires:

- Windows;
- Visual Studio with the Visual Studio extension development workload;
- the SDKs and build tools required by the solution;
- access to the `develop` branch and the repository history.

The exact supported Visual Studio version is determined by the current solution and VSIX manifest.

## Getting started

1. Clone the repository.
2. Check out `develop`.
3. Open the solution in Visual Studio.
4. Restore dependencies.
5. Build the solution.
6. Run the extension in an experimental Visual Studio instance.
7. Execute the unit tests.
8. Verify the relevant feature manually when changing Visual Studio integration or deployment behavior.

## Testing principles

Changes to cleaning and formatting behavior should include regression tests whenever practical.

Particular care is required for:

- Razor and HTML boundaries;
- C# embedded in Razor;
- preprocessor directives;
- comments and string literals;
- line endings and indentation;
- idempotence, meaning that running a formatter twice produces the same result as running it once;
- files that the formatter cannot safely parse.

## Shared transformation corpus

Behavior that must stay in lockstep with the VS Code extension lives in `shared/tests/transformations` as JSON fixtures (input, settings, required and forbidden output substrings). The same files are executed by `SharedTransformationCorpusTests` (MSTest) here and by `test/sharedTransformationCorpus.test.ts` (Vitest) in the VS Code repository. When a change alters shared cleanup behavior, add or update a fixture instead of writing two disconnected native tests; when the implementations legitimately diverge (Roslyn semantic analysis vs. the VS Code lexical parser), document the divergence in the fixture description or leave the case out of the corpus.

## Experimental hive

For changes to cleanup preview, follow the explicit
[Visual Studio verification checklist](cleanup-preview.md#visual-studio-verification-checklist).
Its native diff host and editor integration cannot be validated by the headless
unit tests alone.

Visual Studio extension changes should be tested in an experimental instance before being considered ready. This is especially important for:

- package registration;
- command registration;
- settings registration;
- VSIX manifests;
- `pkgdef` generation;
- deployment scripts.

## Code scanning

`.github/workflows/codeql.yml` runs GitHub CodeQL on pull requests to `develop`, on pushes to
`develop` and weekly (Monday 03:27 UTC). It scans:

- C# sources (`csharp`, build mode `none`, so no Visual Studio build is needed);
- GitHub Actions workflows (`actions`).

Results are uploaded to the repository's **Security → Code scanning** page. The job only has
`contents: read` and `security-events: write` permissions. CodeQL does not change the Build VSIX
workflow.

## Pull requests

Pull requests should target `develop` and explain:

- the problem;
- the design;
- compatibility impact;
- test coverage;
- documentation changes;
- any changes to inherited CodeMaid behavior.

See [CONTRIBUTING.md](../CONTRIBUTING.md).
