# Builds a release DisplayApp.exe (single file, needs the .NET 10 Desktop Runtime) into .\publish
# Usage:  .\publish.ps1            framework-dependent (~1 MB)
#         .\publish.ps1 -SelfContained   runs without .NET installed (much larger)
param([switch]$SelfContained)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$running = Get-Process DisplayApp -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$PSScriptRoot\publish\*" }
if ($running) { Write-Host "Stopping the published DisplayApp so it can be replaced..."; $running | Stop-Process; Start-Sleep 1 }

$sc = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish src/DisplayApp -c Release -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`nPublished: $PSScriptRoot\publish\DisplayApp.exe"
