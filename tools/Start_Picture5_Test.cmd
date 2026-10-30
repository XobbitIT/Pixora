@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Picture5_Experiment.ps1" -Package "%~dp0."
set "PixoraPicture5Exit=%errorlevel%"
echo.
pause
exit /b %PixoraPicture5Exit%
