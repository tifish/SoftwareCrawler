# Offline installer for Codex CLI.
#
# The official installer (https://chatgpt.com/codex/install.ps1) downloads
# codex-package-<target>.tar.gz and unpacks it into a versioned release directory that
# two junctions point at. This script does that same unpacking locally, from the
# package SoftwareCrawler already downloaded next to it.
#
# Layout a normal install leaves behind, and what this reproduces:
#   %USERPROFILE%\.codex\packages\standalone\releases\<version>-<target>\   the release
#   %USERPROFILE%\.codex\packages\standalone\current                       -> that release
#   %LOCALAPPDATA%\Programs\OpenAI\Codex\bin                               -> current\bin

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$archive = Get-ChildItem -LiteralPath $scriptDir -Filter 'codex-package-*.tar.gz' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $archive) {
    throw "No codex-package-*.tar.gz was found next to this script ($scriptDir). Download Codex CLI first."
}

# tar shipped with Windows 10 1803 and later. Without it there is nothing here that can
# read the archive - 7-Zip is not part of the download directory.
if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
    throw 'tar.exe was not found. It ships with Windows 10 1803 and later.'
}

$codexHome = if ([string]::IsNullOrWhiteSpace($env:CODEX_HOME)) {
    Join-Path $env:USERPROFILE '.codex'
} else {
    $env:CODEX_HOME
}
$standaloneRoot = Join-Path $codexHome 'packages\standalone'
$releasesDir = Join-Path $standaloneRoot 'releases'
$currentDir = Join-Path $standaloneRoot 'current'
$visibleBinDir = if ([string]::IsNullOrWhiteSpace($env:CODEX_INSTALL_DIR)) {
    Join-Path $env:LOCALAPPDATA 'Programs\OpenAI\Codex\bin'
} else {
    $env:CODEX_INSTALL_DIR
}

# Replaces a junction in place. Remove-Item on a junction can follow it into the target
# and delete the release it points at, so the reparse point is dropped on its own.
function Set-Junction {
    param([string]$LinkPath, [string]$TargetPath)

    $existing = Get-Item -LiteralPath $LinkPath -Force -ErrorAction SilentlyContinue
    if ($existing) {
        if (-not ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "$LinkPath is a real directory, not a junction. Remove it by hand and run this again."
        }
        [System.IO.Directory]::Delete($LinkPath)
    }

    New-Item -ItemType Junction -Path $LinkPath -Target $TargetPath | Out-Null
}

$staging = Join-Path $releasesDir ".staging.$PID"
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Unpacking $($archive.Name)..." -ForegroundColor Cyan
    tar -xzf $archive.FullName -C $staging
    if ($LASTEXITCODE -ne 0) { throw "tar exited with code $LASTEXITCODE." }

    # Same completeness check the official installer runs, for the same reason: a
    # truncated or wrong-target archive should fail here, not halfway through the swap.
    $expected = @(
        'codex-package.json',
        'bin\codex.exe',
        'bin\codex-code-mode-host.exe',
        'codex-path\rg.exe',
        'codex-resources\codex-command-runner.exe',
        'codex-resources\codex-windows-sandbox-setup.exe'
    )
    foreach ($name in $expected) {
        if (-not (Test-Path -LiteralPath (Join-Path $staging $name) -PathType Leaf)) {
            throw "The package is missing $name; it is not a complete Codex package."
        }
    }

    $manifest = Get-Content -Raw -LiteralPath (Join-Path $staging 'codex-package.json') | ConvertFrom-Json
    $releaseDir = Join-Path $releasesDir "$($manifest.version)-$($manifest.target)"

    Write-Host "Installing Codex CLI $($manifest.version) ($($manifest.target))..." -ForegroundColor Cyan

    # The junctions have to go before the directory they point at can be replaced.
    foreach ($link in @($currentDir, $visibleBinDir)) {
        $existing = Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
        if ($existing -and ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            [System.IO.Directory]::Delete($link)
        }
    }

    if (Test-Path -LiteralPath $releaseDir) { Remove-Item -LiteralPath $releaseDir -Recurse -Force }
    Move-Item -LiteralPath $staging -Destination $releaseDir

    Set-Junction -LinkPath $currentDir -TargetPath $releaseDir
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $visibleBinDir) | Out-Null
    Set-Junction -LinkPath $visibleBinDir -TargetPath (Join-Path $currentDir 'bin')
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host "  Installed to $visibleBinDir\codex.exe." -ForegroundColor DarkGray

# --- Ensure codex is on PATH ---

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = if ($userPath) { $userPath -split ';' | Where-Object { $_ -ne '' } } else { @() }
if ($pathEntries -notcontains $visibleBinDir) {
    [Environment]::SetEnvironmentVariable('Path', ((@($visibleBinDir) + $pathEntries) -join ';'), 'User')
    Write-Host "  Added $visibleBinDir to your PATH (takes effect in new shells)." -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "$([char]0x2705) Codex CLI installed." -ForegroundColor Green
