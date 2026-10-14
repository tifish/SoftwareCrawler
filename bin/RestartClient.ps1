# Restarts the client that lives beside this script, in the notification area.
# Usage: RestartClient.ps1 [-Configuration Debug|Release]
#
# With -Configuration it rebuilds from the repository this bin folder belongs to
# before starting again; without it, it only restarts (an installed copy, which
# gets its code through the updater).
#
# The auto-fix agent runs this as its last step. The new client is created through
# WMI rather than as a child of this script, so it is not part of the agent's
# process tree: an agent CLI that cleans up its tree on exit would otherwise take
# the client down with it.
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration
)

$ErrorActionPreference = "Stop"
$exePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "SoftwareCrawler.exe"))

# Only the copy in this folder: other worktrees' instances keep running.
$running = @(Get-CimInstance Win32_Process -Filter "Name='SoftwareCrawler.exe'" | Where-Object {
    $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -eq $exePath
})
foreach ($process in $running) {
    Write-Host "Stopping SoftwareCrawler (pid $($process.ProcessId))..."
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    Wait-Process -Id $process.ProcessId -Timeout 30 -ErrorAction SilentlyContinue
}

$buildFailed = $false
if ($Configuration) {
    $repository = Split-Path -Parent $PSScriptRoot
    Write-Host "Building ($Configuration)..."
    dotnet build (Join-Path $repository "SoftwareCrawler\SoftwareCrawler.csproj") -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        # Still start what is there: a client that stays down misses every round.
        $buildFailed = $true
        Write-Host "Build FAILED; starting the previous build."
    } else {
        # An MCP session keeps the adapter locked, so failing here is only a warning.
        dotnet publish (Join-Path $repository "Tools\SoftwareCrawlerMcp\SoftwareCrawlerMcp.csproj") -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            Write-Host "WARNING: SoftwareCrawlerMcp was not updated (likely in use by an MCP client)."
        }
    }
}

$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine      = "`"$exePath`" --tray"
    CurrentDirectory = $PSScriptRoot
}
if ($result.ReturnValue -ne 0) {
    Write-Host "Failed to start $exePath (Win32_Process.Create returned $($result.ReturnValue))."
    exit 1
}

Write-Host "Started SoftwareCrawler (pid $($result.ProcessId)) in the notification area."
if ($buildFailed) {
    exit 1
}
