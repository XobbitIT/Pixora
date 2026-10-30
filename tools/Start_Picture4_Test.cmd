@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Picture4_Experiment.ps1" -Package "%~dp0."
set "PixoraPicture4Exit=%errorlevel%"
echo.
pause
exit /b %PixoraPicture4Exit%
