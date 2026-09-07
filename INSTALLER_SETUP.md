# KRemote Installer Setup Guide

## Overview
This guide helps you create an installer for the KRemote WPF application using Inno Setup.

## Prerequisites

1. **Windows Environment**
   - Windows 10 Build 19041 or later
   - Administrator access for installation

2. **Development Tools**
   - .NET 9 SDK (for building)
   - Visual Studio 2026 or later (optional, but recommended)

3. **Installer Tool**
   - Inno Setup 6.3 or later (https://jrsoftware.org/isdl.php)

## Quick Start

### Step 1: Install Inno Setup
1. Download Inno Setup from: https://jrsoftware.org/isdl.php
2. Run the installer
3. Follow the installation wizard
4. Accept the default installation directory
5. Complete the installation

### Step 2: Build the Installer

#### Option A: Using PowerShell Script (Recommended)
```powershell
cd C:\Users\Hussein.Merhi\source\T1-Merhi\KRemote
.\Build-Installer.ps1
```

#### Option B: Using Batch File
```cmd
cd C:\Users\Hussein.Merhi\source\T1-Merhi\KRemote
Build-Installer.bat
```

#### Option C: Manual Build with Inno Setup
1. Open Inno Setup GUI application
2. File > Open
3. Select `KRemote-Installer.iss` from the project root
4. Build > Compile
5. The installer will be created in the `Output` folder

## Installer Output
The generated installer will be located at:
```
C:\Users\Hussein.Merhi\source\T1-Merhi\KRemote\Output\KRemote-Setup.exe
```

## Customizing the Installer

### Modify Application Details
Edit `KRemote-Installer.iss` to change:
- **AppName**: Application name
- **AppVersion**: Version number (should match version in KRemote.csproj)
- **AppPublisher**: Company/Developer name
- **DefaultDirName**: Installation directory

### Add Files to Installer
In the `[Files]` section of `KRemote-Installer.iss`, add:
```ini
Source: "path\to\file"; DestDir: "{app}"; Flags: ignoreversion
```

### Change Installation Scope
Modify `InstallScope` in the `[Setup]` section:
- `perUser` - Install for current user only
- `perMachine` - Install for all users (requires admin)

### Add Desktop/Startup Shortcuts
Modify the `[Tasks]` and `[Icons]` sections in `KRemote-Installer.iss`

## Troubleshooting

### "Inno Setup Not Found"
- Ensure Inno Setup is installed at: `C:\Program Files (x86)\Inno Setup 6`
- Restart your terminal after installation
- Manually verify installation path if using custom location

### Build Fails
- Ensure the project builds successfully:
  ```
  dotnet build KRemote.sln -c Release
  ```
- Check that all dependencies are installed
- Verify the build output directory path in `KRemote-Installer.iss`

### Installer Won't Create
- Check that you have write permissions to the project directory
- Ensure `Output` folder exists or will be created automatically
- Verify Inno Setup permissions

## Installer Features

The generated installer includes:
- ✓ Modern Windows installer UI
- ✓ 64-bit Windows 10+ support
- ✓ Start Menu shortcuts
- ✓ Optional Desktop shortcuts
- ✓ Automatic uninstaller
- ✓ Registry entries for Add/Remove Programs
- ✓ Custom installation directory support

## Distribution

1. The installer EXE is standalone and can be distributed directly
2. Upload to GitHub Releases or your distribution platform
3. Provide installation instructions to users
4. Consider code signing for additional security (optional)

## File Descriptions

- **KRemote-Installer.iss** - Inno Setup script defining installer behavior
- **Build-Installer.ps1** - PowerShell script automating the build process
- **Build-Installer.bat** - Batch file wrapper for easier access

## Support
For issues or questions:
- GitHub: https://github.com/T1-Merhi/KRemote
- Inno Setup Documentation: https://jrsoftware.org/ishelp/
