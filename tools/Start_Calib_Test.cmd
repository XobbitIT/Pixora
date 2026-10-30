@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Calib_Experiment.ps1" -Package "%~dp0."
set "PixoraCalibExit=%errorlevel%"
echo.
pause
exit /b %PixoraCalibExit%
