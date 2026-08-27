# Offline installer for Claude Code CLI.
#
# The official installer (https://claude.ai/install.ps1) downloads claude.exe and then
# hands off to `claude.exe install`, which goes back to the network. This script does
# the local half of that work using the claude.exe SoftwareCrawler already downloaded
# next to it, so it needs no connection at all.
#
# It reproduces the layout a normal install leaves behind:
#   %USERPROFILE%\.local\share\claude\versions\<version>   the versioned binary
#   %USERPROFILE%\.local\bin\claude.exe                    the one on PATH

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $scriptDir 'claude.exe'

if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "claude.exe was not found next to this script ($scriptDir). Download Claude Code CLI first."
}

# The download carries no version in its file name, so the binary is the only thing
# that knows which version this is. It prints something like "2.1.247 (Claude Code)".
Write-Host 'Reading version from claude.exe...' -ForegroundColor DarkGray
$versionOutput = (& $source --version 2>&1 | Out-String)
if ($versionOutput -notmatch '(\d+\.\d+\.\d+(?:-[^\s]+)?)') {
    throw "Could not read a version from claude.exe (got: $($versionOutput.Trim()))."
}
$version = $Matches[1]

$binDir = Join-Path $env:USERPROFILE '.local\bin'
$versionsDir = Join-Path $env:USERPROFILE '.local\share\claude\versions'
$versionedBinary = Join-Path $versionsDir $version
$launcher = Join-Path $binDir 'claude.exe'

Write-Host "Installing Claude Code $version..." -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $binDir, $versionsDir | Out-Null
Copy-Item -LiteralPath $source -Destination $versionedBinary -Force

# A running claude holds its own image open, so a plain overwrite can fail. Renaming
# the locked file out of the way is what the official installer does; Windows allows
# that on an open file where it refuses the overwrite.
try {
    Copy-Item -LiteralPath $versionedBinary -Destination $launcher -Force
} catch {
    if (-not (Test-Path -LiteralPath $launcher)) { throw }
    $old = "claude.exe.old.$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())"
    Rename-Item -LiteralPath $launcher -NewName $old -Force
    Copy-Item -LiteralPath $versionedBinary -Destination $launcher -Force
}

Write-Host "  Installed to $launcher." -ForegroundColor DarkGray

# --- Ensure claude is on PATH ---

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = if ($userPath) { $userPath -split ';' | Where-Object { $_ -ne '' } } else { @() }
if ($pathEntries -notcontains $binDir) {
    [Environment]::SetEnvironmentVariable('Path', ((@($binDir) + $pathEntries) -join ';'), 'User')
    Write-Host "  Added $binDir to your PATH (takes effect in new shells)." -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "$([char]0x2705) Claude Code $version installed." -ForegroundColor Green
