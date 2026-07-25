@echo off
rem Publishes an optimized build (ReadyToRun + NetBeauty) to .\bin.
rem Usage: Publish.cmd [runtime]
rem   runtime : RID, default win-x64
setlocal
set "RID=%~1"
if "%RID%"=="" set "RID=win-x64"

rem Stop the running app so publish can replace locked files.
taskkill /f /im "SoftwareCrawler.exe" >nul 2>nul

rem Drop stale binaries so files dropped by the new version do not linger.
del /q "%~dp0bin\*.dll" "%~dp0bin\*.json" "%~dp0bin\*.xml" "%~dp0bin\*.pdb" >nul 2>nul
rd /s /q "%~dp0bin\runtimes" >nul 2>nul
rd /s /q "%~dp0bin\Libs" >nul 2>nul

echo Publishing Release for %RID% (ReadyToRun + NetBeauty) -^> "%~dp0bin"
dotnet publish "%~dp0SoftwareCrawler\SoftwareCrawler.csproj" -c Release -r %RID% --no-self-contained -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:PublishSingleFile=false
if errorlevel 1 (
    echo.
    echo Publish FAILED. If a file is locked, close the published app and retry.
    pause
    exit /b 1
)

rd /s /q "%~dp0bin\runtimes" >nul 2>nul
del /q "%~dp0bin\*.pdb" >nul 2>nul

echo.
echo Published -^> "%~dp0bin"
pause
