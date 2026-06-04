# TARA-Flasher Build and Package Script
# This script automates the entire process of building, creating installer, and packaging

$ErrorActionPreference = "Stop"

# Colors for output
$success = "Green"
$error_color = "Red"
$info = "Cyan"
$warning = "Yellow"

function Write-Status {
    param([string]$Message, [string]$Color = $info)
    Write-Host "[BUILD] $Message" -ForegroundColor $Color
}

function Test-Inno {
    $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $isccPath)) {
        Write-Status "Inno Setup 6 is required but not found!" $error_color
        Write-Status "Download from: https://jrsoftware.org/isdl.php" $warning
        return $false
    }
    return $true
}

try {
    Write-Status "========================================" $info
    Write-Status "TARA-Flasher Build and Package Script" $info
    Write-Status "========================================" $info
    
    # Get script directory
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    
    # Step 1: Clean previous builds
    Write-Status "Step 1: Cleaning previous builds..." $info
    if (Test-Path "$scriptDir\dist") {
        Remove-Item "$scriptDir\dist" -Recurse -Force
    }
    New-Item -ItemType Directory -Path "$scriptDir\dist" -Force | Out-Null
    Write-Status "Previous build cleaned" $success
    
    # Step 2: Build project in Release mode
    Write-Status "Step 2: Building project in Release mode..." $info
    Push-Location $scriptDir
    & dotnet build .\TARA-Flasher.csproj -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Pop-Location
    Write-Status "Build completed successfully" $success
    
    # Step 3: Create dist structure and copy files
    Write-Status "Step 3: Copying release files to dist folder..." $info
    $sourcePath = "$scriptDir\bin\Release\net48"
    $distPath = "$scriptDir\dist\TARA-Flasher"
    
    # Create destination folder
    New-Item -ItemType Directory -Path $distPath -Force | Out-Null
    
    # Copy all files
    Copy-Item "$sourcePath\*" -Destination $distPath -Recurse -Force
    
    # Copy logo.ico to dist folder
    if (Test-Path "$scriptDir\logo.ico") {
        Copy-Item "$scriptDir\logo.ico" -Destination $distPath -Force
        Write-Status "logo.ico copied to dist folder" $success
    } else {
        Write-Status "Warning: logo.ico not found in project root" $warning
    }
    
    Write-Status "Files copied to $distPath" $success
    
    # Step 4: Check Inno Setup installation
    Write-Status "Step 4: Checking Inno Setup installation..." $info
    $hasInnoSetup = Test-Inno
    if (-not $hasInnoSetup) {
        Write-Status "Inno Setup 6 not found - installer will be created when Inno Setup is installed" $warning
        Write-Status "Continue anyway? (Y/N)" $warning
        $response = Read-Host
        if ($response -ne "Y" -and $response -ne "y") {
            throw "Build cancelled by user"
        }
    } else {
        Write-Status "Inno Setup 6 found" $success
    }
    
    
    # Step 5: Compile installer with Inno Setup
    Write-Status "Step 5: Compiling installer with Inno Setup..." $info
    $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    $issFile = "$scriptDir\installer.iss"
    
    if (-not (Test-Path $issFile)) {
        throw "installer.iss not found at $issFile"
    }
    
    if ($hasInnoSetup) {
        & $isccPath $issFile
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
        }
        Write-Status "Installer compiled successfully" $success
    } else {
        Write-Status "Skipped: Inno Setup not installed" $warning
        Write-Status "To complete installer creation, install Inno Setup 6 and run this script again:" $info
        Write-Status "  Download: https://jrsoftware.org/isdl.php" $info
    }
    
    # Step 6: Create zip package
    Write-Status "Step 6: Creating zip package..." $info
    $installerPath = "$scriptDir\dist\TARA-Flasher_Setup.exe"
    $zipPath = "$scriptDir\dist\TARA-Flasher_Package.zip"
    
    if (Test-Path $installerPath) {
        # Remove old zip if exists
        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }
        
        # Create clean zip with only the installer
        $tempZipDir = "$scriptDir\dist\temp_zip"
        New-Item -ItemType Directory -Path $tempZipDir -Force | Out-Null
        Copy-Item $installerPath -Destination $tempZipDir -Force
        
        # Create zip
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($tempZipDir, $zipPath)
        
        # Cleanup temp
        Remove-Item $tempZipDir -Recurse -Force
        
        Write-Status "Zip package created at $zipPath" $success
    } else {
        Write-Status "Skipped: Installer not created (install Inno Setup to create it)" $warning
    }
    
    # Final Summary
    Write-Status "========================================" $info
    Write-Status "Build Complete!" $success
    Write-Status "========================================" $info
    Write-Status ""
    Write-Status "Output Files:" $info
    
    if (Test-Path "$scriptDir\dist\TARA-Flasher_Setup.exe") {
        Write-Status "  [OK] Installer:  dist\TARA-Flasher_Setup.exe" $success
        Write-Status "  [OK] Package:    dist\TARA-Flasher_Package.zip" $success
    } else {
        Write-Status "  [NO] Installer:  NOT CREATED (install Inno Setup 6 to create)" $warning
        Write-Status "  [NO] Package:    NOT CREATED" $warning
    }
    
    Write-Status "  [OK] Files Dir:  dist\TARA-Flasher\" $success
    Write-Status ""
    
    if (Test-Path "$scriptDir\dist\TARA-Flasher_Setup.exe") {
        Write-Status "To distribute:" $info
        Write-Status "  Option 1: Send dist\TARA-Flasher_Package.zip to users" $success
        Write-Status "  Option 2: Send dist\TARA-Flasher_Setup.exe directly" $success
        Write-Status ""
    } else {
        Write-Status "NEXT STEPS:" $warning
        Write-Status "  1. Install Inno Setup 6: https://jrsoftware.org/isdl.php" $info
        Write-Status "  2. Run this script again to create the installer" $info
        Write-Status ""
    }
    
    Write-Status "Note: Users must have STM32CubeProgrammer installed." $warning
    Write-Status "      The installer will prompt to download it if missing." $warning
    
} catch {
    Write-Status "ERROR: $($_.Exception.Message)" $error_color
    exit 1
}
