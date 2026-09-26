# build-release.ps1
# Automates: clean -> dotnet publish -> Inno Setup compile -> artifacts

[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$scriptRoot = $PSScriptRoot
Set-Location $scriptRoot

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Etherprof Release Build Pipeline       " -ForegroundColor Cyan
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

Write-Host "[1/5] Target Version: $Version" -ForegroundColor Green

# 2. Locate Inno Setup Compiler (iscc.exe)
Write-Host "[2/5] Locating Inno Setup compiler (ISCC.exe)..." -ForegroundColor Yellow

$isccPath = $null
$isccCmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
if ($isccCmd) {
    $isccPath = $isccCmd.Source
} else {
    $possiblePaths = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $possiblePaths) {
        if (Test-Path $p) {
            $isccPath = $p
            break
        }
    }
}

if (-not $isccPath) {
    Write-Error "Inno Setup compiler (ISCC.exe) not found! Install it via: winget install JRSoftware.InnoSetup"
}
Write-Host "      Found ISCC at: $isccPath" -ForegroundColor Gray

# 3. Clean previous artifacts
Write-Host "[3/5] Cleaning output directories..." -ForegroundColor Yellow
$publishDir = Join-Path $scriptRoot "publish"
$artifactsDir = Join-Path $scriptRoot "artifacts"

if (Test-Path $publishDir) { Remove-Item -Path $publishDir -Recurse -Force }
if (Test-Path $artifactsDir) { Remove-Item -Path $artifactsDir -Recurse -Force }

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

# 4. Dotnet Publish (Self-Contained x64)
Write-Host "[4/5] Publishing Etherprof (Release win-x64, self-contained)..." -ForegroundColor Yellow
$projectPath = Join-Path $scriptRoot "src\Etherprof.App\Etherprof.App.csproj"

$publishArgs = @(
    "publish",
    $projectPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:Version=$Version",
    "-o", $publishDir,
    "--nologo"
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
}

# 5. Compile Inno Setup Installer
Write-Host "[5/5] Compiling Inno Setup installer..." -ForegroundColor Yellow
$issPath = Join-Path $scriptRoot "installer\Etherprof.iss"

& $isccPath "/DMyAppVersion=$Version" "/O$artifactsDir" "/FEtherprof-Setup-$Version" "/Q" $issPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compiler failed with exit code $LASTEXITCODE"
}

# 6. Verify and output results
$installerPath = Join-Path $artifactsDir "Etherprof-Setup-$Version.exe"
if (-not (Test-Path $installerPath)) {
    Write-Error "Installer executable not found at: $installerPath"
}

$hash = (Get-FileHash -Algorithm SHA256 $installerPath).Hash
$sizeMb = [math]::Round((Get-Item $installerPath).Length / 1MB, 2)

Write-Host "`n=========================================" -ForegroundColor Green
Write-Host "  BUILD SUCCESSFUL!                      " -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
Write-Host "Installer: $installerPath" -ForegroundColor White
Write-Host "Size:      $sizeMb MB" -ForegroundColor White
Write-Host "SHA-256:   $hash" -ForegroundColor White
Write-Host "=========================================`n" -ForegroundColor Green

return [PSCustomObject]@{
    Version = $Version
    InstallerPath = $installerPath
    Sha256 = $hash
    SizeMB = $sizeMb
}
