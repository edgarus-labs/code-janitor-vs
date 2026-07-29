# Migration TODO

## Cel dokumentu
Operacyjna lista krokow migracji do VS2026 z biezacymi statusami.

## Data ostatniej aktualizacji
2026-07-29

## Aktualny status
IN PROGRESS (migracja podstawowa DONE; pozostaly prace jakosciowe STEP-007/008)

## Lista krokow
- STEP-001 Inwentaryzacja repozytorium
  - Status: DONE
  - Dowod walidacji: docs/migration/00-repository-inventory.md

- STEP-002 Raport stanu poczatkowego
  - Status: DONE
  - Dowod walidacji: docs/migration/01-initial-assessment.md

- STEP-003 Wybór referencyjnego toolchain build/test
  - Status: DONE
  - Dowod walidacji:
    - VS2026 MSBuild Debug/Release SUCCESS
    - ADR-0001 accepted

- STEP-004 Walidacja testow automatycznych
  - Status: DONE
  - Dowod walidacji: 176/176 testow passed (vstest.console)

- STEP-005 Walidacja pakowania VSIX
  - Status: DONE
  - Dowod walidacji: 4 artefakty .vsix wykryte

- STEP-006 Walidacja runtime w VS2026 (instalacja + instancja VS + load)
  - Status: DONE
  - Dowod walidacji:
    - VSIX CodeMaid VS2026 (b1b6...) zainstalowany (all-users, Program Files Extensions)
    - jedna instancja VS (PID 44000)
    - rozszerzenie widoczne i UI dziala (potwierdzenie uzytkownika)
  - Uwaga: spojnosc WPF z motywem VS -> backlog BL-006.

- STEP-007 Redukcja warningow VSTHRD*
  - Status: IN PROGRESS
  - Zweryfikowany wzorzec: dodanie ThreadHelper.ThrowIfNotOnUIThread() w metodach dotykajacych DTE (bez zmiany zachowania dla poprawnych wywolan na watku UI).
  - Increment (9 plikow): SolutionHelper.cs, UIHierarchyHelper.cs, CodeElementHelper.cs, TextDocumentHelper.cs, CodeCleanupManager.cs, UpdateLogic.cs, CodeReorganizationManager.cs, GenerateRegionLogic.cs, RemoveWhitespaceLogic.cs.
  - Wynik: warningi projektu 820 -> 553, Release 0 errors, Debug (DeployExtension=false) 0 errors, testy 183/183.
  - Obserwacja techniczna: VSTHRD010 kaskaduje - oznaczenie metody jako main-thread przenosi wymog na jej miejsca wywolania, wiec pelne domkniecie wymaga spojnego przejscia po grafie wywolan (od entry pointow UI w dol).
  - Dowod wymagany do DONE: dalsza etapowa redukcja po grafie wywolan + utrzymanie 176/176.

- STEP-008 Weryfikacja zgodnosci CI
  - Status: IN PROGRESS
  - Zmiana: appveyor.yml image "Visual Studio 2019" -> "Visual Studio 2022".
  - Dowod wymagany do DONE: zielony pipeline AppVeyor (build/test/pack) na nowym obrazie.

## Jednoznaczne wnioski
- Krytyczna sciezka build/test jest zamknieta.
- Walidacja runtime GUI zakonczona (rozszerzenie zaladowane w VS2026).
- Pozostaje domkniecie CI oraz prace jakosciowe (VSTHRD*, spojnosc UI).
