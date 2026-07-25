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

echo Starting SoftwareCrawler...
start "" "%APP_EXE%" %2 %3 %4 %5 %6 %7 %8 %9
