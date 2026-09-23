# Changelog

This file records changes made in Code Janitor after the project became an independently maintained fork of CodeMaid.

## Unreleased

### Added

- Selected-scope C# text cleanup preview with native read-only diff, per-file/rule
	selection and stale-buffer protection. Applies approved text to editors without
	saving files; AI, type splitting and editor-only operations are not included.
- Cleanup preview help, runtime verification checklist and sequential feature delivery plan.
- Safe Razor and Blazor formatter.
- Razor formatter settings and configuration UI.
- Formatting support for Razor control blocks, including `@try`, `@catch` and `@finally`.
- Namespace fixer and namespace cleanup support.
- Optional AI-assisted XML documentation cleanup.
- Advanced XMLDoc controls, filters and budget limits.
- One-time cleanup options dialog for selected-scope cleanup.
- Documentation describing project origin, attribution, licensing, architecture and roadmap.
- Script `scripts/test-cleanup-build.ps1` for running post-cleanup build verification.
- Opt-in C# diagnostic cleanup driven by `.editorconfig` and Roslyn. Four categories
	(formatting, naming, code style, other analyzer fixes; settings `Cleaning_ApplyEditorConfigFormatting`,
	`Cleaning_ApplyEditorConfigNaming`, `Cleaning_ApplyEditorConfigCodeStyle`, `Cleaning_ApplyAnalyzerCodeFixes`,
	also available as `.codejanitor` keys) apply the existing Roslyn and analyzer code fixes after the
	other cleanup steps. Only `suggestion`, `warning` and `error` diagnostics are fixed. Fixes that add
	compiler errors or change anything other than document text are rejected. Unfixed diagnostics are
	reported in the output pane and in the cleanup summary. Visual Studio Code Cleanup profiles are not
	used. Requires a Visual Studio build with Roslyn 5.9 or newer.
- CI runs the unit tests with `vstest.console` after the Release build, and the workflow fails on
	test failures.

### Changed

- Added Visual Studio 2026 installation compatibility.
- Migrated the legacy Options experience to the native Visual Studio settings system.
- Organized approximately 191 settings into 21 categories.
- Removed the legacy classic Options UI after the native settings migration.
- Added secure configuration boundaries for AI-assisted operations.

### Fixed

- Fixed "Seal Classes" cleanup breaking compilation on classes declaring `virtual` members (`CS0549`), classes used as generic type constraints (`where T : ThatType`, `CS0701`), or classes with subclasses across the solution (`CS0509`). Added solution-wide disqualified type discovery before applying class sealing.
- Fixed "Make Fields Readonly" cleanup adding `readonly` to private fields mutated via `ref` or `out` arguments (including `Interlocked.Increment(ref field)` and `Interlocked.Decrement(ref field)`) or writes inside nested types, or fields whose address is taken directly (`&field`, `CS0192`).
- Fixed legacy EnvDTE access modifier insertion corrupting code or injecting misplaced `private` tokens on generic method declarations and constraints; added a hard stop guarding generic declarations in `InsertExplicitAccessModifierLogic`.
- Added post-cleanup compilation check and syntax error reporting so cleanup passes report errors and warnings instead of unconditionally claiming success.

## CodeMaid history

The functionality inherited from CodeMaid has its own historical release record in the original repository:

- [CodeMaid repository](https://github.com/codecadwallader/codemaid)
- [CodeMaid changelog history](https://github.com/codecadwallader/codemaid/blob/dev/CHANGELOG.md)

Code Janitor preserves that history for attribution, but future changes are recorded above as Code Janitor changes.
