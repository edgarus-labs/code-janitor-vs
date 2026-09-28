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
- Options notes for settings overridden by `.editorconfig`: under each affected Cleaning option, a note
	names the `.editorconfig` key and file that decide it for the open solution (evaluated for a C# file in
	the solution directory). No note is shown when no solution is open or the key is ignored.
- Options controls whose setting the open solution's `.editorconfig` overrides are disabled (the same
	controls that show the override note).
- Blank lines between a documentation comment (`///` in C#, `'''` in VB) and the declaration it documents
	are always removed.
- Cleaning > Code Style options: Roslyn code-style rules (modifiers, blocks, expression-bodied members,
	pattern matching, null checking, modern expressions, `this.` qualification, language keywords vs.
	framework type names, parentheses) that the diagnostic cleanup applies through the Roslyn code fixes
	when the repository's `.editorconfig` does not enforce them. Each rule has an on/off switch (off by
	default) and a value, and can be set per repository in the `.codejanitor` `codeStyle` section. A rule
	`.editorconfig` enforces keeps its `.editorconfig` value and is locked in Options. Naming rules stay
	`.editorconfig`-only.
- Options linked to their Roslyn `.editorconfig` counterpart, with the override note and lock: simplify
	single-statement lambdas (`csharp_style_expression_bodied_lambdas`), pattern-matching null checks
	(`csharp_style_prefer_null_check_over_type_check`, `dotnet_style_prefer_is_null_check_over_reference_equality_method`),
	seal classes (CA1852), `nameof` (CA1507), `JsonSerializerOptions` reuse (CA1869), multiple blank lines
	(`dotnet_style_allow_multiple_blank_lines_experimental`), blank lines at braces
	(`csharp_style_allow_blank_lines_between_consecutive_braces_experimental`), Remove and Sort Usings (IDE0005)
	and Format Document (IDE0055).
- `.editorconfig` severities now decide cleanup steps even without the option key: a
	`dotnet_diagnostic.<id>.severity` of `suggestion` or higher (for example IDE0005, IDE0055, CA1852) decides the
	step with Roslyn's default value, and `dotnet_analyzer_diagnostic.category-<category>.severity` or
	`dotnet_analyzer_diagnostic.severity` does the same for every rule in scope that is enabled by default,
	regardless of the Visual Studio setting. Roslyn's default turns some steps off (for example `var` when
	apparent, blank-line removal, file-scoped namespaces).

### Changed

- Migrated the legacy Options experience to the native Visual Studio settings system.
- Organized approximately 191 settings into 21 categories.
- Removed the legacy classic Options UI after the native settings migration.
- Added secure configuration boundaries for AI-assisted operations.
- Cleanup settings are resolved per file with one precedence in the editor and for closed files:
	`.editorconfig` (for the keys it maps) over the `.codejanitor` policy over the Visual Studio settings. For
	keys tied to Roslyn diagnostics the severity of each diagnostic is resolved as in Roslyn
	(`dotnet_diagnostic.<id>.severity`, then category, then global severity, then the key's suffix; a `:none`
	suffix always turns the rule off); `suggestion` or higher enforces the value, `none`, `silent` or
	`refactoring` leaves the decision to the next source. `:silent` previously enforced the value. Editor cleanup previously ignored
	`.codejanitor` for every step except moving using directives, and read only a few `.editorconfig` keys for
	closed files, with incorrect section matching and file precedence. Closed-file cleanup applies the resolved
	settings to each kind of explicit access modifier, blank-line padding and single-line method/accessor update,
	not only to the decision whether the step runs, so it gives the same result as editor cleanup.
- Closed C# files are no longer opened in an invisible editor for "Remove and Sort Usings" and "Format
	Document": their Roslyn equivalents run on the closed file with the same settings (including the
	auto-save skip and the using statements to reinsert). A closed file whose cleanup changes only that
	file is written straight to disk; a file opened meanwhile, read-only or under source control checkout
	goes through Visual Studio as before. Such changes are logged in the output pane and are not counted
	as diagnostic fixes in the cleanup summary.
- Splitting top-level types into their own files now also moves structs (and record structs); classes,
	interfaces, records, enums and delegates were already split. Partial types stay in place.

### Removed

- **Breaking:** dropped Visual Studio 2022 (17.x) support. The VSIX installs only on Visual Studio 2026
	(18.0 or newer); existing Visual Studio 2022 installations no longer receive updates (v1.3.0 was the last release
	that installed there, but it already required Roslyn 5.9 for C# cleanup, which Visual Studio 2022 does not ship).
	Code Janitor uses the Roslyn that Visual Studio ships (the VSIX does not carry its own copy) and is
	built against Roslyn 5.0, the version of Visual Studio 2026 18.0; Visual Studio 2022 ships Roslyn 4.x.

### Fixed

- Fixed "Seal Classes" cleanup sealing classes designed for inheritance, such as classes with a `protected`
	constructor (#43) or other `protected`, `protected internal` or `private protected` members (`CS0628`). Class
	sealing now runs on the Visual Studio Roslyn workspace: it seals a class only when it has no virtual or protected
	members, no class of the solution derives from it and no generic constraint names it, in every project compiling
	the file, including cleanup-on-save of a single file. It replaces the name-based disqualified type discovery;
	the cleanup preview no longer includes class sealing.
- Fixed "Seal Classes" not sealing a class that the cleanup of an open document moved to its own file
	("Move top-level types to separate files"); it was sealed only by the next cleanup of the created file.
- Fixed two `.editorconfig` key names in Options > Cleaning > Update (`csharp_style_namespace_declarations`,
	`csharp_using_directive_placement`) shown without an underscore, because WPF read it as an access key.
- Fixed closed non-C# files in a batch cleanup being counted as no-op and never cleaned.
- Fixed a batch cleanup not counting a file as changed when a semantic step (using placement, class sealing,
	null checks) rewrote it and a later step on the same file failed.
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
	namespaces (C# 10, `CS8370`) and to collection expressions (C# 12) now run only when every project and
	target framework that compiles the file uses that version or newer, as read from the Visual Studio Roslyn
	workspace. Otherwise, or when the version cannot be determined, the code is left as it is and the reason is
	written to the output pane. This applies to the editor cleanup, the closed-file cleanup and the preview.
	String-format-to-interpolation no longer moves a multi-line argument into an interpolation hole, which
	needs C# 11.
- Fixed "Convert block-scoped namespace to file-scoped" changing the content of multi-line verbatim, raw and
	interpolated string literals and of `#if`-disabled code by removing their indentation, and dropping
	everything after the namespace's closing brace (a trailing `#endif`, `#endregion` or comment). The
	conversion also leaves the file unchanged when `#if`-disabled code before the namespace declares types or
	namespaces, which would break the build configurations that enable it (`CS8956`, `CS8955`).
- Fixed "Make Fields Readonly" cleanup breaking compilation or behavior: it no longer makes a field
	`readonly` when a constructor writes it through another instance (`other.field = ...`, object or `with`
	initializers), when it is written through a deconstruction, when code excluded by `#if` mentions it, when it
	is a `fixed` buffer, or when a method is called on it and its type may be a mutable struct (the call would
	run on a defensive copy). The field's indentation is kept when `readonly` is its first modifier.
- Fixed "Inline `out` variable declarations" moving a declaration into a statement that scopes the variable to
	itself (loops, `using`, `lock`, lambdas, queries), which broke later uses, and dropping comments between the
	declaration and the call.
- Fixed "Simplify single-statement lambdas" changing the chosen overload (for example `Func<Task>` instead of
	`Action`, or an `IQueryable` expression-tree overload) for lambdas passed as arguments or collection
	elements, converting parameterless anonymous methods whose target needs a parameter list, and dropping
	comments or directives inside the body.
- Fixed "Convert to collection expressions" converting multi-dimensional and jagged arrays of another shape
	and dropping comments or preprocessor directives between the initializer braces.
- Fixed "Convert to `var` when the type is apparent" producing `const var` (`CS0822`) and converting arrays
	whose rank differs from the declared type.
- Fixed string-format-to-interpolation treating escaped braces (`{{`, `}}`) as placeholders, not escaping
	braces in format specifiers, and not parenthesizing conditional expressions and `global::` names in holes.
- Fixed pattern-matching null checks changing behavior or breaking compilation: the conversion now runs on
	the Visual Studio Roslyn workspace and changes a check only when `==`/`!=` binds to the built-in operator
	(no user-defined or lifted operator from any file, project or referenced assembly, e.g.
	`UnityEngine.Object`), the operand is a reference type, `Nullable<T>` or a type parameter not constrained
	to a value type, and the C# version allows it (`is null` C# 7.0, `is not null` C# 9), in every project and
	target framework compiling the file. It also runs for open documents now, and is no longer part of the
	text cleanup preview. `a == b == null` (`CS0037`) and comments between the operands are handled.
- Fixed "Reuse `JsonSerializerOptions`" replacing an argument with a positional `null` that is ambiguous
	between overloads (`CS0121`); the argument is now named `options:`.
- Fixed explicit access modifier insertion adding `private` to types and fields nested in interfaces and
	adding an access modifier to `file`-scoped types.
- Fixed region removal and "Update `#endregion` directives" changing lines inside multi-line string literals
	and comments, and `#endregion` updates skipping nameless regions and changing the file's line endings.
- Fixed splitting top-level types into files breaking compilation when a `#region` spans several types
	(`CS1028`) or a type is `file`-scoped, and moving the file header away with the first type.
- Fixed blank-line padding: blank lines went between a declaration or `return`/`throw` and the comments
	above it, file headers were attached to the first declaration, `case` padding and `//` comment padding
	were applied inside string literals, and inserted blank lines used a different line ending than the file.
	A `return` or `throw` that shares its line with other code is left alone.
- Fixed comment formatting changing string literals, `///` documentation comments, `////` separators and
	trailing comments after code, and changing the file's line endings.
- Fixed "Remove trailing whitespace" leaving whitespace on a final line without a line break and in some
	comments and directives. Whitespace inside multi-line string literals and `#if`-disabled code is kept.
- Fixed multiple-blank-line removal collapsing blank lines inside multi-line string literals and missing
	blank lines at the start of the file.
- Fixed tab-to-space conversion skipping the indentation of documentation comment continuation lines.
- Fixed "Ensure final newline" adding `\n` to files that use `\r` line breaks.
- Fixed single-line method and accessor updates hard-coding CRLF line endings and four-space indentation,
	dropping comments, and compressing accessor bodies that hold comments, directives or multi-line statements.

## CodeMaid history

The functionality inherited from CodeMaid has its own historical release record in the original repository:

- [CodeMaid repository](https://github.com/codecadwallader/codemaid)
- [CodeMaid changelog history](https://github.com/codecadwallader/codemaid/blob/dev/CHANGELOG.md)

Code Janitor preserves that history for attribution, but future changes are recorded above as Code Janitor changes.
