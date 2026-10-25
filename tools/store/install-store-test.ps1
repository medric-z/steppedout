<#
.SYNOPSIS
Tries the Store version on this PC without the Store: registers the package's folder for the
current user, and removes it again with -Remove.

.DESCRIPTION
Run tools/store/build.ps1 first. Windows only registers an unsigned package folder when Developer
Mode is on, and this script never turns it on: switch it on yourself in Settings, System, Advanced,
For developers (older Windows 11: System, For developers; Windows 10: Update & Security, For
developers), only if you choose to, and switch it off again afterwards.

Quit the SteppedOut that is already running first (right-click its icon, Quit). Two copies would
both watch the PC and both want the Ctrl+Alt+L shortcut. The script stops if it finds one running;
it never closes it for you.

  powershell -ExecutionPolicy Bypass -File tools\store\install-store-test.ps1
  powershell -ExecutionPolicy Bypass -File tools\store\install-store-test.ps1 -Remove

Settings: when %APPDATA%\SteppedOut\config.json already exists, the Store version reads and saves
that same file, so it starts with your settings and changes them in place. Files it makes that were
not there before (today.json, a settings backup) go to its own folder instead,
%LOCALAPPDATA%\Packages\<package family>\LocalCache\Roaming\SteppedOut, and -Remove deletes that
folder with the test install. Before installing, this script copies %APPDATA%\SteppedOut to
%APPDATA%\SteppedOut-before-store-test-<date and time>, so nothing is lost either way.

.PARAMETER Layout
The package folder to register. Defaults to a layout\x64 or layout\arm64 folder next to this
script, and otherwise to the build's folder in the repository for this PC's processor.

.PARAMETER Remove
Removes the test install instead.
#>
[CmdletBinding()]
param(
    [string]$Layout,
    [switch]$Remove
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Host "install-store-test.ps1: $message" -ForegroundColor Red
    exit 1
}

$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
if (-not $Layout) {
    $Layout = Join-Path $PSScriptRoot "layout\$arch"
    if (-not (Test-Path $Layout)) { $Layout = Join-Path $PSScriptRoot "..\..\publish\store\layout\$arch" }
}
$manifest = Join-Path $Layout 'AppxManifest.xml'
$settings = Join-Path $env:APPDATA 'SteppedOut'
$backupPattern = 'SteppedOut-before-store-test-*'

if ($Remove) {
    if (-not (Test-Path $manifest)) { Fail "$manifest is missing, so the package name is unknown; pass -Layout" }
    $name = ([xml](Get-Content -Raw -Encoding UTF8 $manifest)).Package.Identity.Name
    $installed = @(Get-AppxPackage -Name $name)
    if ($installed.Count -eq 0) {
        Write-Host "$name is not installed."
    }
    foreach ($package in $installed) {
        Remove-AppxPackage -Package $package.PackageFullName
        Write-Host "Removed $($package.PackageFullName) and its own folder."
    }
    $backup = Get-ChildItem -Directory -Path $env:APPDATA -Filter $backupPattern -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($backup) {
        Write-Host "Your settings from before the test are in $($backup.FullName)."
        Write-Host "To go back to them, quit SteppedOut and copy that folder's files over $settings."
    }
    exit 0
}

if (-not (Test-Path $manifest)) { Fail "$manifest is missing; run tools\store\build.ps1 first" }

$devMode = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if (-not $devMode -or $devMode.AllowDevelopmentWithoutDevLicense -ne 1) {
    Fail 'Developer Mode is off. This script does not turn it on; do that yourself in Settings, System, Advanced, For developers (older Windows 11: System, For developers; Windows 10: Update & Security, For developers), if you choose to.'
}

$running = @(Get-Process -Name SteppedOut -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Fail 'SteppedOut is running. Quit it first (right-click its icon, Quit), then run this again.'
}

if (Test-Path $settings) {
    $backup = Join-Path $env:APPDATA ('SteppedOut-before-store-test-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Copy-Item -Recurse -LiteralPath $settings -Destination $backup
    Write-Host "Copied your settings to $backup"
}

Add-AppxPackage -Register (Resolve-Path $manifest).Path
$name = ([xml](Get-Content -Raw -Encoding UTF8 $manifest)).Package.Identity.Name
$package = Get-AppxPackage -Name $name | Select-Object -First 1
Write-Host "Installed $($package.PackageFullName)"
Write-Host 'Open it from the Start menu: SteppedOut.'
Write-Host "Its own folder: $env:LOCALAPPDATA\Packages\$($package.PackageFamilyName)\LocalCache\Roaming\SteppedOut"
Write-Host 'Remove it afterwards with: tools\store\install-store-test.ps1 -Remove'
