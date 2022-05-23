@echo off
schtasks /create /f /tn "My\Software Crawler" /sc daily /st 04:00 /tr "'%~dp0SoftwareCrawler.exe' --download-all --auto-close"
pause
