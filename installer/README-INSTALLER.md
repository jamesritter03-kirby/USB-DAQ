USB DAQ Installer Package

This package contains a self-contained Windows x64 build of USB DAQ and scripts to install/uninstall it.

Files:
- app/: application binaries
- Install-UsbDaq.ps1: installs app to %LOCALAPPDATA%\Programs\UsbDaq612 and creates a Start Menu shortcut
- Uninstall-UsbDaq.ps1: removes installed files and Start Menu shortcut

Install:
1. Extract this package.
2. Right-click PowerShell and run:
   powershell -ExecutionPolicy Bypass -File .\Install-UsbDaq.ps1

Uninstall:
- powershell -ExecutionPolicy Bypass -File .\Uninstall-UsbDaq.ps1
