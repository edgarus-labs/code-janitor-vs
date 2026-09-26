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
`VisualStudioRoslynWorkspace` (shared with the using-directive move below) it:

- resolves `VisualStudioWorkspace` through MEF (`IComponentModel`) by type name;
- supplies the C# `CodeFixProvider` MEF exports, cached per package;
- builds the input as `CurrentSolution.WithDocumentText(...)` from the cleaned editor
  buffer or the file written by headless cleanup;
- runs the engine off the UI thread; the engine's gate rejects a fix that adds any compiler
  error to a project it changed, even one that removed another error (`CompilerErrors`
  compares the errors as a multiset by id and file, matching an error either by message
  (`CompilerErrorMatching.MatchByMessage`, shared with the using placement's compile-error
  check) or by its position mapped through the fix's text changes, so a rename that only
  changes the message of an existing error is not rejected);
- before applying, checks every other project flavor of each changed file (linked files,
  shared projects, multi-targeting) with the new text and fails the cleanup, naming the
  project and the first new error, when any flavor gets a compiler error it did not have;
- applies the result with `Workspace.TryApplyChanges` on the UI thread, recomputing once
  when the workspace changed and then failing explicitly.

`CodeCleanupManager` calls the adapter after the existing C# cleanup, both in the headless
path (`RunDiagnosticCleanupAsync`) and in the editor path. `CleanupProgressViewModel` calls
it one file at a time after the parallel pass. Outcomes are recorded as
`DiagnosticChangedItems`, `DiagnosticUnresolvedItems` and failed items in
`CleanupExecutionStats`.

### Cleanup settings precedence

`EffectiveCleanupSettings.For(filePath)` resolves every per-file cleanup setting for both the
editor path (the EnvDTE `*Logic` classes, resolved once per document in
`CodeCleanupManager`) and the headless text pipeline (`CreateHeadlessCSharpPipeline`):

1. `.editorconfig`, for the keys it maps (see `docs/features.md`), read with Roslyn's
   `AnalyzerConfigSet` (`EditorConfigHelper`), so sections, globs, `root = true` and
   nearest-file-wins follow the EditorConfig rules. A value with severity `:none` is ignored, so
   the next source decides; any other severity, or no suffix, enforces the value;
2. the `.codejanitor` repository policy (`RepositoryCleanupSettings`);
3. the user's Visual Studio settings.

Namespace declarations, using placement and indentation are directional
(`NamespaceDeclarationPreference`, `UsingDirectivePlacementPreference`,
`IndentationPreference`): the reverse direction is implemented natively
(`FileScopedNamespaceConverter.ConvertToBlockScoped`, the inward move below,
`SpaceToTabConverter`, `RemoveFinalNewlineConverter`). Other reverse directions (for example
`var` to explicit types) are left to the diagnostic cleanup, which fixes rules reported as
`suggestion`, `warning` or `error` (never `silent` or `none`). Steps that emit syntax newer than
C# 7.3 (file-scoped namespaces, collection expressions, ...) additionally require that language
version in every project flavor (read from the workspace parse options).

### Using directive placement

`UsingDirectivePlacementConverter` is host-agnostic. `MoveUsingsInsideAsync` moves file-level
using directives into the single top-level namespace, keeping a directive's text when it binds
to the same symbol inside the namespace and writing it `global::`-qualified otherwise, with the
same all-or-nothing checks as the outward move. Given a Roslyn `Document`,
`MoveUsingsOutsideAsync`:

- resolves every namespace-level using directive with the semantic model;
- keeps a directive's text when it binds to the same symbol at file level, and writes the
  others fully qualified; comments attached to using directives are kept;
- rejects the move when the directives would move across preprocessor directives, when a
  fully qualified directive would refer to something else at its new place (a target reached
  only through an extern alias declared inside the namespace), when the document's compile
  errors grow, when any name, member or implicitly called member
  (`foreach`/`await`/deconstruction/pattern/query/list-pattern members, `operator true` in
  conditions) binds to a different symbol (symbols are compared including their declaring
  assemblies, so same-named types of extern-aliased references are told apart), or when a
  moved import brings an extension member
  that the compiler calls without exposing the binding (collection expression `Add`, spread
  `GetEnumerator`, `fixed` `GetPinnableReference`, tuple `==`/`!=` element operators);
- verifies conditional compilation through `ConditionalCompilationVariants`: the relevant
  symbols are those in the document's `#if`/`#elif` conditions plus those whose value, alone
  or together with the other condition symbols of that document, changes a declaration
  signature, a using directive (including `global using`) or an extern alias in another
  document of the project or of a project it references (every assignment of up to six
  condition symbols per document is parsed; a document with more counts all of them).
  Every assignment of at most four relevant symbols (16 variants) is re-parsed at
  project level and must pass the same checks; more symbols reject the move. Usings of a
  namespace in a region inactive in the active configuration stay where they are.
  Configuration-dependent metadata references are not varied.

It returns the moved text or a skip reason; it never returns a partial move.

`UsingDirectivePlacementLogic` takes the direction from `EffectiveCleanupSettings` and resolves every C# document of the file in
`VisualStudioWorkspace` (one per project and target framework that compiles it: linked files,
shared projects, multi-targeted projects), injects the current text into each, and runs the
converter on all of them. It applies the move only when every flavor returns the same moved
text; otherwise the usings stay in place and the skip reason names the disagreeing project.
Region directives are removed before moving whenever the cleanup removes them anyway
(`EffectiveCleanupSettings.RemovesRegions`, in both paths).

Call sites, per file and cleanup:

- Closed files: `CodeCleanupManager.Cleanup(ProjectItem)`/`CleanupAsync` (and
  `CleanupProgressViewModel` before its parallel pass) run it before the headless cleanup and
  rewrite the file with its original encoding, so the header, using organization and
  type-split steps see the moved directives. `CleanupProgressViewModel` passes a token that the
  dialog's Cancel cancels (linked with package disposal) to its pre-pass and to `CleanupAsync`
  in its sequential and non-parallel loops; a cancelled move leaves the file unchanged and is
  not recorded as a failure. A file the pre-pass rewrites counts as
  changed as soon as it is written, so it is counted even if the batch is then cancelled.
- Editor, type splitting enabled: `CodeCleanupManager.Cleanup(Document)` runs it before the
  split, as its own undo unit, so the created files inherit the moved directives.
- Editor: `RunCodeCleanupCSharp` runs it first inside the cleanup undo transaction, and again
  after external formatting and Remove and Sort Usings, which can put directives back inside a
  namespace. When nothing is inside a namespace a call is a syntax-only check.

Once an attempt leaves the directives in place (skip reason, I/O or workspace failure), the
later call sites of the same cleanup do not retry it, so the semantic analysis and its warning
happen once per file. Skip reasons and failures go to the output pane. The step is not a text
transformation, so the headless text pipeline and the cleanup preview do not include it.

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
