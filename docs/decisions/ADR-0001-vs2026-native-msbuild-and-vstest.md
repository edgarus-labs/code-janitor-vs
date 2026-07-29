# ADR-0001: Use VS2026 native MSBuild and VSTest as primary build pipeline

## Status
Accepted

## Context
Repozytorium korzysta z klasycznych projektow VSIX .NET Framework (non-SDK csproj). Build przez dotnet CLI (SDK 10.0.302) failuje z MSB3822/MSB3823 dla zasobow non-string. Build przez natywne narzedzia Visual Studio 2026 przechodzi.

## Decision
Oficjalna sciezka build/test dla migracji do VS2026 to:
- MSBuild.exe z instalacji Visual Studio 2026
- vstest.console.exe z instalacji Visual Studio 2026

Dotnet CLI pozostaje narzedziem diagnostycznym, nie referencyjnym pipeline dla tego repo.

## Alternatives considered
- A1: Wymusic dotnet build jako glowna sciezke.
  - Odrzucone: utrzymujace sie MSB3822/MSB3823.
- A2: Duza migracja projektow do SDK-style.
  - Odrzucone na tym etapie: poza zakresem minimalnej, bezpiecznej migracji.

## Consequences
- Plusy:
  - Stabilny build/test zgodny z typem projektu.
  - Brak ryzykownego refaktoru.
- Minusy:
  - Potrzeba jawnej dokumentacji polecen VS-native i zaleznosc od instalacji VS.

## Validation
- "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Debug /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary" -> SUCCESS
- "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Release /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary" -> SUCCESS
- "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" CodeMaid.UnitTests/bin/Debug/SteveCadwallader.CodeMaid.UnitTests.dll -> 176/176 PASS

## Related files
- CodeMaid.sln
- CodeMaid/CodeMaid.csproj
- CodeMaid.VS2022/CodeMaid.VS2022.csproj
- CodeMaid.UnitTests/CodeMaid.UnitTests.csproj
- docs/migration/04-validation-results.md
