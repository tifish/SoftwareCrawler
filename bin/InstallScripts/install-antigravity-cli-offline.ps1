# Offline installer for Antigravity CLI (agy).
#
# The official installer (https://antigravity.google/cli/install.ps1) always downloads
# cli_windows_x64.exe from the network and refuses to overwrite an existing binary.
# This script reproduces the local install and upgrade steps using the cli_windows_x64*.exe
# SoftwareCrawler already downloaded next to it, so it needs no connection at all.
#
# Layout a normal install leaves behind, and what this reproduces:
#   %LOCALAPPDATA%\agy\bin\agy.exe   the binary on PATH

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$source = Get-ChildItem -LiteralPath $scriptDir -Filter 'cli_windows_x64*.exe' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $source) {
    throw "No cli_windows_x64*.exe was found next to this script ($scriptDir). Download Antigravity CLI first."
}

Write-Host "Reading version from $($source.Name)..." -ForegroundColor DarkGray
$versionOutput = (& $source.FullName --version 2>&1 | Out-String)
if ($versionOutput -notmatch '(\d+\.\d+\.\d+(?:-[^\s]+)?)') {
    throw "Could not read a version from $($source.Name) (got: $($versionOutput.Trim()))."
}
$version = $Matches[1]

$binDir = Join-Path $env:LOCALAPPDATA 'agy\bin'
$dest = Join-Path $binDir 'agy.exe'

Write-Host "Installing Antigravity CLI $version..." -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $binDir | Out-Null

# A running agy holds its own image open, so a plain overwrite can fail. Renaming
# the locked file out of the way allows Windows to place the new binary beside it.
try {
    Copy-Item -LiteralPath $source.FullName -Destination $dest -Force
} catch {
    if (-not (Test-Path -LiteralPath $dest)) { throw }
    $old = "agy.exe.old.$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())"
    Rename-Item -LiteralPath $dest -NewName $old -Force
    Copy-Item -LiteralPath $source.FullName -Destination $dest -Force
}

Unblock-File -LiteralPath $dest -ErrorAction SilentlyContinue
Write-Host "  Installed to $dest." -ForegroundColor DarkGray

# Run native environment setup (same handoff as the official install.ps1; absorb soft warnings).
try {
    & $dest install 2>&1 | Out-Null
} catch {
    # Non-fatal: the binary is copied and PATH is also ensured below.
}

# --- Ensure agy is on PATH ---

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = if ($userPath) { $userPath -split ';' | Where-Object { $_ -ne '' } } else { @() }
if ($pathEntries -notcontains $binDir) {
    [Environment]::SetEnvironmentVariable('Path', ((@($binDir) + $pathEntries) -join ';'), 'User')
    Write-Host "  Added $binDir to your PATH (takes effect in new shells)." -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "$([char]0x2705) Antigravity CLI $version installed." -ForegroundColor Green
