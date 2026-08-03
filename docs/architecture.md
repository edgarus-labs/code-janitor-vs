# Architecture Overview

Code Janitor is a Visual Studio extension built around the original CodeMaid extension architecture, with modernization work being introduced incrementally.

## Main areas

### Visual Studio integration

This area contains the package, commands, tool windows, settings registration and VSIX deployment metadata required to run inside Visual Studio.

### Shared logic

Shared logic contains cleaning, formatting, organization and parsing behavior that can be tested independently from the Visual Studio shell.

### Unit tests

Unit tests protect behavior that is especially sensitive to source syntax, including code cleaning and Razor formatting.

### Deployment

The VSIX manifest, package registration and deployment scripts define how the extension is installed and loaded by Visual Studio.

## Modernization direction

The current modernization direction is:

- reduce coupling to legacy Visual Studio UI patterns;
- use native Visual Studio settings where appropriate;
- isolate formatting logic from shell integration;
- increase regression-test coverage;
- preserve safe behavior for files that cannot be parsed confidently;
- maintain compatibility across supported Visual Studio versions.

The architecture document will evolve as larger components are separated and the solution is modernized further.
