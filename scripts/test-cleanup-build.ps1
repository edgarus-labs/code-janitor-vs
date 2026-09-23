<#
.SYNOPSIS
    Runs a cleanup pass and verifies that the solution or touched projects still compile.
.DESCRIPTION
    Executes a build check after code cleanup to ensure no compilation breaks were introduced
    (e.g. CS0549 virtual members in sealed classes, CS0192 readonly ref mutation, CS0701 generic constraints).
.PARAMETER SolutionPath
    Path to the solution file to build and check. Defaults to CodeJanitor.sln in the repo root.
.PARAMETER Configuration
    Build configuration (Debug or Release). Defaults to Release.
#>
[CmdletBinding()]
param(
    [string]$SolutionPath = "CodeJanitor.sln",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "=== CodeJanitor Post-Cleanup Build Verification ==="
Write-Host "Solution: $SolutionPath"
Write-Host "Configuration: $Configuration"

if (-not (Test-Path $SolutionPath)) {
    [Console]::Error.WriteLine("Solution file not found: $SolutionPath")
    exit 1
}

# Determine build tool (dotnet or msbuild). msbuild is preferred: CodeJanitor.sln contains a
# legacy .NET Framework VSIX project (CodeJanitor.csproj) that requires the VSSDK MSBuild
# targets and cannot be built by the dotnet CLI (see ADR-0001).
$buildTool = $null
if (Get-Command "msbuild" -ErrorAction SilentlyContinue) {
    $buildTool = "msbuild"
} elseif (Get-Command "dotnet" -ErrorAction SilentlyContinue) {
    $buildTool = "dotnet"
} else {
    [Console]::Error.WriteLine("Neither dotnet CLI nor msbuild could be found on PATH.")
    exit 1
}

Write-Host "Running build check with $buildTool..."
$buildExitCode = 0

if ($buildTool -eq "dotnet") {
    & dotnet build $SolutionPath -c $Configuration --no-incremental
    $buildExitCode = $LASTEXITCODE
} else {
    & msbuild $SolutionPath /t:Build /p:Configuration=$Configuration "/clp:ErrorsOnly;Summary"
    $buildExitCode = $LASTEXITCODE
}

if ($buildExitCode -ne 0) {
    [Console]::Error.WriteLine("Post-cleanup build verification FAILED with exit code $buildExitCode. The solution does not compile cleanly.")
    exit $buildExitCode
}

Write-Host "Post-cleanup build verification PASSED: solution compiled successfully with 0 errors." -ForegroundColor Green
exit 0
