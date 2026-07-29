# 01 - Initial Assessment

## Cel dokumentu
Raport stanu poczatkowego przed i w trakcie migracji do Visual Studio 2026.

## Data ostatniej aktualizacji
2026-07-29

## Aktualny status
DONE

## Architektura
### Lista projektow i role
- CodeMaid/CodeMaid.csproj
  - Rola: rozszerzenie VSIX dla VS 2019 (InstallationTarget [16.0,17.0)).
- CodeMaid.VS2022/CodeMaid.VS2022.csproj
  - Rola: rozszerzenie VSIX dla VS 2022+ (InstallationTarget [17.0,19.0), obejmuje VS 2026/18.x).
- CodeMaid.UnitTests/CodeMaid.UnitTests.csproj
  - Rola: testy automatyczne logiki.
- CodeMaidShared/CodeMaidShared.shproj
  - Rola: wspolna logika i integracja VS dla obu projektow VSIX.

### Zaleznosci miedzy projektami
- CodeMaid.UnitTests -> CodeMaid
- CodeMaid i CodeMaid.VS2022 -> importuja CodeMaidShared.projitems

### Mechanizm ladowania rozszerzenia
- Atrybuty rejestracyjne i implementacja pakietu w CodeMaidShared/CodeMaidPackage.cs.
- Rejestracja komend i komponentow pod CodeMaidShared/Integration.

### Mechanizm pakowania VSIX
- source.extension.vsixmanifest per projekt VSIX.
- GeneratePkgDefFile=true i Asset Type=Microsoft.VisualStudio.VsPackage.
- Artefakt wyjsciowy .vsix w katalogach bin/Debug i bin/Release.

### Uruchamianie/debugowanie
- StartProgram=$(DevEnvDir)devenv.exe
- StartArguments=/rootsuffix Exp
- Zakladana weryfikacja przez instancje eksperymentalna Visual Studio.

## Aktualny toolchain
- Visual Studio: Professional 2026 18.8.1
- MSBuild (VS): 18.8.2+ce25c0108 for .NET Framework
- dotnet SDK: 10.0.302
- Target frameworks: .NET Framework 4.7.2 (wszystkie projekty)
- VS SDK NuGet:
  - CodeMaid: Microsoft.VisualStudio.SDK 16.10.31321.278
  - CodeMaid.VS2022: Microsoft.VisualStudio.SDK 17.0.0-previews-2-31512-422
- Build tools:
  - CodeMaid: Microsoft.VSSDK.BuildTools 16.11.35
  - CodeMaid.VS2022: Microsoft.VSSDK.BuildTools 17.0.3177-preview3

## Problemy
### Blokujace migracje
- MIG-001
  - Plik/lokalizacja: mechanizm budowania przez dotnet CLI (dotnet build CodeMaid.sln)
  - Objaw: MSB3822/MSB3823 (non-string resources)
  - Przyczyna: dotnet CLI (SDK-style build host) nie jest rownowazna sciezka dla legacy VSIX non-SDK csproj.
  - Wplyw: brak mozliwosci uznania dotnet build za glowna sciezke walidacji migracji.
  - Proponowana poprawka: budowac i testowac przez VS2026 MSBuild + vstest.console.
  - Ryzyko poprawki: niskie.
  - Status: DONE
  - Blokuje migracje: NIE (po wyborze poprawnego toolchain).
  - Dowod walidacji: VS MSBuild Debug/Release = 0 errors; testy 176/176 passed.

### Blokujace build
- MIG-002
  - Plik/lokalizacja: n/a (toolchain mismatch)
  - Objaw: dotnet build fails, VS MSBuild succeeds.
  - Przyczyna: roznice miedzy hostem build i typem projektu.
  - Wplyw: mozliwa konfuzja deweloperow/CI.
  - Proponowana poprawka: jawna dokumentacja oficjalnego polecenia build.
  - Ryzyko poprawki: niskie.
  - Status: DONE
  - Blokuje migracje: NIE.

### Blokujace testy
- MIG-003
  - Plik/lokalizacja: dotnet test CodeMaid.sln
  - Objaw: testy nie startuja przez zaleznosc od nieudanego builda dotnet.
  - Przyczyna: jak MIG-001.
  - Wplyw: brak wiarygodnej walidacji przez dotnet CLI.
  - Proponowana poprawka: uruchamiac testy przez vstest.console po buildzie VS MSBuild.
  - Ryzyko poprawki: niskie.
  - Status: DONE
  - Blokuje migracje: NIE.

### Problemy kompatybilnosci
- MIG-004
  - Plik/lokalizacja: CodeMaid.VS2022/CodeMaid.VS2022.csproj
  - Objaw: uzycie preview wersji Microsoft.VisualStudio.SDK i Microsoft.VSSDK.BuildTools.
  - Przyczyna: historyczna konfiguracja projektu.
  - Wplyw: potencjalne ryzyko przyszlej niestabilnosci lub zmian API.
  - Proponowana poprawka: kontrolowana aktualizacja do stabilnych wersji 18.x w osobnym kroku z pelna regresja.
  - Ryzyko poprawki: srednie.
  - Status: TODO
  - Blokuje migracje: NIE (obecnie build/test przechodza).

### Ostrzezenia i dlug techniczny
- MIG-005
  - Plik/lokalizacja: liczne pliki pod CodeMaidShared/*
  - Objaw: 1652 warningi VSTHRD* w Release build.
  - Przyczyna: restrykcyjne analyzery watkowosci VS SDK.
  - Wplyw: zwiekszone ryzyko bledow watkowych i trudniejsza konserwacja.
  - Proponowana poprawka: etapowa redukcja warningow bez zmiany zachowania uzytkowego.
  - Ryzyko poprawki: srednie.
  - Status: TODO
  - Blokuje migracje: NIE.

### Potencjalne regresje
- MIG-006
  - Plik/lokalizacja: CodeMaid/source.extension.vsixmanifest
  - Objaw: pakiet VS2019 pozostaje ograniczony do [16.0,17.0).
  - Przyczyna: intencjonalne rozdzielenie na osobny projekt VS2022+.
  - Wplyw: brak instalacji tego konkretnego VSIX na VS2026 (oczekiwane).
  - Proponowana poprawka: brak; uzywac VSIX z projektu CodeMaid.VS2022.
  - Ryzyko poprawki: niskie.
  - Status: NOT APPLICABLE
  - Blokuje migracje: NIE.

### Problemy niezwiązane bezposrednio z migracja
- MIG-007
  - Plik/lokalizacja: appveyor.yml
  - Objaw: image ustawiony na Visual Studio 2019.
  - Przyczyna: historyczna konfiguracja CI.
  - Wplyw: ryzyko rozjazdu CI vs lokalny toolchain 2026.
  - Proponowana poprawka: aktualizacja pipeline (po weryfikacji wsparcia obrazu).
  - Ryzyko poprawki: srednie.
  - Status: TODO
  - Blokuje migracje: NIE (lokalnie zweryfikowano).

## Polecenia uzyte do walidacji
- dotnet --info
- dotnet restore CodeMaid.sln
- dotnet build CodeMaid.sln -c Debug
- dotnet test CodeMaid.sln -c Debug
- "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Debug /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
- "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Release /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
- "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" "c:\Dev\codemaid\CodeMaid.UnitTests\bin\Debug\SteveCadwallader.CodeMaid.UnitTests.dll"

## Jednoznaczne wnioski
1. Migracja do VS2026 jest wykonalna bez duzego refaktoru.
2. Warunkiem powodzenia jest uzycie natywnego MSBuild/vstest z Visual Studio 2026.
3. Dotnet CLI nalezy traktowac jako diagnostyke pomocnicza, nie glowny pipeline build dla tego repo.
4. Najwazniejsze dalsze prace to redukcja warningow VSTHRD* oraz aktualizacja CI.
