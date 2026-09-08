# Feature Delivery Plan

Scope: the feature proposals discussed on 2026-09-08. Deliver sequentially;
update the README, feature help and verification notes with each usable increment.
No unrelated refactoring, deployment, commits, staging or external AI calls.

## Steps

1. **Preview Cleanup** (in progress). Compute a read-only plan, inspect before/after
   text, exclude files/rules, apply only approved results and reject stale input.
   Begin with the deterministic C# text pipeline. Editor-only operations, AI and
   type splitting must not be silently executed outside the preview.
   Gate: plan/run equivalence, exclusions, no writes before approval, stale-input
   protection, build and manual Visual Studio dialog verification.
2. **Explain Cleanup** (planned). Per-rule outcomes and skip reasons, effective
   configuration provenance and non-blocking configuration diagnostics.
   Gate: outcome/provenance tests and user-facing report.
3. **Clean Branch Changes** (planned). Working tree, staged-file selection and
   changes since merge-base with a chosen branch, without modifying the index.
   Changed-fragment formatting is a separate increment restricted to compatible
   rules. Gate: Git argument/path tests and temporary-repository scenarios.
4. **Cleanup Profiles** (planned). Named settings for On Save, Before Commit,
   Modernize C# and Documentation Only; repository persistence and command choice.
   Gate: round-trip, precedence and restoration tests; no implicit AI on save.
5. **Verify Generated Tests** (planned). First import measured coverage reports;
   then explicit-consent build/test execution and bounded AI follow-up proposals.
   Clearly distinguish estimates from measurements. Gate: report parsing and
   project/test-runner validation; no unapproved code execution.
6. **Check Cleanup / CLI** (planned). Reuse the preview plan for a no-write check
   in Visual Studio, then expose supported deterministic rules outside the shell.
   Gate: no-write checks, exit codes, documented rule parity and CI examples.
7. **Blazor maintenance diagnostics** (planned). Component/code-behind namespace
   and isolated CSS checks with navigation; corrections only after diagnostics.
   Gate: positive/negative Razor fixtures and manual navigation verification.
8. **Complexity navigation** (planned). Extend Spade with complex-method ranking
   and changes relative to the branch baseline. Gate: metric/baseline tests.
9. **AI context control** (planned). Shared outbound-context and recipient preview
   plus repository path restrictions across AI commands. Gate: exclusion and
   consent tests with fake clients; no live requests during automated testing.

## Delivery Rules

- A tested foundation is not a completed user-facing feature.
- Document limitations of each increment instead of claiming full parity.
- Run focused tests after each code increment; run the full suite when changing
  shared cleanup behavior. Record manual VS checks separately from unit tests.
- Preserve existing uncommitted XMLDoc/client/options changes.

## Step 1 Progress (2026-09-08)

- Implemented: shared plan/run execution, immutable before/after snapshots,
   rule outcomes and exclusions, selected-scope preview entry point, per-file
   selection, native read-only diff with text fallback, stale-buffer rejection,
   approved-result application to editors without automatic saving.
- Verified: Debug MSBuild with deployment/VSIX packaging disabled; 18 focused
   pipeline/preview tests passed; full VSTest suite passed (721/721, zero skipped).
   The full suite also includes existing uncommitted work; this is not a claim
   that all test-count growth belongs to preview.
- Updated: README, features, preview help, documentation index, architecture,
   development guide, roadmap and changelog.
- Pending: [Visual Studio runtime checklist](../cleanup-preview.md#visual-studio-verification-checklist).
   With explicit user approval, the VSIX was built and installed only in
   `18.0_ec255184Exp`, then Visual Studio was launched with `/RootSuffix Exp`.
   The installer returned 0 and logged successful installation; installed and
   built `CodeJanitor.dll` SHA-256 hashes match. No main-instance deployment,
   forced shutdown, hive reset or cache deletion was performed. Interactive
   native diff, WPF and Undo verification still requires the runtime checklist.
- Remaining Step 1 increments: cancellable preparation for large selections,
   explicit planning of type-split file creation and broader editor-operation
   coverage. These must not be claimed as supported by C# text preview.
- Steps 2-9 remain planned; the basic rule outcomes in preview are only the
   foundation for Step 2, not complete configuration provenance/diagnostics.

### Experimental load investigation

The first interactive launch reported `ContainersToolsPackage` initialization
failure (`0x80131500`) and later `CodeJanitorPackage` failure (`0x80131513`).
The contemporaneous Code Janitor diagnostic recorded a missing EnvDTE
`DocumentClosingEventHandler` constructor and source paths from an older build.
An installed-file hash alone therefore did not establish which package was loaded.

Inspection found two registrations of package GUID
`D731B062-E78E-4EFB-A626-245702F3A6A0`, belonging to different extension identities:
the old `0.1` installation and the new `0.1.0.55` VSIX. With explicit user approval,
only `Extensions/John Doe/CodeJanitor/0.1` was moved out of the Exp scan path to
`%LOCALAPPDATA%/CodeJanitor/ExpBackups/legacy-0.1-20260908-102616`.
Exp `/UpdateConfiguration` returned 0, and a new Exp launch was started with
`%TEMP%/codejanitor-preview-exp-retest.xml`. No other extensions, caches or main
instance settings were changed. Package-load recovery and the separate Container
Tools failure must be verified in that fresh log, not assumed from configuration
refresh success.

Retest result: the fresh log records `Begin package load [CodeJanitorPackage]`
at 08:27:17 UTC and `End package load [CodeJanitorPackage]` at 08:27:18 UTC,
with no new Code Janitor initialization error file. This confirms successful
package initialization after the registration conflict was isolated. Container
Tools was not observed loading in this retest, so its recovery is unverified.
The Visual Studio extension auto-update scheduler still reports an
`ArgumentOutOfRangeException`; no changes were made to that separate component.
Interactive preview rendering and Undo checks remain pending.