# Roadmap

This roadmap describes the intended direction of Code Janitor. It is deliberately outcome-focused and may change as compatibility testing and user feedback provide new information.

## Sequential feature delivery

Planned increments, in order: Preview Cleanup, Explain Cleanup, branch-aware
cleanup, profiles, measured test verification, check mode/CLI, Blazor maintenance
diagnostics, complexity navigation and AI context control.

The first C# text preview increment is implemented and automatically tested;
Visual Studio runtime verification and broader preview coverage remain pending.
See the [preview help](cleanup-preview.md) for the exact supported scope.

## Completed or in progress

- Establish the Code Janitor identity and transparent fork documentation.
- Add Visual Studio 2026 installation compatibility.
- Provide grouped WPF pages in Visual Studio's Options dialog.
- Organize the settings model into clear categories.
- Add a safe Razor and Blazor formatter.
- Add Razor control-block support and regression tests.
- Add namespace fixing and cleanup.
- Add optional AI-assisted XMLDoc cleanup with filters and budget limits.
- Add a selected-scope cleanup options dialog.
- Replace inherited project documentation with Code Janitor documentation.

## Near-term priorities

- Improve the existing WPF settings experience.
- Expand Razor and Blazor formatting coverage.
- Expand XMLDoc cleanup safely across supported C# constructs.
- Increase automated test coverage for formatting, AI boundaries and cleaning behavior.
- Review privacy and security controls for AI-assisted processing.
- Review compatibility with current Visual Studio releases.
- Improve build and release automation.
- Publish reproducible development and preview builds.
- Refresh screenshots, user documentation and installation instructions.

## Medium-term priorities

- Reduce technical debt in legacy components.
- Improve diagnostics and error reporting.
- Review support for existing language and file-type integrations.
- Improve accessibility and discoverability of configuration.
- Establish a predictable release cadence.
- Build a contributor and user feedback process.

## Long-term goals

- Provide a stable, actively maintained successor for users who rely on CodeMaid functionality.
- Make Code Janitor a reliable code-maintenance companion for modern .NET, C# and Blazor development.
- Provide safe, transparent and user-controlled AI assistance for repetitive documentation and maintenance tasks.
- Keep the project transparent, open-source and respectful of its upstream history.
