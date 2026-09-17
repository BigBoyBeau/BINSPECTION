<#
.SYNOPSIS
    Builds BINSPECTION.dll (Release) and packages it into BINSPECTION.msi.

.DESCRIPTION
    One-time setup, if not already done:
        dotnet tool install --global wix --version 5.0.2
        wix extension add WixToolset.UI.wixext/5.0.2 WixToolset.Util.wixext/5.0.2 --global

    Then just re-run this script any time to rebuild the MSI.
#>

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Building BINSPECTION.dll (Release)..." -ForegroundColor Cyan
dotnet build (Join-Path $repoRoot "BINSPECTION\BINSPECTION.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "BINSPECTION.csproj build failed." }

Write-Host "Building BINSPECTION.msi..." -ForegroundColor Cyan
dotnet build (Join-Path $PSScriptRoot "Installer.wixproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "Installer.wixproj build failed." }

$msi = Join-Path $PSScriptRoot "bin\x64\Release\BINSPECTION.msi"
Write-Host "Done: $msi" -ForegroundColor Green
