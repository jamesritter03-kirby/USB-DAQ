param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\UsbDaq612"
)

$ErrorActionPreference = 'Stop'

if (Test-Path $InstallRoot) {
    Remove-Item -Path $InstallRoot -Recurse -Force
    Write-Host "Removed $InstallRoot"
} else {
    Write-Host "Install folder not found: $InstallRoot"
}

$shortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\USB DAQ 612CSFCZ10FM.lnk'
if (Test-Path $shortcutPath) {
    Remove-Item -Path $shortcutPath -Force
    Write-Host "Removed shortcut: $shortcutPath"
}
