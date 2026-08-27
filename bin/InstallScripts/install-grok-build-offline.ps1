# Offline installer for Grok CLI (Grok Build).
#
# The official installer (https://x.ai/cli/install.ps1) has no way to point it at a file
# you already have - it always downloads. Everything it does after that download is
# local, and that is what this script reproduces, from the grok exe SoftwareCrawler
# already downloaded next to it.
#
# Layout a normal install leaves behind, and what this reproduces:
#   %USERPROFILE%\.grok\bin\grok.exe and agent.exe   the binaries on PATH
#   %USERPROFILE%\.grok\completions\powershell\grok.ps1
#   %USERPROFILE%\.grok\config.toml                  [cli] installer = "internal"

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$source = Get-ChildItem -LiteralPath $scriptDir -Filter 'grok-*-windows-*.exe' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $source) {
    throw "No grok-*-windows-*.exe was found next to this script ($scriptDir). Download Grok Build first."
}

$grokDir = Join-Path $env:USERPROFILE '.grok'
$binDir = if ($env:GROK_BIN_DIR) { $env:GROK_BIN_DIR } else { Join-Path $grokDir 'bin' }

if ($source.Name -match '^grok-(.+)-windows-') {
    Write-Host "Installing Grok $($Matches[1])..." -ForegroundColor Cyan
} else {
    Write-Host "Installing Grok from $($source.Name)..." -ForegroundColor Cyan
}

New-Item -ItemType Directory -Force -Path $binDir | Out-Null

# grok and agent are the same binary under two names, which is how the official
# installer lays them down. A running instance holds its image open, so a failed
# overwrite is retried after renaming the locked file out of the way.
foreach ($binName in @('grok.exe', 'agent.exe')) {
    $dest = Join-Path $binDir $binName
    $old = "$dest.old"

    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force -ErrorAction SilentlyContinue }

    try {
        Copy-Item -LiteralPath $source.FullName -Destination $dest -Force
    } catch {
        if (-not (Test-Path -LiteralPath $dest)) { throw }
        Rename-Item -LiteralPath $dest -NewName "$binName.old" -Force
        Copy-Item -LiteralPath $source.FullName -Destination $dest -Force
    }
}

Write-Host "  Installed to $binDir\grok.exe and $binDir\agent.exe." -ForegroundColor DarkGray

# --- Generate completions (best-effort, same as the official installer) ---

$completionsDir = Join-Path (Join-Path $grokDir 'completions') 'powershell'
try {
    New-Item -ItemType Directory -Path $completionsDir -Force | Out-Null
    & (Join-Path $binDir 'grok.exe') completions powershell 2>$null |
        Set-Content (Join-Path $completionsDir 'grok.ps1') -ErrorAction SilentlyContinue
} catch {}

# --- Persist installer config ---

# Marks the install as self-managed so grok updates itself in place. Only the
# installer/channel keys under [cli] are touched; anything else in the file stays.
$configFile = Join-Path $grokDir 'config.toml'
$cliLines = @('installer = "internal"')

if (-not (Test-Path -LiteralPath $configFile)) {
    New-Item -ItemType Directory -Path (Split-Path $configFile) -Force | Out-Null
    [System.IO.File]::WriteAllText($configFile, "[cli]`r`n" + ($cliLines -join "`r`n") + "`r`n", [System.Text.Encoding]::UTF8)
} elseif ((Get-Content -Raw $configFile) -match '(?m)^\[cli\]') {
    $output = [System.Collections.ArrayList]::new()
    $inCli = $false

    foreach ($line in (Get-Content $configFile)) {
        if ($line -match '^\[cli\]\s*(#.*)?$') {
            [void]$output.Add($line)
            foreach ($cl in $cliLines) { [void]$output.Add($cl) }
            $inCli = $true
            continue
        }
        if ($line -match '^\[.+\]\s*(#.*)?$') {
            $inCli = $false
        }
        if ($inCli -and $line -match '^\s*(installer|channel)\s*=') {
            continue
        }
        [void]$output.Add($line)
    }
    [System.IO.File]::WriteAllLines($configFile, [string[]]$output.ToArray(), [System.Text.Encoding]::UTF8)
} else {
    Add-Content -Path $configFile -Value "`r`n[cli]`r`n$($cliLines -join "`r`n")`r`n"
}

# --- Ensure grok is on PATH ---

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = if ($userPath) { $userPath -split ';' | Where-Object { $_ -ne '' } } else { @() }
if ($pathEntries -notcontains $binDir) {
    [Environment]::SetEnvironmentVariable('Path', ((@($binDir) + $pathEntries) -join ';'), 'User')
    Write-Host "  Added $binDir to your PATH (takes effect in new shells)." -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "$([char]0x2705) Grok CLI installed." -ForegroundColor Green
