# Development Guide

## Requirements

Development requires:

- Windows;
- Visual Studio with the Visual Studio extension development workload;
- the SDKs and build tools required by the solution;
- access to the `develop` branch and the repository history.

The exact supported Visual Studio version is determined by the current solution and VSIX manifest.

## Getting started

1. Clone the repository.
2. Check out `develop`.
3. Open the solution in Visual Studio.
4. Restore dependencies.
5. Build the solution.
6. Run the extension in an experimental Visual Studio instance.
7. Execute the unit tests.
8. Verify the relevant feature manually when changing Visual Studio integration or deployment behavior.

## Testing principles

Changes to cleaning and formatting behavior should include regression tests whenever practical.

Particular care is required for:

- Razor and HTML boundaries;
- C# embedded in Razor;
- preprocessor directives;
- comments and string literals;
- line endings and indentation;
- idempotence, meaning that running a formatter twice produces the same result as running it once;
- files that the formatter cannot safely parse.

## Experimental hive

Visual Studio extension changes should be tested in an experimental instance before being considered ready. This is especially important for:

- package registration;
- command registration;
- settings registration;
- VSIX manifests;
- `pkgdef` generation;
- deployment scripts.

## Pull requests

Pull requests should target `develop` and explain:

- the problem;
- the design;
- compatibility impact;
- test coverage;
- documentation changes;
- any changes to inherited CodeMaid behavior.

See [CONTRIBUTING.md](../CONTRIBUTING.md).
