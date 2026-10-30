@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uv_Experiment.ps1" -Package "%~dp0."
set "PixoraUvExit=%errorlevel%"
echo.
pause
exit /b %PixoraUvExit%
