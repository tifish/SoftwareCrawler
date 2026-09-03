$ErrorActionPreference = 'Stop'

$exe = Join-Path $PSScriptRoot 'SoftwareCrawler.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Not found: $exe"
}

$action = New-ScheduledTaskAction -Execute $exe -Argument '--download-all --auto-close'
$triggers = @(
    New-ScheduledTaskTrigger -Daily -At '00:00'
    New-ScheduledTaskTrigger -Daily -At '08:00'
    New-ScheduledTaskTrigger -Daily -At '13:00'
    New-ScheduledTaskTrigger -Daily -At '18:30'
)

Register-ScheduledTask -TaskName 'Software Crawler' -TaskPath '\My\' -Action $action -Trigger $triggers -Force | Out-Null
Write-Host "Registered '\My\Software Crawler' daily at 00:00, 08:00, 13:00, 18:30."
