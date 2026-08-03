@echo off
setlocal
cd /d "%~dp0"

rem Release build into bin\. Cleans stale binaries; keeps Templates/ and scripts.
taskkill /f /im "SoftwareCrawler.exe" >nul 2>nul

del /q "%~dp0bin\*.dll" "%~dp0bin\*.json" "%~dp0bin\*.xml" "%~dp0bin\*.pdb" "%~dp0bin\*.deps.json" "%~dp0bin\*.runtimeconfig.json" >nul 2>nul
rd /s /q "%~dp0bin\runtimes" >nul 2>nul
rd /s /q "%~dp0bin\Libs" >nul 2>nul
rd /s /q "%~dp0bin\Logs" >nul 2>nul

echo Building Release...
dotnet build "%~dp0SoftwareCrawler\SoftwareCrawler.csproj" -c Release
if errorlevel 1 (
    echo.
    echo Build FAILED.
    pause
    exit /b 1
)

rem MCP stdio adapter single-file beside the app. Agent sessions may lock it.
dotnet publish "%~dp0Tools\SoftwareCrawlerMcp\SoftwareCrawlerMcp.csproj" -c Release
if errorlevel 1 echo WARNING: SoftwareCrawlerMcp was not updated ^(likely in use by an MCP client^).

rd /s /q "%~dp0bin\runtimes" >nul 2>nul
del /q /s "%~dp0bin\*.pdb" >nul 2>nul

echo.
echo Build succeeded -^> "%~dp0bin"
endlocal
