# 02 - Visual Studio 2026 Migration Plan

## Cel dokumentu
Plan krok po kroku bezpiecznej migracji i walidacji CodeMaid w Visual Studio 2026.

## Data ostatniej aktualizacji
2026-07-29

## Aktualny status
DONE

## Ustalony kierunek migracji
1. Toolchain glowny: Visual Studio 2026 MSBuild + vstest.console.
2. Zachowanie target frameworkow: .NET Framework 4.7.2 (brak migracji do nowoczesnego .NET na tym etapie).
3. Zachowanie dualnego modelu VSIX:
   - CodeMaid (VS2019)
   - CodeMaid.VS2022 (VS2022+ i VS2026)
4. Brak masowej aktualizacji pakietow NuGet bez twardego uzasadnienia.

## Wymagane workloady Visual Studio 2026
- .NET desktop development
- Visual Studio extension development (VSSDK)
- Test tools (VSTest)

## Kroki migracji
### STEP-001
- Cel: Potwierdzic inwentarz repo i zaleznosci.
- Status: DONE
- Pliki do zmiany: brak
- Zaleznosci: brak
- Oczekiwany rezultat: kompletna mapa techniczna repo.
- Polecenie walidacyjne: skan plikow + odczyt .sln/.csproj/.vsixmanifest
- Ryzyko: niskie
- Plan wycofania: n/a
- Warunek zakonczenia: dokument 00-repository-inventory.md uzupelniony.

### STEP-002
- Cel: Zweryfikowac stan poczatkowy build/test.
- Status: DONE
- Pliki do zmiany: brak
- Zaleznosci: STEP-001
- Oczekiwany rezultat: lista blockerow z klasyfikacja.
- Polecenie walidacyjne: dotnet restore/build/test + VS MSBuild/vstest
- Ryzyko: niskie
- Plan wycofania: n/a
- Warunek zakonczenia: dokument 01-initial-assessment.md uzupelniony.

### STEP-003
- Cel: Ustalic oficjalny mechanizm budowania dla VS2026.
- Status: DONE
- Pliki do zmiany: dokumentacja
- Zaleznosci: STEP-002
- Oczekiwany rezultat: jednoznaczny wybor VS-native MSBuild.
- Polecenie walidacyjne: MSBuild Debug i Release 0 errors.
- Ryzyko: niskie
- Plan wycofania: powrot do poprzedniej dokumentacji.
- Warunek zakonczenia: ADR zaakceptowany.

### STEP-004
- Cel: Zweryfikowac uruchamianie testow automatycznych.
- Status: DONE
- Pliki do zmiany: brak
- Zaleznosci: STEP-003
- Oczekiwany rezultat: testy przechodza w VS2026.
- Polecenie walidacyjne: vstest.console na CodeMaid.UnitTests.dll
- Ryzyko: niskie
- Plan wycofania: n/a
- Warunek zakonczenia: 176/176 testow passed.

### STEP-005
- Cel: Zweryfikowac generowanie artefaktow VSIX.
- Status: DONE
- Pliki do zmiany: brak
- Zaleznosci: STEP-003
- Oczekiwany rezultat: .vsix w Debug i Release.
- Polecenie walidacyjne: Get-ChildItem -Recurse -Filter *.vsix
- Ryzyko: niskie
- Plan wycofania: n/a
- Warunek zakonczenia: lista artefaktow potwierdzona.

### STEP-006
- Cel: Manualna walidacja instalacji i uruchomienia rozszerzenia w VS2026.
- Status: BLOCKED
- Pliki do zmiany: dokumentacja
- Zaleznosci: STEP-005
- Oczekiwany rezultat: potwierdzone zaladowanie rozszerzenia w instancji eksperymentalnej.
- Polecenie walidacyjne:
  - devenv.exe /RootSuffix Exp
  - instalacja CodeMaid.VS2022/bin/Release/SteveCadwallader.CodeMaid.VS2022.vsix
  - analiza ActivityLog.xml
- Ryzyko: srednie
- Plan wycofania: odinstalowanie VSIX z instancji Exp.
- Warunek zakonczenia: checklist manualna wykonana.
- Przyczyna blokady: brak automatyzowalnego GUI E2E w tym srodowisku sesji.
- Brakujacy warunek: interaktywna sesja Visual Studio 2026 (UI) z mozliwoscia instalacji i obserwacji ActivityLog.
- Wplyw na migracje: bramka koncowa pozostaje FAIL do czasu recznej walidacji.
- Proponowane nastepne dzialanie: wykonac instrukcje z 04-validation-results.md w lokalnej sesji VS.

### STEP-007
- Cel: Ocena i plan redukcji warningow VSTHRD*.
- Status: TODO
- Pliki do zmiany: CodeMaidShared/* (w kolejnym etapie)
- Zaleznosci: STEP-004
- Oczekiwany rezultat: backlog zmian jakosciowych bez zmiany funkcji.
- Polecenie walidacyjne: MSBuild Release + porownanie liczby warningow.
- Ryzyko: srednie
- Plan wycofania: wycofanie pojedynczych commitow.
- Warunek zakonczenia: backlog i priorytety gotowe.

## Zmiany frameworkow
- Zachowac:
  - .NET Framework 4.7.2 dla wszystkich projektow (uzasadnienie: legacy VSIX i API VS)
- Zmienic:
  - Brak zmian frameworkow na tym etapie.

## Aktualizacje NuGet
- Wymagane teraz: brak krytycznych aktualizacji blokujacych build/test w VS2026.
- Kandydaci do nastepnego etapu:
  - Microsoft.VisualStudio.SDK (projekt VS2022: wersja preview -> stabilna 18.x po testach regresji)

## Kompatybilnosc wsteczna
- Utrzymac dwa pakiety VSIX i nie usuwac wsparcia VS2019 na tym etapie.

## Plan rollback
- Zmiany migracyjne ograniczone do dokumentacji i drobnych ustawien build.
- W razie regresji:
  1. Cofnac zmiany migracyjne.
  2. Odtworzyc poprzedni pipeline build.
  3. Potwierdzic build na ostatnim stabilnym commicie.

## Jednoznaczne wnioski
1. Minimalna migracja do VS2026 jest osiagalna przez zmiane narzedzia budowania, nie przez duza przebudowe kodu.
2. Krytyczne pozostaje domkniecie recznej walidacji instalacji/uruchomienia VSIX.
