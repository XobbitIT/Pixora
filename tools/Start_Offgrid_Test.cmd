@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Offgrid_Experiment.ps1" -Package "%~dp0."
set "PixoraOffgridExit=%errorlevel%"
echo.
pause
exit /b %PixoraOffgridExit%
