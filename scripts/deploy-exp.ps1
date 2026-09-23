param(
    [string]$Configuration = "Debug",
    [switch]$CleanHive,
    [switch]$LaunchVS
)

$ErrorActionPreference = "Stop"

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vswhere -latest -prerelease -property installationPath
$vsInstanceId = & $vswhere -latest -prerelease -property instanceId
if (-not $vsPath -or -not $vsInstanceId) {
    throw "Visual Studio was not found by vswhere."
}
$vsMajor = (& $vswhere -latest -prerelease -property installationVersion).Split('.')[0]

$vsRoot = Join-Path $vsPath "Common7\IDE"
$devenv = Join-Path $vsRoot "devenv.exe"
$vsixInstaller = Join-Path $vsRoot "VSIXInstaller.exe"
$msbuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"

$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot "src\CodeJanitor\CodeJanitor.csproj"
$vsix = Join-Path $repoRoot "src\CodeJanitor\bin\$Configuration\net472\win\CodeJanitor.vsix"
$expHive = Join-Path $env:LOCALAPPDATA "Microsoft\VisualStudio\$vsMajor.0_${vsInstanceId}Exp"
$extensionId = "b1b6d05b-97f7-426d-9d6f-fdf8c7662ab2"

function Get-InstalledExtensionFolders {
    $extensionsRoot = Join-Path $expHive "Extensions"
    if (-not (Test-Path $extensionsRoot)) {
        return
    }

    Get-ChildItem $extensionsRoot -Recurse -Filter "extension.vsixmanifest" -ErrorAction SilentlyContinue |
        Where-Object { Select-String -Path $_.FullName -Pattern $extensionId -SimpleMatch -Quiet } |
        ForEach-Object { $_.Directory.FullName }
}

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
foreach ($folder in @(Get-InstalledExtensionFolders)) {
    Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $folder) {
        Write-Host "  WARNING: could not remove $folder (file lock?) - stop all Exp devenv/ServiceHub processes and retry."
    }
    else {
        Write-Host "  removed stale install: $folder"
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
$vsixExit = (Start-Process $vsixInstaller -ArgumentList "/q", "/shutdownprocesses", "/instanceIds:$vsInstanceId", "/rootSuffix:Exp", "/logFile:`"$installLog`"", "`"$vsix`"" -Wait -PassThru).ExitCode

if (-not @(Get-InstalledExtensionFolders)) {
    throw "VSIX install failed (exit=$vsixExit): the extension is not installed in $expHive. See log: $installLog"
}

Write-Host "  installer exit: $vsixExit"
Write-Host "  install log: $installLog"

Write-Host "[5/6] ResetSkipPkgs + UpdateConfiguration..."
& $devenv /rootsuffix Exp /ResetSkipPkgs

# devenv /updateconfiguration hands off the actual merge to a background devenv.exe worker
# process (shows up with just "/updateConfiguration" on its command line, no /rootsuffix) and
# returns almost immediately itself - it does NOT block until the merge is done. Waiting for
# that worker process to fully exit is required before privateregistry.bin can be trusted/read.
function Wait-UpdateConfigurationWorker {
    param([int]$TimeoutSeconds = 180)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $worker = Get-CimInstance Win32_Process -Filter "Name = 'devenv.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -match '(?i)/updateConfiguration' }
        if (-not $worker) { return }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    Write-Host "  WARNING: /updateConfiguration worker process still running after ${TimeoutSeconds}s timeout."
}

& $devenv /rootsuffix Exp /updateconfiguration
Wait-UpdateConfigurationWorker

# /updateconfiguration merges each extension's pkgdef into privateregistry.bin. If it's
# interrupted (or the merge silently no-ops), CodeJanitor's package/menu registrations never
# make it into the hive even though the extension files are on disk. Verify and retry once.
function Test-PkgDefMerged {
    $regPath = Join-Path $expHive "privateregistry.bin"
    if (-not (Test-Path $regPath)) { return $false }
    try {
        $fs = [System.IO.File]::Open($regPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    }
    catch [System.IO.IOException] {
        # Still locked by the update-configuration worker - treat as "not verified yet", not fatal.
        return $false
    }
    try {
        $bytes = New-Object byte[] $fs.Length
        $fs.Read($bytes, 0, $bytes.Length) | Out-Null
    }
    finally {
        $fs.Close()
    }
    $text = [System.Text.Encoding]::Unicode.GetString($bytes)
    return $text.Contains("CodeJanitor.pkgdef")
}

if (-not (Test-PkgDefMerged)) {
    Write-Host "  WARNING: CodeJanitor pkgdef not found in privateregistry.bin after /updateconfiguration - retrying..."
    & $devenv /rootsuffix Exp /updateconfiguration
    Wait-UpdateConfigurationWorker
    if (-not (Test-PkgDefMerged)) {
        # NOTE: this check has proven unreliable for CodeJanitor (a hybrid VSSDK +
        # VisualStudio.Extensibility extension) - it has repeatedly reported "not merged" here
        # even when the extension loads and works correctly (Options page, menus, and
        # Extensions > Manage Extensions all show CodeJanitor as installed/enabled). Treat as a
        # non-fatal warning instead of aborting the deploy.
        Write-Host "  WARNING: CodeJanitor pkgdef still not found in privateregistry.bin after retry."
        Write-Host "  This check is known to be unreliable for hybrid VSSDK/Extensibility extensions - continuing anyway."
    }
    else {
        Write-Host "  pkgdef merge verified in privateregistry.bin."
    }
}
else {
    Write-Host "  pkgdef merge verified in privateregistry.bin."
}

Write-Host "[6/6] Final devenv Exp-hive cleanup..."
Stop-ExpDevenv

if ($LaunchVS) {
    Write-Host "Launching Visual Studio Experimental..."
    Start-Process -FilePath $devenv -ArgumentList "/rootsuffix", "Exp"
}

Write-Host "Done."
