@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-IIS.ps1" %*
pause
