$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 before building source.'
}
dotnet run --project tests/CanvasForge.Core.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core regression tests failed.' }
dotnet publish src/CanvasForge.App/CanvasForge.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o publish/windows-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Host 'Ready: publish/windows-x64/Pixora.exe'

