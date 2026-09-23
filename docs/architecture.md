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

`EditorConfigDiagnosticCleanupLogic` is the Visual Studio host adapter. It:

- maps the four `Cleaning_Apply*` settings, including `.codejanitor` overrides, to
  categories, and does nothing when none is enabled;
- resolves `VisualStudioWorkspace` through MEF (`IComponentModel`) by type name;
- supplies the C# `CodeFixProvider` MEF exports, cached per package;
- builds the input as `CurrentSolution.WithDocumentText(...)` from the cleaned editor
  buffer or the file written by headless cleanup;
- runs the engine off the UI thread;
- applies the result with `Workspace.TryApplyChanges` on the UI thread, recomputing once
  when the workspace changed and then failing explicitly.

`CodeCleanupManager` calls the adapter after the existing C# cleanup, both in the headless
path and in the editor path. `CleanupProgressViewModel` calls it one file at a time after
the parallel pass. Outcomes are recorded as `DiagnosticChangedItems`,
`DiagnosticUnresolvedItems` and failed items in `CleanupExecutionStats`.

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
