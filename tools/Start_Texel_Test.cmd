@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Texel_Experiment.ps1" -Package "%~dp0."
set "PixoraTexelExit=%errorlevel%"
echo.
pause
exit /b %PixoraTexelExit%
