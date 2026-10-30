@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Picture9_Experiment.ps1" -Package "%~dp0."
set "PixoraPicture9Exit=%errorlevel%"
echo.
pause
exit /b %PixoraPicture9Exit%
