# 03 - Migration Progress

## Cel dokumentu
Dziennik wykonania migracji do Visual Studio 2026 wraz z walidacja po kazdej logicznej grupie zmian.

## Data ostatniej aktualizacji
2026-07-29

## Aktualny status
IN PROGRESS

## Group G1 - Inwentaryzacja i baseline
- Status: DONE
- Co zmieniono:
  - Utworzono strukture dokumentacji pod docs/.
  - Zebrano inwentarz plikow i konfiguracji build.
- Dlaczego:
  - Wymaganie przygotowania mapy technicznej przed zmianami.
- Pliki zmodyfikowane:
  - docs/migration/00-repository-inventory.md
  - docs/migration/01-initial-assessment.md
  - docs/migration/02-visual-studio-2026-migration-plan.md
- Problem rozwiazany:
  - Brak usystematyzowanego obrazu repo i ryzyk migracyjnych.
- Uruchomione polecenia:
  - dotnet --info
  - dotnet --list-sdks
  - dotnet --list-runtimes
  - dotnet restore CodeMaid.sln
  - dotnet build CodeMaid.sln -c Debug
  - dotnet build CodeMaid.sln -c Release
  - dotnet test CodeMaid.sln -c Debug --no-build
- Wynik:
  - dotnet restore SUCCESS
  - dotnet build/test FAIL (MSB3822/MSB3823)
- Nowe problemy:
  - MIG-001, MIG-002, MIG-003
- TODO status changes:
  - STEP-001 DONE
  - STEP-002 DONE

## Group G2 - Ustalenie oficjalnej sciezki build/test dla VS2026
- Status: DONE
- Co zmieniono:
  - Przyjeto natywny MSBuild i vstest.console z instalacji VS2026 jako glowna sciezke walidacji.
- Dlaczego:
  - Legacy VSIX non-SDK csproj buduja sie poprawnie przez VS-native toolchain.
- Pliki zmodyfikowane:
  - docs/decisions/ADR-0001-vs2026-native-msbuild-and-vstest.md
- Problem rozwiazany:
  - Blokada migracji wynikajaca z nieodpowiedniego hosta build (dotnet CLI).
- Uruchomione polecenia:
  - "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Debug /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
  - "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Release /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
- Wynik:
  - Debug SUCCESS (0 errors)
  - Release SUCCESS (0 errors)
- Nowe problemy:
  - Brak nowych blockerow build.
  - Potwierdzono wysoki dlug warningow VSTHRD*.
- TODO status changes:
  - STEP-003 DONE

## Group G3 - Walidacja testow i VSIX
- Status: DONE
- Co zmieniono:
  - Zweryfikowano testy i artefakty VSIX pod VS2026.
- Dlaczego:
  - Potwierdzenie gotowosci uruchamiania i pakowania.
- Pliki zmodyfikowane:
  - docs/migration/04-validation-results.md
- Problem rozwiazany:
  - Niepewnosc czy testy i pakowanie przechodza po migracji.
- Uruchomione polecenia:
  - "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" "c:\Dev\codemaid\CodeMaid.UnitTests\bin\Debug\SteveCadwallader.CodeMaid.UnitTests.dll"
  - Get-ChildItem -Recurse -File -Filter *.vsix
- Wynik:
  - Test Run Successful: 176 passed, 0 failed
  - VSIX artifacts present in Debug i Release
- Nowe problemy:
  - Brak nowych blockerow.
- TODO status changes:
  - STEP-004 DONE
  - STEP-005 DONE

## Group G4 - Proba naprawy dotnet build i rollback
- Status: DONE
- Co zmieniono:
  - Testowo dodano ustawienia/resource package do projektow VSIX, nastepnie wycofano.
- Dlaczego:
  - Proba usuniecia MSB3822 pod dotnet SDK 10.
- Pliki zmodyfikowane:
  - CodeMaid/CodeMaid.csproj (tymczasowo, rollback)
  - CodeMaid.VS2022/CodeMaid.VS2022.csproj (tymczasowo, rollback)
- Problem rozwiazany:
  - Brak skutecznej poprawki bez naruszenia minimalizmu i bez gwarancji kompatybilnosci.
- Uruchomione polecenia:
  - dotnet restore CodeMaid.sln
  - dotnet build CodeMaid.sln -c Debug
  - dotnet test CodeMaid.sln -c Debug
- Wynik:
  - Nadal FAIL (MSB3822)
  - Zmiany wycofane
- Nowe problemy:
  - Potwierdzono, ze obejscie nie jest wiarygodne jako fix produkcyjny.
- TODO status changes:
  - Brak zmiany statusu krokow glownych.

## Group G5 - Walidacja manualna GUI
- Status: DONE
- Co zmieniono:
  - Wyczyszczono hivy testowe, zainstalowano VSIX raz, uruchomiono jedna instancje VS.
  - Potwierdzono zaladowanie rozszerzenia i dzialanie UI w VS2026.
- Dlaczego:
  - Domkniecie walidacji runtime wymaganej do bramki migracji.
- Pliki zmodyfikowane:
  - docs/migration/04-validation-results.md
  - docs/migration/05-final-migration-report.md
  - docs/README.md
  - docs/todo/post-migration-backlog.md
- Problem rozwiazany:
  - Brak potwierdzenia runtime extension load.
- Uruchomione polecenia:
  - VSIXInstaller.exe (install) + weryfikacja Extensions
  - Start-Process devenv.exe (1 instancja, PID 44000)
  - parsowanie ActivityLog.xml
- Wynik:
  - Rozszerzenie CodeMaid VS2026 zainstalowane i zaladowane; UI dziala.
- Nowe problemy:
  - Uwaga UX: spojnosc WPF z motywem VS (backlog BL-006).
- TODO status changes:
  - STEP-006 DONE

## Group G6 - Restart od poczatku (non-GUI), 2026-07-29
- Status: DONE
- Co zmieniono:
  - Ponownie wykonano caly baseline non-GUI: toolchain, restore, build, test, VSIX, CI-check.
  - Wykryto chwilowy blad Debug build (brak plikow *.g.cs), usuniety przez full clean i ponowny build.
- Dlaczego:
  - Wymaganie ponownego przejscia od poczatku przed podejsciem do UI.
- Pliki zmodyfikowane:
  - docs/migration/03-migration-progress.md
  - docs/migration/04-validation-results.md
  - docs/migration/05-final-migration-report.md
  - docs/README.md
- Problem rozwiazany:
  - Niespojnosc artefaktow posrednich po sekwencji buildow.
- Uruchomione polecenia:
  - dotnet --info
  - dotnet --list-sdks
  - dotnet --list-runtimes
  - dotnet restore CodeMaid.sln
  - dotnet build CodeMaid.sln -c Debug
  - dotnet build CodeMaid.sln -c Release
  - dotnet test CodeMaid.sln -c Debug
  - "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Debug /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
  - Remove-Item -Recurse -Force CodeMaid\obj,CodeMaid\bin,CodeMaid.VS2022\obj,CodeMaid.VS2022\bin,CodeMaid.UnitTests\obj,CodeMaid.UnitTests\bin
  - "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Debug /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
  - "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Release /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
  - "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" "c:\Dev\codemaid\CodeMaid.UnitTests\bin\Debug\SteveCadwallader.CodeMaid.UnitTests.dll"
  - Get-ChildItem -Recurse -File -Filter *.vsix
  - Get-Content appveyor.yml
- Wynik:
  - dotnet restore SUCCESS
  - dotnet build/test FAIL (MSB3822/MSB3823) - potwierdzone
  - VS2026 MSBuild Debug/Release SUCCESS po clean
  - VS2026 testy SUCCESS (176/176)
  - VSIX artefakty obecne
- Nowe problemy:
  - Brak nowych trwalych blockerow.
- TODO status changes:
  - Brak zmiany statusow krokow, nadal BLOCKED na etapie UI.

## Group G7 - Celowe zatrzymanie przed UI
- Status: DONE
- Co zmieniono:
  - Zatrzymano proces przed uruchomieniem testow UI, zgodnie z poleceniem uzytkownika.
- Dlaczego:
  - Wymaganie "przy testach UI zatrzymaj sie".
- Problem rozwiazany:
  - Zapewniona kontrola faz i brak przypadkowego wejscia w walidacje GUI.

## Group G8 - Rebranding paczki testowej na VS2026 + unikalny ID
- Status: DONE
- Co zmieniono:
  - Zmieniono nazwe rozszerzenia z "CodeMaid VS2022" na "CodeMaid VS2026".
  - Nadano nowy Identity Id VSIX: b1b6d05b-97f7-426d-9d6f-fdf8c7662ab2.
- Dlaczego:
  - Nazwa VS2022 byla mylaca dla walidacji VS2026.
  - Ten sam ID kolidowal z juz zainstalowana wersja rozszerzenia.
- Pliki zmodyfikowane:
  - CodeMaid.VS2022/source.extension.vsixmanifest
  - CodeMaid.VS2022/source.extension.cs
  - docs/migration/03-migration-progress.md
  - docs/migration/04-validation-results.md
  - docs/migration/05-final-migration-report.md
- Problem rozwiazany:
  - Ryzyko nadpisywania/konfliktu z obecnie zainstalowanym rozszerzeniem.
- Uruchomione polecenia:
  - [guid]::NewGuid().ToString()
  - "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.VS2022\CodeMaid.VS2022.csproj /restore /t:Build /p:Configuration=Release /p:Platform=AnyCPU "/clp:ErrorsOnly;Summary"
  - Get-ChildItem CodeMaid.VS2022\bin\Release -Filter *.vsix
- Wynik:
  - Build SUCCESS (0 errors)
  - Artefakt VSIX wygenerowany
- Nowe problemy:
  - Brak nowych blockerow.
- TODO status changes:
  - Brak zmian statusow krokow glownych.

## Group G9 - Prace jakosciowe po migracji (STEP-008 CI, STEP-007 VSTHRD)
- Status: IN PROGRESS
- Co zmieniono:
  - STEP-008: appveyor.yml image "Visual Studio 2019" -> "Visual Studio 2022".
  - STEP-007: zweryfikowany wzorzec ThreadHelper.ThrowIfNotOnUIThread() w CodeMaidShared/Helpers/SolutionHelper.cs (GetChildren).
- Dlaczego:
  - Zgodnosc CI z toolchain VS2022+ oraz redukcja dlugu VSTHRD bez zmiany zachowania.
- Pliki zmodyfikowane:
  - appveyor.yml
  - CodeMaidShared/Helpers/SolutionHelper.cs
  - docs/migration/04-validation-results.md
  - docs/todo/migration-todo.md
- Uruchomione polecenia:
  - MSBuild Release CodeMaid.VS2022 (rebuild)
  - MSBuild Debug CodeMaid.sln
  - vstest.console CodeMaid.UnitTests
- Wynik:
  - Release: 0 errors; warningi projektu 820 -> 605 (7 plikow: helpery + CodeCleanupManager + UpdateLogic + CodeReorganizationManager).
  - Testy: 176/176 passed (brak regresji, Debug z DeployExtension=false).
  - Debug (pelny): 1 error tylko na kroku DeployExtension.
  - Obserwacja: VSTHRD010 kaskaduje na miejsca wywolan; pelne domkniecie wymaga przejscia po grafie wywolan.
- Nowe problemy:
  - REM-007: przy uruchomionej instalacji all-users (b1b6...), Debug build z DeployExtension=True failuje ("same or lower version"). To skutek instalacji z fazy UI, nie blad kodu. Obejscie do buildow weryfikacyjnych: /p:DeployExtension=false lub odinstalowanie paczki testowej.
- TODO status changes:
  - STEP-008 IN PROGRESS
  - STEP-007 IN PROGRESS

## Podsumowanie statusow krokow
- DONE: STEP-001, STEP-002, STEP-003, STEP-004, STEP-005, STEP-006
- IN PROGRESS: STEP-007 (VSTHRD*), STEP-008 (CI)

## Jednoznaczne wnioski
1. Repo jest zbudowane, testowalne i uruchamialne w VS2026 przy poprawnym toolchain.
2. Dotnet CLI pozostaje niezalecana sciezka build dla tego typu projektu.
3. Walidacja runtime GUI zakonczona; bramka migracji: PASS.
