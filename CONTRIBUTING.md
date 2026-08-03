# Contributing to Code Janitor

Thank you for your interest in Code Janitor. Contributions are welcome, especially improvements that make the extension safer, more useful and compatible with current Visual Studio and .NET development.

## Project context

Code Janitor is an independently maintained fork and continuation of [CodeMaid](https://github.com/codecadwallader/codemaid). Please read [the project origin document](docs/project-origin.md) and [the licensing document](docs/licensing.md) before contributing.

## Before opening an issue

- Search existing issues first.
- Confirm that the problem is reproducible with the latest development build.
- Include the Visual Studio version, Code Janitor version or commit, operating system, project type and steps to reproduce.
- Do not include secrets, proprietary source code or confidential customer data.

## Before opening a pull request

For significant features or architectural changes, open an issue first so the scope can be discussed. Keep pull requests focused and include tests where practical.

A useful pull request should explain:

- the problem being solved;
- the proposed solution;
- compatibility implications;
- how the change was tested;
- whether documentation or changelog updates are required.

## Development workflow

1. Fork or clone the repository.
2. Create a topic branch from `develop`.
3. Make a focused change.
4. Build and test the solution in Visual Studio.
5. Verify that the extension can be deployed to an experimental hive.
6. Update documentation and `CHANGELOG.md` when appropriate.
7. Open a pull request against `develop`.

## Code guidelines

- Preserve existing behavior unless the change intentionally modifies it.
- Prefer small, testable changes.
- Treat source formatting as user-visible behavior.
- Add regression tests for formatter and cleaner changes.
- Avoid introducing dependencies without documenting the reason.
- Keep user-facing text clear and consistent.
- Preserve required third-party notices and attribution.

## Code of conduct

Please keep discussions professional, constructive and focused on the project. Harassment, discrimination, personal attacks and bad-faith behavior are not acceptable.
