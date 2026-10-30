@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Picture3_Experiment.ps1" -Package "%~dp0."
set "PixoraPicture3Exit=%errorlevel%"
echo.
pause
exit /b %PixoraPicture3Exit%
