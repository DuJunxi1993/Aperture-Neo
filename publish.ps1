#requires -Version 5.1
<#
.SYNOPSIS
    Build a self-contained Aperture Neo release and (optionally) package an Inno Setup installer.

.DESCRIPTION
    Publishes Aperture Neo for win-x64 with --self-contained, then compiles Installer/installer.iss
    via Inno Setup 6. Output: publish/ApertureNeo-Setup-v<version>.exe

.PARAMETER Configuration
    Build configuration. Default: Release

.PARAMETER Version
    Version string baked into the installer filename. Default: 1.0.0

.PARAMETER SkipInstaller
    Only run dotnet publish; skip Inno Setup compile.

.PARAMETER Zip
    Also produce a portable .zip alongside the installer.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Version 1.1.0 -Zip
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0',
    [switch]$SkipInstaller,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'publish\win-x64'
$installerOut = Join-Path $root 'publish'

Write-Host "[1/4] Cleaning previous publish output..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

Write-Host "[2/4] dotnet publish (self-contained, win-x64)..." -ForegroundColor Cyan
$publishArgs = @(
    'publish'
    $root
    '-c', $Configuration
    '-r', 'win-x64'
    '--self-contained', 'true'
    '-p:PublishSingleFile=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-p:EnableCompressionInSingleFile=true'
    '-p:DebugType=embedded'
    "-p:Version=$Version"
    "-p:AssemblyVersion=$Version"
    "-p:FileVersion=$Version"
    '-o', $publishDir
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if ($SkipInstaller) {
    Write-Host "[done] publish only (SkipInstaller set)." -ForegroundColor Green
    Write-Host "Output: $publishDir"
    exit 0
}

Write-Host "[3/4] Locating Inno Setup 6..." -ForegroundColor Cyan
$iscc = $null
$candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
foreach ($p in $candidates) {
    if ($p -and (Test-Path $p)) { $iscc = $p; break }
}
if (-not $iscc) {
    throw "Inno Setup 6 not found. Install from https://jrsoftware.org/isdl.php or pass -SkipInstaller."
}
Write-Host "  Found: $iscc" -ForegroundColor DarkGray

Write-Host "[4/4] Compiling installer..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installerOut | Out-Null
$iss = Join-Path $root 'Installer\installer.iss'
& $iscc "/dMyAppVersion=$Version" "/dPublishDir=$publishDir" /O"$installerOut" /F"ApertureNeo-Setup-v$Version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

if ($Zip) {
    Write-Host "[+] Producing portable zip..." -ForegroundColor Cyan
    $zipPath = Join-Path $installerOut "ApertureNeo-v$Version-win-x64-portable.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath, 'Optimal', $false)
    Write-Host "  $zipPath" -ForegroundColor Green
}

Write-Host ""
Write-Host "[done] Build complete." -ForegroundColor Green
Write-Host "  Installer: $installerOut\ApertureNeo-Setup-v$Version.exe"
if ($Zip) { Write-Host "  Portable : $installerOut\ApertureNeo-v$Version-win-x64-portable.zip" }
