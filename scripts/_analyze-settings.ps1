$content = Get-Content "C:\Dev\codemaid\CodeJanitorShared\Properties\Settings.settings" -Raw
$matches = [regex]::Matches($content, '<Setting Name="([^"]+)" Type="([^"]+)" Scope="([^"]+)"')
Write-Host "Total settings: $($matches.Count)"
Write-Host "--- Scopes ---"
$matches | Group-Object { $_.Groups[3].Value } | Select-Object Name, Count | Format-Table -AutoSize
Write-Host "--- Types ---"
$matches | Group-Object { $_.Groups[2].Value } | Select-Object Name, Count | Format-Table -AutoSize
Write-Host "--- Category prefixes ---"
$matches | ForEach-Object { ($_.Groups[1].Value -split '_')[0] } | Group-Object | Select-Object Name, Count | Sort-Object Name | Format-Table -AutoSize
