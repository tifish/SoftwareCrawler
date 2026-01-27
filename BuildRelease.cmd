@echo off
setlocal
cd /d "%~dp0"

del /q bin\*.deps.json bin\*.runtimeconfig.json bin\Libs
dotnet publish SoftwareCrawler.sln -c Release
pause

endlocal
