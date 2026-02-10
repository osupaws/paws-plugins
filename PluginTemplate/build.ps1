# Universal Paws Plugin Build Script
# Usage: .\build.ps1
# Place this in the root of your plugin folder (next to 'ui' and 'src' folders).

$ErrorActionPreference = "Stop"

Write-Host "--- Paws Plugin Build Started ---" -ForegroundColor Cyan

# 1. Build Frontend
if (Test-Path "ui") {
    Write-Host "Building UI..." -ForegroundColor Yellow
    Push-Location "ui"
    try {
        # Check if pnpm is used, otherwise npm
        if (Test-Path "pnpm-lock.yaml") {
            pnpm build
        } else {
            npm run build
        }
        
        if ($LASTEXITCODE -ne 0) { throw "UI Build failed." }
    }
    catch {
        Write-Error $_
        Pop-Location
        exit 1
    }
    Pop-Location
} else {
    Write-Warning "No 'ui' folder found. Skipping UI build."
}

# 2. Build Backend
if (Test-Path "src") {
    Write-Host "Building Backend..." -ForegroundColor Yellow
    Push-Location "src"
    try {
        dotnet build -c Release
        if ($LASTEXITCODE -ne 0) { throw "Backend Build failed." }
    }
    catch {
        Write-Error $_
        Pop-Location
        exit 1
    }
    Pop-Location
} else {
    Write-Error "No 'src' folder found. Cannot build backend."
    exit 1
}

Write-Host "--- Build Complete Successfully ---" -ForegroundColor Green
