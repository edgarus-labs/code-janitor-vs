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

## Settings modernization

The legacy Options experience is being replaced by the native Visual Studio settings system.

The current development branch includes an organized settings tree containing approximately 191 settings in 21 categories. This improves discoverability, aligns the extension with Visual Studio conventions and creates a clearer foundation for future configuration work.

## Navigation and workflow

- Find the active file in Solution Explorer.
- Collapse Solution Explorer nodes recursively.
- Switch between related files.
- Toggle read-only file state.
- Display build progress in Visual Studio and the Windows taskbar.
- Integrate with selected third-party cleanup tools.

## Supported Visual Studio versions

The development branch includes installation compatibility for Visual Studio 2022 and Visual Studio 2026. Release support will be documented per published build after the modernization work is verified.
