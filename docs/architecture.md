# Architecture Overview

Code Janitor is a Visual Studio extension built around the original CodeMaid extension architecture, with modernization work being introduced incrementally.

## Main areas

### Visual Studio integration

This area contains the package, commands, tool windows, settings registration and VSIX deployment metadata required to run inside Visual Studio. Settings use WPF pages registered with the classic Visual Studio SDK. The unused VisualStudio.Extensibility bridge and its SDK dependencies have been removed.

### Shared logic

Shared logic contains cleaning, formatting, organization and parsing behavior that can be tested independently from the Visual Studio shell.

### Unit tests

Unit tests protect behavior that is especially sensitive to source syntax, including code cleaning and Razor formatting.

### Cleanup preview

`CodeCleanupManager.CreateHeadlessCSharpPipeline` builds the same configured text
pipeline used by ordinary headless C# cleanup. `SourceTransformationPipeline.Preview`
returns immutable original/output snapshots and per-step outcomes; excluded rule
indices are evaluated in the original pipeline order. `Run` and `Preview` share
the execution loop to avoid behavior drift.

The selected-scope command captures source snapshots, presents the preview and
applies only selected results to editor buffers after an ordinal source comparison.
It does not save files or invoke ordinary cleanup after approval. The WPF dialog
hosts Visual Studio's read-only difference viewer over in-memory buffers, disposing
the viewer/buffer when the selection changes or the dialog closes. A text fallback
is available when the native difference service cannot be used.

This first increment does not plan disk operations, encoding changes, AI calls or
editor-only cleanup. See [Cleanup Preview](cleanup-preview.md) for the boundary.

### .editorconfig/Roslyn diagnostic cleanup

The engine in `Logic/Cleaning/Diagnostics` (`DiagnosticCleanupEngine`,
`CodeFixProviderCatalog`, `DiagnosticCleanupCategoryClassifier`) is host-agnostic. It
uses only the public Microsoft.CodeAnalysis Workspaces API: it analyzes one `Document`
with the project's analyzers and `.editorconfig` analyzer options, then applies existing
`CodeFixProvider`s in a deterministic, safety-gated loop and returns a
`DiagnosticCleanupResult` (changed solution, applied fixes, unresolved diagnostics).

`EditorConfigDiagnosticCleanupLogic` is the Visual Studio host adapter. Through
`VisualStudioRoslynWorkspace` it:

- resolves `VisualStudioWorkspace` through MEF (`IComponentModel`) by type name;
- supplies the C# `CodeFixProvider` MEF exports, cached per package;
- builds the input as `CurrentSolution.WithDocumentText(...)` from the cleaned editor
  buffer or the file written by headless cleanup;
- for a closed file, first runs `RoslynDocumentCleanup`, the Roslyn equivalents of the Visual
  Studio "Remove and Sort Usings" (removes `CS8019` usings line by line, skips files with `#if`,
  keeps the configured reinsert list, then `Formatter.OrganizeImportsAsync`) and "Format
  Document" (`Formatter.FormatAsync`) commands, gated by the same settings as the editor
  commands, so closed files are never opened in an editor;
- runs the engine off the UI thread; once a fix changed the document, the engine sorts its using
  directives as the step that sorts them before the fixes does
  (`EffectiveCleanupSettings.GetUsingDirectiveSortingAfterFixes`, `DiagnosticCleanupOptions.UsingDirectiveSorting`):
  Roslyn's organize imports when "Remove and Sort Usings" runs, `UsingDirectiveOrganizer` for a closed file
  when only the `organizeUsings` policy applies (the organizer of the headless cleanup; open files are not
  sorted by it), so directives moved by IDE0065 are sorted in the same run and the next cleanup
  does not reorder them; the engine's gate rejects a fix that adds any compiler
  error to a project it changed, even one that removed another error (`CompilerErrors`
  compares the errors as a multiset by id and file, matching an error either by message
  (`CompilerErrorMatching.MatchByMessage`) or by its position mapped through the fix's text changes, so a rename that only
  changes the message of an existing error is not rejected);
- before applying, checks every other project flavor of each changed file (linked files,
  shared projects, multi-targeting) with the new text and fails the cleanup, naming the
  project and the first new error, when any flavor gets a compiler error it did not have;
- for a closed file whose change touches only that file and needs no host operation, writes
  the new text straight to disk (`VisualStudioRoslynWorkspace.TryWriteClosedFileText`, which
  refuses the write when the file on disk no longer matches the cleaned input; the cleanup is
  then recomputed); a file opened meanwhile or one that cannot be written directly (read-only,
  source control checkout) goes through the workspace;
- otherwise applies the result with `Workspace.TryApplyChanges` on the UI thread, recomputing
  once when the workspace changed and then failing explicitly.

`CodeCleanupManager` calls the adapter after the existing C# cleanup, both in the headless
path (`RunDiagnosticCleanupAsync`) and in the editor path. `CleanupProgressViewModel` calls
it one file at a time after the parallel pass. Outcomes are recorded as
`DiagnosticChangedItems` (diagnostic fixes only; changes made only by the Remove and Sort
Usings / Format Document equivalents are logged but not counted), `DiagnosticUnresolvedItems`
and failed items in `CleanupExecutionStats`.

### Cleanup settings precedence

`EffectiveCleanupSettings.For(filePath)` resolves every per-file cleanup setting for both the
editor path (the EnvDTE `*Logic` classes, resolved once per document in
`CodeCleanupManager`) and the headless text pipeline (`CreateHeadlessCSharpPipeline`):

1. `.editorconfig`, for the keys it maps (see `docs/features.md`), read with Roslyn's
   `AnalyzerConfigSet` (`EditorConfigHelper`, which also reports `dotnet_diagnostic.<id>.severity`),
   so sections, globs, `root = true` and nearest-file-wins follow the EditorConfig rules. For keys
   tied to Roslyn diagnostics (`TryReadRule`), each diagnostic's severity is resolved in Roslyn's
   order (`dotnet_diagnostic.<id>.severity`, category, global, then the key's suffix; a `:none` suffix
   disables the rule); the rule is enforced when any diagnostic is `suggestion` or higher. Other keys
   use only their suffix. `none`, `silent` or `refactoring` is ignored, so the next source decides;
2. the `.codejanitor` repository policy (`RepositoryCleanupSettings`);
3. the user's Visual Studio settings.

The same order resolves the Code Style rules (`CodeStyleRules`): a rule `.editorconfig` does not
enforce is taken from `.codejanitor` `codeStyle` or the `Cleaning_CodeStyleRules` setting.
`EffectiveCleanupSettings.AnalyzerConfigOverrides` turns them into analyzer configuration entries
(the rule's value with `suggestion`, its diagnostic IDs raised to `suggestion`, other rules sharing
those IDs set to `none`). `DiagnosticCleanupEngine` appends them as the last section of an
in-memory `.editorconfig` in the document's directory, so they win over every other analyzer
configuration, analyzes and fixes with that solution, and moves only the resulting document texts
onto the original solution, so no configuration change ever reaches the workspace. Each changed
text gets the line ending of its file (`end_of_line`, otherwise the one line ending the original text
uses throughout), because some code fixes insert line breaks of their own.

Indentation is directional (`IndentationPreference`). Indentation and the final newline reverse
natively (`SpaceToTabConverter`, `RemoveFinalNewlineConverter`).

For each Roslyn step below, a setting `.editorconfig` does not decide and that is enabled gets Code
Janitor's option value with `suggestion` and the step's diagnostic IDs raised to `suggestion`. When
`.editorconfig` decides the setting by its option value, that value is re-emitted lower-cased with
`suggestion`, and the diagnostic IDs of the decided direction that have no `dotnet_diagnostic`,
category or global severity are raised to `suggestion`: the step's IDs when the value enables the
step, its reverse IDs when it disables it. Only two steps have a reverse direction applied this
way: IDE0160 (`csharp_style_namespace_declarations = block_scoped`) and IDE0065
(`csharp_using_directive_placement = inside_namespace`). Other disabling values (for example `var`
to explicit types or `never` accessibility modifiers) are not raised by Code Janitor; the diagnostic
cleanup fixes them only when `.editorconfig` reports them at `suggestion`, `warning` or `error`.
When `.editorconfig` decides a setting only through diagnostic severities, no entry is added and
Roslyn reports those IDs as configured. `csharp_style_var_for_built_in_types` and
`csharp_style_var_elsewhere` are set to `true:none` only when `.editorconfig` sets them to `true`
without enforcing them, so IDE0007 does not surface for them; otherwise they are left as configured.

### Steps applied through Roslyn rules

Several Cleaning settings have no Code Janitor implementation; they enable a Roslyn rule.
`EffectiveCleanupSettings.RoslynSteps` lists these steps with their settings, diagnostic
IDs and option values: IDE0007 (`csharp_style_var_when_type_is_apparent = true`, other `var`
options off), IDE0018, IDE0300–IDE0306 (`dotnet_style_prefer_collection_expression = true`),
IDE0053, IDE0040 (`for_non_interface_members`), IDE0161/IDE0160
(`csharp_style_namespace_declarations`), IDE0065 (`csharp_using_directive_placement`) and, for C#,
IDE2000 (`dotnet_style_allow_multiple_blank_lines_experimental = false`), alongside IDE0044. A step
whose rule `.editorconfig` enforces keeps the `.editorconfig` value.
`EffectiveCleanupSettings.AnalyzerConfigOverrides` turns these steps into the same in-memory
analyzer configuration entries as the Code Style rules, and `DiagnosticCleanupEngine` analyzes and
fixes them after the other cleanup steps, for open and closed files. Language-version
requirements (C# 10 for file-scoped namespaces, C# 12 for collection expressions) are the analyzers'
own checks. None of these steps is a text transformation, so the headless text pipeline and the
cleanup preview do not include them.

`DiagnosticCleanupOptions.DiagnosticIds` restricts a run to the given diagnostic IDs.
`DiagnosticCleanupOptions.NamespaceMatchFolder` uses it for the Fix Namespace command, which runs only
IDE0130 (`dotnet_style_namespace_match_folder`) with the project's root namespace and directory from
Visual Studio; the code fix also updates references in other files.

Class sealing (`SealedClassLogic`) and pattern-matching null checks (`NullCheckPatternMatchingLogic`)
run on the Visual Studio Roslyn workspace through the shared `SemanticFileRewriter` (setting check,
document resolution for every project flavor, encoding-preserving file write, editor buffer
replacement, output-pane warnings). They run before the headless cleanup for closed files, and in
the editor before the type split when splitting is enabled (then not again in
`RunCodeCleanupCSharp`), so a type moved to a created file is already processed. Neither is a text
transformation, so the headless pipeline and the cleanup preview do not include them.

Packaging: `Microsoft.CodeAnalysis.CSharp.Workspaces` uses the same version as
`Microsoft.CodeAnalysis.CSharp` (5.0.0, the lowest version that provides every API used). Neither ships in the VSIX: Code Janitor binds to the
host's Roslyn through Visual Studio's binding redirects, which is required anyway because
`VisualStudioWorkspace` and the MEF code fix providers are host objects. The manifest therefore
installs on Visual Studio 2026 18.0 or newer, whose Roslyn is 5.0 or newer. The unit tests use
Roslyn 5.9 so that they cover newer C# syntax.
`Microsoft.VisualStudio.LanguageServices` is not referenced. nuget.org publishes only 4.x,
pinned to its own exact Microsoft.CodeAnalysis version, and the host provides the assembly at
runtime.

### Deployment

The VSIX manifest, package registration and deployment scripts define how the extension is installed and loaded by Visual Studio.

## Modernization direction

The current modernization direction is:

- reduce coupling to legacy Visual Studio UI patterns;
- maintain the grouped WPF Options pages;
- isolate formatting logic from shell integration;
- increase regression-test coverage;
- preserve safe behavior for files that cannot be parsed confidently;
- maintain compatibility across supported Visual Studio versions.

The architecture document will evolve as larger components are separated and the solution is modernized further.
