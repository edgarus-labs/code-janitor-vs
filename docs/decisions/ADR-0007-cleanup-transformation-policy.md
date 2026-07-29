# ADR-0007: Cleanup transformation policy (safety, framework-awareness, testing)

## Status
Accepted

## Context
CodeJanitor rozwija auto-cleanup w stronę "darmowego ReSharpera". Uzytkownik ustalil twarde zasady bezpieczenstwa i kompatybilnosci oraz oczekuje testow gwarantujacych, ze cleanup nie psuje kodu.

## Decision
1. Bezpieczenstwo transformacji:
   - NIE robimy ryzykownych usuniec (martwy kod, nieuzywane skladowe bez pelnej analizy solution).
   - NIE migrujemy .Result/.Wait() -> await (zmiana semantyki, "samoboj").
   - Dodajemy: sealed (gdy bezpieczne), readonly (pola/struktury).
2. Regula var:
   - var TYLKO gdy prawa strona jawnie wskazuje typ (new T(...), (T)x, default(T), literaly, new T[]).
   - Jesli RHS to wywolanie metody -> zostaw typ jawny (NIE var).
   - Poza tym zgodnie ze standardem/.editorconfig.
3. Kompatybilnosc frameworkow:
   - Domyslnie zakladamy nowoczesny .NET (net10) + najnowszy C#, ale kod moze byc netstandard lub .NET Framework 4.8.
   - Kazda regula deklaruje wymagania (min C# LangVersion / TFM). Reguly niedostepne dla danego frameworka sa pomijane.
   - Scenariusze cleanup konfigurowalne per framework.
4. Testowanie:
   - TDD dla kazdej nowej transformacji (pure logic, bez VS).
   - Dodatkowo testy "cleanup safety": po transformacji kod pozostaje kompilowalny (parsowanie Roslyn bez bledow skladni; docelowo kompilacja semantyczna dla wybranych regul) i rownowazny semantycznie tam gdzie to wymagane.
5. Wydajnosc:
   - Cel: nie otwierac kazdego pliku w VS do analizy (obecny CodeMaid otwiera/zamyka dokumenty, niestabilnie). Preferowac analize tekstu/Roslyn bez pelnego otwierania edytora tam gdzie to mozliwe.

## Alternatives considered
- Automatyczne agresywne refaktory - odrzucone (ryzyko regresji, "samoboj").
- Brak swiadomosci frameworka - odrzucone (var/file-scoped/new() zaleza od wersji C#).

## Consequences
- Plusy: bezpieczny, przewidywalny cleanup; kompatybilnosc wieloframeworkowa; zaufanie dzieki testom.
- Minusy: kazda regula wymaga metadanych o wersji i testow; wolniejszy rozwoj, ale stabilny.

## Validation
- Testy jednostkowe per regula (TDD).
- Testy cleanup-safety (kod kompilowalny po transformacji).
- Brak regresji istniejacych testow (aktualnie 183).

## Related files
- CodeJanitorShared/Logic/Transformations/*
- CodeJanitor.UnitTests/Transformations/*
- docs/todo/post-migration-backlog.md (BL-014/016/017/018)
