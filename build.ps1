#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds Auto MIDI Player as a portable single-file .exe and/or a Windows installer.

.DESCRIPTION
    Targets:
      portable  - Self-contained single-file .exe (no .NET install needed on target PC).
                  Output: dist\AutoMidiPlayer-<version>-portable\Auto MIDI Player.exe
      installer - Self-contained folder publish packaged into a setup .exe via Inno Setup.
                  Output: dist\AutoMidiPlayer-Setup-<version>.exe
      all       - Both of the above (default).

.PARAMETER Target
    portable | installer | all  (default: all)

.PARAMETER Runtime
    Runtime identifier. Default: win-x64

.EXAMPLE
    .\build.ps1                      # build both
    .\build.ps1 -Target portable     # portable .exe only
    .\build.ps1 -Target installer    # installer only
#>
[CmdletBinding()]
param(
    [ValidateSet('portable', 'installer', 'all')]
    [string]$Target = 'all',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'AutoMidiPlayer.WPF\AutoMidiPlayer.WPF.csproj'
$dist = Join-Path $root 'dist'

# --- Read <Version> from the csproj ---------------------------------------
[xml]$csproj = Get-Content $proj
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { throw "Could not read <Version> from $proj" }
Write-Host "Auto MIDI Player build  |  version $version  |  runtime $Runtime  |  target $Target" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $dist | Out-Null

# --- Portable single-file .exe --------------------------------------------
function Build-Portable {
    $out = Join-Path $dist "AutoMidiPlayer-$version-portable"
    Write-Host "`n[portable] Publishing single-file self-contained .exe ..." -ForegroundColor Yellow
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }

    dotnet publish $proj -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeAllContentForSelfExtract=true `
        -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (portable) failed." }

    # Drop stray symbol files so the folder is just the portable exe.
    Get-ChildItem $out -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
    $exe = Get-Item (Join-Path $out 'Auto MIDI Player.exe')
    Write-Host ("[portable] Done -> {0}  ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)) -ForegroundColor Green
}

# --- Installer (Inno Setup) -----------------------------------------------
function Build-Installer {
    $appOut = Join-Path $dist 'installer-app'
    Write-Host "`n[installer] Publishing self-contained folder ..." -ForegroundColor Yellow
    if (Test-Path $appOut) { Remove-Item $appOut -Recurse -Force }

    # Folder publish (not single-file) is friendliest for an installed app: clean repair/uninstall.
    dotnet publish $proj -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=false `
        -o $appOut
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (installer) failed." }
    Get-ChildItem $appOut -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

    # Locate the Inno Setup compiler (ISCC.exe).
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    if (-not $iscc) {
        foreach ($p in @(
                "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
                "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
            if (Test-Path $p) { $iscc = $p; break }
        }
    }
    if (-not $iscc) {
        throw "Inno Setup compiler (ISCC.exe) not found. Install with: winget install JRSoftware.InnoSetup"
    }

    $iss = Join-Path $root 'installer\AutoMidiPlayer.iss'
    Write-Host "[installer] Compiling setup with Inno Setup ..." -ForegroundColor Yellow
    & $iscc "/DAppVersion=$version" "/DSourceDir=$appOut" $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

    $setup = Join-Path $dist "AutoMidiPlayer-Setup-$version.exe"
    if (Test-Path $setup) {
        $item = Get-Item $setup
        Write-Host ("[installer] Done -> {0}  ({1:N1} MB)" -f $item.FullName, ($item.Length / 1MB)) -ForegroundColor Green
    }
}

switch ($Target) {
    'portable' { Build-Portable }
    'installer' { Build-Installer }
    'all' { Build-Portable; Build-Installer }
}

Write-Host "`nAll done. Output in: $dist" -ForegroundColor Cyan
