# Code Janitor for Visual Studio

[![Build VSIX](https://github.com/edgarus-labs/code-janitor-vs/actions/workflows/build-vsix.yml/badge.svg)](https://github.com/edgarus-labs/code-janitor-vs/actions/workflows/build-vsix.yml)
[![Visual Studio](https://img.shields.io/badge/Visual%20Studio-2022%2B-purple.svg)](https://visualstudio.microsoft.com/)
[![License: LGPL v3](https://img.shields.io/badge/License-LGPL_v3-blue.svg)](LICENSE.txt)

Code Janitor is an independently maintained open-source Visual Studio extension for cleaning, simplifying, navigating and reorganizing code.

> **Transparent project origin:** Code Janitor is a fork and continuation of [CodeMaid](https://github.com/codecadwallader/codemaid), originally created and maintained by [Steve Cadwallader](https://github.com/codecadwallader). It is not an official project of the original author.

## Why this project exists

CodeMaid became a widely used Visual Studio extension for improving code quality and developer productivity. This fork provides a separate maintenance and development path as Visual Studio and the .NET ecosystem evolve.

Code Janitor exists to provide an independently maintained development path for this valuable open-source codebase. The project preserves the proven capabilities of CodeMaid while improving compatibility, maintainability and support for current development workflows.

The name **Code Janitor** distinguishes this project from the original CodeMaid project and makes clear that it has a new, independent maintainer and roadmap.

## Installation

Download a `.vsix` package from [GitHub Releases](https://github.com/edgarus-labs/code-janitor-vs/releases), if available, close Visual Studio, and run the package to install it. Restart Visual Studio after installation. To build from source, follow the [development guide](docs/development.md).

The current VSIX manifest targets 64-bit Visual Studio Community, Professional and Enterprise versions 17.x and 18.x (Visual Studio 2022 and 2026). Check each release for version-specific requirements.

For Visual Studio Code, use the separate [Code Janitor for VS Code](https://github.com/edgarus-labs/code-janitor-vscode) extension; the two extensions have different feature sets.

## Current development

The current development branch includes:

- Visual Studio 2026 installation compatibility;
- migration from the legacy Options window to the native Visual Studio settings system;
- a structured settings tree covering approximately 191 settings in 21 categories;
- a safe Razor formatter for Blazor applications;
- Razor control-block formatting for `@if`, `@else`, `@for`, `@foreach`, `@while`, `@switch`, `@try`, `@catch` and `@finally`;
- namespace fixing and cleanup;
- optional AI-assisted XML documentation cleanup;
- configurable XMLDoc filters, controls and budget limits;
- secure settings for AI-assisted operations;
- a one-time cleanup options dialog for selected-scope cleanup;
- a C# text cleanup preview with a native Visual Studio diff, per-file and per-rule selection, and stale-buffer protection;
- dedicated Razor formatter options and tests;
- continued maintenance of the original cleaning, navigation and code-organization features.

AI-assisted features are opt-in and designed to provide explicit scope and budget controls. They do not run silently over an entire solution.

To preview deterministic C# changes, select files or folders in Solution Explorer,
run **Cleanup Selected Code**, and choose **Preview C# Text Changes**. Review the
diff and rules, then use **Apply to Editors** to update selected buffers without
saving files. This preview does not run AI, split types, change encoding, or invoke
editor-only cleanup steps. See the [cleanup preview guide](docs/cleanup-preview.md)
for scope, safety behavior and the manual verification checklist.

The `develop` branch contains ongoing work and may differ from packaged releases. Consult the [release notes](https://github.com/edgarus-labs/code-janitor-vs/releases) for the capabilities of each build.

## Features inherited and continued from CodeMaid

Code Janitor continues the original feature set, including:

- code cleaning and whitespace normalization;
- using/namespace cleanup and sorting;
- code reorganizing;
- comment formatting;
- code navigation and hierarchy visualization;
- McCabe complexity information;
- joining and sorting selected code;
- collapsing Solution Explorer nodes;
- switching between related files;
- removing regions;
- build progress indicators;
- configurable third-party cleanup integrations.

The original project supports a broad range of languages and file types, including C#, C++, F#, Visual Basic, PHP, PowerShell, JSON, XAML, XML, ASP, HTML, CSS, LESS, SCSS, JavaScript and TypeScript. Compatibility is being reviewed as part of the Code Janitor modernization work.

## Documentation

- [Project origin and relationship with CodeMaid](docs/project-origin.md)
- [Features and current capabilities](docs/features.md)
- [Development guide](docs/development.md)
- [Architecture overview](docs/architecture.md)
- [Roadmap](docs/roadmap.md)
- [Sequential feature delivery plan](docs/todo/feature-delivery-plan.md)
- [Licensing and attribution](docs/licensing.md)
- [Contributing](CONTRIBUTING.md)
- [Changelog](CHANGELOG.md)

## Building and testing

Code Janitor is a Visual Studio extension. Development requires Windows, Visual Studio with the extension-development workload, and the SDKs required by the solution.

See [docs/development.md](docs/development.md) for the current setup and verification process.

## Relationship with CodeMaid

Code Janitor is derived from the open-source [CodeMaid repository](https://github.com/codecadwallader/codemaid). The original project's license and attribution requirements are respected. Code Janitor's own changes are documented in this repository's commit history and changelog.

For a detailed explanation of the fork, the rename and the project boundaries, see [docs/project-origin.md](docs/project-origin.md).

## License

Code Janitor is distributed under the [GNU Lesser General Public License, version 3](LICENSE.txt), in accordance with the license of the original CodeMaid project.

See [docs/licensing.md](docs/licensing.md) for attribution and licensing information.
