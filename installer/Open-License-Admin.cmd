@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\licensing\admin-key.ps1" -Copy
if errorlevel 1 (
    pause
    exit /b 1
)
start "" "https://spectravision-api.ahmed-mabrouk-dev.workers.dev/admin/"
