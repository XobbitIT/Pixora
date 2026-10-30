@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Picture8_Experiment.ps1" -Package "%~dp0."
set "PixoraPicture8Exit=%errorlevel%"
echo.
pause
exit /b %PixoraPicture8Exit%
