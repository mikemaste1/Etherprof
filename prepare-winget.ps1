# prepare-winget.ps1
# Automates generating and validating WinGet manifests for a release

[CmdletBinding()]
param(
    [string]$Version,
    [string]$InstallerPath,
    [string]$InstallerUrl
)

$ErrorActionPreference = 'Stop'
$scriptRoot = $PSScriptRoot
Set-Location $scriptRoot

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  WinGet Manifest Preparation Tool       " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Determine Version
if ([string]::IsNullOrWhiteSpace($Version)) {
    $propsPath = Join-Path $scriptRoot "Directory.Build.props"
    if (Test-Path $propsPath) {
        [xml]$propsXml = Get-Content $propsPath
        $Version = $propsXml.Project.PropertyGroup.Version
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.4.2"
}

Write-Host "[1/4] Target Version: $Version" -ForegroundColor Green

# 2. Locate Installer & Calculate SHA256
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $scriptRoot "artifacts\Etherprof-Setup-$Version.exe"
}

if (-not (Test-Path $InstallerPath)) {
    Write-Host "Installer not found at: $InstallerPath" -ForegroundColor Yellow
    Write-Host "Running build-release.ps1 to compile installer first..." -ForegroundColor Yellow
    .\build-release.ps1 -Version $Version
}

$sha256 = (Get-FileHash -Algorithm SHA256 $InstallerPath).Hash
Write-Host "[2/4] Installer SHA256: $sha256" -ForegroundColor Green

# 3. Determine Installer URL
if ([string]::IsNullOrWhiteSpace($InstallerUrl)) {
    $InstallerUrl = "https://github.com/mikemaste1/Etherprof/releases/download/v$Version/Etherprof-Setup-$Version.exe"
}
Write-Host "      Installer URL:    $InstallerUrl" -ForegroundColor Gray

# 4. Generate Manifests in WinGet Community Repository Structure
$targetDir = Join-Path $scriptRoot "manifests\m\mikemaste1\Etherprof\$Version"
if (Test-Path $targetDir) { Remove-Item -Path $targetDir -Recurse -Force }
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

$versionYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json

PackageIdentifier: mikemaste1.Etherprof
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

$installerYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json

PackageIdentifier: mikemaste1.Etherprof
PackageVersion: $Version
InstallerType: inno
Scope: machine
InstallModes:
  - interactive
  - silent
  - silentWithProgress
InstallerSwitches:
  Silent: /VERYSILENT /NORESTART /SUPPRESSMSGBOXES /SP-
  SilentWithProgress: /SILENT /NORESTART /SUPPRESSMSGBOXES /SP-
UpgradeBehavior: install
ElevationRequirement: elevationRequired
AppsAndFeaturesEntries:
  - DisplayName: Etherprof
    Publisher: mikemaste1
    DisplayVersion: $Version
    ProductCode: '{E74E8F01-B2C1-4D5F-8A9D-56D25494C201}_is1'
Installers:
  - Architecture: x64
    InstallerUrl: $InstallerUrl
    InstallerSha256: $sha256
ManifestType: installer
ManifestVersion: 1.6.0
"@

$localeYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json

PackageIdentifier: mikemaste1.Etherprof
PackageVersion: $Version
PackageLocale: en-US
Publisher: mikemaste1
PublisherUrl: https://github.com/mikemaste1
PackageName: Etherprof
PackageUrl: https://github.com/mikemaste1/Etherprof
License: MIT
LicenseUrl: https://raw.githubusercontent.com/mikemaste1/Etherprof/main/LICENSE
ShortDescription: Advanced Windows Network Adapter Profile & Diagnostics Tool
Description: Etherprof is a fast, specialized Windows utility for network engineers and power users to quickly capture, manage, switch network adapter configurations (static IP, DHCP, gateway, DNS), monitor Wi-Fi, run continuous visual pings with history trails, and diagnose connection health.
Moniker: etherprof
Tags:
  - networking
  - adapter
  - ip
  - ping
  - wifi
  - diagnostics
  - traceroute
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

Set-Content -Path (Join-Path $targetDir "mikemaste1.Etherprof.yaml") -Value $versionYaml -Encoding utf8
Set-Content -Path (Join-Path $targetDir "mikemaste1.Etherprof.installer.yaml") -Value $installerYaml -Encoding utf8
Set-Content -Path (Join-Path $targetDir "mikemaste1.Etherprof.locale.en-US.yaml") -Value $localeYaml -Encoding utf8

# Also update winget/ template dir
$wingetDir = Join-Path $scriptRoot "winget"
if (-not (Test-Path $wingetDir)) { New-Item -ItemType Directory -Path $wingetDir -Force | Out-Null }
Set-Content -Path (Join-Path $wingetDir "mikemaste1.Etherprof.yaml") -Value $versionYaml -Encoding utf8
Set-Content -Path (Join-Path $wingetDir "mikemaste1.Etherprof.installer.yaml") -Value $installerYaml -Encoding utf8
Set-Content -Path (Join-Path $wingetDir "mikemaste1.Etherprof.locale.en-US.yaml") -Value $localeYaml -Encoding utf8

Write-Host "[3/4] Manifests written to: $targetDir" -ForegroundColor Green

# 5. Validate with winget validate
Write-Host "[4/4] Validating manifests with 'winget validate'..." -ForegroundColor Yellow
$valResult = winget validate $targetDir
Write-Host $valResult -ForegroundColor Gray

Write-Host "`n=========================================" -ForegroundColor Green
Write-Host "  MANIFESTS READY FOR SUBMISSION!        " -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
Write-Host "Directory: manifests\m\mikemaste1\Etherprof\$Version" -ForegroundColor White
Write-Host "`nTo submit to Microsoft WinGet:" -ForegroundColor Cyan
Write-Host "wingetcreate submit manifests\m\mikemaste1\Etherprof\$Version --token <YOUR_GITHUB_TOKEN>`n" -ForegroundColor Yellow
