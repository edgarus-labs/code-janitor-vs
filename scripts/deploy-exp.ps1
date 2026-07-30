param(
    [string]$Configuration = "Debug",
    [switch]$CleanHive,
    [switch]$LaunchVS
)

$ErrorActionPreference = "Stop"

$vsRoot = "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE"
$devenv = Join-Path $vsRoot "devenv.exe"
$vsixInstaller = Join-Path $vsRoot "VSIXInstaller.exe"
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe"

$repoRoot = "C:\Dev\codemaid"
$project = Join-Path $repoRoot "CodeJanitor.VS2026\CodeJanitor.VS2026.csproj"
$vsix = Join-Path $repoRoot "CodeJanitor.VS2026\bin\$Configuration\net472\CodeJanitor.VS2026.vsix"
$expHive = "C:\Users\gawdprpl\AppData\Local\Microsoft\VisualStudio\18.0_ec255184Exp"
$vsInstanceId = "ec255184"

function Stop-ExpDevenv {
    # Only stop devenv.exe instances running the Exp hive (/rootsuffix Exp), never the user's main VS session.
    $expProcesses = Get-CimInstance Win32_Process -Filter "Name = 'devenv.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match '(?i)/rootsuffix\s+Exp' }

    foreach ($p in $expProcesses) {
        Write-Host "  stopping Exp devenv (PID $($p.ProcessId))"
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "[1/6] Stopping devenv Exp-hive instances (if any)..."
Stop-ExpDevenv

Write-Host "[2/6] Removing stale CodeJanitor extension install folders..."
$extensionsRoot = Join-Path $expHive "Extensions"
if (Test-Path $extensionsRoot) {
    Get-ChildItem $extensionsRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $manifest = Join-Path $_.FullName "extension.vsixmanifest"
        $isCodeJanitor = ($_.Name -eq "Steve Cadwallader") -or
            ((Test-Path $manifest) -and (Select-String -Path $manifest -Pattern "<DisplayName>CodeJanitor</DisplayName>" -Quiet))

        if ($isCodeJanitor) {
            Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path $_.FullName) {
                Write-Host "  WARNING: could not remove $($_.FullName) (file lock?) - stop all Exp devenv/ServiceHub processes and retry."
            }
            else {
                Write-Host "  removed stale install: $($_.FullName)"
            }
        }
    }
}

if ($CleanHive) {
    Write-Host "[2b/6] Cleaning Experimental hive caches..."
    $toDelete = @(
        (Join-Path $expHive "ComponentModelCache"),
        (Join-Path $expHive "ImageLibrary"),
        (Join-Path $expHive "Cache"),
        (Join-Path $expHive "privateregistry.bin")
    )

    foreach ($path in $toDelete) {
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  removed: $path"
        }
    }
}

Write-Host "[3/6] Building VSIX project ($Configuration)..."
& $msbuild $project /t:Build /p:Configuration=$Configuration /p:DeployExtension=false "/clp:ErrorsOnly;Summary"

if (-not (Test-Path $vsix)) {
    throw "VSIX not found: $vsix"
}

Write-Host "[4/6] Installing VSIX to Experimental hive..."
$installLog = Join-Path $env:TEMP "codejanitor_vsix_install_exp.log"
& $vsixInstaller /q /shutdownprocesses /instanceIds:$vsInstanceId /rootSuffix:Exp /logFile:$installLog $vsix
$vsixExit = $LASTEXITCODE

# VSIXInstaller may return non-zero (e.g. 2001) even when install completed; trust the log marker.
$installSucceeded = (Test-Path $installLog) -and
    (Select-String -Path $installLog -Pattern "Install to Visual Studio .* completed successfully" -Quiet)

if ((-not $installSucceeded) -or ($vsixExit -ne 0 -and $vsixExit -ne 2001)) {
    throw "VSIX install/update failed (exit=$vsixExit). See log: $installLog"
}

Write-Host "  installer exit: $vsixExit (accepted)"
Write-Host "  install log: $installLog"

Write-Host "[5/6] ResetSkipPkgs + UpdateConfiguration..."
& $devenv /rootsuffix Exp /ResetSkipPkgs
& $devenv /rootsuffix Exp /updateconfiguration

Write-Host "[6/6] Final devenv Exp-hive cleanup..."
Stop-ExpDevenv

if ($LaunchVS) {
    Write-Host "Launching Visual Studio Experimental..."
    Start-Process -FilePath $devenv -ArgumentList "/rootsuffix", "Exp"
}

Write-Host "Done."
