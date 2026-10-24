<#
.SYNOPSIS
    Verifies that the release tag, the VSIX file name, the VSIX manifest and the packaged assembly carry the same version.
.DESCRIPTION
    Expects exactly one .vsix file in ArtifactDirectory and fails when any of these disagree:
      - the release tag and ExpectedVersion (v<major>.<minor>.<patch> maps to <major>.<minor>.<patch>.0 and
        v<major>.<minor>.<patch>-<label>.<revision> maps to <major>.<minor>.<patch>.<revision>);
      - the file name and edgarus-labs.code-janitor.vs.<tag>.vsix, or edgarus-labs.code-janitor.vs.v<ExpectedVersion>.vsix
        when no tag is given;
      - the Identity Version of extension.vsixmanifest inside the package and ExpectedVersion;
      - the AssemblyVersion and AssemblyFileVersion of CodeJanitor.dll inside the package and ExpectedVersion.
    Exits with 0 only when every check passes.
.PARAMETER ArtifactDirectory
    Directory that contains the collected VSIX file.
.PARAMETER ExpectedVersion
    The four-part version stamped into the build, for example 1.4.1.0.
.PARAMETER Tag
    The release tag, for example v1.4.1. Omit it for builds that are not tagged.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,

    [string]$Tag = ''
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$failures = [System.Collections.Generic.List[string]]::new()

if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Expected version '$ExpectedVersion' must use <major>.<minor>.<patch>.<revision>."
}

if ($Tag) {
    if ($Tag -notmatch '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-[A-Za-z][A-Za-z0-9.-]*\.(?<revision>\d+))?$') {
        $failures.Add("Tag '$Tag' must use v<major>.<minor>.<patch> or v<major>.<minor>.<patch>-<label>.<revision>.")
    }
    else {
        $revision = if ($Matches['revision']) { $Matches['revision'] } else { '0' }
        $tagVersion = "$($Matches['major']).$($Matches['minor']).$($Matches['patch']).$revision"
        if ($tagVersion -ne $ExpectedVersion) {
            $failures.Add("Tag '$Tag' maps to version $tagVersion, but the build was stamped with $ExpectedVersion.")
        }
    }

    $expectedFileName = "edgarus-labs.code-janitor.vs.$Tag.vsix"
}
else {
    $expectedFileName = "edgarus-labs.code-janitor.vs.v$ExpectedVersion.vsix"
}

$vsixFiles = @(Get-ChildItem -Path $ArtifactDirectory -Filter *.vsix -File)
if ($vsixFiles.Count -ne 1) {
    throw "Expected exactly one VSIX file in '$ArtifactDirectory', found $($vsixFiles.Count)."
}

$vsixFile = $vsixFiles[0]
if ($vsixFile.Name -cne $expectedFileName) {
    $failures.Add("VSIX file name '$($vsixFile.Name)' does not match '$expectedFileName'.")
}

$extractDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
try {
    [System.IO.Compression.ZipFile]::ExtractToDirectory($vsixFile.FullName, $extractDirectory)

    $manifestPath = Join-Path $extractDirectory 'extension.vsixmanifest'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        $failures.Add("The VSIX package does not contain extension.vsixmanifest.")
    }
    else {
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
        $manifestVersion = $manifest.PackageManifest.Metadata.Identity.Version
        if ($manifestVersion -ne $ExpectedVersion) {
            $failures.Add("VSIX manifest Identity Version '$manifestVersion' does not match $ExpectedVersion.")
        }
    }

    $assemblyPath = Join-Path $extractDirectory 'CodeJanitor.dll'
    if (-not (Test-Path -LiteralPath $assemblyPath)) {
        $failures.Add("The VSIX package does not contain CodeJanitor.dll.")
    }
    else {
        $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
        if ($assemblyVersion -ne $ExpectedVersion) {
            $failures.Add("CodeJanitor.dll AssemblyVersion '$assemblyVersion' does not match $ExpectedVersion.")
        }

        $fileVersion = (Get-Item -LiteralPath $assemblyPath).VersionInfo.FileVersion
        if ($fileVersion -ne $ExpectedVersion) {
            $failures.Add("CodeJanitor.dll AssemblyFileVersion '$fileVersion' does not match $ExpectedVersion.")
        }
    }
}
finally {
    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        [Console]::Error.WriteLine("Version mismatch: $failure")
    }

    exit 1
}

Write-Host "VSIX version check passed: $($vsixFile.Name) carries version $ExpectedVersion in its manifest and CodeJanitor.dll."
