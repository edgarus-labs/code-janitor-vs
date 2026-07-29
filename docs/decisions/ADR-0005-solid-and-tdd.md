# ADR-0005: Adopt SOLID design and TDD for new functionality

## Status
Accepted

## Context
CodeJanitor rozwija sie w kierunku "darmowego ReSharpera". Nowe funkcje (transformacje kodu, analizy) musza byc niezawodne i latwe do utrzymania. Dotychczasowy silnik (EnvDTE) jest slabo testowalny; istniejace testy pokrywaja czysta logike (formatowanie, helpery).

## Decision
Dla KAZDEJ nowej funkcjonalnosci obowiazuje:
- TDD: najpierw test (RED), potem minimalna implementacja (GREEN), potem refaktor.
- SOLID:
  - SRP: jedna klasa = jedna odpowiedzialnosc (np. czysty konwerter tekstu osobno od integracji z DTE/VS).
  - OCP/DIP: logika za interfejsami; zaleznosci wstrzykiwane, nie tworzone statycznie w srodku.
  - Separacja: czysta logika (bez VS/DTE) w klasach testowalnych jednostkowo; cienka warstwa integracji VS na zewnatrz.
- Nowa logika musi byc jednostkowo testowalna bez uruchamiania Visual Studio.

## Alternatives considered
- Kontynuacja bez testow dla nowych funkcji - odrzucone (ryzyko regresji, brak zaufania).
- Testy tylko integracyjne (w VS) - odrzucone (wolne, kruche, wymagaja IDE).

## Consequences
- Plusy: mniej regresji, latwiejszy refaktor, projektowanie pod testowalnosc wymusza dobra architekture.
- Minusy: wiekszy naklad na starcie kazdej funkcji; konieczna izolacja logiki od DTE.

## Validation
- Kazdy nowy feature dostarcza testy jednostkowe uruchamiane przez vstest.console (aktualnie 176/176 baseline).
- Brak regresji istniejacych testow.

## Related files
- CodeJanitor.UnitTests/* (testy)
- CodeJanitorShared/Logic/* (czysta logika)
- docs/migration/06-post-migration-recommendations.md (REC-007 warstwa Roslyn)
