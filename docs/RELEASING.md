# Releasing Etherprof & WinGet Distribution Guide

This document describes the complete release workflow for Etherprof, from local build to Inno Setup packaging, GitHub Releases, and publication to the Microsoft WinGet Community Repository.

---

## 🏗️ Architecture & Workflow Overview

```
Source Code
   ↓
Directory.Build.props (Single version source of truth)
   ↓
dotnet publish (Release win-x64, self-contained)
   ↓
Inno Setup (installer/Etherprof.iss → artifacts/Etherprof-Setup-<version>.exe)
   ↓
GitHub Release (v<version> tag triggers GitHub Actions CI)
   ↓
WinGet Manifests (prepare-winget.ps1)
   ↓
microsoft/winget-pkgs Pull Request (wingetcreate submit)
   ↓
winget install mikemaste1.Etherprof
```

---

## 🛠️ Prerequisites

1. **.NET 10.0 SDK**: [Download .NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
2. **Inno Setup 6**: Install via WinGet:
   ```powershell
   winget install JRSoftware.InnoSetup
   ```
3. **WinGet Manifest Creator CLI**: Install via WinGet:
   ```powershell
   winget install Microsoft.WingetCreate
   ```

---

## 🚀 Part 1: First Release (v0.4.2) Step-by-Step

### 1. Build the Installer Locally
Run the automated release build script from the repository root:
```powershell
.\build-release.ps1
```
This script:
- Reads the version from `Directory.Build.props`.
- Executes `dotnet publish` (Release, self-contained, x64).
- Invokes Inno Setup compiler (`iscc.exe`).
- Generates `artifacts\Etherprof-Setup-0.4.2.exe`.
- Computes and prints the SHA-256 hash.

### 2. Verify Silent Install & Uninstall Locally
Test the installer in silent mode (requires elevated PowerShell):
```powershell
# Silent Install
Start-Process "artifacts\Etherprof-Setup-0.4.2.exe" -ArgumentList "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /SP-" -Verb RunAs -Wait

# Verify files and Start Menu shortcut
Test-Path "C:\Program Files\Etherprof\Etherprof.exe"
Test-Path "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Etherprof.lnk"

# Silent Uninstall
Start-Process "C:\Program Files\Etherprof\unins000.exe" -ArgumentList "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES" -Verb RunAs -Wait
```
*(User settings in `%LocalAppData%\Etherprof` are safely preserved across upgrades and uninstalls).*

### 3. Create the GitHub Release
1. Push git commits and tag:
   ```powershell
   git push origin main
   git push origin v0.4.2
   ```
2. Open: **[https://github.com/mikemaste1/Etherprof/releases/new?tag=v0.4.2](https://github.com/mikemaste1/Etherprof/releases/new?tag=v0.4.2)**
3. Title: `Etherprof v0.4.2`
4. Attach `artifacts\Etherprof-Setup-0.4.2.exe`.
5. Click **Publish release**.

> **Installer URL**: Once published, the installer will be available at:  
> `https://github.com/mikemaste1/Etherprof/releases/download/v0.4.2/Etherprof-Setup-0.4.2.exe`

### 4. Prepare & Validate WinGet Manifests
Run the manifest helper script:
```powershell
.\prepare-winget.ps1
```
This script:
- Verifies the installer binary in `artifacts/`.
- Computes the exact SHA-256 hash.
- Generates valid Inno Setup manifests in `manifests\m\mikemaste1\Etherprof\0.4.2\`:
  - `mikemaste1.Etherprof.yaml` (version)
  - `mikemaste1.Etherprof.installer.yaml` (installer type `inno`, switches, scope `machine`, hash, URL)
  - `mikemaste1.Etherprof.locale.en-US.yaml` (metadata & descriptions)
- Validates the manifests using `winget validate`.

### 5. Submit to Microsoft WinGet Repository

#### Option A: Automatic Submission with `wingetcreate` (Recommended)
```powershell
wingetcreate submit "manifests\m\mikemaste1\Etherprof\0.4.2" --token <YOUR_GITHUB_TOKEN>
```
*Note: If your GitHub account already has an outdated fork of `microsoft/winget-pkgs`, delete it in [GitHub Settings](https://github.com/mikemaste1/winget-pkgs/settings) first so `wingetcreate` can create a clean fork automatically.*

#### Option B: Manual Pull Request via GitHub Web UI
1. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) on GitHub.
2. In your fork, create folder: `manifests/m/mikemaste1/Etherprof/0.4.2/`.
3. Upload the 3 YAML files from `manifests\m\mikemaste1\Etherprof\0.4.2\`.
4. Open a Pull Request against `microsoft/winget-pkgs:master`.

Microsoft's automated CI will run checks, verify the SHA-256 hash, and merge the PR.

---

## 🔄 Part 2: Workflow for Future Releases (e.g., v0.4.3)

For all subsequent versions, the process is streamlined to 6 simple steps:

1. **Bump Version**:
   Edit `Directory.Build.props`:
   ```xml
   <Version>0.4.3</Version>
   ```
2. **Commit & Tag**:
   ```powershell
   git add Directory.Build.props
   git commit -m "chore: bump version to 0.4.3"
   git tag v0.4.3
   git push origin main --tags
   ```
3. **Automated CI Build**:
   GitHub Actions (`.github/workflows/release.yml`) automatically builds the Inno Setup installer and publishes the GitHub Release with `Etherprof-Setup-0.4.3.exe` attached.
4. **Prepare Manifests**:
   ```powershell
   .\prepare-winget.ps1 -Version 0.4.3
   ```
5. **Submit to WinGet**:
   ```powershell
   wingetcreate submit "manifests\m\mikemaste1\Etherprof\0.4.3" --token <YOUR_GITHUB_TOKEN>
   ```
6. **Users Upgrade**:
   ```powershell
   winget upgrade mikemaste1.Etherprof
   ```

---

## 📋 Key Technical Reference

| Item | Value |
| :--- | :--- |
| **PackageIdentifier** | `mikemaste1.Etherprof` |
| **PackageName** | `Etherprof` |
| **Publisher** | `mikemaste1` |
| **Inno Setup AppId** | `{{E74E8F01-B2C1-4D5F-8A9D-56D25494C201}}` |
| **Registry Uninstall Key** | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E74E8F01-B2C1-4D5F-8A9D-56D25494C201}_is1` |
| **Silent Install Switch** | `/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /SP-` |
| **Silent Uninstall Switch** | `/VERYSILENT /NORESTART /SUPPRESSMSGBOXES` |
| **Installation Directory** | `C:\Program Files\Etherprof\` |
| **User Data Directory** | `%LocalAppData%\Etherprof\` |
| **Install Scope** | `machine` (Elevation required for network adapter configuration) |
