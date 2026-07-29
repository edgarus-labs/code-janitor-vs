# CodeMaid Migration Documentation

## Cel dokumentacji
Kompletna dokumentacja analizy, planowania, wykonania i walidacji migracji repozytorium CodeMaid do Visual Studio 2026.

## Data ostatniej aktualizacji
2026-07-29

## Aktualny status
IN PROGRESS

## Dokument startowy
Rozpocznij od: [migration/05-final-migration-report.md](migration/05-final-migration-report.md)

## Aktualny etap migracji
Etap: walidacja runtime GUI zakonczona; migracja podstawowa domknieta

## Migration Quality Gate
VISUAL_STUDIO_2026_MIGRATION_GATE: PASS

## Najwazniejsze blokady
- Brak blokad krytycznych migracji.
- Prace jakosciowe (nie-blokujace): redukcja VSTHRD*, aktualizacja CI, spojnosc UI (WPF) z motywem VS (BL-006).

## Indeks dokumentow
### Migration
- [migration/00-repository-inventory.md](migration/00-repository-inventory.md)
- [migration/01-initial-assessment.md](migration/01-initial-assessment.md)
- [migration/02-visual-studio-2026-migration-plan.md](migration/02-visual-studio-2026-migration-plan.md)
- [migration/03-migration-progress.md](migration/03-migration-progress.md)
- [migration/04-validation-results.md](migration/04-validation-results.md)
- [migration/05-final-migration-report.md](migration/05-final-migration-report.md)
- [migration/06-post-migration-recommendations.md](migration/06-post-migration-recommendations.md)

### Decisions
- [decisions/README.md](decisions/README.md)
- [decisions/ADR-0001-vs2026-native-msbuild-and-vstest.md](decisions/ADR-0001-vs2026-native-msbuild-and-vstest.md)
- [decisions/ADR-0002-keep-net-framework-472.md](decisions/ADR-0002-keep-net-framework-472.md)
- [decisions/ADR-0003-keep-dual-vsix-targeting.md](decisions/ADR-0003-keep-dual-vsix-targeting.md)

### TODO
- [todo/migration-todo.md](todo/migration-todo.md)
- [todo/post-migration-backlog.md](todo/post-migration-backlog.md)

## Jednoznaczne wnioski
1. Build i testy przechodza w natywnym toolchain VS2026.
2. Dotnet CLI nie jest rownowaznym pipeline build dla tego typu projektu.
3. Walidacja runtime GUI zakonczona: rozszerzenie CodeMaid VS2026 zaladowane i dzialajace w VS2026.

## Deploy do VS Experimental (stabilny workflow)
Uzywaj zawsze jednego skryptu, ktory ubija wszystkie procesy devenv PRZED deployem:

```powershell
& "C:\Dev\codemaid\scripts\deploy-exp.ps1" -Configuration Debug
```

Opcjonalnie (gdy Exp jest rozjechany):

```powershell
& "C:\Dev\codemaid\scripts\deploy-exp.ps1" -Configuration Debug -CleanHive
```

Opcjonalnie z automatycznym uruchomieniem VS po deployu:

```powershell
& "C:\Dev\codemaid\scripts\deploy-exp.ps1" -Configuration Debug -LaunchVS
```
