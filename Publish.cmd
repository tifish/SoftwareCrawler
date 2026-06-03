@echo off
setlocal

set PROJECT_NAME=SoftwareCrawler

rem Stop the running app so publish can replace locked files.
taskkill /f /im "%PROJECT_NAME%.exe" >nul 2>nul

del /s /q "bin\*.dll" "bin\*.json" "bin\*.xml" >nul 2>nul
rd /s /q "bin\runtimes" >nul 2>nul
rd /s /q "bin\Libs" >nul 2>nul

dotnet publish --configuration Release "%PROJECT_NAME%.csproj"
if errorlevel 1 exit /b %errorlevel%

rd /s /q "bin\runtimes" >nul 2>nul
del /s /q "bin\*.pdb" >nul 2>nul

endlocal
