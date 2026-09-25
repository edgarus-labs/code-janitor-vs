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
- C# diagnostic cleanup driven by `.editorconfig` and Roslyn. Rules the repository's `.editorconfig`
	configures (formatting, naming, code style, other analyzers) are fixed with the existing Roslyn and
	analyzer code fixes after the other cleanup steps; rules active only through Visual Studio defaults
	are left alone. Only `suggestion`, `warning` and `error` diagnostics are fixed. Fixes that add any
	compiler error (even while removing another), add a compiler error in another project or target
	framework that compiles the same file, or change anything other than document text are rejected.
	Unfixed diagnostics are reported in the output pane and in the cleanup summary. Visual Studio Code
	Cleanup profiles are not used. Requires a Visual Studio build with Roslyn 5.9 or newer.
- CI runs the unit tests with `vstest.console` after the Release build, and the workflow fails on
	test failures.
- CodeQL code scanning (`.github/workflows/codeql.yml`) for C# and GitHub Actions workflows on
	pull requests and pushes to `develop`, and weekly. Results are uploaded to GitHub code scanning.

### Changed

- Migrated the legacy Options experience to the native Visual Studio settings system.
- Organized approximately 191 settings into 21 categories.
- Removed the legacy classic Options UI after the native settings migration.
- Added secure configuration boundaries for AI-assisted operations.

### Removed

- **Breaking:** dropped Visual Studio 2022 (17.x) support. The VSIX installs only on Visual Studio 2026
	(18.x); existing Visual Studio 2022 installations no longer receive updates. The features that use the
	Visual Studio Roslyn workspace (diagnostic cleanup, moving using directives outside namespaces) need
	Roslyn 5.9 or newer, which Visual Studio 2022 does not ship. Visual Studio 2026 builds with an older Roslyn
	still install; on them those two features log the failure and leave files unchanged.

### Fixed

- Fixed "Seal Classes" cleanup breaking compilation on classes declaring `virtual` members (`CS0549`), classes used as generic type constraints (`where T : ThatType`, `CS0701`), or classes with subclasses across the solution (`CS0509`). Added solution-wide disqualified type discovery before applying class sealing.
- Fixed "Make Fields Readonly" cleanup adding `readonly` to private fields mutated via `ref` or `out` arguments (including `Interlocked.Increment(ref field)` and `Interlocked.Decrement(ref field)`) or writes inside nested types, or fields whose address is taken directly (`&field`, `CS0192`).
- Fixed legacy EnvDTE access modifier insertion corrupting code or injecting misplaced `private` tokens on generic method declarations and constraints; added a hard stop guarding generic declarations in `InsertExplicitAccessModifierLogic`.
- Added post-cleanup compilation check and syntax error reporting so cleanup passes report errors and warnings instead of unconditionally claiming success.
- Fixed "Move using directives outside namespace" breaking compilation (`CS0246`) for namespace-relative
	directives such as `using Services;` inside `namespace Company.App`. Each moved directive, alias target and
	`using static` is now resolved with the Roslyn semantic model: directives that mean the same at file level
	keep their exact text, the others are written fully qualified (`using Company.App.Services;`). If a
	directive cannot be resolved, the directives would move across preprocessor directives, or the move would
	add compile errors or make a name, member or implicitly called member (such as an extension
	`GetEnumerator`, `Add`, `Count` or `operator true`) refer to a different symbol in any project, target
	framework or conditional-compilation variant (up to four relevant `#if` symbols, including ones that
	change declarations in other files or referenced projects) that compiles the file, the directives are
	left in place, the rest of the cleanup still runs, and the reason is written to the output pane once per
	cleanup. The step runs against the Visual Studio Roslyn workspace before the
	text cleanup (and before type splitting), honors the `.codejanitor` `moveUsingsOutsideNamespace` policy in
	the editor too, and is no longer part of the selected-scope text preview. Converting to a file-scoped
	namespace no longer moves using directives on its own: without the move step they stay inside the
	file-scoped namespace, where they keep compiling. Requires a Visual Studio build with Roslyn 5.9 or newer;
	otherwise, and for files not compiled in a loaded C# project, the directives are left in place.

## CodeMaid history

The functionality inherited from CodeMaid has its own historical release record in the original repository:

- [CodeMaid repository](https://github.com/codecadwallader/codemaid)
- [CodeMaid changelog history](https://github.com/codecadwallader/codemaid/blob/dev/CHANGELOG.md)

Code Janitor preserves that history for attribution, but future changes are recorded above as Code Janitor changes.
