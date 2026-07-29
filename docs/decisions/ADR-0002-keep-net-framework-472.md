# ADR-0002: Keep target frameworks on .NET Framework 4.7.2 in migration phase

## Status
Accepted

## Context
Wszystkie projekty .csproj targetuja v4.7.2 i sa powiazane z API Visual Studio SDK/EnvDTE oraz modelem legacy VSIX. Celem etapu jest minimalna migracja do VS2026, bez duzego refaktoru.

## Decision
Utrzymac target framework v4.7.2 dla:
- CodeMaid
- CodeMaid.VS2022
- CodeMaid.UnitTests

Nie migrowac teraz do nowoczesnego .NET ani SDK-style.

## Alternatives considered
- A1: Podniesienie TFM i migracja do SDK-style.
  - Odrzucone: duze ryzyko, poza zakresem etapu.
- A2: Czesciowa migracja tylko testow.
  - Odrzucone: nie rozwiazuje glownego ryzyka dla VSIX i komplikowaloby pipeline.

## Consequences
- Plusy:
  - Minimalny zakres zmian.
  - Niskie ryzyko regresji funkcjonalnych.
- Minusy:
  - Utrzymanie dlugu technologicznego legacy csproj.

## Validation
- VS2026 MSBuild Debug/Release -> SUCCESS
- VS2026 vstest.console -> SUCCESS (176/176)

## Related files
- CodeMaid/CodeMaid.csproj
- CodeMaid.VS2022/CodeMaid.VS2022.csproj
- CodeMaid.UnitTests/CodeMaid.UnitTests.csproj
- docs/migration/05-final-migration-report.md
