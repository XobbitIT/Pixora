@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Picture7_Experiment.ps1" -Package "%~dp0."
set "PixoraPicture7Exit=%errorlevel%"
echo.
pause
exit /b %PixoraPicture7Exit%
