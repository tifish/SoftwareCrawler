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

rem The MCP stdio adapter is published beside the app as a single file. An agent's
rem session keeps it open, which locks the exe, so a failure here is a warning and
rem not a failed build.
dotnet publish "%~dp0Tools\SoftwareCrawlerMcp\SoftwareCrawlerMcp.csproj" -c %CONFIG%
if errorlevel 1 echo WARNING: SoftwareCrawlerMcp was not updated ^(likely in use by an MCP client^).

echo.
echo Build succeeded -^> "%~dp0bin"
pause
