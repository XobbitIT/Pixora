@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Product1Session_Start.ps1" -Package "%~dp0."
set "PixoraProduct1Exit=%errorlevel%"
echo.
pause
exit /b %PixoraProduct1Exit%
