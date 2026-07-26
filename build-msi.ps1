#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes Soundboard (self-contained win-x64) and builds SoundboardSetup.msi
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$publishDir = Join-Path $root "publish\win-x64"
$msiOutDir = Join-Path $root "dist"
$installerProj = Join-Path $root "installer\Soundboard.Installer.wixproj"

Write-Host "==> Publishing Soundboard (self-contained win-x64)..." -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish (Join-Path $root "SoundboardApp.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:Version=$Version `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $publishDir "SoundboardApp.exe"
if (-not (Test-Path $exe)) {
    throw "Publish output missing SoundboardApp.exe"
}

Write-Host "==> Building MSI with WiX..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $msiOutDir | Out-Null

# Ensure trailing slash for WiX Files Include
$publishDirArg = $publishDir.TrimEnd('\') + '\'

dotnet build $installerProj `
    -c $Configuration `
    -p:PublishDir=$publishDirArg `
    -p:Version=$Version `
    -p:OutputPath=$msiOutDir\

if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

$msi = Get-ChildItem $msiOutDir -Filter "*.msi" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msi) { throw "MSI file not found in $msiOutDir" }

Write-Host ""
Write-Host "MSI ready:" -ForegroundColor Green
Write-Host "  $($msi.FullName)"
Write-Host "  Size: $([math]::Round($msi.Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "Install with:" -ForegroundColor Yellow
Write-Host "  msiexec /i `"$($msi.FullName)`""
