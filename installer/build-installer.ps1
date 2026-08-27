<#
.SYNOPSIS
    Builds KRemote-Setup-<version>.exe.

.DESCRIPTION
    Two steps: publish the app self-contained (so the target PC needs no .NET
    installed), then compile installer\KRemote.iss around that output.

    Requires the .NET SDK and Inno Setup 6. If Inno Setup is missing, install it
    with:  winget install --id JRSoftware.InnoSetup --source winget

.PARAMETER Version
    Version stamped on the setup file and shown in Add/Remove Programs.

.PARAMETER SkipPublish
    Reuse whatever is already in publish\ instead of rebuilding it.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = '1.1.0',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'publish'
$distDir = Join-Path $root 'dist'
$issPath = Join-Path $PSScriptRoot 'KRemote.iss'

Write-Host "KRemote installer build" -ForegroundColor Cyan
Write-Host "  repository : $root"
Write-Host "  version    : $Version"

# --- 1. locate the Inno Setup compiler --------------------------------------
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
}
if (-not $iscc) {
    throw "Inno Setup 6 not found. Install it with: winget install --id JRSoftware.InnoSetup --source winget"
}
Write-Host "  compiler   : $iscc"

# --- 2. publish self-contained ----------------------------------------------
if (-not $SkipPublish) {
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    Write-Host "`nPublishing self-contained win-x64..." -ForegroundColor Cyan
    & dotnet publish (Join-Path $root 'KRemote.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:Version=$Version `
        -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path (Join-Path $publishDir 'KRemote.exe'))) {
    throw "publish\KRemote.exe is missing -- publish the app before compiling the installer."
}

$payloadMb = [Math]::Round((Get-ChildItem $publishDir -Recurse -File |
                            Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Host "  payload    : $payloadMb MB in $publishDir"

# --- 3. compile the installer ------------------------------------------------
Write-Host "`nCompiling the installer..." -ForegroundColor Cyan
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

& $iscc "/DAppVersion=$Version" $issPath
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

$setup = Join-Path $distDir "KRemote-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "Expected $setup, but it was not produced." }

$setupMb = [Math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "`nDone." -ForegroundColor Green
Write-Host "  $setup ($setupMb MB)"
Write-Host "  Copy that one file to the other PC and run it."
