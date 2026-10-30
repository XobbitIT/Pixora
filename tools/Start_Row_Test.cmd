@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Row_Experiment.ps1" -Package "%~dp0."
set "PixoraRowExit=%errorlevel%"
echo.
pause
exit /b %PixoraRowExit%
