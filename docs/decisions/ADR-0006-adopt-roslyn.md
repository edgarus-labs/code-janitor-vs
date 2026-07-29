# ADR-0006: Introduce Roslyn (Microsoft.CodeAnalysis.CSharp) for code transformations

## Status
Accepted

## Context
Nowe funkcje (file-scoped namespace, readonly, placement usingow) wymagaja poprawnej analizy/transformacji skladni C#. Obecny silnik EnvDTE + regex jest kruchy i jest zrodlem bugow (np. usuwanie file-scoped namespace, usuwanie `#endif`, gubienie `fixed`). Roslyn zapewnia poprawne parsowanie i przeksztalcenia (SyntaxTree/SyntaxRewriter).

## Decision
Wprowadzamy Microsoft.CodeAnalysis.CSharp jako warstwe analizy/transformacji dla nowych funkcji.
- Czyste transformacje typu `string -> string` (parsuj, przeksztalc, zwroc tekst) - jednostkowo testowalne bez VS.
- Integracja z DTE/VS pozostaje cienka (pobierz tekst dokumentu, wywolaj transformacje, zapisz wynik).
- Wersja pakietu kompatybilna z net472 (netstandard2.0).

## Alternatives considered
- Regex/manipulacja tekstem - odrzucone (kruche, znane bugi).
- Wlasny parser - odrzucone (ogromny koszt, gorsza jakosc niz Roslyn).

## Consequences
- Plusy: poprawnosc transformacji, mozliwosc analizy semantycznej w przyszlosci, mniejsze ryzyko bugow.
- Minusy: nowa zaleznosc; mozliwe binding redirects na net472; wieksze artefakty.
- Uwaga: w srodowisku VS Roslyn jest juz dostepny; dla testow jednostkowych pakiet dostarcza zaleznosci.

## Validation
- Pierwsza funkcja realizowana w tym modelu: konwerter file-scoped namespace (BL-007) z pelnym zestawem testow jednostkowych (TDD).
- Build i testy (vstest.console) zielone; brak regresji 176 istniejacych testow.

## Related files
- CodeJanitorShared/Logic/* (transformacje Roslyn)
- CodeJanitor.UnitTests/* (testy)
- docs/todo/post-migration-backlog.md (BL-007)
