# TARA-Flasher - Build and Deployment Guide

## Overview
This guide explains how to build, create an installer, and package your TARA-Flasher application for distribution.

## Prerequisites
- **Windows 10/11**
- **.NET Framework 4.8** or higher
- **Inno Setup 6** (for creating the installer)

## Files Included

### Build Files
- **build_package.ps1** - Automated PowerShell script that handles the entire build process
- **installer.iss** - Inno Setup configuration file for creating the installer
- **logo.ico** - Application icon (displayed in installer and shortcuts)
- **TARA-Flasher.csproj** - Project file with application icon configuration

### Output Directory
After running the build script, the following files will be created in the `dist/` folder:

```
dist/
├── TARA-Flasher/              # All application files
│   ├── TARA-Flasher.exe       # Main application
│   ├── logo.ico              # Application icon
│   └── (other dependencies)
├── TARA-Flasher_Setup.exe     # Installer executable
└── TARA-Flasher_Package.zip   # Packaged installer for distribution
```

## Quick Start

### Step 1: Install Inno Setup (One-time setup)
1. Download Inno Setup 6 from: https://jrsoftware.org/isdl.php
2. Run the installer and follow the default installation steps
3. Install to the default location: `C:\Program Files (x86)\Inno Setup 6\`

### Step 2: Run the Build Script
Open PowerShell in the project root directory and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build_package.ps1
```

**From PowerShell in the project folder:**
```powershell
.\build_package.ps1
```

### Step 3: Distribute to Users
After the script completes, you'll have:

- **TARA-Flasher_Setup.exe** - The installer (recommended for distribution)
- **TARA-Flasher_Package.zip** - Zipped installer (for email/file sharing)

**Give users either:**
- `dist\TARA-Flasher_Setup.exe` - Run directly
- `dist\TARA-Flasher_Package.zip` - Extract and run the .exe inside

---

## What the Build Script Does

### Step 1: Clean Previous Builds
Removes the `dist/` folder to ensure a clean build.

### Step 2: Build in Release Mode
Compiles the project with optimizations:
```
dotnet build .\TARA-Flasher.csproj -c Release
```

### Step 3: Prepare Distribution Files
Copies all compiled files to `dist\TARA-Flasher\`:
- TARA-Flasher.exe
- All dependent assemblies
- logo.ico
- Configuration files

### Step 4: Compile Installer
Uses Inno Setup to create the installer:
```
C:\Program Files (x86)\Inno Setup 6\ISCC.exe installer.iss
```

This creates: `dist\TARA-Flasher_Setup.exe`

### Step 5: Create Distribution Package
Packages the installer into a ZIP file:
- File: `dist\TARA-Flasher_Package.zip`
- Contents: Only the TARA-Flasher_Setup.exe

---

## What the Installer Does

When users run **TARA-Flasher_Setup.exe**, the installer will:

1. **Extract files** to: `C:\Users\{UserName}\AppData\Local\TARA-Flasher\`

2. **Create shortcuts:**
   - Start Menu: "TARA-Flasher"
   - Desktop: "TARA-Flasher" (optional)
   - Quick Launch: "TARA-Flasher" (optional)

3. **Check for STM32CubeProgrammer:**
   - If not found, prompts user to download from:
     https://www.st.com/en/development-tools/stm32cubeprog.html
   - Does NOT bundle STM32CubeProgrammer (keep installer size small)

4. **Optionally run the application** after installation

---

## Troubleshooting

### Issue: "Inno Setup 6 is required but not found"

**Solution:**
1. Download Inno Setup 6: https://jrsoftware.org/isdl.php
2. Run the installer with default settings
3. Restart PowerShell
4. Run the build script again

### Issue: Build fails with compilation errors

**Solution:**
1. Ensure .NET Framework 4.8 is installed
2. Run: `dotnet restore`
3. Try building manually: `dotnet build .\TARA-Flasher.csproj -c Release`

### Issue: Files seem to be missing from the installer

**Solution:**
1. Check that `dist\TARA-Flasher\` folder contains all files
2. Verify `logo.ico` exists in the project root
3. Delete the `dist/` folder and run the script again

### Issue: Installer creation skipped

This happens if Inno Setup is not installed. You'll see:
```
[BUILD] Step 4: Checking Inno Setup installation...
[BUILD] Inno Setup 6 is required but not found!
[BUILD] Continue anyway? (Y/N)
```

Type `N` and install Inno Setup, then run the script again.

---

## Advanced: Manual Installer Compilation

If you need to manually compile the Inno Setup script:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
```

---

## Version Information

- **Application:** TARA-Flasher v1.0
- **Target:** .NET Framework 4.8
- **Platform:** Windows x86
- **Installer:** Inno Setup 6
- **Compression:** LZMA + Solid Compression

---

## File Size Estimates

- **TARA-Flasher.exe:** ~1-5 MB (compiled application)
- **TARA-Flasher_Setup.exe:** ~3-8 MB (compressed installer)
- **TARA-Flasher_Package.zip:** ~2-6 MB (zipped installer)

---

## Support & Issues

For issues building or deploying:

1. Check that all prerequisites are installed
2. Ensure you have Write permissions to the project folder
3. Try running PowerShell as Administrator
4. Review the detailed output messages from the build script

---

## Customization

### Changing Application Icon

1. Replace `logo.ico` in the project root
2. The application icon settings are configured in:
   - `TARA-Flasher.csproj` → `<ApplicationIcon>logo.ico</ApplicationIcon>`
   - `installer.iss` → `SetupIconFile=logo.ico`
   - `AdvancedMainForm.cs` → Icon loading code

### Changing Installer Settings

Edit `installer.iss` to modify:
- Application name: `AppName`
- Version: `AppVersion`
- Installation directory: `DefaultDirName`
- Company name: `AppPublisher`

### Changing STM32 Dependency Check

The STM32CubeProgrammer check is in the `[Code]` section of `installer.iss`:
```
STM32CLIPath := ExpandConstant('C:\Program Files\STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe');
```

---

## License

This build system is part of the TARA-Flasher project.

---

**Last Updated:** April 2026
