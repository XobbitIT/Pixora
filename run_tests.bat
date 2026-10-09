@echo off
cd /d "%~dp0"
dotnet run --project tests\CanvasForge.Core.Tests -c Release
pause

