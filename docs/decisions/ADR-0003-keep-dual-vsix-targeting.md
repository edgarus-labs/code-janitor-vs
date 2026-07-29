# ADR-0003: Keep dual VSIX packaging strategy for backward compatibility

## Status
Accepted

## Context
Repo utrzymuje dwa projekty VSIX:
- CodeMaid (VS2019 range [16.0,17.0))
- CodeMaid.VS2022 (VS2022+ range [17.0,19.0))

Visual Studio 2026 (18.x) miesci sie w zakresie [17.0,19.0), wiec obecny projekt VS2022 jest kompatybilny docelowo.

## Decision
Zachowac aktualny model dual VSIX bez laczenia projektow i bez usuwania wsparcia dla VS2019 w tym etapie.

## Alternatives considered
- A1: Usunac projekt VS2019.
  - Odrzucone: niepotrzebna utrata kompatybilnosci wstecznej.
- A2: Scalac projekty w jeden multi-target.
  - Odrzucone: wieksza zlozonosc i ryzyko poza zakresem.

## Consequences
- Plusy:
  - Kompatybilnosc wsteczna zachowana.
  - Brak duzego refaktoru build/publish.
- Minusy:
  - Dwa artefakty do utrzymania.

## Validation
- Oba projekty buduja .vsix w Debug i Release.
- Artefakty:
  - CodeMaid/bin/Release/SteveCadwallader.CodeMaid.vsix
  - CodeMaid.VS2022/bin/Release/SteveCadwallader.CodeMaid.VS2022.vsix

## Related files
- CodeMaid/source.extension.vsixmanifest
- CodeMaid.VS2022/source.extension.vsixmanifest
- docs/migration/04-validation-results.md
