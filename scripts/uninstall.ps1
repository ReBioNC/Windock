<#
.SYNOPSIS
  Remove Windock installed by install.ps1. Settings in %AppData%\Windock are kept unless -RemoveSettings.
#>
param([switch]$RemoveSettings)
$ErrorActionPreference = "Stop"
$dest = Join-Path $env:LOCALAPPDATA "Programs\Windock"

$running = Get-Process Windock -ErrorAction SilentlyContinue | Select-Object -First 1
if ($running) {
    Start-Process -FilePath $running.Path -ArgumentList "--exit" -Wait
    for ($i = 0; $i -lt 40 -and (Get-Process Windock -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 250 }
}

foreach ($folder in @([Environment]::GetFolderPath("Programs"), [Environment]::GetFolderPath("Desktop"))) {
    foreach ($name in "Windock.lnk", "Pengaturan Windock.lnk") {
        $path = Join-Path $folder $name
        if (Test-Path $path) { Remove-Item $path; Write-Host "Dihapus: $path" }
    }
}
foreach ($progId in "exefile", "lnkfile") {
    $key = "HKCU:\Software\Classes\$progId\shell\WindockPin"
    if (Test-Path $key) { Remove-Item $key -Recurse; Write-Host "Menu klik kanan dihapus: $progId" }
}
if (Test-Path $dest) { Remove-Item $dest -Recurse; Write-Host "Dihapus: $dest" }
if ($RemoveSettings) {
    $data = Join-Path $env:APPDATA "Windock"
    if (Test-Path $data) { Remove-Item $data -Recurse; Write-Host "Dihapus: $data" }
}
Write-Host "Windock sudah dihapus."
