param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\UsbDaq612"
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appSource = Join-Path $scriptDir 'app'
$exeSource = Join-Path $appSource 'UsbDaq.App.exe'

if (-not (Test-Path $exeSource)) {
    throw "Cannot find application executable at $exeSource"
}

if (Test-Path $InstallRoot) {
    Remove-Item -Path $InstallRoot -Recurse -Force
}

New-Item -Path $InstallRoot -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $appSource '*') -Destination $InstallRoot -Recurse -Force

$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $startMenuDir 'USB DAQ 612CSFCZ10FM.lnk'
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $InstallRoot 'UsbDaq.App.exe'
$shortcut.WorkingDirectory = $InstallRoot
$shortcut.IconLocation = Join-Path $InstallRoot 'UsbDaq.App.exe'
$shortcut.Save()

Write-Host "Installed to $InstallRoot"
Write-Host "Start Menu shortcut created: $shortcutPath"
