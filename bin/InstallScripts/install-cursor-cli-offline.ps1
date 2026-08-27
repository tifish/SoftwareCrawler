# Offline installer for Cursor CLI (cursor-agent).
#
# The official installer (https://cursor.com/install?win32=true) downloads that same
# package and unpacks it; this does the unpacking locally, from the
# agent-cli-package.zip SoftwareCrawler already downloaded next to it.
#
# One deliberate difference: the official script wipes the whole cursor-agent directory
# before installing. Old version directories are left alone here - the launcher picks the
# highest version anyway, and the wipe fails outright while an agent is running.
#
# Layout an existing install has, and what this reproduces:
#   %LOCALAPPDATA%\cursor-agent\versions\<version>\              the unpacked package
#   %LOCALAPPDATA%\cursor-agent\{cursor-agent,agent}.{cmd,ps1}   the launchers on PATH

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$archive = Get-ChildItem -LiteralPath $scriptDir -Filter 'agent-cli-package.zip' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $archive) {
    throw "No agent-cli-package.zip was found next to this script ($scriptDir). Download Cursor CLI first."
}

$root = Join-Path $env:LOCALAPPDATA 'cursor-agent'
$versionsDir = Join-Path $root 'versions'
$staging = Join-Path $versionsDir ".staging.$PID"

New-Item -ItemType Directory -Force -Path $versionsDir | Out-Null
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }

try {
    Write-Host "Unpacking $($archive.Name)..." -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archive.FullName, $staging)

    # The archive wraps everything in one directory, which the official script drops
    # with --strip-components=1.
    $package = Join-Path $staging 'dist-package'
    foreach ($name in @('node.exe', 'index.js', 'cursor-agent.cmd', 'cursor-agent.ps1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $package $name) -PathType Leaf)) {
            throw "The package is missing $name; it is not a complete Cursor CLI package."
        }
    }

    # Nothing in the archive or its file name carries the version - the directory it
    # installs into is named after what the binary reports.
    $reported = (& (Join-Path $package 'node.exe') (Join-Path $package 'index.js') --version 2>&1 | Out-String)
    $version = ($reported -split "`n" | ForEach-Object { $_.Trim() } | Where-Object {
        $_ -match '^\d{4}\.\d{1,2}\.\d{1,2}(-\d{2}-\d{2}-\d{2})?-[a-f0-9]+$'
    } | Select-Object -First 1)
    if (-not $version) {
        throw "Could not read a version from the package (got: $($reported.Trim()))."
    }

    $target = Join-Path $versionsDir $version

    if (Test-Path -LiteralPath $target) {
        # Already installed. The official updater stops here too and only refreshes the
        # launchers - replacing the directory would fail anyway while an agent is running.
        Write-Host "Cursor CLI $version is already unpacked; refreshing launchers." -ForegroundColor DarkGray
    } else {
        Write-Host "Installing Cursor CLI $version..." -ForegroundColor Cyan
        Move-Item -LiteralPath $package -Destination $target
    }

    # Everything named cursor-agent.* is a launcher, matched by pattern the way the
    # official script does it - the package has .cmd and .ps1 today and the official
    # script also looks for a .exe.
    $launchers = @(Get-ChildItem -LiteralPath $target -Filter 'cursor-agent*' -File)
    if (-not $launchers) {
        throw "No cursor-agent launcher was found in $target."
    }

    foreach ($launcher in $launchers) {
        Copy-Item -LiteralPath $launcher.FullName -Destination (Join-Path $root $launcher.Name) -Force
        # agent is the primary command; it is the same file under a second name.
        $alias = $launcher.Name -replace '^cursor-agent', 'agent'
        Copy-Item -LiteralPath $launcher.FullName -Destination (Join-Path $root $alias) -Force
    }
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host "  Installed to $root." -ForegroundColor DarkGray

# --- Ensure cursor-agent is on PATH ---

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = if ($userPath) { $userPath -split ';' | Where-Object { $_ -ne '' } } else { @() }
if ($pathEntries -notcontains $root) {
    # Appended, not prepended: that is where the official script puts it.
    [Environment]::SetEnvironmentVariable('Path', (($pathEntries + @($root)) -join ';'), 'User')
    Write-Host "  Added $root to your PATH (takes effect in new shells)." -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "$([char]0x2705) Cursor CLI $version installed." -ForegroundColor Green
