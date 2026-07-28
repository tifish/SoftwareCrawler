@echo off
rem Builds Software Crawler. Usage: Build.cmd [Debug^|Release]
setlocal
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

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

echo.
echo Build succeeded -^> "%~dp0bin"
pause
