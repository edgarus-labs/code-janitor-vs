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
  compares the errors as a multiset keyed by id, file and message, not by count);
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

### Moving using directives outside namespaces

`MoveUsingsOutsideNamespaceConverter` is host-agnostic. Given a Roslyn `Document`, it:

- resolves every namespace-level using directive with the semantic model;
- keeps a directive's text when it binds to the same symbol at file level, and writes the
  others fully qualified; comments attached to using directives are kept;
- rejects the move when the directives would move across preprocessor directives, when the
  document's compile errors grow, when any name, member or implicitly called member
  (`foreach`/`await`/deconstruction/pattern/query/list-pattern members, `operator true` in
  conditions) binds to a different symbol, or when a moved import brings an extension member
  that the compiler calls without exposing the binding (collection expression `Add`, spread
  `GetEnumerator`, `fixed` `GetPinnableReference`, tuple `==`/`!=` element operators);
- verifies conditional compilation through `ConditionalCompilationVariants`: the relevant
  symbols are those in the document's `#if`/`#elif` conditions plus those whose toggling
  changes a declaration signature in another document of the project or of a project it
  references. Every assignment of at most four such symbols (16 variants) is re-parsed at
  project level and must pass the same checks; more symbols reject the move. Usings of a
  namespace in a region inactive in the active configuration stay where they are.
  Configuration-dependent metadata references are not varied.

It returns the moved text or a skip reason; it never returns a partial move.

`MoveUsingsOutsideNamespaceLogic` resolves every C# document of the file in
`VisualStudioWorkspace` (one per project and target framework that compiles it: linked files,
shared projects, multi-targeted projects), injects the current text into each, and runs the
converter on all of them. It applies the move only when every flavor returns the same moved
text; otherwise the usings stay in place and the skip reason names the disagreeing project.
Region directives are removed before moving whenever the cleanup would remove them anyway
(editor: always; headless: `RepositoryCleanupOverrides.RemovesRegions`).

Call sites, per file and cleanup:

- Closed files: `CodeCleanupManager.Cleanup(ProjectItem)`/`CleanupAsync` (and
  `CleanupProgressViewModel` before its parallel pass) run it before the headless cleanup and
  rewrite the file with its original encoding, so the header, using organization and
  type-split steps see the moved directives.
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
`Microsoft.CodeAnalysis.CSharp` (5.9.0) and ships in the VSIX like the existing Roslyn
assemblies. `VisualStudioWorkspace` and the MEF code fix providers are host objects, so
the feature works only when Visual Studio's binding redirects unify
Microsoft.CodeAnalysis* to a host Roslyn of 5.9 or newer. On older hosts the private copy
loads side by side, and the adapter reports an explicit failure (type identity or load
error). `Microsoft.VisualStudio.LanguageServices` is not referenced. nuget.org publishes
only 4.x, pinned to its own exact Microsoft.CodeAnalysis version, and the host provides
the assembly at runtime.

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
