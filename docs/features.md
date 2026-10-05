# Features

Code Janitor combines the established CodeMaid feature set with ongoing modernization work.

## Code cleaning

- Normalize whitespace.
- Add unspecified access modifiers (one setting; Roslyn IDE0040).
- Use Visual Studio formatting capabilities.
- Remove and sort using statements.
- Apply cleanup to a file, project or solution.
- Run cleanup on demand or automatically on save.
- Apply inclusion and exclusion rules.
- Support configurable file headers.
- Fix namespaces to match the folder structure (Roslyn IDE0130).
- Show a one-time cleanup options dialog for selected-scope cleanup.

### Steps applied through Roslyn rules (C#)

These Cleaning settings apply Roslyn's own analyzer and code fix in the diagnostic cleanup
(see [Settings precedence](#settings-precedence-editorconfig-codejanitor-user-settings)), the same way
**Make Fields Readonly** applies IDE0044. When the repository's `.editorconfig` enforces the rule, its
value wins; otherwise Code Janitor applies the value listed here while the setting is on.

| Setting | Rule | Value applied when `.editorconfig` does not decide the rule |
|---|---|---|
| Convert local variables to `var` when the type is apparent | [IDE0007](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0007-ide0008) | `csharp_style_var_when_type_is_apparent = true`; `csharp_style_var_for_built_in_types` and `csharp_style_var_elsewhere` are off unless `.editorconfig` enforces them, so built-in and non-apparent types keep their explicit type |
| Inline `out` variable declarations | [IDE0018](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0018) | `csharp_style_inlined_variable_declaration = true` |
| Convert array and collection initializations to collection expressions when the types match exactly | [IDE0300–IDE0306](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0300) | `dotnet_style_prefer_collection_expression = true` (types must match exactly; `when_types_loosely_match` can change semantics, so `IEnumerable<int> x = new int[] { 1 }` keeps the array). Needs C# 12, checked by Roslyn |
| Use expression bodies for lambdas | [IDE0053](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0053) | `csharp_style_expression_bodied_lambdas = true` |
| Insert explicit accessibility modifiers | [IDE0040](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0040) | `dotnet_style_require_accessibility_modifiers = for_non_interface_members` (interface members get no modifier) |
| Convert block-scoped namespace to file-scoped | [IDE0161](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0160-ide0161) | `csharp_style_namespace_declarations = file_scoped` (`block_scoped` from `.editorconfig` converts back, IDE0160). Needs C# 10, checked by Roslyn |
| Move using directives outside namespace | [IDE0065](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0065) | `csharp_using_directive_placement = outside_namespace` (`inside_namespace` from `.editorconfig` moves them in) |
| Remove multiple consecutive blank lines | [IDE2000](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide2000) (experimental) | `dotnet_style_allow_multiple_blank_lines_experimental = false`. C# only; VB, C/C++, markup and other files use Code Janitor's own text step |

**Insert explicit accessibility modifiers** is one checkbox under Cleaning > Insert
(`.codejanitor` key `insertExplicitAccessModifiers`, on by default); there are no per-kind settings.

These steps run in the diagnostic cleanup after the other Code Janitor steps, for open and closed
files, and are not part of the C# text cleanup preview. They run on the cleaned file only, so files
created by **Move top-level types to separate files** get them when those files are cleaned.

The **Fix Namespace** command runs only
[IDE0130](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0130)
(`dotnet_style_namespace_match_folder`) through the diagnostic cleanup. Its code fix also updates
references in other files. It needs the project's root namespace and directory, which Visual Studio
provides, and reports the changed files, the IDE0130 diagnostics it could not fix, and failures. Files
IDE0130 does not report (for example a type declared in several files, or a file without a namespace)
are left unchanged.

Some steps keep Code Janitor's own implementation because no Roslyn rule of the Visual Studio host
covers them:

- pattern-matching null checks: IDE0041 covers only `ReferenceEquals(x, null)` and
  `(object)x == null`, IDE0150 only `is T` type checks; no rule converts `x == null`;
- `nameof` (CA1507) and `JsonSerializerOptions` reuse (CA1869): .NET analyzers, not part of the
  Visual Studio host analyzers;
- string-format-to-interpolation: there is only a refactoring, no diagnostic;
- blank lines after `{` / before `}`: IDE2002 removes only blank lines between two consecutive braces.

### Class sealing (C#)

With **Seal Classes** enabled (CA1852), a class gets the `sealed` modifier only where the Roslyn
semantic model shows that nothing depends on it staying open, in every project that compiles the
file. A class is left unchanged when:

- it is abstract, static, already sealed or declared in several parts, or declares a virtual or abstract member or a
  non-overriding protected member (`protected`, `protected internal`, `private protected`),
  including a protected constructor;
- any class in the solution derives from it, or a generic constraint names it;
- code elsewhere in the solution casts, `as`-converts, pattern-matches, uses as a `case` type label or `foreach`
  element type, or compares with `==`/`!=` to or from an interface the class does not implement, including
  through arrays, tuple elements, covariant delegate type arguments and generic collection interfaces (sealing
  would turn these into compile errors);
- its project, or a project referencing it, contains code excluded by a preprocessor directive (an inactive `#if`
  branch is compiled by other build configurations and can derive from, constrain or convert the class without
  naming it);
- a project written in another language than C# references its project, directly or through other projects
  (only C# is analyzed); for projects outside the Roslyn workspace, such as C++/CLI projects, this is read from
  their `ProjectReference` items, whatever their conditions.

Documents produced by source generators are analyzed like written files. The step is skipped for a file, with
a warning in the output pane, while the solution is loading or a project is unloaded or failed to load, when
Visual Studio cannot tell whether that is the case, or when a `ProjectReference` of a project outside the Roslyn
workspace names its project with an MSBuild property or a wildcard, because such a project could contain a class
that derives from the sealed one. The analysis cannot see runtime uses: a
class that a test project mocks or proxies (`Mock<Foo>`, `Substitute.For<Foo>()`) compiles once sealed but fails
at run time, so turn **Seal Classes** off for such projects.

The step needs the Visual Studio Roslyn workspace and runs for open documents and closed files
alike, before the type split. It is not part of the C# text cleanup preview.

### Pattern-matching null checks (C#)

With **Convert null checks to pattern matching** enabled, `x == null` becomes `x is null` and
`x != null` becomes `x is not null`, only where the Roslyn semantic model proves the result means
the same. A check is left unchanged when:

- `==`/`!=` binds to a user-defined operator, declared in the same file, another file, another
  project or a referenced assembly (for example `UnityEngine.Object`), including a lifted
  operator of a nullable struct and an operator reached through a type parameter's constraint;
- the compared operand is not known to be a reference type, `Nullable<T>` or a type parameter
  not constrained to a value type (non-nullable value types, `dynamic`, unresolved types);
- the project's C# version does not allow it: `is null` needs C# 7.0, `is not null` needs C# 9;
- it is in a non-async expression-bodied lambda or a query clause, which may become an expression tree;
- it compares an equality (`a == b == null`) or has comments between the operands with `null` on
  the left.

A file compiled by several projects or target frameworks is converted only where the
conversion is safe in every one of them. The step needs the Visual Studio Roslyn workspace and
runs for open documents and closed files alike, before the type split. It is not part of the C#
text cleanup preview.

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
other analyzers (for example analyzer NuGet packages). `.editorconfig` decides which rules
apply; the Code Style rules enabled in Options add the rules it does not enforce (see
[Code Style rules](#code-style-rules)). Compiler diagnostics are never fixed.

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

### Code Style rules

**Tools > Options > Code Janitor > Cleaning > Code Style** lists the Roslyn code-style options
that Code Janitor can apply when the repository's `.editorconfig` does not enforce them: modifiers
(`csharp_preferred_modifier_order`, static local and anonymous functions, `readonly` structs and
struct members), blocks (`csharp_prefer_braces`, simple `using`, method group conversion),
expression-bodied members, pattern matching, null checking, modern expressions (primary
constructors, target-typed `new()`, index and range operators, UTF-8 literals, tuple swap,
deconstruction, unused value assignments, `default` literal, auto-properties, compound assignment,
simplified boolean expressions and interpolation, object and collection initializers, tuple names),
`this.` qualification, language keywords vs. framework type names, and parentheses. Each rule has an
on/off switch and its value; all are off by default. Naming rules (`dotnet_naming_*`) are configured
in `.editorconfig` only.

- **Precedence.** A rule `.editorconfig` enforces (see
  [Settings precedence](#settings-precedence-editorconfig-codejanitor-user-settings)) keeps its
  `.editorconfig` value: Options shows the override note and disables the rule. Otherwise the
  `.codejanitor` `codeStyle` entry decides, and otherwise the Options switch.
- **How it applies.** The diagnostic cleanup analyzes the file with the enabled rules added on top
  of `.editorconfig` (as `suggestion`) and applies the Roslyn code fixes, with the same safety rules:
  a rule without a code fix, or whose fix would add a compiler error, is reported as unresolved and
  the file is left unchanged by it. The added configuration is used for analysis only; no
  `.editorconfig` file is written or changed.
- **Rules sharing a diagnostic.** Some options report the same diagnostic (for example every
  `dotnet_style_qualification_for_*` option reports IDE0003/IDE0009). Enabling one of them does not
  apply the others, unless `.editorconfig` enforces them.

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

1. `.editorconfig`, for the keys in the table below and the Code Style rules. `.editorconfig` is
   then the source of truth in both directions. For a key tied to Roslyn diagnostics (marked with
   their IDs below, and every Code Style rule), Code Janitor resolves the severity of each diagnostic
   as Roslyn does: `dotnet_diagnostic.<id>.severity`, then
   `dotnet_analyzer_diagnostic.category-<category>.severity` (`Style` for IDE rules), then
   `dotnet_analyzer_diagnostic.severity`, then the key's own suffix. A key without a suffix counts as
   enforcing, and a `:none` suffix turns the rule off whatever else is set. The rule is enforced
   when any of its diagnostics resolves to `suggestion`, `warning` or `error`. The key's value is used,
   or Roslyn's default when only a severity is set. `none`, `silent` and `refactoring` do not enforce,
   so, for example, `csharp_style_namespace_declarations = file_scoped:silent` alone is ignored as if the key
   were not there, but together with `dotnet_diagnostic.IDE0161.severity = warning` it is enforced.
   A key ignored this way, or with an unrecognized value, leaves the decision to the `.codejanitor`
   entry for that step, or else to your Visual Studio setting. A bulk
   `dotnet_analyzer_diagnostic.category-Style.severity = suggestion` or higher therefore enforces every
   IDE-backed step and Code Style rule, and locks them in Options. Keys without diagnostics (`indent_*`,
   `insert_final_newline`, `trim_trailing_whitespace`, the using order keys, `file_header_template`)
   use only their own suffix;
2. the `.codejanitor` repository policy;
3. your Visual Studio settings.

| `.editorconfig` key | Code Janitor step |
|---|---|
| `csharp_style_namespace_declarations` (IDE0160, IDE0161) = `file_scoped` / `block_scoped` | convert to file-scoped / block-scoped namespace (Roslyn code fix) |
| `csharp_using_directive_placement` (IDE0065) = `outside_namespace` / `inside_namespace` | move using directives outside / inside the namespace (Roslyn code fix) |
| `indent_style` = `space` / `tab` (`tab_width`, `indent_size`) | leading tabs to spaces / leading spaces to tabs (closed-file cleanup; the editor uses Visual Studio formatting) |
| `insert_final_newline` = `true` / `false` | ensure / remove the final newline |
| `trim_trailing_whitespace` | remove end-of-line whitespace |
| `dotnet_sort_system_directives_first`, `dotnet_separate_import_directive_groups` | organize using directives (only when `System` directives go first and groups are not separated) |
| `csharp_style_var_when_type_is_apparent` (IDE0007, IDE0008) | convert to `var` when the type is apparent |
| `dotnet_style_require_accessibility_modifiers` (IDE0040) | insert explicit access modifiers (Roslyn code fix; Code Janitor's value is `for_non_interface_members`) |
| `csharp_style_inlined_variable_declaration` (IDE0018) | inline `out` variable declarations |
| `dotnet_style_prefer_collection_expression` (IDE0300–IDE0306) | convert to collection expressions |
| `dotnet_style_readonly_field` (IDE0044) | make fields `readonly` when safe |
| `file_header_template` (`unset` = no header) | C# file header (each template line is written as a `//` comment) |
| `csharp_style_expression_bodied_lambdas` (IDE0053) | simplify single-statement lambdas (`false` turns it off) |
| `csharp_style_prefer_null_check_over_type_check` (IDE0150), `dotnet_style_prefer_is_null_check_over_reference_equality_method` (IDE0041) | convert to pattern-matching null checks (off when an enforced key is `false`) |
| `dotnet_diagnostic.CA1852.severity` | seal classes when safe (on; disabled by default in Roslyn, so category and global severities do not enable it) |
| `dotnet_diagnostic.CA1507.severity` | convert strings to `nameof` (on) |
| `dotnet_diagnostic.CA1869.severity` | reuse `JsonSerializerOptions` (on) |
| `dotnet_style_allow_multiple_blank_lines_experimental` (IDE2000) | remove multiple consecutive blank lines (`false` turns it on; C# through the Roslyn code fix) |
| `csharp_style_allow_blank_lines_between_consecutive_braces_experimental` (IDE2002) | remove blank lines after opening / before closing braces (`false` turns them on) |
| `dotnet_diagnostic.IDE0005.severity` | run Visual Studio Remove and Sort Usings (on) |
| `dotnet_diagnostic.IDE0055.severity` | run Visual Studio Format Document (on) |

Rules without a Code Janitor setting for the opposite style (for example `var` to explicit types)
are fixed by the diagnostic cleanup when the rule is reported as `suggestion`, `warning` or `error`
(not `silent` or `none`).

Options shows which of these settings the open solution's `.editorconfig` overrides: under each
affected option (Cleaning > Visual Studio, Remove, Update, Insert and Code Style), a note names the key and the `.editorconfig`
file that sets it, for example *Overridden by .editorconfig: csharp_style_var_when_type_is_apparent
in C:\repo\.editorconfig*. Visual Studio options are global, so the note describes a C# file in the
solution's directory: `.editorconfig` files nested below it are not considered, and a key that is not
enforced (see above) or has an unrecognized value shows no note. An option with a note is disabled, because its
Visual Studio setting has no effect for the open solution; it still applies to files outside that
`.editorconfig`'s scope, but cannot be edited while the note is shown. No note is shown, and every
option is editable, when no solution is open.

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
    "fileHeaderUpdateMode": "replace",
    "codeStyle": {
      "csharp_prefer_braces": "when_multiline",
      "dotnet_style_qualification_for_field": null
    }
  }
}
```

The schema mirrors the VS Code `codeJanitor.cleanup.*` settings: camelCase keys, the group alias `insertBlankLinePadding` (individual keys override the alias), the single key `insertExplicitAccessModifiers` for access modifiers, and string-encoded enums for the file header. Repository-only policies without a Visual Studio user setting include `removeRegions` (region removal opt-out) and `organizeUsings` (force using organization when `.editorconfig` does not configure the using order). `codeStyle` sets the [Code Style rules](#code-style-rules), keyed by `.editorconfig` option name: a string value (matched ignoring case, so `"True"` works; JSON booleans are ignored) enables the rule with that value and `null` disables it; a rule it does not list follows the Options switch. `.codejanitor` values apply in the editor as well as to closed files; keys that `.editorconfig` enforces take precedence over them.

Two commands manage the file from the Code Janitor menu:

- **Export Settings to .codejanitor** writes the current user settings next to the solution file. Its `codeStyle` section lists only the enabled rules, so disabled rules are not pinned and follow each user's Options switch; add `null` entries by hand to pin a rule off.
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
