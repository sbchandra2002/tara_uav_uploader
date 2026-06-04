# TARA-Flasher

## Overview
Standalone Windows Forms application for flashing firmware to CUAV X7 devices via STM32CubeProgrammer CLI.

## Project Structure
```
CUAVFlasher_Standalone_FixedUploaderOnly/
├── TARA-Flasher.csproj      (Project configuration)
├── TARA-Flasher.sln         (Solution file)
├── Program.cs               (Application entry point)
├── AdvancedMainForm.cs      (Main UI and logic)
└── README.md                (This file)
```

## Requirements
- .NET Framework 4.8
- Windows Forms support
- STM32CubeProgrammer CLI installed at: `C:\Program Files\STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe`
- Visual Studio 2022 or later (for development)

## Features
- **Dark/Light Theme Toggle** - Switch between dark and light color schemes
- **Multi-File Firmware Upload** - Add and flash multiple binary files to different addresses
- **Session Management** - Save and load flashing sessions as JSON files
- **Password Protection** - Secure upload operations with password verification
- **Storage Indicator** - Visual representation of flash memory usage
- **Real-time Logging** - Console output from STM32CubeProgrammer CLI
- **Drag & Drop Support** - Drag firmware files directly into the application
- **ST-LINK Detection** - Detect connected programmer devices

## Building the Project
```powershell
# Restore dependencies
dotnet restore TARA-Flasher.csproj

# Build the project
dotnet build TARA-Flasher.csproj

# Run the application
dotnet run --project TARA-Flasher.csproj
```

## Usage

### Adding Files
1. Click "Add Files" button or drag files directly into the grid
2. Specify flash memory address for each file (e.g., 0x08000000)
3. Files can be .bin or .hex format

### Uploading Firmware
1. Click "Connect" to detect ST-LINK programmer
2. Add firmware files and set addresses
3. Click "Upload All" 
4. Enter the default password (12345) when prompted
5. Monitor progress in the log window

### Managing Sessions
- **Save Session**: Store current file list and addresses
- **Load Session**: Restore previously saved configuration

### Changing Password
1. Click menu (☰)
2. Select "Change Password"
3. Enter new password
4. Password is securely hashed with SHA256

## Default Password
- **Initial Password**: `12345`
- Password is stored hashed in `password.dat` file

## Configuration
### CLI Path
Edit `CLI_PATH` constant in `AdvancedMainForm.cs` if STM32CubeProgrammer is installed elsewhere:
```csharp
private const string CLI_PATH = @"C:\Program Files\STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe";
```

### Default Flash Address
Edit `DefaultAddress` constant:
```csharp
private const string DefaultAddress = "0x08000000";
```

## Troubleshooting

### "CLI not found" Error
- Verify STM32CubeProgrammer is installed
- Check the CLI path in code matches installation location
- Ensure `STM32_Programmer_CLI.exe` exists at the specified path

### "No ST-LINK detected"
- Check USB connection to programmer
- Install ST-LINK USB drivers
- Try running as Administrator

### Upload Failures
- Verify file format (.bin or .hex)
- Check memory addresses don't conflict
- Ensure password is correct (default: 12345)
- Check device flash memory isn't corrupted

## Color Themes

### Dark Theme (Default)
- Background: #1C202E
- Panel: #242933
- Accent: #20B9FF
- Text: #DCE6F5

### Light Theme
- Background: #EEF3FA
- Panel: #DCE7F5
- Accent: #3C8CDC
- Text: #282D2D

## Technical Details
- **Language**: C# 7.3
- **Framework**: .NET Framework 4.8
- **UI Framework**: Windows Forms
- **Security**: SHA256 password hashing with salt
- **Process Communication**: Async/await with process redirection

## File Layout
- **FileName**: Binary file name
- **Address**: Hex memory address (e.g., 0x08000000)
- **Size**: File size in bytes
- **Status**: Pending/Flashing/Success/Failed/Missing/Invalid Address

## License
[Add license information here]

## Contact
[Add contact information here]
