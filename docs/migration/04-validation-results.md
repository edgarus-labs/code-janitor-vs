# 04 - Validation Results

## Cel dokumentu
Szczegolowe wyniki walidacji migracji do Visual Studio 2026, w tym polecenia, kody wyjscia i statusy.

## Data ostatniej aktualizacji
2026-07-29

## Aktualny status
IN PROGRESS

## Srodowisko
- OS: Windows 10.0.22631
- dotnet SDK: 10.0.302
- Visual Studio: Professional 2026 18.8.1
- MSBuild: 18.8.2+ce25c0108 (.NET Framework)

## Wyniki walidacji
### VAL-001 Restore (dotnet)
- Data: 2026-07-29
- Polecenie: dotnet restore CodeMaid.sln
- Exit code: 0
- Skrocony wynik: Restore complete
- Status: DONE
- Artefakty/logi: output terminala
- Problemy: brak

### VAL-002 Build Debug (dotnet)
- Data: 2026-07-29
- Polecenie: dotnet build CodeMaid.sln -c Debug
- Exit code: 1
- Skrocony wynik: FAIL, MSB3822/MSB3823 w Microsoft.Common.CurrentVersion.targets (non-string resources)
- Status: DONE
- Artefakty/logi: output terminala
- Problemy: dotnet CLI host mismatch dla legacy VSIX

### VAL-003 Build Release (dotnet)
- Data: 2026-07-29
- Polecenie: dotnet build CodeMaid.sln -c Release
- Exit code: 1
- Skrocony wynik: FAIL, MSB3822/MSB3823
- Status: DONE
- Artefakty/logi: output terminala
- Problemy: jak VAL-002

### VAL-004 Tests (dotnet)
- Data: 2026-07-29
- Polecenie: dotnet test CodeMaid.sln -c Debug
- Exit code: 1
- Skrocony wynik: FAIL, build dependency error (MSB3822)
- Status: DONE
- Artefakty/logi: output terminala
- Problemy: jak VAL-002

### VAL-005 Build Debug (VS2026 MSBuild)
- Data: 2026-07-29
- Polecenie: "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Debug /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
- Exit code: 0
- Skrocony wynik: Build succeeded, 1652 warnings, 0 errors (po full clean/rebuild)
- Status: DONE
- Artefakty/logi: bin/Debug outputs
- Problemy:
   - Incydent przejsciowy: w pierwszym uruchomieniu restartu wystapil CS2001 (brakujace *.g.cs w obj/Debug).
   - Dzialanie naprawcze: full clean katalogow bin/obj + ponowny build.
   - Wynik koncowy: problem usuniety, build stabilny.

### VAL-006 Build Release (VS2026 MSBuild)
- Data: 2026-07-29
- Polecenie: "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Release /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
- Exit code: 0
- Skrocony wynik: Build succeeded, 1652 warnings, 0 errors
- Status: DONE
- Artefakty/logi: bin/Release outputs
- Problemy: duzy dlug warningow VSTHRD*

### VAL-007 Tests (VS2026 vstest.console)
- Data: 2026-07-29
- Polecenie: "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" "c:\Dev\codemaid\CodeMaid.UnitTests\bin\Debug\SteveCadwallader.CodeMaid.UnitTests.dll"
- Exit code: 0
- Skrocony wynik: Test Run Successful, Total 176, Passed 176
- Status: DONE
- Artefakty/logi: output terminala
- Problemy: brak

### VAL-008 VSIX package
- Data: 2026-07-29
- Polecenie: Get-ChildItem -Recurse -File -Filter *.vsix
- Exit code: 0
- Skrocony wynik: 4 artefakty VSIX wykryte
- Status: DONE
- Artefakty/logi:
  - CodeMaid/bin/Debug/SteveCadwallader.CodeMaid.vsix
  - CodeMaid/bin/Release/SteveCadwallader.CodeMaid.vsix
  - CodeMaid.VS2022/bin/Debug/SteveCadwallader.CodeMaid.VS2022.vsix
  - CodeMaid.VS2022/bin/Release/SteveCadwallader.CodeMaid.VS2022.vsix
- Problemy: brak

### VAL-009 VSIX installation (VS2026)
- Data: 2026-07-29
- Polecenie: VSIXInstaller.exe (install all-users) + weryfikacja folderu Extensions
- Exit code: 0
- Skrocony wynik: CodeMaid VS2026 (b1b6d05b-97f7-426d-9d6f-fdf8c7662ab2) zainstalowany i potwierdzony w Program Files Extensions
- Status: DONE
- Artefakty/logi: dd_VSIXInstaller_20260729091123_24fc.log; sciezka Extensions/vwx2cgtl.fwr
- Problemy:
  - Wczesniejsze proby /rootSuffix:CodeMaidClean failowaly (VSHiveStub 0x80070003); skuteczna byla instalacja machine-wide.

### VAL-010 Experimental instance startup
- Data: 2026-07-29
- Polecenie: devenv.exe (jedna instancja, PID 44000)
- Exit code: 0
- Skrocony wynik: VS uruchomione, dokladnie 1 instancja devenv
- Status: DONE
- Artefakty/logi: Get-Process devenv = 1
- Problemy: brak

### VAL-011 Extension load and command registration
- Data: 2026-07-29
- Polecenie: manualna weryfikacja w uruchomionym VS
- Exit code: n/a
- Skrocony wynik: uzytkownik potwierdzil - rozszerzenie widoczne, UI dziala
- Status: DONE
- Artefakty/logi: potwierdzenie uzytkownika (UI widoczne)
- Problemy:
  - Uwaga UX: okno konfiguracji WPF wizualnie niespojne z motywem VS (backlog BL-006).

### VAL-012 ActivityLog review
- Data: 2026-07-29
- Polecenie: parsowanie ActivityLog.xml glownego hiva + weryfikacja wizualna
- Exit code: 0
- Skrocony wynik:
  - ActivityLog glownego hiva jest przestarzaly (Modified 2026-06-26, sprzed instalacji), brak wpisow CodeMaid.
  - Biezaca instancja startowana bez /Log, wiec swiezy ActivityLog nie powstal.
  - Runtime-load potwierdzony bezposrednio: uzytkownik widzi rozszerzenie i dzialajace UI.
- Status: DONE (dowod: bezposrednia obserwacja UI; ActivityLog opcjonalny)
- Artefakty/logi: %APPDATA%\Microsoft\VisualStudio\18.0_ec255184\ActivityLog.xml (stary)
- Problemy:
  - Dla pelnego sladu w logu nalezaloby raz uruchomic VS z /Log (opcjonalne, wymaga kontrolowanego restartu jednej instancji).

### VAL-013 Compiler and analyzer warnings
- Data: 2026-07-29
- Polecenie: MSBuild Release (VAL-006)
- Exit code: 0
- Skrocony wynik: 1652 warnings, dominujace VSTHRD*
- Status: DONE
- Artefakty/logi: output build
- Problemy: dlug techniczny nieblokujacy

### VAL-014 CI compatibility
- Data: 2026-07-29
- Polecenie: aktualizacja appveyor.yml (image)
- Exit code: n/a
- Skrocony wynik: image zmieniony z "Visual Studio 2019" na "Visual Studio 2022" (zgodny z toolchain VS2022+ targetowanym przez projekt)
- Status: DONE (lokalna zmiana konfiguracji; pelna weryfikacja wymaga uruchomienia AppVeyor)
- Artefakty/logi: appveyor.yml
- Problemy:
  - AppVeyor nie udostepnia jeszcze obrazu "Visual Studio 2026"; najblizszy zgodny to "Visual Studio 2022".
  - Realna weryfikacja pipeline wymaga builda na AppVeyor (poza lokalnym srodowiskiem).

### VAL-015 Restart non-GUI checkpoint
- Data: 2026-07-29
- Polecenie: komplet faz non-GUI (toolchain + build/test + VSIX + CI-check)
- Exit code: mixed (zgodnie z VAL-001..VAL-014)
- Skrocony wynik: non-GUI checkpoint zakonczony; przygotowane do fazy UI
- Status: DONE
- Artefakty/logi: ten dokument + docs/migration/03-migration-progress.md
- Problemy: brak nowych trwalych blockerow

### VAL-016 UI phase execution
- Data: 2026-07-29
- Polecenie: clean hivow testowych -> instalacja VSIX -> jedna instancja VS -> weryfikacja UI
- Exit code: 0
- Skrocony wynik: faza UI wykonana; rozszerzenie CodeMaid VS2026 widoczne i dzialajace
- Status: DONE
- Artefakty/logi: potwierdzenie uzytkownika; devenv PID 44000 (1 instancja)
- Problemy:
   - Uwaga UX: okno konfiguracji WPF niespojne z motywem VS (backlog BL-006).

### VAL-017 VSIX identity isolation for VS2026
- Data: 2026-07-29
- Polecenie:
   - update CodeMaid.VS2022/source.extension.vsixmanifest + source.extension.cs
   - "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.VS2022\CodeMaid.VS2022.csproj /restore /t:Build /p:Configuration=Release /p:Platform=AnyCPU "/clp:ErrorsOnly;Summary"
   - Get-ChildItem CodeMaid.VS2022\bin\Release -Filter *.vsix
- Exit code: 0
- Skrocony wynik:
   - Name: CodeMaid VS2026
   - New Id: b1b6d05b-97f7-426d-9d6f-fdf8c7662ab2
   - InstallationTarget: Community + Pro + Enterprise, range [17.0,19.0)
   - Build Release: SUCCESS (0 errors)
- Status: DONE
- Artefakty/logi:
   - CodeMaid.VS2022/bin/Release/SteveCadwallader.CodeMaid.VS2022.vsix
- Problemy: brak

### VAL-018 CodeJanitor 0.1 rebrand runtime validation
- Data: 2026-07-29
- Polecenie:
   - full rename CodeMaid -> CodeJanitor (namespace/foldery/pliki/projekty/CI), version 0.1
   - MSBuild Release CodeJanitor.VS2022 (DeployExtension=false) -> 0 errors
   - VSIXInstaller /q /rootSuffix:Exp SteveCadwallader.CodeJanitor.VS2022.vsix -> exit 0
   - devenv /rootSuffix Exp (1 instancja)
- Exit code: 0
- Skrocony wynik:
   - Zainstalowano i zaladowano "CodeJanitor v0.1" (Id b1b6d05b...) w hifie Exp.
   - Uzytkownik potwierdzil: VS wystartowal, CodeJanitor dziala.
- Status: DONE
- Artefakty/logi: hive Exp extension.vsixmanifest = CodeJanitor v0.1; devenv PID 39588 (1 instancja)
- Problemy:
   - Pozostaly branding graficzny (ikony/preview) - BL-012.
   - Stara testowa instalacja 12.0 i leftovery usuniete; glowny hive zachowuje prawdziwy CodeMaid uzytkownika (9079).

## Instrukcja manualnej weryfikacji (wymagana do zamkniecia BLOCKED)
### Wariant zalecany: dziewicza instancja testowa (izolowany RootSuffix)
Ten wariant nie korzysta z glownej instancji VS, wiec aktualnie zainstalowany CodeMaid dla VS2022 nie wplywa na walidacje.

1. Otworz Developer PowerShell i przejdz do repo.
2. Zbuduj Release:
   - "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Release /p:Platform="Any CPU"
3. Wyczyść (lub utworz od nowa) izolowany profil VS:
   - "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\devenv.exe" /RootSuffix CodeMaidClean /ResetSettings General
4. Zainstaluj VSIX tylko do izolowanego profilu:
   - "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\VSIXInstaller.exe" /rootSuffix:CodeMaidClean "CodeMaid.VS2022\bin\Release\SteveCadwallader.CodeMaid.VS2022.vsix"
5. Uruchom izolowana instancje:
   - "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\devenv.exe" /RootSuffix CodeMaidClean /Log
6. Zweryfikuj:
   - rozszerzenie widoczne w Extensions
   - komendy CodeMaid dostepne w menu/kontekscie
   - podstawowy scenariusz: cleanup aktywnego pliku
7. Sprawdz ActivityLog dla izolowanego profilu:
   - %APPDATA%\Microsoft\VisualStudio\18.0*_CodeMaidClean\ActivityLog.xml
   - brak errorow ladowania pakietu CodeMaid

### Wariant alternatywny: RootSuffix Exp
1. Uruchom VS przez /RootSuffix Exp.
2. Upewnij sie, ze w Exp nie ma starej wersji rozszerzenia.
3. W razie potrzeby odinstaluj z Exp:
   - "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\VSIXInstaller.exe" /u:9079e73d-3fbb-4e07-8dab-f44fa5d8e8b5 /rootSuffix:Exp
   - "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\VSIXInstaller.exe" /u:b1b6d05b-97f7-426d-9d6f-fdf8c7662ab2 /rootSuffix:Exp
4. Zainstaluj testowany VSIX do Exp i wykonaj te same kroki walidacyjne.

## Jednoznaczne wnioski
1. Build, testy i pakowanie sa potwierdzone w VS2026 toolchain.
2. Dotnet CLI pozostaje niezalecana i nieprzechodzaca sciezka build dla tego repo.
3. Faza UI zakonczona: rozszerzenie zainstalowane i zaladowane w VS2026, UI potwierdzone przez uzytkownika.
4. Pozostala uwaga UX (spojnosc WPF z motywem VS) jest w backlogu BL-006 i nie blokuje migracji.
