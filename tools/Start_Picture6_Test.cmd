@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Picture6_Experiment.ps1" -Package "%~dp0."
set "PixoraPicture6Exit=%errorlevel%"
echo.
pause
exit /b %PixoraPicture6Exit%
