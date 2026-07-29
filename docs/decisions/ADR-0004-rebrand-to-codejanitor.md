# ADR-0004: Rebrand extension to CodeJanitor

## Status
Accepted

## Context
Projekt jest forkiem CodeMaid rozwijanym w kierunku "darmowego ReSharpera" (Swiss-army-knife do C#).
Podczas migracji do VS2026 nadano juz nowy, unikalny VSIX Id (b1b6d05b-97f7-426d-9d6f-fdf8c7662ab2) i tymczasowa nazwe "CodeMaid VS2026", ktora jest mylaca (sugeruje tylko wersje). Potrzebna jest wlasna nazwa produktu.

## Decision
Nazwa produktu: CodeJanitor.
Uzasadnienie: oddaje glowna funkcje (sprzatanie/porzadkowanie kodu po "niesfornych developerach"), jest chwytliwa, w stylu CodeRush/ReSharper/CodeMaid, i nie koliduje bezposrednio ze znakami JetBrains.

Zakres rebrandingu: PELNY (zrealizowany).
- DisplayName + Description + stala Name/Version w obu pakietach (VS2019 i VS2026).
- Namespace: SteveCadwallader.CodeMaid -> SteveCadwallader.CodeJanitor (zachowano autora dla atrybucji LGPL).
- Typy: CodeMaidPackage -> CodeJanitorPackage, CodeMaidSettingsProvider -> CodeJanitorSettingsProvider itd.
- Foldery/pliki: CodeMaid/CodeMaidShared/CodeMaid.VS2022/CodeMaid.UnitTests -> CodeJanitor*, CodeMaid.sln -> CodeJanitor.sln, CodeMaid.snk -> CodeJanitor.snk, CodeMaid.config -> CodeJanitor.config, vsct/imagemanifest/theme xaml/png -> CodeJanitor*.
- Wersja produktu zresetowana do 0.1 (nowy produkt).
- CI (appveyor.yml): sciezki i test assembly zaktualizowane, version 0.1.{build}.
- ZACHOWANE (LGPL): LICENSE.txt, linia copyright "Copyright 2007-2021 Steve Cadwallader (LGPL v3)", atrybucja "a fork of CodeMaid" w opisie, oraz docs/ (rejestr migracji).

## Alternatives considered
- CodeSurgeon / Suture / Scalpel (motyw chirurgiczny, uklon w strone B. Braun) - odrzucone na rzecz bardziej bezposredniego przekazu.
- CodeCustodian / Custodian - odrzucone jako mniej chwytliwe.
- Zachowanie "CodeMaid VS2026" - odrzucone jako mylace i nie-wlasne.

## Consequences
- Plusy: wlasna, spojna marka; jasny przekaz funkcji; brak kolizji z istniejaca instalacja CodeMaid (inny Id).
- Minusy: pozostaje branding graficzny (ikony/preview) i tresc README do dopracowania.
- Uwaga wersjonowanie: pakiet VS2026 (Id b1b6...) mial testowo zainstalowana wersje 12.0 all-users; po zejsciu na 0.1 nalezy najpierw odinstalowac stara instalacje przed ponowna instalacja (konflikt "same or lower version").
- Atrybucja: LICENCJA to LGPL v3 (NIE MIT). Zachowano LICENSE.txt i note copyright autora - wymagane przez LGPL.

## Validation
- Pelny rename: 296 plikow zaktualizowanych tekstowo; wszystkie pliki/foldery CodeMaid* przemianowane; 0 pozostalych wystapien "CodeMaid" w kodzie/projekcie (poza docs/LICENSE, celowo).
- Build Debug (CodeJanitor.sln, DeployExtension=false): 0 errors.
- Build Release (CodeJanitor.sln): 0 errors; artefakty: SteveCadwallader.CodeJanitor.vsix, SteveCadwallader.CodeJanitor.VS2022.vsix.
- Testy: 176/176 passed (SteveCadwallader.CodeJanitor.UnitTests.dll).
- Weryfikacja wizualna w VS2026 (Extensions): POTWIERDZONA - CodeJanitor v0.1 zainstalowany w hifie Exp, VS wystartowal, rozszerzenie dziala (potwierdzenie uzytkownika, devenv PID 39588).

## Related files
- CodeJanitor.sln, CodeJanitor/, CodeJanitorShared/, CodeJanitor.VS2022/, CodeJanitor.UnitTests/
- CodeJanitor.VS2022/source.extension.vsixmanifest, CodeJanitor.VS2022/source.extension.cs
- CodeJanitor/source.extension.vsixmanifest, CodeJanitor/source.extension.cs
- appveyor.yml
- docs/todo/post-migration-backlog.md (BL-012: pozostaly branding graficzny/README)
