# Development Guide

## Requirements

Development requires:

- Windows;
- Visual Studio 2026 with the Visual Studio extension development workload (the extension binds to the Roslyn that Visual Studio ships, so the experimental instance must be Visual Studio 2026 too);
- the SDKs and build tools required by the solution;
- access to the `develop` branch and the repository history.

The supported Visual Studio range is defined by `src/CodeJanitor/source.extension.vsixmanifest` (`[18.0, 19.0)`); keep it, the README requirements and `docs/features.md` in sync. The product references Microsoft.CodeAnalysis 5.0.0, the Roslyn of Visual Studio 18.0; raising that reference raises the lowest Visual Studio version that can load the extension, so change both together.

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

### Automated Experimental-instance checks

`scripts/test-exp-integration.ps1` runs the commands the unit tests cannot reach in the Experimental instance, unattended:
it installs the VSIX with `scripts/deploy-exp.ps1`, creates a temporary solution (dirty C# fixtures, `.editorconfig`,
`.codejanitor`, a solution-specific `CodeJanitor.config` so the user's settings file is never written, a git repository),
starts `devenv.exe /rootsuffix Exp` and drives it through the DTE. It verifies by file bytes, editor buffers and
`dotnet build`: Cleanup Active Document (edited unsaved buffer), Cleanup Open Code, Cleanup Selected Code, Cleanup All
Code, Cleanup Changed Files (git), Automatic Cleanup On Save, Fix Namespace, Remove All Regions, Reorganize, Sort and Join
Lines, the Options command, the external file prompt, read-only files, line endings and byte order mark preservation, undo
after cleanup, and idempotence (a second run changes nothing). Modal dialogs are found with Win32 window enumeration of
the Experimental process and answered without mouse or keyboard input (`BM_CLICK` for message boxes, UI Automation
`InvokePattern` for WPF dialogs).

```powershell
powershell -STA -File scripts\test-exp-integration.ps1                       # deploy, then run everything
powershell -STA -File scripts\test-exp-integration.ps1 -SkipDeploy -Scenario ActiveDocument,Undo
```

It prints one `PASS`/`FAIL` line per check and exits with 0 only when all checks pass. It stops only the Experimental
`devenv.exe` it started (never another Visual Studio session), deletes its temporary directory (`-KeepWorkspace` keeps it)
and holds the `Global\cj-build-test` mutex of the unit test runner while deploying. It needs Visual Studio 2026, `dotnet`
and `git` on the path, and a desktop session, so it is a local release check, not a CI job.

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
