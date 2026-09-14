# Security Policy

## Supported versions

Code Janitor is currently under active development. Security fixes are prioritized for the latest development version and the latest published release, when available.

## Reporting a vulnerability

Please do not disclose security vulnerabilities in a public issue.

Use GitHub's private vulnerability reporting where available, or contact the project maintainer privately through the contact details associated with the repository. Include:

- a description of the vulnerability;
- affected version or commit;
- reproduction steps;
- potential impact;
- a suggested mitigation, if known.

Please allow reasonable time for investigation and coordinated disclosure.

## Scope

Security reports may include issues involving:

- unsafe processing of source files;
- unexpected file modification or data loss;
- extension package loading;
- deployment and update mechanisms;
- sensitive data exposure through logs or diagnostics.

Optional AI-assisted operations send source-code context to the configured AI provider. Review the provider, endpoint and applicable data-handling terms before using these operations with private code. Ordinary deterministic cleanup does not require an AI request.

Include unintended disclosure through AI requests or credential handling in security reports.
