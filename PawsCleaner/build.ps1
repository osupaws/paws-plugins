Write-Host "Building UI for PawsCleaner..." -ForegroundColor Cyan
Push-Location "ui"
pnpm build
if ($LASTEXITCODE -ne 0) {
    Write-Error "UI Build failed!"
    Pop-Location
    exit 1
}
Pop-Location

Write-Host "Building Backend for PawsCleaner..." -ForegroundColor Cyan
Push-Location "src"
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Backend Build failed!"
    Pop-Location
    exit 1
}
Pop-Location

Write-Host "PawsCleaner Build Complete!" -ForegroundColor Green
