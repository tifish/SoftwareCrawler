@rem Thin shim so the tray and a double-click both land on the .ps1 next to it.
@rem No pause on success: batch installs must not stop on a keypress.
@powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dpn0.ps1"
@if errorlevel 1 pause
