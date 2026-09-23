# Issue #9 – przekazanie do walidacji na Windows / Visual Studio

Branch: `fix/9-editorconfig-diagnostic-cleanup` (bazuje na `develop`). PR jeszcze nie istnieje.
Status: implementacja + review zakończone na Linuxie. **#9 NIE jest zamknięte** – brakuje walidacji w prawdziwym Visual Studio.

## Decyzja architektoniczna (nie zmieniać)
Opcja 2: Code Janitor **nie** używa profilu Code Cleanup z VS. Deterministyczny przepływ:
`.editorconfig` (przez Roslyn, także zagnieżdżone) + kategorie Janitora → diagnostyki Roslyn →
istniejące `CodeFixProvider`y → FixAll / pojedyncze poprawki. Zakazane: listy ID diagnostyk
(IDE1006 to tylko przypadek testowy), reimplementacja reguł, tłumienie diagnostyk.

## Co zostało zrobione
- **Silnik (niezależny od VS)**: `src/CodeJanitor/Logic/Cleaning/Diagnostics/`
  - `DiagnosticCleanupCategoryClassifier` – kategorie (Formatting, Naming, CodeStyle, AnalyzerFixes)
    z rodziny analizatora (hierarchia typów) i `Descriptor.Category`; `Compiler` → nigdy.
  - `CodeFixProviderCatalog` – providery z MEF hosta + skan assembly z analyzer references.
  - `DiagnosticCleanupEngine` – aktywne = severity ≥ suggestion, niewytłumione, w włączonej kategorii,
    tylko w czyszczonym dokumencie. FixAll gdy provider wspiera, inaczej pętla (max 50 przebiegów).
    Bramki: dokładnie jedna `ApplyChangesOperation` (pozostałe operacje zwracane jako
    `PostApplyOperations`, silnik ich nie wykonuje); odrzucenie jeśli rośnie liczba błędów kompilatora
    albo zmiana nie jest czysto tekstowa.
  - `DiagnosticCleanupResult` – `Unresolved` z powodem, `IsComplete`, `PostApplyOperations`.
- **Integracja VS**: `src/CodeJanitor/Logic/Cleaning/EditorConfigDiagnosticCleanupLogic.cs`
  - `VisualStudioWorkspace` pobierany refleksyjnie (brak pakietu `Microsoft.VisualStudio.LanguageServices` 5.x na nuget.org).
  - Wejście = `CurrentSolution.WithDocumentText(docId, tekst po cleanupie)`; silnik poza UI thread;
    `TryApplyChanges` na UI thread z jedną ponowną próbą; potem wykonanie `PostApplyOperations`
    (np. `SymbolRenamedOperation` → aktualizacja XAML/designer).
  - Nieaktualne zamknięte dokumenty (inne niż docelowy) → wstrzyknięcie tekstu z dysku i ponowne
    uruchomienie; jeśli dalej niezgodne → jawny błąd.
  - Wywołanie po istniejącym cleanupie: ścieżka headless, edytor, cleanup-on-save, równoległa (sekwencyjnie po pętli).
  - Nierozwiązane diagnostyki → output pane, statystyki, status bar, MessageBox (nigdy „czysty sukces”).
- **Ustawienia** (domyślnie `false`, nadpisywalne w `.codejanitor`): `Cleaning_ApplyEditorConfigFormatting`,
  `Cleaning_ApplyEditorConfigNaming`, `Cleaning_ApplyEditorConfigCodeStyle`, `Cleaning_ApplyAnalyzerCodeFixes`.
  UI: Options → Cleaning → Update (`CleaningUpdateDataTemplate.xaml`).
- Aliasy EnvDTE (`Document`, `TextDocument`) w `CodeCleanupManager.cs`, `RazorFormatterLogic.cs`,
  `AiXmlDocumentationLogic.cs` – konflikt nazw po dodaniu Workspaces.
- **Testy**: `tests/CodeJanitor.UnitTests/Cleaning/Diagnostics/` (56 przypadków w CI): naming IDE1006 z dokładnymi
  nazwami z konfiguracji, formatowanie, code style, severity (silent/none/suggestion/warning/error, oba zapisy),
  zagnieżdżony `.editorconfig`, filtrowanie kategorii, FixAll/wiele przebiegów, brak providera, bramka bezpieczeństwa.
- **CI**: `build-vsix.yml` uruchamia teraz `vstest.console`. Naprawiono wcześniej istniejący konflikt
  `System.Collections.Immutable` w projekcie testowym (`AssemblySearchPath_*` = false, usunięty stary redirect) –
  na `develop` padało 433/762 testów.
- Docs: `docs/features.md`, `docs/architecture.md`, `CHANGELOG.md`.

## Walidacja do tej pory
| Poziom | Wynik |
|---|---|
| Build VSIX na Windows (CI run 35859277062, `26020987`) | OK |
| Testy jednostkowe na Windows (ten sam run) | 820/820 OK |
| Harness Roslyn na Linuxie | 58/58 OK |
| Integracja w Visual Studio | **NIEZWERYFIKOWANE** |

## Do zrobienia na Windows (wymagane przed zamknięciem #9)
1. Zbuduj i zainstaluj VSIX w experimental instance (`scripts/deploy-exp.ps1` lub F5).
2. Przygotuj solucję testową z `.editorconfig`, np.:
   ```ini
   [*.cs]
   dotnet_naming_rule.pf.symbols = pf
   dotnet_naming_rule.pf.style = st
   dotnet_naming_rule.pf.severity = suggestion
   dotnet_naming_symbols.pf.applicable_kinds = field
   dotnet_naming_symbols.pf.applicable_accessibilities = private
   dotnet_naming_style.st.required_prefix = m_
   dotnet_naming_style.st.capitalization = pascal_case
   dotnet_diagnostic.IDE0055.severity = warning
   ```
   plus zagnieżdżony `.editorconfig` w podfolderze z innym prefiksem albo `severity = none`.
3. Sprawdź i zapisz wyniki:
   - [ ] Kategoria Naming włączona → IDE1006 naprawione dokładnie wg konfiguracji, referencje w innych plikach zaktualizowane.
   - [ ] Włączona tylko jedna kategoria → inne diagnostyki nietknięte.
   - [ ] `severity = none` / `silent` → brak zmian.
   - [ ] Pliki zamknięte (headless) i otwarte (edytor), cleanup-on-save, cleanup całej solucji (ścieżka równoległa).
   - [ ] Plik zamknięty świeżo zapisany przez Janitora → brak utraty zmian, brak podwójnych edycji.
   - [ ] Rename pola/metody używanej w XAML (WPF) → XAML zaktualizowany (`PostApplyOperations`).
   - [ ] Diagnostyka bez fixa → raport w output pane, podsumowanie nie mówi „sukces”.
   - [ ] Wszystkie kategorie wyłączone → zachowanie jak na `develop`.
   - [ ] Czy `VisualStudioWorkspace` zawiera hostowe analizatory IDE w `Solution.AnalyzerReferences` (jeśli nie, IDE1006 nie będzie wykrywane – wtedy trzeba to naprawić).
4. Sprawdź zgodność wersji: VS 2022 (Roslyn 4.x) vs VS 2026/18.x. Projekt kompiluje się z Roslyn 5.9; na starszym hoście krok powinien zalogować jawny błąd, a nie cicho „przejść”. To już istniejące ryzyko (wcześniejsza referencja `Microsoft.CodeAnalysis.CSharp` 5.9).
5. Jeśli wszystko OK: otwórz PR do `develop` (`Fixes #9`), w opisie tabela walidacji z rozróżnieniem
   build / unit / Roslyn harness / integracja VS.

## Znane ograniczenia
- Zmiana nazwy przez automatyczny fix wysyła do listenerów XAML/designer tylko powiadomienie „po rename” – nie mogą jej zawetować.
- Bramka błędów kompilacji widzi tylko C#; zepsute referencje w XAML wyjdą dopiero przy buildzie.
