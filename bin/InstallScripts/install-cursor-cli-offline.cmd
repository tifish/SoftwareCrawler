@rem Thin shim so the tray and a double-click both land on the .ps1 next to it.
@rem Always pause: the window is the only place the result shows up. pause returns 0,
@rem so the exit code has to be stashed first or the tray reads every run as a success.
@powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dpn0.ps1"
@set INSTALL_EXIT=%ERRORLEVEL%
@pause
@exit /b %INSTALL_EXIT%
