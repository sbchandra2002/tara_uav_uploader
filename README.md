# TARA-Flasher

A Windows desktop app for flashing STM32-based flight-controller firmware over SWD via STM32CubeProgrammer CLI.

---

## Requirements

| Requirement | Notes |
|---|---|
| Windows 10 / 11 (64-bit) | |
| .NET Framework 4.8 | Ships with Windows 10 1903 and later |
| STM32CubeProgrammer | Required for flashing; the app will offer to install it automatically |
| ST-LINK v2 / v3 probe | Connected via USB |

---

## Installation (end users)

### Option A — Combined installer (recommended)

1. Go to [**Releases**](../../releases) and download the latest `TARA-Flasher-Setup.exe`.
2. If the release also includes `SetupSTM32CubeProgrammer_win64.exe`, download it into the **same folder**.
3. Run `TARA-Flasher-Setup.exe` and follow the wizard.
   - Installs to `C:\Program Files\TARA-Flasher\`
   - Creates a desktop shortcut and Start Menu entry

### Option B — Auto-install on first launch

If STM32CubeProgrammer is not yet installed on the target PC:

1. Run `TARA-Flasher-Setup.exe`.
2. Copy `SetupSTM32CubeProgrammer_win64.exe` into `C:\Program Files\TARA-Flasher\`.
3. Launch **TARA-Flasher**. The app detects the missing CLI and shows:

   > **SETUP :: STM32CubeProgrammer Required**  
   > Installer found: SetupSTM32CubeProgrammer_win64.exe  
   > Click **Install** to run it now.

4. Click **Install**, complete the wizard, then close it.  
   TARA-Flasher auto-detects the CLI and is immediately ready to flash.

---

## Using the app

```
1. Connect ST-LINK probe + target board via SWD.
2. The connection pill (top-right) turns GREEN when the device is detected.
   Click it to confirm ("Connected").
3. Menu > Set Firmware Folder — point to the folder with your
   bootloader / metadata / firmware .bin or .hex files.
4. Verify the file list and flash addresses in the FILE MAP table.
5. Click UPLOAD — the animated overlay shows real-time per-file progress.
6. A result dialog confirms success or lists any failed files.
```

### Connection pill states

| Colour | Label | Meaning |
|---|---|---|
| Red | Connect | No ST-LINK detected |
| Green | Connect | Device found — click to confirm |
| Bright green | Connected | Actively connected, ready to flash |
| Amber | Scanning… | Detection in progress |

### Full chip erase

**Menu > Full Chip Erase** wipes the entire flash (requires admin password).  
The animated overlay turns red/orange during the erase.

---

## Building from source

```powershell
# 1. Clone and switch to the release branch
git clone https://github.com/sbchandra2002/tara_uav_uploader.git
cd tara_uav_uploader
git checkout v1

# 2. Build the app
dotnet publish TARA-Flasher.csproj -c Release -f net48 -o publish_out

# 3. Create the installer  (requires Inno Setup 6)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
# Output: installer_out\TARA-Flasher-Setup.exe
```

To also bundle the STM32CubeProgrammer setup, place  
`SetupSTM32CubeProgrammer_win64.exe` (or any file matching `STM32CubeProgrammer*.exe`)  
next to `installer.iss` before running ISCC — it is picked up automatically.

---

## CI/CD — GitHub Actions

The workflow at [`.github/workflows/build.yml`](.github/workflows/build.yml) runs on every push to `v1` and on version tags.

| Trigger | Result |
|---|---|
| Push to `v1` | Builds app + installer, uploads as a 30-day downloadable artifact |
| Tag `v1.0.0` | Same **+** creates a public GitHub Release with both files attached |

### One-time setup: store the STM32 setup in a `deps` release

The STM32CubeProgrammer installer (~180 MB) exceeds GitHub's 100 MB per-file git limit.  
Store it once in a special **`deps`** release and every CI run will download and bundle it automatically.

```powershell
# Run this once from your local machine (needs gh CLI + repo write access)
gh release create deps SetupSTM32CubeProgrammer_win64.exe `
  --title "Build Dependencies" `
  --notes "Large build-time deps. Not a user-facing release."
```

**What happens in CI after that:**

1. Workflow downloads `SetupSTM32CubeProgrammer_win64.exe` from the `deps` release.
2. Inno Setup bundles it inside `TARA-Flasher-Setup.exe`.
3. On a version tag, both `TARA-Flasher-Setup.exe` **and** `SetupSTM32CubeProgrammer_win64.exe` are attached to the GitHub Release for direct user download.

> If the `deps` release doesn't exist (or the download fails), the build continues without bundling — the installer still works and prompts the user to install STM32CubeProgrammer on first launch.

---

## Default passwords

| Password | Default | Change via |
|---|---|---|
| Upload password | `12345` | Menu > Change Upload Password |
| Admin password | `Tara@123` | Menu > Change Admin Password |

> Change both passwords before distributing to a team.
