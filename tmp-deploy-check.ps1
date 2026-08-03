$ErrorActionPreference = 'Stop'
$procId = 56248
Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
try { Wait-Process -Id $procId -Timeout 30 -ErrorAction SilentlyContinue | Out-Null } catch {}
Set-Location 'C:\Dev\codemaid'
$logPath = Join-Path $PWD 'deploy-exp-output.txt'
Remove-Item $logPath -Force -ErrorAction SilentlyContinue
$scriptPath = Join-Path $PWD 'scripts\deploy-exp.ps1'
$output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Configuration Debug 2>&1 | Tee-Object -FilePath $logPath
$exitCode = $LASTEXITCODE
$text = Get-Content -Path $logPath -Raw
Write-Host "OVERALL_EXIT_CODE=$exitCode"
if ($text -match 'pkgdef merge verification') {
  if ($text -match 'succeeded|success|passed|successful') { Write-Host 'PKGDEF_MERGE_VERIFICATION=SUCCEEDED' }
  else { Write-Host 'PKGDEF_MERGE_VERIFICATION=FAILED_OR_NOT_SUCCEEDED' }
}
else { Write-Host 'PKGDEF_MERGE_VERIFICATION=NOT_FOUND_IN_OUTPUT' }
