# KRemote Installer Build Script
# This script builds the application and creates an Inno Setup installer

param(
	[string]$Configuration = "Release",
	[string]$Platform = "x64",
	[switch]$SkipBuild,
	[switch]$SkipClean
)

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionFile = Join-Path $ProjectRoot "KRemote.sln"
$InnoSetupScript = Join-Path $ProjectRoot "KRemote-Installer.iss"

# Colors for output
$InfoColor = "Cyan"
$SuccessColor = "Green"
$ErrorColor = "Red"
$WarningColor = "Yellow"

Write-Host "========================================" -ForegroundColor $InfoColor
Write-Host "KRemote Installer Builder" -ForegroundColor $InfoColor
Write-Host "========================================" -ForegroundColor $InfoColor
Write-Host ""

# Check if solution exists
if (-not (Test-Path $SolutionFile)) {
	Write-Host "Error: Solution file not found at $SolutionFile" -ForegroundColor $ErrorColor
	exit 1
}

# Step 1: Clean (optional)
if (-not $SkipClean) {
	Write-Host "[1/4] Cleaning previous builds..." -ForegroundColor $InfoColor
	dotnet clean $SolutionFile -c $Configuration
	if ($LASTEXITCODE -ne 0) {
		Write-Host "Warning: Clean failed, continuing anyway..." -ForegroundColor $WarningColor
	}
}

# Step 2: Restore packages
Write-Host "[2/4] Restoring NuGet packages..." -ForegroundColor $InfoColor
dotnet restore $SolutionFile
if ($LASTEXITCODE -ne 0) {
	Write-Host "Error: Package restore failed" -ForegroundColor $ErrorColor
	exit 1
}

# Step 3: Build
if (-not $SkipBuild) {
	Write-Host "[3/4] Building application ($Configuration | $Platform)..." -ForegroundColor $InfoColor
	dotnet build $SolutionFile -c $Configuration
	if ($LASTEXITCODE -ne 0) {
		Write-Host "Error: Build failed" -ForegroundColor $ErrorColor
		exit 1
	}
	Write-Host "Build completed successfully" -ForegroundColor $SuccessColor
}

# Step 4: Create installer
Write-Host "[4/4] Creating installer with Inno Setup..." -ForegroundColor $InfoColor

# Check if Inno Setup is installed
$InnoSetupPath = Get-ChildItem -Path "C:\Program Files*" -Filter "ISCC.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $InnoSetupPath) {
	Write-Host ""
	Write-Host "========================================" -ForegroundColor $WarningColor
	Write-Host "Inno Setup Not Found" -ForegroundColor $WarningColor
	Write-Host "========================================" -ForegroundColor $WarningColor
	Write-Host ""
	Write-Host "Inno Setup is required to build the installer." -ForegroundColor $WarningColor
	Write-Host "Please download and install it from: https://jrsoftware.org/isdl.php" -ForegroundColor $WarningColor
	Write-Host ""
	Write-Host "After installation, run this script again." -ForegroundColor $WarningColor
	exit 1
}

Write-Host "Found Inno Setup at: $($InnoSetupPath.FullName)" -ForegroundColor $SuccessColor

# Run Inno Setup
& $InnoSetupPath.FullName $InnoSetupScript

if ($LASTEXITCODE -eq 0) {
	Write-Host ""
	Write-Host "========================================" -ForegroundColor $SuccessColor
	Write-Host "Installer Created Successfully!" -ForegroundColor $SuccessColor
	Write-Host "========================================" -ForegroundColor $SuccessColor
	Write-Host "Output: $ProjectRoot\Installer\KRemote-Setup.exe" -ForegroundColor $SuccessColor
	Write-Host ""
} else {
	Write-Host "Error: Inno Setup failed with exit code $LASTEXITCODE" -ForegroundColor $ErrorColor
	exit 1
}
