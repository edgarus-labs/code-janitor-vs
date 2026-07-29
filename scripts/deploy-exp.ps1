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
$project = Join-Path $repoRoot "CodeJanitor.VS2022\CodeJanitor.VS2022.csproj"
$vsix = Join-Path $repoRoot "CodeJanitor.VS2022\bin\$Configuration\SteveCadwallader.CodeJanitor.VS2022.vsix"
$expHive = "C:\Users\gawdprpl\AppData\Local\Microsoft\VisualStudio\18.0_ec255184Exp"

Write-Host "[1/6] Killing all devenv processes..."
Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force

if ($CleanHive) {
    Write-Host "[2/6] Cleaning Experimental hive caches..."
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
& $vsixInstaller /q /f /rootSuffix:Exp /logFile:$installLog $vsix
Write-Host "  install log: $installLog"

Write-Host "[5/6] ResetSkipPkgs + UpdateConfiguration..."
& $devenv /rootsuffix Exp /ResetSkipPkgs
& $devenv /rootsuffix Exp /updateconfiguration

Write-Host "[6/6] Final devenv cleanup..."
Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force

if ($LaunchVS) {
    Write-Host "Launching Visual Studio Experimental..."
    Start-Process -FilePath $devenv -ArgumentList "/rootsuffix", "Exp"
}

Write-Host "Done."
