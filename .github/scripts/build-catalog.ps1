param (
    [string]$RepositoryPath = "USER/REPO"
)

$catalog = @{
    last_updated = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    plugins      = @()
}

$WorkspaceRoot = $PWD.Path
$artifactsDir = Join-Path $WorkspaceRoot "release_artifacts"
New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

$excludedDirs = @("DbTest", "PluginTemplate")

# We find directories that have src\plugin.json, ignoring excluded ones
$plugins = Get-ChildItem -Directory | Where-Object { 
    $_.Name -notin $excludedDirs -and (Test-Path (Join-Path $_.FullName "src\plugin.json"))
}

foreach ($plugin in $plugins) {
    Write-Host "=========================================" -ForegroundColor Cyan
    Write-Host "Building plugin: $($plugin.Name)" -ForegroundColor Cyan
    Write-Host "=========================================" -ForegroundColor Cyan
    
    # 1. Install UI Dependencies
    if (Test-Path (Join-Path $plugin.FullName "ui\package.json")) {
        Write-Host "Installing UI dependencies..." -ForegroundColor Cyan
        Push-Location (Join-Path $plugin.FullName "ui")
        pnpm install --no-frozen-lockfile
        if ($LASTEXITCODE -ne 0) {
            Write-Error "pnpm install failed for $($plugin.Name)"
            exit 1
        }
        Pop-Location
    }

    # 2. Run the specific plugin's build script
    Push-Location $plugin.FullName
    if (Test-Path "build.ps1") {
        & .\build.ps1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build script failed for $($plugin.Name)"
            exit 1
        }
    }
    Pop-Location

    # 3. Read plugin manifest correctly
    $jsonPath = Join-Path $plugin.FullName "src\plugin.json"
    $jsonContent = Get-Content -Raw -Path $jsonPath | ConvertFrom-Json

    # 4. Copy artifact to output folder
    $distDir = Join-Path $plugin.FullName "dist"
    $builtFiles = Get-ChildItem -Path $distDir -Filter "*.pawsplugin" -ErrorAction SilentlyContinue
    
    if ($builtFiles.Count -gt 0) {
        $pluginFile = $builtFiles[0].FullName
        $builtFileName = $builtFiles[0].Name
        Copy-Item -Path $pluginFile -Destination $artifactsDir -Force
        Write-Host "Copied $pluginFile to artifacts." -ForegroundColor Green
        
        $envRepo = $env:GITHUB_REPOSITORY
        if ([string]::IsNullOrWhiteSpace($envRepo)) {
            $envRepo = $RepositoryPath
        }

        $downloadUrl = "https://github.com/$envRepo/releases/download/latest/$builtFileName"

        # Create plugin entry
        $pluginEntry = @{
            id               = $jsonContent.id
            name             = $jsonContent.name
            version          = $jsonContent.version
            min_paws_version = $jsonContent.minAppVersion
            download_url     = $downloadUrl
        }
        
        $catalog.plugins += $pluginEntry
    }
    else {
        Write-Error "Could not find expected *.pawsplugin artifact in $distDir"
    }
}

$catalogJsonPath = Join-Path $artifactsDir "catalog.json"
$catalog | ConvertTo-Json -Depth 5 | Out-File -FilePath $catalogJsonPath -Encoding utf8
Write-Host "Catalog generated successfully at $catalogJsonPath" -ForegroundColor Green
