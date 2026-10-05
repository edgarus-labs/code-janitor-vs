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
- Fix Namespace command: makes namespaces match the folder structure with Roslyn's IDE0130
	(`dotnet_style_namespace_match_folder`) analyzer and code fix through the diagnostic cleanup, which also
	updates references in other files. It needs the project's root namespace and directory from Visual Studio
	and reports the changed files, the IDE0130 diagnostics it could not fix, and failures. Files IDE0130 does
	not report (for example a type declared in several files) are left unchanged.
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
	`csharp_style_namespace_declarations = block_scoped` converts file-scoped namespaces back to block-scoped
	(IDE0160), `csharp_using_directive_placement = inside_namespace` moves file-level using directives into the
	namespace (IDE0065), both through the Roslyn code fixes of the diagnostic cleanup, `indent_style = tab`
	converts leading spaces to tabs and `insert_final_newline = false` removes the final newline in closed-file
	cleanup. See the key mapping in `docs/features.md`.
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
	settings to blank-line padding and single-line method/accessor update, not only to the decision whether the
	step runs, so it gives the same result as editor cleanup.
- Closed C# files are no longer opened in an invisible editor for "Remove and Sort Usings" and "Format
	Document": their Roslyn equivalents run on the closed file with the same settings (including the
	auto-save skip and the using statements to reinsert). A closed file whose cleanup changes only that
	file is written straight to disk; a file opened meanwhile, read-only or under source control checkout
	goes through Visual Studio as before. Such changes are logged in the output pane in Diagnostics Mode and
	are not counted as diagnostic fixes in the cleanup summary.
- Splitting top-level types into their own files now also moves structs (and record structs); classes,
	interfaces, records, enums and delegates were already split. Partial types stay in place.
- Cleanup skip reasons and informational messages (batch completed/canceled, build verification passed, repository
	settings import/export results) are written to the output pane only in Diagnostics Mode.
- A code fix provider that fails during diagnostic cleanup no longer aborts the cleanup of the file: it is named in
	the output pane with its error message, asked only once per diagnostic id per file cleanup, the other fixes are
	still applied and the diagnostic is reported as unresolved. A failure that means the host Roslyn cannot be bound
	(older than the Roslyn 5.0 Code Janitor is built against) is still reported as a failure that leaves the file
	unchanged, not blamed on a provider.
- Cleanup steps that were syntax-only converters now apply Roslyn's own analyzers and code fixes through the
	diagnostic cleanup, like "Make Fields Readonly" (IDE0044): convert to `var` when the type is apparent
	(IDE0007), inline `out` variable declarations (IDE0018), convert to collection expressions (IDE0300–IDE0306),
	simplify single-statement lambdas (IDE0053), insert explicit access modifiers (IDE0040), convert to a
	file-scoped namespace (IDE0161), move using directives outside the namespace (IDE0065) and, for C# only,
	remove multiple consecutive blank lines (IDE2000, experimental). When `.editorconfig` does not decide the
	rule, Code Janitor applies `csharp_style_var_when_type_is_apparent = true` (with the other `var` options
	off, so built-in and non-apparent types keep their explicit type), `csharp_style_inlined_variable_declaration
	= true`, `dotnet_style_prefer_collection_expression = true` (types must match exactly; for example
	`IEnumerable<int> x = new int[] { 1 }` keeps the array), `csharp_style_expression_bodied_lambdas = true`,
	`dotnet_style_require_accessibility_modifiers = for_non_interface_members`,
	`csharp_style_namespace_declarations = file_scoped`, `csharp_using_directive_placement = outside_namespace`
	and `dotnet_style_allow_multiple_blank_lines_experimental = false`; an `.editorconfig` that enforces the rule
	wins. A rule `.editorconfig` sets by value alone (for example `csharp_style_namespace_declarations =
	block_scoped` without a severity) is applied with that value, even where Roslyn would report it as silent;
	option values are matched ignoring case. The language-version requirements (C# 10 for file-scoped namespaces, C# 12 for collection expressions)
	are Roslyn's own checks. These steps run after the other cleanup steps, for open and closed files (inlining
	`out` variables previously ran on closed files only), and are no longer part of the C# text cleanup preview.
	They run on the cleaned file only, so files created by "Move top-level types to separate files" get them
	when those files are cleaned. VB, C/C++, markup and other files keep Code
	Janitor's own multiple-blank-line removal. Pattern-matching null checks, `nameof`, `JsonSerializerOptions`
	reuse, string-format-to-interpolation and blank lines at braces keep their own steps: no Roslyn rule of the
	Visual Studio host covers them.
- **Breaking:** one setting, Cleaning > Insert > **Insert explicit accessibility modifiers**
	(`Cleaning_InsertExplicitAccessModifiers`, `.codejanitor` key `insertExplicitAccessModifiers`, on by default),
	replaces the nine per-kind settings (classes, delegates, enumerations, events, fields, interfaces, methods,
	properties, structs) and their `.codejanitor` keys, which are no longer read. Interface members get no modifier.
	If you had turned the per-kind settings off, turn the new setting (or `insertExplicitAccessModifiers`) off.
- Cleanup of a closed file now honors the inclusion expression like the editor cleanup, and skips a file that holds
	an `<auto-generated` marker, as the editor cleanup does.
- A file header is inserted below a shebang line, the XML declaration of XML, XAML and HTML files, and the opening
	tag of PHP files instead of above it.
- Fixed the diagnostic cleanup leaving a blank first line when a code fix moves the first member of a file, for
	example the using directives moved above a file-scoped namespace.
- Fixed removing blank lines after attributes leaving one blank line when several followed, and the editor's
	blank-line steps changing the line endings of an LF document. The blank-line steps of closed-file cleanup no
	longer change the content of multi-line string literals.

### Removed

- **Breaking:** dropped Visual Studio 2022 (17.x) support. The VSIX installs only on Visual Studio 2026
	(18.0 or newer); existing Visual Studio 2022 installations no longer receive updates (v1.3.0 was the last release
	that installed there, but it already required Roslyn 5.9 for C# cleanup, which Visual Studio 2022 does not ship).
	Code Janitor uses the Roslyn that Visual Studio ships (the VSIX does not carry its own copy) and is
	built against Roslyn 5.0, the version of Visual Studio 2026 18.0; Visual Studio 2022 ships Roslyn 4.x.
- Code Janitor's own converters for the steps now applied through Roslyn rules: `OutVarInliningConverter`,
	`SingleStatementLambdaConverter`, `CollectionExpressionConverter`, `VarWhenApparentConverter`,
	`ExplicitAccessModifierConverter` and `InsertExplicitAccessModifierLogic`, `FileScopedNamespaceConverter`,
	`UsingDirectivePlacementConverter`, `NamespaceFixerConverter` and `NamespaceFixerLogic`,
	`NormalizeBlankLinesConverter`, and the C# language-version check `CSharpLanguageVersionSupport`.

### Fixed

- Fixed the diagnostic cleanup leaving mixed line endings when a code fix inserts line breaks of its own (for
	example Roslyn's "Move misplaced using directives" fix always inserts CRLF): every changed file keeps its
	`end_of_line`, otherwise the one line ending it used throughout, also in open documents.
- Fixed "Seal Classes" cleanup sealing classes designed for inheritance, such as classes with a `protected`
	constructor (#43) or other `protected`, `protected internal` or `private protected` members (`CS0628`). Class
	sealing now runs on the Visual Studio Roslyn workspace: it seals a class only when it has no virtual or protected
	members, no class of the solution derives from it and no generic constraint names it, in every project compiling
	the file, including cleanup-on-save of a single file. It replaces the name-based disqualified type discovery;
	the cleanup preview no longer includes class sealing.
- Fixed "Seal Classes" not sealing a class that the cleanup of an open document moved to its own file
	("Move top-level types to separate files"); it was sealed only by the next cleanup of the created file.
- Fixed "Seal Classes" sealing a class that code elsewhere in the solution casts, `as`-converts or pattern-matches
	to or from an interface it does not implement (`CS0030`, `CS0039`, `CS8121`), including through arrays and generic
	collection interfaces (`(IList<IBar>)foos`, `foos as IEnumerable<IBar>`), or whose project, or a project
	referencing it, contains an inactive `#if` branch.
- Fixed "Seal Classes" sealing classes it could not prove unused: cleanup is skipped with a warning while the
	solution is loading, a project is unloaded or failed to load, or Visual Studio cannot tell whether that is the
	case, because such projects are missing from the Roslyn workspace and their derived classes, generic constraints
	and casts would go unseen. Classes of a project that a project outside the Roslyn workspace (for example C++/CLI)
	references, directly or through other projects, are not sealed. Documents produced by source
	generators are scanned like written files. The solution-wide scans are cached per project version instead of per
	solution snapshot, so a batch rescans only what a rewritten file changed. Known limit: a class used as a runtime
	proxy or mock (`Mock<Foo>`, `Substitute.For<Foo>()`) compiles once sealed but fails at run time; turn off Seal
	Classes for such projects.
- Fixed the AI XML documentation option "Run during cleanup" having no effect: cleanup now adds the AI-generated
	XML documentation to each file a cleanup batch cleans up, open or closed. Cleanup Active Document and the
	automatic cleanup on save never add it. Canceling a cleanup batch also stops its XML documentation.
- Fixed "Move top-level types to separate files" leaving the files it had already created behind when a later file
	could not be written; a retry no longer creates duplicate `Name~1.cs` files.
- Fixed the cleanup summary counting a file both as changed and as an editor item when a semantic step changed it and
	editor cleanup was required.
- Fixed the cleanup progress dialog staying open when completing the batch failed; the error is written to the output
	pane.
- Fixed a diagnostic cleanup fix provider failure losing its stack trace; the full exception is now written to the
	output pane in Diagnostics Mode.
- Fixed the accessor consistency cleanup adding a blank line and extra indentation when it expanded an accessor whose
	body already ended its line.
- `!= null` checks left unchanged because a project compiles the file with a language version older than C# 9 are
	reported in the output pane in Diagnostics Mode, with the file path.
- Fixed single-line method spreading indenting the body from a wrapped parameter or `where` line.
- Fixed a `#region` around a single top-level type blocking the split into separate files; the region now moves
	with the type.
- Fixed type splitting overwriting a file that appeared after the split was planned; the new file gets the next
	free name (`Name~1.cs`).
- Fixed two `.editorconfig` key names in Options > Cleaning > Update (`csharp_style_namespace_declarations`,
	`csharp_using_directive_placement`) shown without an underscore, because WPF read it as an access key.
- Fixed closed non-C# files in a batch cleanup being counted as no-op and never cleaned.
- Added post-cleanup compilation check and syntax error reporting so cleanup passes report errors and warnings instead of unconditionally claiming success.
- Fixed "Move using directives outside namespace" breaking compilation (`CS0246`) for namespace-relative
	directives such as `using Services;` inside `namespace Company.App`, and dropping using directives: the step
	now applies Roslyn's IDE0065 code fix through the diagnostic cleanup and honors the `.codejanitor`
	`moveUsingsOutsideNamespace` policy in the editor too. With "Remove and Sort Usings" on, or `organizeUsings`
	in effect for a closed file, the moved directives are sorted in the same cleanup.
- Fixed string-format-to-interpolation moving a multi-line argument into an interpolation hole, which needs
	C# 11.
- Fixed "Make Fields Readonly" cleanup breaking compilation or behavior (fields written through `ref`, `out`,
	`ref this` extension methods or another instance, mutating calls and getters on struct fields, code excluded by
	`#if`): the step now applies Roslyn's own "Make field readonly" analyzer and code fix (IDE0044) through the
	diagnostic cleanup, which sees every write semantically, instead of a syntax-only check. The step no longer
	appears in the C# text cleanup preview.
- Fixed string-format-to-interpolation treating escaped braces (`{{`, `}}`) as placeholders, not escaping
	backslashes, quotes and control characters in format specifiers (a format specifier containing a brace leaves
	the call unchanged), not parenthesizing conditional expressions and `global::` names in holes, copying
	placeholders with spaces or an empty format (`{1, 10}`, `{0 }`, `{0:}`) as literal text, and dropping comments in
	the call. Calls whose arguments have side effects are converted only when every argument is still evaluated
	exactly once and in order: only literals, `this` and locals or parameters in scope may be repeated, reordered or
	dropped (any other name may be a property), a member read such as `DateTime.Now` is never duplicated, and a local
	is not moved across an argument that assigns, increments or passes it `ref`/`out`. A call is also left unchanged
	when a hole other than a literal comes before an argument that calls a method, creates an object, assigns,
	increments or awaits: `string.Format` formats its arguments after evaluating all of them, an interpolated string
	formats each hole before evaluating the next.
- Fixed pattern-matching null checks changing behavior or breaking compilation: the conversion now runs on
	the Visual Studio Roslyn workspace and changes a check only when `==`/`!=` binds to the built-in operator
	(no user-defined or lifted operator from any file, project or referenced assembly, e.g.
	`UnityEngine.Object`), the operand is a reference type, `Nullable<T>` or a type parameter not constrained
	to a value type, and the C# version allows it (`is null` C# 7.0, `is not null` C# 9), in every project and
	target framework compiling the file. It also runs for open documents now, and is no longer part of the
	text cleanup preview. `a == b == null` (`CS0037`) and comments between the operands are handled, and constant
	null checks (`const` initializers, default parameter values, attribute arguments, `case` labels), where
	`is null` does not compile, are left unchanged.
- Fixed "Reuse `JsonSerializerOptions`" replacing an argument with a positional `null` that is ambiguous
	between overloads (`CS0121`); the argument is now named `options:`, or cast to the options type when a positional
	argument follows it (`(JsonSerializerOptions)null, ct`), which compiles in every C# version, or when the call is not
	known to be on `System.Text.Json.JsonSerializer`. An options allocation passed as the first argument (the value)
	is left unchanged.
- Fixed region removal and "Update `#endregion` directives" changing lines inside multi-line string literals
	and comments, and `#endregion` updates skipping nameless regions and changing the file's line endings.
- Fixed splitting top-level types into files breaking compilation when a `#region` spans several types
	(`CS1028`) or a type is `file`-scoped, and moving the file header away with the first type.
- Fixed blank-line padding: blank lines went between a declaration or `return`/`throw` and the comments
	above it, file headers were attached to the first declaration, `case` padding and `//` comment padding
	were applied inside string literals, and inserted blank lines used a different line ending than the file.
	A `return` or `throw` that shares its line with other code is left alone.
- Fixed comment formatting changing string literals, `///` documentation comments, `////` separators and
	trailing comments after code, skipping comments inside inactive `#if` branches, and changing the file's line
	endings.
- Fixed "Remove trailing whitespace" leaving whitespace on a final line without a line break and in some
	comments and directives. Whitespace inside multi-line string literals and `#if`-disabled code is kept.
- Fixed multiple-blank-line removal collapsing blank lines inside multi-line string literals and missing
	blank lines at the start of the file.
- Fixed tab-to-space conversion skipping the indentation of documentation comment continuation lines.
- Fixed "Ensure final newline" adding `\n` to files that use `\r` line breaks.
- Fixed single-line method and accessor updates hard-coding CRLF line endings and four-space indentation,
	dropping comments, and compressing accessor bodies that hold comments, directives or multi-line statements.
- Fixed blank-line padding adding a blank line directly below `#if`, `#elif` or `#else` or above `#elif`, `#else` or
	`#endif`, and `return`/`throw` padding adding one directly below a preprocessor directive. `return`/`throw`
	padding applies only to statements of a braced block; top-level statements and `switch` sections are not padded.
- Fixed region removal and "Update `#endregion` directives" taking exponential time on deeply nested inactive `#if`
	branches and allocating a substring per line.
- Fixed "Move top-level types to separate files" leaving the files it created on disk, next to the unchanged
	original, when the original could not be saved afterwards (headless cleanup) or its editor text could not be
	replaced; the files are removed again. `global using` directives are no longer copied into the created files.
- Fixed the cleanup progress dialog switching to the UI thread through a captured dispatcher instead of the joinable
	task factory, counting a file whose cleanup failed once per cleanup pass instead of once, and Add XMLDoc reporting
	a Cancel pressed just before completion as completed. The dialog must now be created on the UI thread.
- Fixed `nameof` conversion replacing the message argument of `ArgumentException(string)`,
	`ArgumentException(string, string)` and similar constructors; only the parameter-name argument is converted.

## CodeMaid history

The functionality inherited from CodeMaid has its own historical release record in the original repository:

- [CodeMaid repository](https://github.com/codecadwallader/codemaid)
- [CodeMaid changelog history](https://github.com/codecadwallader/codemaid/blob/dev/CHANGELOG.md)

Code Janitor preserves that history for attribution, but future changes are recorded above as Code Janitor changes.
