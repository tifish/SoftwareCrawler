@echo off
rem Builds then launches Software Crawler. Usage: Run.cmd [Debug^|Release]
setlocal
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

rem Stop only the copy built by this worktree. Debug instances from other
rem worktrees intentionally keep running for side-by-side verification.
set "APP_EXE=%~dp0bin\SoftwareCrawler.exe"
powershell.exe -NoProfile -Command "$target=[IO.Path]::GetFullPath($env:APP_EXE); foreach($process in (Get-CimInstance Win32_Process -Filter 'Name=''SoftwareCrawler.exe''')) { if($process.ExecutablePath -and [IO.Path]::GetFullPath($process.ExecutablePath) -eq $target) { Stop-Process -Id $process.ProcessId -Force } }"

echo Building (%CONFIG%)...
dotnet build "%~dp0SoftwareCrawler\SoftwareCrawler.csproj" -c %CONFIG%
if errorlevel 1 (
    echo.
    echo Build FAILED.
    pause
    exit /b 1
)

rem The MCP stdio adapter lives beside the app. An agent's session keeps it open,
rem which locks the exe, so a failure here is a warning and not a failed build.
dotnet build "%~dp0Tools\ScMcp\ScMcp.csproj" -c %CONFIG%
if errorlevel 1 echo WARNING: ScMcp was not updated ^(likely in use by an MCP client^).

echo Starting SoftwareCrawler...
start "" "%APP_EXE%" %2 %3 %4 %5 %6 %7 %8 %9
