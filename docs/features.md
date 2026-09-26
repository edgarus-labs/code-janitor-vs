# Features

Code Janitor combines the established CodeMaid feature set with ongoing modernization work.

## Code cleaning

- Normalize whitespace.
- Add unspecified access modifiers where configured.
- Use Visual Studio formatting capabilities.
- Remove and sort using statements.
- Apply cleanup to a file, project or solution.
- Run cleanup on demand or automatically on save.
- Apply inclusion and exclusion rules.
- Support configurable file headers.
- Fix and normalize namespaces.
- Show a one-time cleanup options dialog for selected-scope cleanup.

### Using directive placement (C#)

With **Move using directives outside namespace** enabled, using directives declared inside a
block-scoped or file-scoped namespace are moved to the top of the file. When `.editorconfig`
sets `csharp_using_directive_placement = inside_namespace`, file-level using directives are
moved into the namespace instead (see [Settings precedence](#settings-precedence-editorconfig-codejanitor-user-settings)).
Inside a namespace a
name such as `using Services;` can refer to `Company.App.Services`; at file level it could not.
Every moved directive, alias target and `using static` is therefore resolved with the Roslyn
semantic model. Directives that mean the same at file level keep their exact text (for example
`using Str = System.String;` or `using V1::Lib;`); the others are written fully qualified
(`using Company.App.Services;`). Duplicates of existing top-level directives are dropped; their
comments move to the surviving directive. Comments on using directives, the file header, line
endings and the blank line after the directives are kept.

The move is all-or-nothing per file. The using directives are left in place (the rest of the
cleanup still runs), and the reason is written to the Code Janitor output pane, when:

- a directive cannot be resolved, or its fully qualified form would refer to something else
  at its new place (for example a target reached only through an `extern alias` declared
  inside the namespace, which stays there);
- the file, another document of the project or a project it references uses conditional
  compilation (`#if`/`#elif`) and the move is unsafe in any combination of the relevant
  symbols (symbols of another document count when they change its declarations or directives
  alone or only together, as in `#if A && B`; every combination of up to four symbols is
  verified; with more than four the directives are left in place). Usings inside a namespace
  that is excluded in the current build configuration stay where they are;
- the directives would move across other preprocessor directives (`#region`, `#nullable`,
  `#pragma`, ...) between the file-level using directives and the namespace;
- the file is not part of a C# project in the Visual Studio workspace;
- the file is compiled by several projects or target frameworks (linked files, shared
  projects, multi-targeting) and the move is unsafe in any of them or gives a different
  result in each of them;
- the moved file would have compile errors the original did not have (for example an
  ambiguity after merging the directives of several namespaces);
- a name, member or implicitly called member (for example the `GetEnumerator` of a `foreach`,
  a collection-initializer `Add`, `GetAwaiter`, `Deconstruct` or a query operator) would bind
  to a different symbol after the move (an import searched after a same-named type or an
  extension method of an enclosing namespace, or a same-named type of another assembly);
- a moved directive imports an extension member that the compiler calls without exposing the
  binding to verify: `Add` or `GetEnumerator` used by a collection expression or spread
  element, `GetPinnableReference` used by a `fixed` statement, or `operator ==`/`!=` used
  element-wise by tuple equality.

The step needs the Visual Studio Roslyn workspace (Roslyn 5.0 or newer, which every supported Visual Studio ships). It runs before the
text cleanup and the type split, so the file header, using organization and split files see the
moved directives. Region directives are removed before moving whenever cleanup would remove
them anyway (unless the repository policy keeps regions).
It is not part of the C# text cleanup preview. Converting to a
file-scoped namespace keeps using directives inside the namespace; they are moved only by this
step. The file-scoped conversion itself runs only when every project and target framework that
compiles the file uses C# 10 or newer (read from the Visual Studio Roslyn workspace); otherwise,
or when the language version is unknown, the namespace stays block-scoped and the reason is
written to the output pane. With `csharp_style_namespace_declarations = block_scoped`, a
file-scoped namespace is converted back to a block-scoped one.

The inward move uses the same all-or-nothing checks. A directive that would bind to a
different symbol inside the namespace is written `global::`-qualified. Files with no namespace
or with several namespaces, and files with anything other than using and extern alias
directives outside their single namespace (a type, a delegate, top-level statements or an
assembly attribute such as `[assembly: InternalsVisibleTo(...)]`), are left unchanged without
analysis, so no reason is written to the output pane for them.

### C# text cleanup preview

The selected-scope cleanup options dialog includes **Preview C# Text Changes**.
It builds a plan using the configured deterministic C# text transformations without
writing source files or invoking AI. The preview provides:

- a native, read-only Visual Studio side-by-side diff, with before/after text fallback;
- per-file inclusion and per-file rule selection, recalculated from the original text;
- rule outcomes: Changed, No change or Excluded;
- explicit skip/error statuses;
- application of the approved result to editor buffers, rejecting stale input;
- existing undo-transaction integration, without automatic file saving.

This is not a preview of the complete editor cleanup workflow. AI, type splitting,
namespace fixing, reorganization, Visual Studio formatting and encoding changes
are outside this first increment. **Start Cleanup** retains its existing behavior.
See [Cleanup Preview](cleanup-preview.md) for instructions and limitations.

### .editorconfig and Roslyn diagnostic cleanup (C#)

C# cleanup fixes the Roslyn diagnostics that the repository's `.editorconfig` configures,
with the code fixes that already exist in Visual Studio and in the project's analyzers:
formatting, naming (the fix renames the symbol and its references), IDE code style and
other analyzers (for example analyzer NuGet packages). There is no separate setting:
`.editorconfig` decides which rules apply. Compiler diagnostics are never fixed.

- **Configured rules only.** A rule is fixed only when the repository configures it:
  `dotnet_diagnostic.<id>.severity`, an analyzer category or global severity, an
  `option = value:severity` suffix or a naming rule's `severity`. Rules that are active
  only through Visual Studio defaults (for example IDE0130, namespace must match folder)
  are left alone.

- **Source of truth.** Rules and severities come from the `.editorconfig` files that apply
  to the file, including nested files and `root = true`, as evaluated by Roslyn
  itself. Code Janitor does not parse or reimplement the rules.
- **Severity.** Only diagnostics reported as `suggestion`, `warning` or `error` are
  fixed. `silent` and `none` are not, and suppressed diagnostics are ignored.
- **Severity syntax.** Both `dotnet_diagnostic.<id>.severity = warning` and the
  `option = value:severity` suffix (plus the naming rule's `severity`) are honored as
  Roslyn reports them.
- **Fix selection.** For each diagnostic, cleanup uses the first top-level code action of
  the first provider that offers one, ignoring actions that only offer nested choices.
  Cleanup applies the fix to the whole document when the provider supports Fix All.
  Otherwise it fixes one diagnostic at a time and analyzes the document again, up to
  50 passes.
- **Safety.** A fix is rejected when it would add any compiler error to a changed project,
  even if it also removes another one. It is also rejected when it does more than change
  document text, for example adding or removing files or references. When the file is also
  compiled by other projects or target frameworks (linked files, shared projects,
  multi-targeting), the new text is checked in each of them first; if any gets a new compiler
  error, nothing is applied and the failure names that project and error.
- **Unsupported and unsafe diagnostics.** Code Janitor never modifies code for a
  diagnostic without a code fix, without a usable code action, with a rejected fix or
  that does not converge. It reports the diagnostic in the CodeJanitor output pane.
  Batch cleanup reports `Diagnostics: N fixed / M unresolved` and warns on completion
  when any file still has unresolved diagnostics. The active-document cleanup status bar
  also says that some diagnostics were not fixed. Cleanup counts diagnostic cleanup
  failures as failed items.
- **When it runs.** It runs after the other C# cleanup steps, both for headless file
  cleanup and for editor cleanup, including cleanup on save. With parallel cleanup it
  runs one file at a time after the parallel pass. Changes are applied through the Visual
  Studio Roslyn workspace. A rename can therefore change other files.
- **No Code Cleanup profiles.** Visual Studio's Code Cleanup profiles are never used, so
  the result does not depend on per-user profile configuration.
- **Multi-targeted and linked files.** A file shared by several projects or target
  frameworks is analyzed in one context: the project that contains the cleaned item,
  otherwise the first by project path and name.
- **Requirements.** This feature needs Roslyn 5.0 or newer, which every supported Visual
  Studio version (2026, 18.0 or newer) ships.

## Code organization

- Reorganize members according to configured conventions.
- Generate or remove regions.
- Sort selected code.
- Join adjacent lines or selected code.
- Navigate through code structure.
- Display McCabe complexity information where supported.

## Razor and Blazor

Code Janitor adds a safe Razor formatter for modern Razor and Blazor development.

The formatter currently targets:

- Razor control blocks;
- C# code embedded in Razor;
- HTML markup inside Razor blocks;
- `@if`, `@else`, `@else if`, `@for`, `@foreach`, `@while`, `@switch`;
- `@try`, `@catch` and `@finally`;
- regression-tested formatting behavior;
- configurable Razor formatting options.

The formatter is designed to be conservative. It should not alter content that it cannot safely understand.

## AI-assisted XML documentation

Code Janitor includes optional AI-assisted XML documentation cleanup for C# code.

The feature is designed to help improve or complete XML documentation while keeping the developer in control through:

- explicit XMLDoc controls;
- configurable filters;
- scope selection;
- budget limits;
- secure settings;
- controlled cleanup operations.

AI-assisted processing is opt-in. The project does not treat AI processing as a replacement for compiler diagnostics, code review or developer judgment. Sensitive or proprietary code should only be processed when the user has reviewed the configured provider and deployment model.

## Settings

Settings are provided by grouped WPF pages under **Tools > Options > Code Janitor**, registered through the classic Visual Studio SDK. The experimental VisualStudio.Extensibility settings bridge has been removed; it is not part of the extension's current feature set.

## Settings precedence (.editorconfig, .codejanitor, user settings)

Each cleanup setting is resolved per file, in the editor and for closed files alike:

1. `.editorconfig`, for the keys in the table below. `.editorconfig` is then the source of
   truth in both directions. A value with the severity suffix `:none` (for example
   `csharp_style_namespace_declarations = file_scoped:none`) is ignored, as if the key were
   not there: the `.codejanitor` entry for that step decides, or else your Visual Studio
   setting. Any other severity (`silent`, `suggestion`, `warning`, `error`) or no suffix
   enforces the value for Code Janitor's own steps;
2. the `.codejanitor` repository policy;
3. your Visual Studio settings.

| `.editorconfig` key | Code Janitor step |
|---|---|
| `csharp_style_namespace_declarations` = `file_scoped` / `block_scoped` | convert to file-scoped (C# 10+) / convert to block-scoped; the body moves by one `indent_size` (`tab_width` when `indent_size = tab`, otherwise 4 spaces) |
| `csharp_using_directive_placement` = `outside_namespace` / `inside_namespace` | move using directives outside / inside the namespace |
| `indent_style` = `space` / `tab` (`tab_width`, `indent_size`) | leading tabs to spaces / leading spaces to tabs (closed-file cleanup; the editor uses Visual Studio formatting) |
| `insert_final_newline` = `true` / `false` | ensure / remove the final newline |
| `trim_trailing_whitespace` | remove end-of-line whitespace |
| `dotnet_sort_system_directives_first`, `dotnet_separate_import_directive_groups` | organize using directives (only when `System` directives go first and groups are not separated) |
| `csharp_style_var_when_type_is_apparent` | convert to `var` when the type is apparent |
| `dotnet_style_require_accessibility_modifiers` (`always`, `for_non_interface_members` / `never`, `omit_if_default`) | insert explicit access modifiers / do not insert them |
| `csharp_style_inlined_variable_declaration` | inline `out` variable declarations |
| `dotnet_style_prefer_collection_expression` | convert to collection expressions |
| `dotnet_style_readonly_field` | make fields `readonly` when safe |
| `file_header_template` (`unset` = no header) | C# file header (each template line is written as a `//` comment) |

Where Code Janitor has no step for the opposite style (for example `var` to explicit types),
its own step is turned off and the diagnostic cleanup applies the Roslyn code fix when the rule
is reported as `suggestion`, `warning` or `error` (not `silent` or `none`).

Options shows which of these settings the open solution's `.editorconfig` overrides: under each
affected option (Cleaning > Remove, Update and Insert), a note names the key and the `.editorconfig`
file that sets it, for example *Overridden by .editorconfig: csharp_style_var_when_type_is_apparent
in C:\repo\.editorconfig*. Visual Studio options are global, so the note describes a C# file in the
solution's directory: `.editorconfig` files nested below it are not considered, and a key ignored with
`:none` or an unrecognized value shows no note. The option stays editable and applies wherever
`.editorconfig` does not set the key. No note is shown when no solution is open.

## Repository-level settings (.codejanitor)

Cleanup behavior can be pinned per repository with a `.codejanitor` (or `.code-janitor.json`) file shared with the VS Code extension. The file is discovered by walking up from the cleaned file's directory; the nearest file wins. Unknown keys, wrong value types and invalid JSON are ignored.

```json
{
  "cleanup": {
    "convertToFileScopedNamespace": true,
    "insertBlankLinePadding": false,
    "removeRegions": false,
    "fileHeaderCSharp": "// Copyright (c) Example",
    "fileHeaderPosition": "documentStart",
    "fileHeaderUpdateMode": "replace"
  }
}
```

The schema mirrors the VS Code `codeJanitor.cleanup.*` settings: camelCase keys, the group aliases `insertBlankLinePadding` and `insertExplicitAccessModifiers` (individual keys override the alias), and string-encoded enums for the file header. Repository-only policies without a Visual Studio user setting include `removeRegions` (region removal opt-out) and `organizeUsings` (force using organization when `.editorconfig` does not configure the using order). `.codejanitor` values apply in the editor as well as to closed files; keys that `.editorconfig` defines take precedence over them.

Two commands manage the file from the Code Janitor menu:

- **Export Settings to .codejanitor** writes the current user settings next to the solution file.
- **Import Settings from .codejanitor** applies the repository file to the current user settings.

## Navigation and workflow

- Find the active file in Solution Explorer.
- Collapse Solution Explorer nodes recursively.
- Switch between related files.
- Toggle read-only file state.
- Display build progress in Visual Studio and the Windows taskbar.
- Integrate with selected third-party cleanup tools.

## Supported Visual Studio versions

The VSIX installs only on Visual Studio 2026 (18.0 or newer; Community, Professional and Enterprise). Code Janitor uses the Roslyn that Visual Studio ships instead of carrying its own copy and is built against Roslyn 5.0, the version of Visual Studio 2026 18.0. Visual Studio 2022 (Roslyn 4.x) is not supported, and the installer rejects it.
