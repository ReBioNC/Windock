<#
.SYNOPSIS
  Build Windock and install it for the current user, with Desktop and Start Menu shortcuts.
.DESCRIPTION
  Installs to %LocalAppData%\Programs\Windock (no administrator rights needed) and creates
  "Windock" and "Windock Settings" shortcuts. Settings in %AppData%\Windock are kept.
  A running Windock is asked to exit first, so the taskbar is restored before files are replaced.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Launch
#>
param(
    [switch]$NoDesktopShortcuts,
    [switch]$Launch
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA "Programs\Windock"
$exe = Join-Path $dest "Windock.exe"

function Stop-Windock {
    $running = Get-Process Windock -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $running) { return }
    Write-Host "Closing the running Windock..."
    Start-Process -FilePath $running.Path -ArgumentList "--exit" -Wait
    for ($i = 0; $i -lt 40 -and (Get-Process Windock -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 250 }
    if (Get-Process Windock -ErrorAction SilentlyContinue) { throw "Windock did not exit. Press Ctrl+Alt+Shift+W, then run this script again." }
}

Stop-Windock

Write-Host "Building Windock into $dest ..."
dotnet publish (Join-Path $root "src\Windock") -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o $dest | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
# The SDK leaves its compiler/MSBuild servers running (".NET Host", ~250 MB) for faster rebuilds; stop them.
dotnet build-server shutdown | Out-Null

$shell = New-Object -ComObject WScript.Shell
function New-Link([string]$path, [string]$arguments, [string]$description) {
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.Arguments = $arguments
    $link.WorkingDirectory = $dest
    $link.IconLocation = "$exe,0"
    $link.Description = $description
    $link.Save()
    Write-Host "Shortcut: $path"
}

$folders = @([Environment]::GetFolderPath("Programs"))
if (-not $NoDesktopShortcuts) { $folders += [Environment]::GetFolderPath("Desktop") }
foreach ($folder in $folders) {
    New-Link (Join-Path $folder "Windock.lnk") "" "A macOS-style dock for Windows"
    New-Link (Join-Path $folder "Windock Settings.lnk") "--settings" "Change how Windock looks and behaves"
    $old = Join-Path $folder "Pengaturan Windock.lnk"  # name used by versions before 0.9
    if (Test-Path $old) { Remove-Item $old }
}

Write-Host "Done. Windock is installed in $dest"
if ($Launch) { Start-Process -FilePath $exe -ArgumentList "--settings" }
