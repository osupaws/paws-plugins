Write-Host "Building Backend for DbTestPlugin..." -ForegroundColor Cyan
Push-Location "src"
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Backend Build failed!"
    Pop-Location
    exit 1
}
Pop-Location

Write-Host "DbTest Build Complete!" -ForegroundColor Green
