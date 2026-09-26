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
	compiler error (even while removing another) or change anything other than document text are rejected.
	If the fixed text would add a compiler error in another project or target framework that compiles the
	same file, no fixes are applied to that file and the cleanup reports a failure naming that project and
	error. Unfixed diagnostics are reported in the output pane and in the cleanup summary. Visual Studio Code
	Cleanup profiles are not used. Requires Visual Studio 2026 (Roslyn 5.0 or newer).
- CI runs the unit tests with `vstest.console` after the Release build, and the workflow fails on
	test failures.
- CodeQL code scanning (`.github/workflows/codeql.yml`) for C# and GitHub Actions workflows on
	pull requests and pushes to `develop`, and weekly. Results are uploaded to GitHub code scanning.
- `.editorconfig` as the source of truth for the cleanup steps it configures, in both directions:
	`csharp_style_namespace_declarations = block_scoped` converts file-scoped namespaces back to block-scoped,
	`csharp_using_directive_placement = inside_namespace` moves file-level using directives into the namespace
	(semantically verified, `global::`-qualified where a name would bind differently, all-or-nothing),
	`indent_style = tab` converts leading spaces to tabs and `insert_final_newline = false` removes the final
	newline in closed-file cleanup. Namespace conversion moves the body by one `indent_size` (`tab_width` when
	`indent_size = tab`). See the key mapping in `docs/features.md`.

### Changed

- Migrated the legacy Options experience to the native Visual Studio settings system.
- Organized approximately 191 settings into 21 categories.
- Removed the legacy classic Options UI after the native settings migration.
- Added secure configuration boundaries for AI-assisted operations.
- Cleanup settings are resolved per file with one precedence in the editor and for closed files:
	`.editorconfig` (for the keys it maps; a key with the `:none` severity is ignored, any other severity enforces
	the value) over the `.codejanitor` policy over the Visual Studio settings. Editor cleanup previously ignored
	`.codejanitor` for every step except moving using directives, and read only a few `.editorconfig` keys for
	closed files, with incorrect section matching and file precedence. Closed-file cleanup applies the resolved
	settings to each kind of explicit access modifier, blank-line padding and single-line method/accessor update,
	not only to the decision whether the step runs, so it gives the same result as editor cleanup.

### Removed

- **Breaking:** dropped Visual Studio 2022 (17.x) support. The VSIX installs only on Visual Studio 2026
	(18.0 or newer); existing Visual Studio 2022 installations no longer receive updates (v1.3.0 was the last release
	that installed there, but it already required Roslyn 5.9 for C# cleanup, which Visual Studio 2022 does not ship).
	Code Janitor uses the Roslyn that Visual Studio ships (the VSIX does not carry its own copy) and is
	built against Roslyn 5.0, the version of Visual Studio 2026 18.0; Visual Studio 2022 ships Roslyn 4.x.

### Fixed

- Fixed "Seal Classes" cleanup breaking compilation on classes declaring `virtual` members (`CS0549`), classes used as generic type constraints (`where T : ThatType`, `CS0701`), or classes with subclasses across the solution (`CS0509`). Added solution-wide disqualified type discovery before applying class sealing.
- Fixed "Make Fields Readonly" cleanup adding `readonly` to private fields mutated via `ref` or `out` arguments (including `Interlocked.Increment(ref field)` and `Interlocked.Decrement(ref field)`) or writes inside nested types, or fields whose address is taken directly (`&field`, `CS0192`).
- Fixed legacy EnvDTE access modifier insertion corrupting code or injecting misplaced `private` tokens on generic method declarations and constraints; added a hard stop guarding generic declarations in `InsertExplicitAccessModifierLogic`.
- Added post-cleanup compilation check and syntax error reporting so cleanup passes report errors and warnings instead of unconditionally claiming success.
- Fixed "Move using directives outside namespace" breaking compilation (`CS0246`) for namespace-relative
	directives such as `using Services;` inside `namespace Company.App`. Each moved directive, alias target and
	`using static` is now resolved with the Roslyn semantic model: directives that mean the same at file level
	keep their exact text, the others are written fully qualified (`using Company.App.Services;`). If a
	directive cannot be resolved or its fully qualified form would refer to something else at file level (a
	target reached only through an `extern alias` declared inside the namespace), the directives would move
	across preprocessor directives, a moved directive
	imports an extension `Add`, `GetEnumerator`, `GetPinnableReference`, `==` or `!=` that collection
	expressions, spreads, `fixed` statements or tuple comparisons in the file call implicitly (such calls cannot
	be compared, so the move is skipped even if nothing would bind differently), or the move would
	add compile errors or make a name, member or implicitly called member (such as an extension
	`GetEnumerator`, `Add`, `Count` or `operator true`) refer to a different symbol (including a same-named
	type of another assembly) in any project, target framework or conditional-compilation variant (up to four
	relevant `#if` symbols, including ones that change declarations or using directives, including
	`global using`, in other files or referenced projects, alone or only together as in `#if A && B`) that
	compiles the file, the directives are
	left in place, the rest of the cleanup still runs, and the reason is written to the output pane once per
	cleanup. The step runs against the Visual Studio Roslyn workspace before the
	text cleanup (and before type splitting), honors the `.codejanitor` `moveUsingsOutsideNamespace` policy in
	the editor too, and is no longer part of the selected-scope text preview. Converting to a file-scoped
	namespace no longer moves using directives on its own: without the move step they stay inside the
	file-scoped namespace, where they keep compiling. For files not compiled in a loaded C# project, the
	directives are left in place.
- Fixed cleanup emitting syntax the project's C# version does not support: conversion to file-scoped
	namespaces (C# 10, `CS8370`), to collection expressions (C# 12) and to `is not null` (C# 9) now run only
	when every project and target framework that compiles the file uses that version or newer, as read from the
	Visual Studio Roslyn workspace. Otherwise, or when the version cannot be determined, the code is left as it
	is (`== null` still becomes `is null`) and the reason is written to the output pane. This applies to the
	editor cleanup, the closed-file cleanup and the preview. String-format-to-interpolation no longer moves a
	multi-line argument into an interpolation hole, which needs C# 11.
- Fixed "Convert block-scoped namespace to file-scoped" changing the content of multi-line verbatim, raw and
	interpolated string literals and of `#if`-disabled code by removing their indentation, and dropping
	everything after the namespace's closing brace (a trailing `#endif`, `#endregion` or comment). The
	conversion also leaves the file unchanged when `#if`-disabled code before the namespace declares types or
	namespaces, which would break the build configurations that enable it (`CS8956`, `CS8955`).

## CodeMaid history

The functionality inherited from CodeMaid has its own historical release record in the original repository:

- [CodeMaid repository](https://github.com/codecadwallader/codemaid)
- [CodeMaid changelog history](https://github.com/codecadwallader/codemaid/blob/dev/CHANGELOG.md)

Code Janitor preserves that history for attribution, but future changes are recorded above as Code Janitor changes.
