# 06 - Post Migration Recommendations

## Cel dokumentu
Rekomendacje po podstawowej migracji do VS2026, bez implementacji w tym etapie.

## Data ostatniej aktualizacji
2026-07-29

## Aktualny status
DONE

## Rekomendacje priorytetowe
### REC-001 Redukcja warningow VSTHRD*
- Status: TODO
- Priorytet: High
- Zakres: CodeMaidShared/*
- Uzasadnienie: 1652 warningow obniza jakosc i zwieksza ryzyko bugow watkowych.
- Walidacja: spadek warning count po kazdym PR.

### REC-002 Aktualizacja VS SDK z preview do stable
- Status: TODO
- Priorytet: Medium
- Zakres: CodeMaid.VS2022/CodeMaid.VS2022.csproj
- Uzasadnienie: stabilnosc i przewidywalnosc API/tooling.
- Walidacja: build/test/VSIX install w VS2026 po aktualizacji.

### REC-003 Aktualizacja CI do toolchain zgodnego z VS2026
- Status: TODO
- Priorytet: Medium
- Zakres: appveyor.yml (lub migracja do nowego CI)
- Uzasadnienie: unikniecie rozjazdu miedzy lokalnym i CI build.
- Walidacja: zielony pipeline Build+Test+VSIX.

### REC-004 Manual smoke test checklist jako staly artefakt release
- Status: TODO
- Priorytet: Medium
- Zakres: docs + release process
- Uzasadnienie: kontrola runtime extension load i rejestracji komend.
- Walidacja: wypelniona checklista per release.

### REC-005 Ograniczenie dlugu test stack
- Status: TODO
- Priorytet: Low
- Zakres: CodeMaid.UnitTests
- Uzasadnienie: projekt laczy MSTest i NUnit; warto ujednolicic strategię.
- Walidacja: prostszy i szybszy run testow.

### REC-006 Spojnosc UI (WPF) z motywem Visual Studio
- Status: TODO
- Priorytet: Medium
- Zakres: CodeMaidShared/UI (Options, dialogi WPF)
- Uzasadnienie: po walidacji w VS2026 UI dziala, ale konfiguracja otwiera sie jako oddzielne okno WPF, wizualnie niespojne z VS.
- Propozycja:
  - uzycie zasobow motywu VS (VsBrushes/VsColors),
  - rozwazenie integracji z Tools > Options (DialogPage/ProvideOptionPage),
  - obsluga zmiany motywu (Light/Dark/Blue).
- Walidacja: strony ustawien spojne z motywem VS, potwierdzone wizualnie w VS2026.
- Powiazane: backlog BL-006.

### REC-007 Warstwa analizy Roslyn obok silnika EnvDTE
- Status: TODO (nowy etap, poza migracja)
- Priorytet: High (dla wizji "darmowy ReSharper")
- Zakres: nowy modul analizy oparty o Microsoft.CodeAnalysis + integracja z EditorConfig
- Uzasadnienie:
  - Obecny silnik CodeMaid bazuje na EnvDTE/FileCodeModel, ktory nie nadaje sie do analizy semantycznej (np. czy pole jest tylko-do-odczytu) ani do bezpiecznych przeksztalcen skladni.
  - Funkcje BL-007 (file-scoped namespace), BL-008 (XML doc), BL-009 (readonly) sa dokladniejsze i bezpieczniejsze w Roslyn.
- Propozycja etapowa:
  - Krok 1: dla przeksztalcen skladniowych/stylu preferowac .editorconfig + VS Code Cleanup (bez nowej architektury).
  - Krok 2: wprowadzic warstwe Roslyn (SyntaxRewriter/code-fix) dla operacji wymagajacych analizy semantycznej.
  - Krok 3: konfigurowalne kroki cleanup delegujace do Roslyn, spiete z istniejacym UI ustawien.
- Ryzyka: wieksza zlozonosc, koniecznosc utrzymania dwoch modeli (DTE + Roslyn) w okresie przejsciowym.
- Walidacja: nowe testy jednostkowe per przeksztalcenie; brak regresji istniejacych 176 testow.
- Powiazane: BL-007, BL-008, BL-009.

## Ryzyka
- Duza liczba warningow VSTHRD* moze maskowac nowe regresje.
- CI moze raportowac inne wyniki niz lokalny VS2026.
- Zbyt szybka aktualizacja SDK bez regresji moze zmienic zachowanie rozszerzenia.

## Jednoznaczne wnioski
1. Migracja funkcjonalna jest zasadniczo udana, ale wymaga domkniecia manualnej walidacji GUI.
2. Najwieksza praca nastepnego etapu to jakosc i stabilizacja, nie nowe funkcje.
