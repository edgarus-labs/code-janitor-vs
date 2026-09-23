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

Four opt-in settings (all off by default) under **Tools > Options > Code Janitor > Cleaning**
(Update section, group **.editorconfig and Roslyn diagnostics (C#)**) let cleanup fix Roslyn diagnostics with the
code fixes that already exist in Visual Studio and in the project's analyzers:

| Setting | `.codejanitor` key | Fixes |
| --- | --- | --- |
| Apply .editorconfig formatting rules | `applyEditorConfigFormatting` | diagnostics of Roslyn's formatting analyzers |
| Apply .editorconfig naming rules | `applyEditorConfigNaming` | naming-style diagnostics (the fix renames the symbol and its references) |
| Apply .editorconfig code-style preferences | `applyEditorConfigCodeStyle` | other Roslyn IDE code-style diagnostics |
| Apply code fixes from other analyzers | `applyAnalyzerCodeFixes` | diagnostics of any other analyzer (for example analyzer NuGet packages) |

Categories are derived from the analyzer family and descriptor category, not from lists
of diagnostic IDs. Compiler diagnostics are never fixed.

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
- **Safety.** A fix is rejected when it would increase the number of compiler errors in
  any changed project. It is also rejected when it does more than change document text,
  for example adding or removing files or references.
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
- **Requirements.** This feature needs a Visual Studio build whose Roslyn is 5.9 or
  newer. On older hosts it fails with a logged error, and the other cleanup still runs.

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

## Repository-level settings (.codejanitor)

Cleanup behavior can be pinned per repository with a `.codejanitor` (or `.code-janitor.json`) file shared with the VS Code extension. The file is discovered by walking up from the cleaned file's directory; the nearest file wins. Unknown keys, wrong value types and invalid JSON are ignored.

```json
{
  "cleanup": {
    "convertToFileScopedNamespace": true,
    "applyEditorConfigNaming": true,
    "insertBlankLinePadding": false,
    "removeRegions": false,
    "fileHeaderCSharp": "// Copyright (c) Example",
    "fileHeaderPosition": "documentStart",
    "fileHeaderUpdateMode": "replace"
  }
}
```

The schema mirrors the VS Code `codeJanitor.cleanup.*` settings: camelCase keys, the group aliases `insertBlankLinePadding` and `insertExplicitAccessModifiers` (individual keys override the alias), and string-encoded enums for the file header. Repository-only policies without a Visual Studio user setting include `removeRegions` (region removal opt-out) and `organizeUsings` (force using organization independent of `.editorconfig`).

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

The development branch includes installation compatibility for Visual Studio 2022 and Visual Studio 2026. Release support will be documented per published build after the modernization work is verified.
