@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Session1_Experiment.ps1" -Package "%~dp0."
set "PixoraSession1Exit=%errorlevel%"
echo.
pause
exit /b %PixoraSession1Exit%
