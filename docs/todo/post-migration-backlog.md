# Post Migration Backlog

## Cel dokumentu
Uporzadkowany backlog prac po podstawowej migracji do VS2026.

## Data ostatniej aktualizacji
2026-07-29

## Aktualny status
TODO

## Backlog
- BL-001 Reducja warningow VSTHRD010/VSTHRD001/VSTHRD110
  - Priorytet: High
  - Status: IN PROGRESS
  - Obszar: CodeMaidShared/Helpers, CodeMaidShared/Logic, CodeMaidShared/UI
  - Postep: 820 -> 553 (9 plikow: helpery DTE + managery Cleanup/Reorganize + UpdateLogic + GenerateRegionLogic + RemoveWhitespaceLogic), 0 errors, testy 183/183.
  - Postep 2 (ta sesja, liczba warningow VSTHRD* w Release, oba projekty CodeJanitor.csproj+CodeJanitor.VS2022.csproj razem): 1120 -> 972 (4 pliki): InsertBlankLinePaddingLogic.cs (58->0), InsertExplicitAccessModifierLogic.cs (38->0), RemoveRegionLogic.cs (30->0, w tym guard w kazdym zagniezdzonym lambda/closure przekazywanym do UndoTransactionHelper.Run), SpadeToolWindow.cs (40->6, pozostale 6 to VSTHRD110/VSTHRD001 przy fire-and-forget JoinableTaskFactory.RunAsync w OnToolWindowCreated - swiadomie NIE ruszane, bo wymagaloby restrukturyzacji async inicjalizacji z ryzykiem zmiany zachowania). Build 0 errors, testy 228/228.
  - Postep 3 (kontynuacja tej samej sesji): 972 -> 796 (7 plikow): FindInSolutionExplorerCommand.cs (38->0), SortLinesCommand.cs (36->0), BaseCodeItemElement.cs (36->0), CodeComment.cs (32->0, w tym guard wewnatrz lambd przekazywanych do prywatnej metody Expand(TextPoint, Action<EditPoint>)), CommentFormatCommand.cs (32->0), CodeCleanupAvailabilityLogic.cs (32->0), CodeModelManager.cs (32->20, TYLKO metody zawsze wywolywane na UI thread: OnDocumentChanged, OnDocumentClosing, RetrieveAllCodeItems, RetrieveAllCodeItemsAsync (czesc przed Task.Run) - zostaly zguardowane; BuildCodeItems/LoadLazyInitializedValues/RaiseCodeModelBuilt swiadomie NIE ruszane, bo sa wywolywane takze z wewnatrz Task.Run w RetrieveAllCodeItemsAsync przy wlaczonym Settings.General_LoadModelsAsynchronously - dodanie guard tam spowodowaloby realny wyjatek w runtime na background threadzie, nie tylko usunieloby warning). Build 0 errors, testy 228/228 (build+testy weryfikowane dwukrotnie w tej turze - raz po Task alias fix, raz po closure fix w CodeComment.cs).
  - WAZNE odkrycie bezpieczenstwa: CodeModelHelper.cs (30 warningow) i CodeModelBuilder.cs (caly, bez guardow) SWIADOMIE POMINIETE w tej turze - ich metody (RetrieveCodeRegions, IsCodeRegionUnderCursor, RetrieveAllCodeItems) sa czescia lancucha wywolan osiaganego z Task.Run (async ladowanie modelu kodu), wiec dodanie ThreadHelper.ThrowIfNotOnUIThread() tam zepsuloby dzialajaca funkcje (rzucaloby wyjatek zamiast tylko uciszac analizator). Przed guardowaniem KAZDEGO pliku z listy "remaining candidates" trzeba sprawdzic czy nie jest wywolywany z Task.Run/background thread - jesli tak, zostawic jako accepted debt (jak VSTHRD110/001 w SpadeToolWindow).
  - Uwaga: VSTHRD010 kaskaduje - asercja na metodzie przenosi wymog na callerow; nalezy isc po grafie wywolan od entry pointow UI. Potwierdzone: trzeba dodac ThreadHelper.ThrowIfNotOnUIThread() do KAZDEJ metody w lancuchu wywolan (nie wystarczy w metodzie lisciowej) - analizator nie propaguje dowodu miedzyproceduralnie. Dodatkowo potwierdzone: dotyczy to takze zagniezdzonych lambda/closures (np. przekazywanych do UndoTransactionHelper.Run(() => ...) lub jako Action<T> argument do prywatnej metody pomocniczej) - kazdy taki blok wymaga wlasnego wywolania guard na poczatku, inaczej nadal zglasza warning mimo ze metoda zewnetrzna juz ma guard.
  - Uwaga (pulapka techniczna): po dodaniu `using Microsoft.VisualStudio.Shell;` do plikow ktore juz mialy `using System.Threading.Tasks;` (bez aliasu) i uzywaly `Task` (np. `Task.Run`, `async Task`), pojawia sie CS0104 (ambiguous reference miedzy Microsoft.VisualStudio.Shell.Task i System.Threading.Tasks.Task). Naprawa: zamienic `using System.Threading.Tasks;` na `using Task = System.Threading.Tasks.Task;` (wzorzec juz stosowany w innych plikach Commands).
  - Kryterium done: istotny spadek warningow w Release bez regresji testow.

- BL-002 Aktualizacja preview VS SDK do wersji stabilnych 18.x
  - Priorytet: Medium
  - Status: TODO
  - Obszar: CodeMaid.VS2022/CodeMaid.VS2022.csproj
  - TODO po testach runtime: decyzja nazewnicza warstwy VSIX/projektu - albo dodac osobny tor `VS2026` (np. nowy projekt/artefakt nazewniczy), albo przemianowac obecny `*.VS2022` do `*.VS2026`/neutralnej nazwy. Zmienic spojnie: nazwy projektu, assembly/artefakt VSIX, wpisy manifestu, skrypty release/CI.
  - Kryterium done: build/test/VSIX install przechodza po aktualizacji.

- BL-003 Aktualizacja CI image/toolchain
  - Priorytet: Medium
  - Status: TODO
  - Obszar: appveyor.yml
  - Kryterium done: CI zielone dla build/test/pack.

- BL-004 Polautomatyczna checklista smoke-test dla VSIX runtime
  - Priorytet: Medium
  - Status: TODO
  - Obszar: docs + release process
  - Kryterium done: raport release zawiera wynik smoke test i ActivityLog.

- BL-005 Ujednolicenie strategii testow (MSTest/xUnit)
  - Priorytet: Low
  - Status: IN PROGRESS (zmigrowano testy NUnit do xUnit v3)
  - Obszar: CodeJanitor.UnitTests
  - Kryterium done: uproszczony pipeline test i mniejsza zlozonosc adapterow.

- BL-006 Spojnosc wygladu UI (WPF) z Visual Studio
  - Priorytet: Medium
  - Status: IN PROGRESS (natywne strony VS wdrozone; pozostaje runtime smoke-test)
  - Obszar: CodeMaidShared/UI (okna Options i dialogi WPF)
  - Kontekst: po instalacji w VS2026 UI dziala, ale konfiguracja otwiera sie jako oddzielne okno WPF, wizualnie niespojne z motywem VS.
  - Zrobione (2026-07-29):
    - Integracja z VS: `ProvideOptionPage` + `CodeJanitorOptionsDialogPage` (UIElementDialogPage) i osadzenie konfiguracji w natywnym `Tools > Options`.
    - Komendy `Options` i `SpadeOptions` otwieraja teraz strone VS Options (Spade wykonuje deep-link do sekcji Digging).
    - Widok hostowany jako `OptionsPageControl` bez customowych fontow/kolorow/rozmiarow i bez wlasnego theme dictionary - render i typography pochodza z natywnego hosta VS.
    - Zachowane funkcje: switch user/solution settings, import/export, reset, apply przez przyciski OK/Apply Visual Studio.
  - Uwaga runtime (2026-07-29): po migracji do `ProvideOptionPage` strona widnieje w nowym shellu opcji, ale nadal uruchamia sie przez tryb kompatybilnosci (legacy launcher/modal flow) zamiast natywnego nowego hosta ustawien.
  - Naprawione (2026-07-29, czesc metadanych): usunieto rejestracje `#0` dla ToolsOptionsPages (String load failed ID:0) przez ustawienie resource IDs `110/111` w `ProvideOptionPage` i dodanie zasobu `111` (General) w `source.extension*.resx`.
  - Zrobione (2026-07-30): pelne drzewo podstron opcji w natywnym `Tools > Options` (resource IDs 114-133 w obu pakietach), komendy `Options`/`SpadeOptions` otwieraja konkretne strony natywne, usuniety legacy tor opcji (`CodeJanitorOptionsDialogPage`, `OptionsPageNavigation`, `OptionsWindow`, `OptionsPageControl`) z kompilacji i repo.
  - Decyzja uzytkownika (2026-07-30): pelna migracja na natywne `VisualStudio.Extensibility` Settings API (SettingCategory/Setting.*), mimo statusu preview/experimental - "tylko tak maja opcje dzialac".
  - Pilotaz (2026-07-30, ta sama sesja): `CodeJanitor.VS2022.csproj` przekonwertowany na SDK-style hybrydowy projekt `VSSDK+VisualStudio.Extensibility` (VssdkCompatibleExtension=true, EnableDefaultItems=false, GenerateAssemblyInfo=false, pakiety Microsoft.VisualStudio.Extensibility.Sdk/.Build/(API) w17.14.40254/2098 zamiast Microsoft.VSSDK.BuildTools). Dodano `CodeJanitor.VS2022/NativeSettings/` z `Extension` (RequiresInProcessHosting=true) i pilotazowym `SettingCategory`/`Setting.String` dla jedynego ustawienia strony Switching (Switching_RelatedFileExtensionsExpression). Build headless (MSBuild, bez devenv/VSIXInstaller) 0 bledow; wygenerowany VSIX zawiera JEDNOCZESNIE klasyczny `.pkgdef` (stare komendy/tool windows/ProvideOptionPage nienaruszone) ORAZ nowy `.vsextension/settingsRegistration.json` z poprawna kategoria "Switching" i ustawieniem. Nie instalowane/nie uruchamiane w realnym VS w tej turze (na prosbe uzytkownika - bez odpalania VS).
  - Pozostaje (natywna migracja): instalacja+wizualna weryfikacja w VS2026; zaprojektowanie mostka odczyt/zapis do istniejacego `Settings.Default...` (ReadEffectiveValueAsync/WriteAsync/SubscribeAsync); decyzja o skalowaniu na pozostale 18 stron/194 ustawienia, uwzgledniajac ze natywne API renderuje plaski automatyczny UI (bez customowego WPF layoutu jak dzis).
  - Kryterium done: strony ustawien osadzone/spojne z motywem VS ORAZ otwierane bez legacy launcher (pelne nowoczesne podejscie opcji).

## Wizja funkcjonalna: "darmowy ReSharper" (nowy etap, poza migracja)
Ponizsze pozycje to nowe funkcje produktowe. Nie wchodza w zakres migracji do VS2026.
Wspolny wniosek techniczny: wieksza czesc tych funkcji jest bezpieczniejsza i dokladniejsza w oparciu o Roslyn (Microsoft.CodeAnalysis) + EditorConfig niz o obecny silnik EnvDTE/FileCodeModel. Rekomendowane wprowadzenie warstwy analizy Roslyn obok istniejacego silnika DTE (patrz REC-007).

- BL-007 Wymuszanie file-scoped namespace (konfigurowalne)
  - Priorytet: Medium
  - Status: DONE (code-complete; pozostaje manualna weryfikacja runtime w VS)
  - Zrobione (TDD, ADR-0005/0006):
    - INamespaceScopeConverter + FileScopedNamespaceConverter (Roslyn) w CodeJanitor/Logic/Transformations.
    - 7 testow jednostkowych (RED->GREEN). Suite 176 -> 183, 0 errors.
    - Integracja: ustawienie Cleaning_ConvertToFileScopedNamespace (default False, opt-in) w Settings.settings/Designer/app.config.
    - FileScopedNamespaceLogic (cienka warstwa DTE) wpieta w CodeCleanupManager.RunCodeCleanupCSharp (przed interpretacja code model).
    - Options: checkbox w CleaningUpdate (ViewModel property + mapping + xaml).
    - Build Debug/Release 0 errors; testy 183/183.
  - Pozostaje:
    - manualna weryfikacja runtime: wlaczyc opcje, uruchomic cleanup na pliku z blokowym namespace, potwierdzic konwersje i brak efektow ubocznych.
  - Kryterium done: konfigurowalny krok konwertuje w cleanup bez zmiany innej tresci; testy pokrywaja warianty. [SPELNIONE na poziomie kodu+testow]

- BL-008 Skrot: generowanie XML doc dla publicznych elementow przez GitHub Copilot
  - Priorytet: Medium
  - Status: TODO (wymaga researchu API)
  - Poziom procesu (CodeMaid):
    - Nowa komenda (wzorzec BaseCommand + wpis w .vsct + rejestracja w pakiecie) z wlasnym skrotem klawiszowym, jak istniejace komendy (Reorganize itd.).
    - Komenda enumeruje publiczne/chronione elementy (class/property/method/event) bez XML doc i uzupelnia dokumentacje.
    - Regula interfejsow: jesli metoda/wlasciwosc pochodzi z interfejsu, pelny XML doc generowac po stronie DEFINICJI w interfejsie; implementacja moze uzywac <inheritdoc />. (wymaga analizy semantycznej Roslyn: mapowanie member -> interface member).
  - Integracja z Copilot / LLM (do zbadania):
    - Preferowane: oficjalne API modelu jezykowego w VS 2026 (AI extensibility), jesli dostepne dla VSIX.
    - Alternatywy: istniejaca komenda VS (np. Edit.GenerateDocumentationComment / komenda Copilot) wywolana przez DTE.ExecuteCommand; albo zewnetrzne API z kluczem uzytkownika.
    - Uwaga watkowosc: wywolania async na JoinableTaskFactory, bez blokowania watku UI.
  - Poziom konfiguracji:
    - Ustawienia: zakres elementow (public/protected, typy/wlasciwosci/metody), tryb (on-demand/on-save), wybor providera/modelu, opcjonalny wlasny prompt.
  - Ryzyka: brak stabilnego, wspieranego API do programowego wywolania Copilot; koszt/token; prywatnosc kodu wysylanego do modelu.
  - Kryterium done: dzialajaca komenda ze skrotem, konfigurowalna, dodaje sensowny XML doc do brakujacych publicznych elementow.

- BL-009 Dodawanie readonly do pol/wlasciwosci, ktore sie nie zmieniaja (konfigurowalne)
  - Priorytet: Medium
  - Status: TODO
  - Poziom konfiguracji:
    - Pola: .editorconfig `dotnet_style_readonly_field = true:suggestion` (Roslyn IDE0044) + Code Cleanup.
  - Poziom procesu (CodeMaid):
    - Wymaga analizy przeplywu danych (czy pole jest przypisywane tylko w konstruktorze) - to zadanie dla Roslyn (analiza semantyczna), nie dla EnvDTE.
    - Nowy krok cleanup delegujacy do Roslyn code-fix IDE0044 lub wlasny analizator.
    - Dla wlasciwosci: rozroznic get-only vs init; automatyczne "readonly" dotyczy pol, dla wlasciwosci to inna semantyka (get-only/init).
  - Kryterium done: konfigurowalny krok oznacza pola jako readonly gdy bezpieczne, bez zmiany zachowania; testy na przypadkach granicznych.

- BL-010 Migracja konfiguracji do formatu TOML/YAML
  - Priorytet: Low/Medium
  - Status: TODO (poza migracja; oryginalne zadanie wyklucza migracje systemu ustawien na tym etapie)
  - Stan obecny: ustawienia oparte o .NET Settings (Properties.Settings) + CodeMaid.config; edycja malo wygodna.
  - Propozycja:
    - warstwa wczytujaca/zapisujaca ustawienia z pliku .toml lub .yaml (czytelny, latwy do edycji i wersjonowania),
    - zachowac wsteczna zgodnosc: import istniejacych ustawien, mapowanie 1:1 na obecne klucze,
    - walidacja schematu i wartosci domyslne.
  - Ryzyka: duza zmiana w systemie ustawien, ryzyko regresji wielu funkcji; wymaga pelnej migracji i testow.
  - Kryterium done: konfiguracja czytana/zapisywana z TOML/YAML, import starych ustawien, brak regresji.

- BL-011 Oznaczanie metod jako `static` gdy nie odwoluja sie do instancji (optymalizacja)
  - Priorytet: Low
  - Status: TODO
  - Obszar: cala baza kodu (CodeJanitor + projekt testowy)
  - Kontekst: metody w klasach, ktore nie odwoluja sie do innych metod instancyjnych ani do wlasciwosci/pol instancji, powinny byc zmieniane na `static` tam, gdzie to mozliwe (Roslyn IDE0062 "Make local function static" / analogiczna regula dla metod czlonkowskich, mniejszy narzut na wywolanie, jasniejszy kontrakt braku zaleznosci od stanu instancji).
  - Uwaga: nie dotyczy metod wirtualnych/nadpisywanych, metod z interfejsow, oraz tych uzywanych jako delegaty/event handlery, gdzie zmiana sygnatury mogloby zerwac powiazania.
  - Kryterium done: przeglad kandydatow (np. przez analizator Roslyn), zmiana bezpiecznych przypadkow na `static`, build 0 errors, testy bez regresji.

- BL-011 Poprawa pozycji using i namespace podczas cleanup
  - Priorytet: Medium
  - Status: TODO
  - Kontekst:
    - CodeMaid deleguje remove/sort usingow do wbudowanej komendy VS (EditorContextMenus.CodeWindow.RemoveAndSort) w UsingStatementCleanupLogic.
    - Placement usingow (wewnatrz vs na zewnatrz namespace) jest sterowany przez VS + .editorconfig: `csharp_using_directive_placement = outside_namespace:warning`.
  - Problem: niektore narzedzia/pliki wrzucaja using wewnatrz namespace mimo standardu.
  - Propozycja:
    - konfigurowalny krok wymuszajacy placement (outside/inside) spojnie z .editorconfig,
    - implementacja przez Roslyn (przenoszenie using) lub przez ustawienie i wywolanie odpowiedniej komendy VS,
    - przeglad logiki "reinsert" (wstawianie usingow na StartPoint pliku) pod katem plikow z file-scoped namespace.
  - Kryterium done: cleanup konsekwentnie ustawia usingi zgodnie z konfiguracja; testy na plikach z using wewnatrz namespace.

- BUG-001 Cleanup usuwa file-scoped namespace
  - Priorytet: High
  - Status: TODO (do potwierdzenia reprodukcja i root-cause)
  - Objaw: uruchomienie cleanup na pliku z `namespace X;` (file-scoped) usuwa/uszkadza deklaracje namespace.
  - Znaczenie: powazny blad poprawnosci na nowoczesnym C# (VS2026), moze psuc kompilacje pliku.
  - Hipotezy do zbadania:
    - logika whitespace/region (RemoveWhitespaceLogic, region handling) zakladajaca blokowy `namespace { }`,
    - logika "reinsert" usingow wstawiajaca tekst na StartPoint,
    - zalozenia dotyczace nawiasow klamrowych w parsowaniu tekstu.
  - Nastepne dzialanie: przygotowac minimalny plik repro z file-scoped namespace, uruchomic cleanup krok po kroku, zlokalizowac krok powodujacy usuniecie, dodac test regresyjny.
  - Kryterium done: cleanup zachowuje file-scoped namespace; test regresyjny.

- BUG-002 / feature: formatowanie typow bez cialaimplementacji (placeholder) i opcja `typ;`
  - Priorytet: Medium
  - Status: TODO
  - Objaw: gdy typ jest tylko placeholderem, koncowy `;` bywa ignorowany przez formatowanie.
  - Wazna uwaga jezykowa (zweryfikowana):
    - `record R;` (bezparametrowy rekord bez ciala) jest poprawnym C#.
    - `class C;`, `interface I;`, `struct S;` NIE sa poprawnym C# - wymagaja ciala `{ }`.
    - Zatem opcja "zamien `{}` na `;`" jest bezpieczna wylacznie dla record (i record struct bezparametrowego); dla class/interface/struct nalezy ja wykluczyc lub tylko normalizowac puste ciala `{ }`.
  - Propozycja:
    - naprawic ignorowanie `;` przy typach-placeholderach,
    - konfigurowalna opcja: dla record zamieniac puste ciala `{ }` na `;` (i odwrotnie), z jawnym wykluczeniem class/interface/struct.
  - Kryterium done: poprawne formatowanie placeholderow; opcja dziala tylko tam gdzie jest legalna; testy jednostkowe.

- BL-012 Pelny rebranding do CodeJanitor
  - Priorytet: Medium
  - Status: IN PROGRESS (rename kodu/projektu ZAKONCZONY; zostaje branding graficzny)
  - Zrobione:
    - Pelny rename: namespace SteveCadwallader.CodeMaid -> SteveCadwallader.CodeJanitor, typy, foldery, pliki, sln/snk/config, vsct, resx, xaml, CI.
    - Wersja produktu ustawiona na 0.1; DisplayName/Description w obu pakietach.
    - Walidacja: build Debug/Release CodeJanitor.sln 0 errors; testy 176/176; artefakty SteveCadwallader.CodeJanitor(.VS2022).vsix.
    - Zachowano (LGPL v3): LICENSE.txt, copyright autora, atrybucja "a fork of CodeMaid", docs/.
  - Do zrobienia:
    - grafika brandingu: CodeJanitor.png / CodeJanitor_Large.png / source.extension.ico (obecnie stare obrazy CodeMaid pod nowymi nazwami),
    - tresc README.md (marketing) + zachowanie atrybucji forka,
    - okno About (AboutCommand) - teksty/branding jesli wymaga,
    - reinstalacja: stara testowa instalacja 12.0 (Id b1b6...) zostala odinstalowana (all-users) wraz z leftoverami Exp/CodeMaidClean; pozostal tylko prawdziwy CodeMaid uzytkownika (Id 9079...). Sciezka czysta pod instalacje CodeJanitor 0.1.
  - Kryterium done: spojna marka CodeJanitor w UI, manifescie, grafikach i dokumentacji; brak regresji build/test.

- BL-013 Wycofanie generowania regionow + auto-usuwanie (regiony jako antipattern)
  - Priorytet: Medium
  - Status: TODO (deprecacja fazowa, 2-3 wersje)
  - Kontekst: silnik ma juz RemoveRegionLogic + ustawienie Reorganizing_RegionsRemoveExistingRegions oraz generowanie regionow (GenerateRegionLogic).
  - Propozycja:
    - Faza 1: oznaczyc generowanie regionow jako deprecated w UI; domyslnie wlaczyc usuwanie istniejacych regionow podczas cleanup (opcja).
    - Faza 2: ukryc/oflagowac opcje generowania regionow.
    - Faza 3: usunac GenerateRegionLogic i powiazane ustawienia.
  - Ryzyko: zmiana zachowania dla uzytkownikow polegajacych na regionach; komunikacja w CHANGELOG.
  - Kryterium done: cleanup domyslnie nie tworzy regionow; opcjonalne auto-usuwanie; testy na plikach z regionami.

- BL-014 Integracja z .editorconfig podczas cleanup (Roslyn code-style)
  - Priorytet: High (kluczowe dla wizji "darmowy ReSharper")
  - Status: TODO
  - Cel: CodeJanitor podczas cleanup stosuje reguly stylu z .editorconfig, np.:
    - klamry otwierajace w nowej linii (csharp_new_line_before_open_brace),
    - preferencja `is` zamiast porownan referencyjnych/`!=` null (IDE0041 / csharp_style_prefer_is_null_check_over_reference_equality, wzorce IDE0150),
    - inne reguly formatowania i code-style honorowane przez Roslyn/.editorconfig.
  - Poziom implementacji:
    - opcja A (najprostsza): wywolac VS "Edit.FormatDocument"/Code Cleanup, ktore juz honoruja .editorconfig (czesciowo jest przez RunVisualStudioFormatDocument),
    - opcja B (docelowa): Roslyn Workspace + code-fixy honorujace opcje z .editorconfig (wieksza kontrola, wymaga Microsoft.CodeAnalysis.Workspaces),
    - konfigurowalny wybor ktore reguly stosowac.
  - Zaleznosci: rozszerzenie ADR-0006 (Roslyn) o Workspaces; TDD dla transformacji.
  - Kryterium done: konfigurowalny krok cleanup stosuje wybrane reguly editorconfig; testy na wariantach.

- BL-015 Jeden typ per plik + nazewnictwo plikow generycznych
  - Priorytet: Medium
  - Status: TODO
  - Cel:
    - egzekwowac jedna klasa/enum/record/interfejs na plik (opcjonalnie: analiza + ostrzezenie lub rozdzielenie do osobnych plikow),
    - konwencja nazw plikow dla typow generycznych: `<>` niedozwolone w nazwach plikow Windows, wiec `IDupa<Tin>` -> plik `IDupa{Tin}.cs` (klamry zamiast nawiasow katowych); typ niegeneryczny `IDupa` -> `IDupa.cs`.
  - Poziom implementacji:
    - analiza typow przez Roslyn (liczba deklaracji top-level per plik),
    - komenda/akcja: rozdziel typy do plikow wg konwencji nazw (w tym `{T}`),
    - konfigurowalne (tryb ostrzezenia vs automatyczne rozdzielenie).
  - Ryzyko: przenoszenie typow miedzy plikami to operacja na solution (zmiana projektu), wieksza zlozonosc.
  - Kryterium done: wykrywanie wielu typow per plik; opcjonalne rozdzielenie z poprawnym nazewnictwem generykow; testy nazewnictwa (`IDupa<Tin>` -> `IDupa{Tin}`).

- BL-016 Katalog dodatkowych regul auto-cleanup (Roslyn / .editorconfig)
  - Priorytet: High (realizowany glownie przez BL-014)
  - Status: TODO
  - Uwaga: wiekszosc ponizszych regul to code-style Roslyn sterowany .editorconfig; najtaniej wdrozyc je przez warstwe z BL-014, a nie jako osobne kroki.
  - Bezpieczne (zachowuja zachowanie):
    - var vs typ jawny (IDE0007/0008) - DONE jako VarWhenApparentConverter (Roslyn, 9 testow): var tylko gdy RHS jawnie wskazuje typ (new/cast/array z tekstowym dopasowaniem typu); metoda/literal -> typ jawny. Wpiecie do cleanup: DONE (VarWhenApparentLogic + setting Cleaning_ConvertToVarWhenApparent, default False, opt-in, Options checkbox). Build 0 errors, testy 204/204.
    - readonly dla pol (IDE0044-podobne) - DONE jako ReadonlyFieldConverter (Roslyn, 15 testow): dodaje readonly TYLKO dla pol private, non-partial typu, pojedynczy deklarator, gdy wszystkie zapisy sa bezposrednio w konstruktorze wlasciwego typu (instance) lub konstruktorze statycznym (static); zapis w zwyklej metodzie/akcesorze/lokalnej funkcji/lambdzie dyskwalifikuje; ref/out w konstruktorze dozwolone (zgodnie ze specyfikacja C#). Pola public/internal/protected zawsze pomijane (brak analizy calego solution). Wpiecie do cleanup: DONE (ReadonlyFieldLogic + setting Cleaning_MakeFieldsReadonlyWhenSafe, default False, opt-in, Options checkbox). Build 0 errors, testy 218/218.
    - kwalifikator this. add/remove (IDE0003/0009),
    - kolejnosc modyfikatorow (IDE0036),
    - expression-bodied members (IDE0021-0027),
    - target-typed new() (IDE0090),
    - default zamiast default(T) (IDE0034),
    - nameof zamiast literalow (IDE0280),
    - usuwanie zbednych nawiasow (IDE0047/0048),
    - object/collection initializers (IDE0017/0028),
    - pattern matching / is not / negacje (IDE0019/0078/0083),
    - interpolacja stringow / raw string literals,
    - braces dla single-line if (IDE0011),
    - switch statement -> switch expression gdzie proste (IDE0066),
    - normalizacja EOL/wciec/kodowania wg .editorconfig.
  - Opt-in (wymaga uwagi/semantyki):
    - readonly struct/member (IDE0250/0251),
    - sealed dla klas (CA1852, zmiana API) - DONE jako SealedClassConverter (Roslyn, 10 testow): sealed TYLKO dla klas top-level, nie public/protected (brak zmiany zewnetrznego API zestawu), nie sealed/abstract/static/partial, i gdy zaden inny typ w TYM SAMYM pliku po niej nie dziedziczy. Ograniczenie: nie widzi klas pochodnych w innych plikach tego samego assembly - stad ustawienie domyslnie WYLACZONE (opt-in, jawne ryzyko udokumentowane). Wpiecie do cleanup: DONE (SealedClassLogic + setting Cleaning_SealClassesWhenSafe, default False, Options checkbox z opisem ryzyka). Build 0 errors, testy 228/228.
    - usuwanie nieuzywanych prywatnych czlonkow/zmiennych/parametrow (IDE0051/0052) - ryzyko refleksja/DI,
    - primary constructors / konwersja do record.
  - NIE do auto-cleanup (zmiana zachowania):
    - .Result/.Wait() -> await,
    - usuwanie "martwego" kodu bez pelnej analizy solution.
  - Kryterium done: konfigurowalny zestaw regul stosowany w cleanup (przez BL-014), kazda z testami; brak regresji.

- BL-017 Cleanup tylko zmienionych plikow (git/TFVC)
  - Priorytet: High (bardzo wazne, brak w CodeMaid)
  - Status: DONE (git; TFVC nieobjete)
  - Cel: uruchomic cleanup wylacznie na plikach zmienionych/widocznych w git status (docelowo takze TFVC).
  - Architektura (SOLID):
    - IGitStatusParser + GitStatusParser (czysty parser `git status --porcelain` -> lista sciezek) - DONE, 10 testow (modified/added/untracked/renamed/deleted/ignored/quoted/multi/empty).
    - IChangedFilesProvider + GitChangedFilesProvider (cienki runner: znajdz repo root, uruchom git przez IProcessRunner, wywolaj parser) - DONE, 2 testy.
    - komenda CleanupChangedFilesCommand (CmdIDCodeJanitorCleanupChangedFiles = 0x4000, wpisy w CodeJanitor.vsct/.en-US.vsct/.zh-Hans.vsct, setting Feature_CleanupChangedFiles domyslnie True, Options checkbox "Cleanup Changed Files (git)") - DONE: filtruje AllProjectItems przez SolutionHelper wg sciezek zwroconych przez ChangedFilesProvider, pokazuje potwierdzenie z liczba plikow, otwiera CleanupProgressWindow. Zarejestrowana w CodeJanitorPackage.RegisterCommandsAsync.
  - Zaleznosci: mapowanie sciezek repo -> ProjectItem w solution (proste porownanie FileNames, case-insensitive) - DONE dla pojedynczego repo per solution; wielo-repo nieobslugiwane.
  - Kryterium done: komenda czysci tylko zmienione pliki; testy parsera i providera pokrywaja przypadki. [SPELNIONY dla git] Build 0 errors, testy 228/228.


- BL-018 Wydajnosc cleanup (nie otwierac kazdego pliku w VS)
  - Priorytet: High
  - Status: IN PROGRESS (fundament pomiarowy zrobiony; wlasciwa rearchitektura wymaga decyzji projektowej - patrz nizej)
  - Problem: obecny silnik otwiera kazdy plik w edytorze do analizy (EnvDTE), czasem zamyka, czasem nie; niestabilne i wolne.
  - Analiza (2026-07-29): hotspot to CodeCleanupManager.Cleanup(ProjectItem) - dla kazdego elementu wola projectItem.Open(vsViewKindTextView), potem Cleanup(Document), potem opcjonalnie Document.Close (gdy Cleaning_AutoSaveAndCloseIfOpenedByCleanup i plik nie byl wczesniej otwarty). Petla batchowa: CleanupProgressViewModel.backgroundWorker_DoWork iteruje elementy i wola Cleanup(item) sekwencyjnie na wątku BackgroundWorker (uwaga: Cleanup(ProjectItem) ma ThreadHelper.ThrowIfNotOnUIThread(), wiec faktyczne wywolania DTE i tak trafiaja na UI thread przez marshalling COM - to jest czesc kosztu). Caly silnik cleanup (RunCodeCleanupCSharp itd.) operuje na EnvDTE Document/TextDocument/EditPoint, wiec "nieotwieranie pliku" NIE jest zmiana punktowa - wymaga rownoleglej sciezki analizy (Roslyn/tekst) dla regul ktore tego nie potrzebuja. To pokrywa sie z REC-007 (warstwa Roslyn).
  - Zrobione (fundament, niskie ryzyko, odwracalne): instrumentacja pomiaru czasu w CodeCleanupManager.Cleanup(ProjectItem) - Stopwatch + OutputWindowHelper.DiagnosticWriteLine loguje czas per plik i flage openedByCleanup. Pozwala zmierzyc koszt PRZED optymalizacja i zidentyfikowac ktore pliki/reguly sa najdrozsze, bez zmiany zachowania. Build 0 errors, testy 228/228.
  - Zrobione (MVP klocek #1, headless-Roslyn, C#-only): Logic/Transformations/UsingDirectiveOrganizer.cs (+ IUsingDirectiveOrganizer) - czysta, testowalna bez VS logika sortowania dyrektyw `using` z surowego tekstu (bez otwierania edytora, bez DTE). Sortuje na poziomie compilation unit i namespace (blokowy/file-scoped/zagniezdzony): System-first, grupy zwykle < static < alias, ordinal alfabetycznie. Zachowuje formatowanie (trivia per-slot). Konserwatywnie POMIJA bloki z komentarzami / dyrektywami preprocesora (#if) / `global using`, zeby nie zgubic komentarzy/struktury. 16 testow, build 0 errors, testy 244/244. UWAGA: to na razie samodzielny klocek (pure logic) - NIE jest jeszcze podpiety do zadnej komendy ani ustawienia; integracja w sciezke headless dojdzie gdy powstanie silnik wykonawczy bez-otwierania (glowna, ryzykowna czesc BL-018). Nie usuwa nieuzywanych usingow (to operacja semantyczna, poza zakresem tego klocka).
  - Zrobione (MVP szkielet FLOW + klocek #2): architektura pipeline z klockow. Logic/Transformations/ISourceTransformation.cs = wspolny kontrakt klocka (`string Name`, `string Apply(string source)`; klocek zwraca wejscie bez zmian gdy nie ma nic do zrobienia). Logic/Transformations/SourceTransformationPipeline.cs = orkiestrator, przepuszcza tekst przez uporzadkowana liste klockow po kolei (kazdy dostaje wyjscie poprzedniego; dowolny podzbior/kolejnosc bezpieczne). Logic/Transformations/TabToSpaceConverter.cs = klocek #2 "taby -> spacje" (Roslyn: zamienia TYLKO WhitespaceTrivia, wiec taby w stringach/verbatim/komentarzach zostaja nietkniete; konfigurowalny TabSize, domyslnie 4). UsingDirectiveOrganizer dopiety do ISourceTransformation (Apply->Organize). Testy: TabToSpaceConverter 8, SourceTransformationPipeline 6 (w tym flow: taby->spacje NASTEPNIE sortuj usingi). Build 0 errors, testy 258/258. To jest docelowa forma BL-018: silnik headless bedzie budowal SourceTransformationPipeline z wybranych (wg ustawien) klockow i przepuszczal przez nia tresc pliku C#. Kolejne klocki do zrobienia jako ISourceTransformation: RemoveTrailingWhitespace, NormalizeBlankLines, i adaptery istniejacych czystych konwerterow (VarWhenApparent, ReadonlyField, SealedClass, FileScopedNamespace).
  - Zrobione (MVP klocki #3 i #4): RemoveTrailingWhitespaceConverter (Roslyn: usuwa spacje/taby na koncu linii i czysci linie zlozone tylko z bialych znakow; dziala na trivia - zdejmuje WhitespaceTrivia tylko gdy nastepuje po nim EndOfLine, wiec wciecia zostaja, a biale znaki wewnatrz literalow string/verbatim sa nietkniete; obsluga EOF bez koncowego newline). EnsureFinalNewlineConverter (standard insert_final_newline: dodaje jeden lamacz linii gdy go brak, zachowuje styl \r\n vs \n z pliku, nie rusza istniejacych/wielokrotnych koncowych newline - nie kloci sie z osobna regula pustych linii). Oba implementuja ISourceTransformation. Testy: RemoveTrailingWhitespace 9, EnsureFinalNewline 8, + integracyjny test pipeline 4-klockowego (taby->spacje -> trailing -> sort usingow -> final newline). Build 0 errors, testy 276/276.
  - DECYZJE (uzytkownik niedostepny, agent autonomicznie 2026-07-29): (1) sciezka wykonania headless (komenda czytajaca/zapisujaca plik + wiring VSCT/settings/resx) = ODLOZONA do zatwierdzenia na zywo, bo to duza, trudna do review zmiana; budujemy dalej klocki pure-logic. (2) Konfiguracja klockow = rozsadne domyslne w konstruktorach (TabSize=4 itd.) + DODANY repo-root `.editorconfig` baseline (C#) pod flow BL-018: `indent_style=space`, `indent_size=4`, `trim_trailing_whitespace=true`, `insert_final_newline=true`, `dotnet_sort_system_directives_first=true`, `dotnet_separate_import_directive_groups=false`, `csharp_using_directive_placement=outside_namespace`, `dotnet_style_namespace_match_folder=true:suggestion`, jawne preferencje `var`. (3) Kolejne klocki: zrobione RemoveTrailingWhitespace + EnsureFinalNewline; do zrobienia adaptery istniejacych konwerterow (VarWhenApparent/ReadonlyField/SealedClass/FileScopedNamespace) do ISourceTransformation, ewentualnie NormalizeBlankLines (bardziej opiniotworcze - wymaga decyzji, moze kolidowac z DTE InsertBlankLinePaddingLogic).
  - Standardy Microsoft (C#/.NET/MVP) - gate wykonawczy dla BL-018: kazdy nowy klocek ma byc mapowany na oficjalna regule/konwencje (MS Learn + EditorConfig), miec testy jednostkowe i test integracyjny w pipeline; brak mapowania = brak merge. Zrodla bazowe: Common C# code conventions, Code-style rule options, Overview of .NET source code analysis.
  - Propozycja (wlasciwa rearchitektura - WYMAGA decyzji przed kodowaniem, wysokie ryzyko regresji):
    - dla regul opartych o tekst/Roslyn analizowac zawartosc pliku bez pelnego otwierania edytora,
    - ograniczyc operacje DTE do niezbednego minimum; batchowanie,
    - pomiar czasu cleanup solution przed/po (fundament juz jest).
  - Uwaga: pelne przepisanie sciezki otwierania plikow na Roslyn to duza zmiana dotykajaca calego silnika cleanup; nie robic "na slepo" - najpierw zebrac dane z instrumentacji na realnym rozwiazaniu w VS2026, potem zdecydowac ktore reguly przeniesc na sciezke bez-otwierania (kandydaci: czysto tekstowe/Roslyn jak RemoveWhitespace, FileScopedNamespace, VarWhenApparent, ReadonlyField, SealedClass - te ktore juz maja pure logic; reguly zalezne od FileCodeModel/EditPoint zostaja na DTE).
  - Kryterium done: istotna redukcja liczby otwarc dokumentow i czasu cleanup; brak wiszacych otwartych dokumentow.


- BL-019 Namespace fixer (dopasowanie namespace do sciezki pliku + RootNamespace)
  - Priorytet: Medium
  - Status: TODO
  - Cel: wykrywac i naprawiac niespojnosci miedzy zadeklarowanym `namespace` w pliku .cs a namespace wynikajacym ze sciezki pliku wzgledem projektu, uwzgledniajac `RootNamespace` z .csproj + strukture katalogow (np. RootNamespace = "Acme.App", plik w folderze "Services\Billing" -> oczekiwany namespace "Acme.App.Services.Billing"). Obslugiwac tez `<RootNamespace>` puste (fallback na nazwe projektu / AssemblyName) oraz linki plikow i foldery wykluczone.
  - Zakres:
    - Osobna komenda (nie czesc domyslnego cleanup) - wzorzec jak BL-017 CleanupChangedFilesCommand (Feature_* flag, wpisy vsct/en-US/zh-Hans, rejestracja w RegisterCommandsAsync).
    - Okno planowania (preview) PRZED wprowadzeniem zmian: lista plikow gdzie namespace sie NIE zgadza, pokazac aktualny vs oczekiwany namespace, zaznaczyc niespojnosci; uzytkownik zatwierdza zakres. Wzorzec UI jak CleanupProgressWindow/OptionsWindow (WPF + ViewModel + DataTemplate).
    - Przy zastosowaniu zmiany: poprawic deklaracje `namespace` w pliku ORAZ zaktualizowac wszystkie `using` w plikach ktore referencuja zmieniony typ/namespace (przenoszenie namespace = update referencji w calym rozwiazaniu). To wymaga analizy semantycznej (znalezienie referencji) - Roslyn `RenameNamespace`/symbol rename przez Workspace, albo wlasne wyszukanie i podmiana `using`.
  - Architektura (SOLID, TDD, headless-Roslyn - patrz SCOPE DECISION: tylko C#):
    - INamespaceResolver (czysta logika: (rootNamespace, projectDir, filePath) -> oczekiwany namespace) - w pelni unit-testowalna bez VS, wzorzec jak inne Transformations/*Converter.
    - INamespaceInconsistencyScanner (dla zbioru plikow -> lista niespojnosci: sciezka, aktualny namespace, oczekiwany namespace) - czysta logika na SyntaxTree.
    - Warstwa aplikujaca zmiany + update referencji: wymaga Roslyn Workspace/Compilation (semantyka) do bezpiecznego renamu i update usingow - to najciezsza czesc, zalezna od BL-018 (Workspace/MSBuildWorkspace) i REC-007.
  - Ryzyka/uwagi: przenoszenie namespace + update usingow w calym solution to operacja semantyczna wysokiego ryzyka (regresje kompilacji) - MUSI byc preview + zatwierdzenie + najlepiej dry-run/porownanie przed zapisem; nie robic bez pelnego zestawu testow. Krok 1 (low-risk, TDD): sam INamespaceResolver + skaner niespojnosci (read-only, tylko raportuje w oknie planowania). Krok 2 (high-risk): faktyczne przepisanie namespace + update referencji.
  - Kryterium done: komenda pokazuje w oknie planowania wszystkie niespojnosci namespace vs sciezka/RootNamespace; po zatwierdzeniu poprawia deklaracje namespace i wszystkie zaleznie referencje (using) w rozwiazaniu; brak zlaman kompilacji na projektach testowych.

## Przeglad zgloszen GitHub (upstream codecadwallader/codemaid) i mapowanie na kod
Data przegladu: 2026-07-29. Repo upstream: 498 otwartych issues, slabo utrzymywane (potwierdzone w #1082).
Wspolny root-cause wielu bugow cleanup: silnik oparty o EnvDTE + regex/manipulacje tekstem blednie interpretuje nowoczesny C# i dyrektywy preprocesora. Docelowe, trwale rozwiazanie: warstwa Roslyn (REC-007). Ponizej ocena low-effort vs wymaga researchu.

- UP-1083 "Not supported in Visual Studio 2026 18.6.x"
  - Effort: LOW (juz zaadresowane przez migracje)
  - Nasz kod: CodeMaid.VS2022/source.extension.vsixmanifest (InstallationTarget [17.0,19.0), Pro/Enterprise), rename CodeMaid VS2026 + nowy Id.
  - Status: DONE w ramach migracji (do potwierdzenia end-to-end w VS2026 18.8).

- UP-1079 "ReSharper cleanup won't work with R# 2026.1"
  - Effort: LOW-MEDIUM
  - Nasz kod: CodeCleanupManager.RunJetBrainsReSharperCleanup - wykonuje komendy "ReSharper_SilentCleanupCode"/"ReSharper.ReSharper_SilentCleanupCode".
  - Hipoteza: R# 2026.1 zmienil nazwe komendy; low-effort to dodac aktualna nazwe komendy do listy wywolan (konfigurowalne).
  - Status: TODO (wymaga potwierdzenia nowej nazwy komendy R#).

- UP-1070 "#endif got removed while cleaning up"
  - Effort: MEDIUM-HIGH (bez repro trudne)
  - Nasz kod: kandydaci - RemoveWhitespaceLogic, RemoveRegionLogic, UpdateLogic.UpdateEndRegionDirectives (operuje na liniach `^[ \t]*#`).
  - Uwaga: UpdateEndRegionDirectives filtruje po "region "/"endregion", wiec `#if`/`#endif` teoretycznie pomija; realny sprawca do zlokalizowania przez repro.
  - Status: TODO (repro + test regresyjny).

- UP-1073 "Fixed keyword removed despite skip directives"
  - Effort: MEDIUM-HIGH
  - Repro: `public fixed byte data[8];` -> po cleanup `public byte data[8];`.
  - Nasz kod: kandydaci - InsertExplicitAccessModifierLogic / obsluga pol (CodeElementHelper.GetFieldDeclaration), logika modyfikatorow.
  - Status: TODO (repro + test).

- UP-1078 "Fields region always placed at end of file despite ordering settings"
  - Effort: MEDIUM
  - Nasz kod: logika reorganizacji/regionow (CodeReorganizationManager, GenerateRegionLogic, CodeItemTypeComparer).
  - Status: TODO.

- UP-1075 "Should cleanup remove a pragma line?" / UP-1071 "Annotation issues" / UP-1073 / UP-1070
  - Wspolny watek: cleanup usuwa/modyfikuje dyrektywy/atrybuty, ktorych nie powinien.
  - Powiazanie z BUG-001 (file-scoped namespace) - ta sama klasa problemow manipulacji tekstem.

### Wniosek z przegladu
- Jedyny realnie LOW-effort i juz zrobiony: UP-1083 (wsparcie VS2026) - efekt migracji.
- UP-1079 (komenda R# 2026.1) jest kandydatem low-effort, ale wymaga potwierdzenia aktualnej nazwy komendy.
- Bugi usuwania kodu (UP-1070/1073/1075/1071 + BUG-001/BUG-002) NIE sa low-effort: wymagaja reprodukcji, lokalizacji kroku i testow; docelowo warstwa Roslyn (REC-007).
- Nie wprowadzac "na slepo" poprawek do manipulacji tekstem bez repro i testow (ryzyko nowych regresji).

## Jednoznaczne wnioski
- Backlog dotyczy stabilizacji i jakosci po migracji.
- Pozycje BL-007..BL-011 to nowy etap funkcjonalny (wizja "darmowy ReSharper"); nie sa implementowane w ramach migracji i wymagaja warstwy Roslyn.
- BUG-001 (usuwanie file-scoped namespace) to realny blad poprawnosci - kandydat do priorytetowej naprawy po zebraniu reprodukcji.
- Wiele bugow upstream (UP-1070/1073/1075) ma wspolny root-cause z BUG-001; rozwiazanie systemowe to warstwa Roslyn, nie punktowe laty regex.
