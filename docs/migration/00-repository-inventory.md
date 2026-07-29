# 00 - Repository Inventory

## Cel dokumentu
Techniczna inwentaryzacja repozytorium CodeMaid jako podstawa migracji do Visual Studio 2026.

## Data ostatniej aktualizacji
2026-07-29

## Aktualny status
DONE

## Zakres skanowania
- Rozwiazania: CodeMaid.sln
- Projekty: CodeMaid/CodeMaid.csproj, CodeMaid.VS2022/CodeMaid.VS2022.csproj, CodeMaid.UnitTests/CodeMaid.UnitTests.csproj, CodeMaidShared/CodeMaidShared.shproj
- Manifesty VSIX: CodeMaid/source.extension.vsixmanifest, CodeMaid.VS2022/source.extension.vsixmanifest
- Konfiguracja CI: appveyor.yml
- Dokumentacja build: README.md, CONTRIBUTING.md

## Pliki i artefakty wykryte
- .sln/.slnx: 1 (CodeMaid.sln)
- .csproj: 3 (CodeMaid, CodeMaid.VS2022, CodeMaid.UnitTests)
- .shproj/.projitems: 1 shared project (CodeMaidShared)
- .vsixmanifest: 2
- .vsct: 3 (CodeMaid/*.vsct)
- appveyor CI: 1
- GitHub Actions: NOT FOUND
- Azure Pipelines: NOT FOUND
- Directory.Build.props: NOT FOUND
- Directory.Build.targets: NOT FOUND
- Directory.Packages.props: NOT FOUND
- global.json: NOT FOUND
- NuGet.config (repo-local): NOT FOUND
- packages.config: NOT FOUND
- .pkgdef source files: NOT FOUND (pkgdef generowany przez MSBuild, GeneratePkgDefFile=true)

## Architektura techniczna (inwentarz)
- Projekt CodeMaid:
  - Typ: klasyczny VSIX (.NET Framework, non-SDK csproj)
  - TargetFrameworkVersion: v4.7.2
  - MinimumVisualStudioVersion: 15.0
  - VSIX target: [16.0,17.0)
  - VS SDK NuGet: Microsoft.VisualStudio.SDK 16.10.31321.278
  - Build tools: Microsoft.VSSDK.BuildTools 16.11.35
- Projekt CodeMaid.VS2022:
  - Typ: klasyczny VSIX (.NET Framework, non-SDK csproj)
  - TargetFrameworkVersion: v4.7.2
  - VSIX target: [17.0,19.0), ProductArchitecture=amd64
  - VS SDK NuGet: Microsoft.VisualStudio.SDK 17.0.0-previews-2-31512-422
  - Build tools: Microsoft.VSSDK.BuildTools 17.0.3177-preview3
- Projekt CodeMaid.UnitTests:
  - Typ: klasyczny MSTest/NUnit test project (.NET Framework 4.7.2)
  - Microsoft.NET.Test.Sdk 17.0.0
  - MSTest.TestAdapter/TestFramework 2.2.7
  - NUnit 3.13.2 + NUnit3TestAdapter 4.0.0
- Projekt CodeMaidShared:
  - Shared project zawierajacy glowna logike i integracje z API VS
  - Szerokie uzycie EnvDTE oraz Microsoft.VisualStudio.Shell

## Zaleznosci i API (wynik skanowania kodu)
- EnvDTE: FOUND (w wielu plikach pod CodeMaidShared)
- Microsoft.VisualStudio.Shell: FOUND
- JoinableTaskFactory / threading analyzers: FOUND (ostrzezenia VSTHRD*)
- MEF/export: wykryte wzorce zwiazane z integracja VS
- Roslyn API: wykorzystywane przez rozszerzenie (indirectly przez SDK i logike analizy kodu)

## Toolchain i komendy diagnostyczne
### Komendy
- dotnet --info
- dotnet --list-sdks
- dotnet --list-runtimes
- dotnet restore CodeMaid.sln
- dotnet build CodeMaid.sln -c Debug
- dotnet build CodeMaid.sln -c Release
- dotnet test CodeMaid.sln -c Debug --no-build
- "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Debug /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
- "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" CodeMaid.sln /restore /t:Build /p:Configuration=Release /p:Platform="Any CPU" "/clp:ErrorsOnly;Summary"
- "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" "c:\Dev\codemaid\CodeMaid.UnitTests\bin\Debug\SteveCadwallader.CodeMaid.UnitTests.dll"

### Wyniki
- dotnet SDK: 10.0.302
- VS instance: Visual Studio Professional 2026 18.8.1
- dotnet restore: SUCCESS
- dotnet build: FAIL (MSB3822/MSB3823 dla projektow VSIX)
- dotnet test: FAIL (zalezny od dotnet build)
- VS2026 MSBuild Debug: SUCCESS (0 errors)
- VS2026 MSBuild Release: SUCCESS (0 errors, duza liczba warningow VSTHRD*)
- VS2026 vstest.console: SUCCESS (176/176 testow passed)

## Artefakty walidacyjne
- Wygenerowane VSIX:
  - CodeMaid/bin/Debug/SteveCadwallader.CodeMaid.vsix
  - CodeMaid/bin/Release/SteveCadwallader.CodeMaid.vsix
  - CodeMaid.VS2022/bin/Debug/SteveCadwallader.CodeMaid.VS2022.vsix
  - CodeMaid.VS2022/bin/Release/SteveCadwallader.CodeMaid.VS2022.vsix

## Wnioski
1. Repozytorium jest klasycznym rozwiazaniem VSIX .NET Framework i poprawnie buduje sie w natywnym MSBuild Visual Studio 2026.
2. Budowanie przez dotnet CLI nie jest rownowazne dla tego typu projektu i obecnie failuje na zasobach non-string (MSB3822/MSB3823).
3. Sciezka migracyjna do VS2026 powinna pozostac oparta o VS-native MSBuild/devenv oraz manualna walidacje instalacji w instancji eksperymentalnej.
4. Najwieksze ryzyko techniczne po migracji to wysoki poziom ostrzezen VSTHRD* (jakosc, nie blocker builda).
