param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$webDir = Join-Path $repoRoot 'Mma.Cli.Web'
$bffProj = Join-Path $repoRoot 'Mma.Cli.Bff\Mma.Cli.Bff.csproj'
$uiOutDir = Join-Path $repoRoot 'Mma.Cli.AppV4\UI'

Write-Host "==> Building Mma.Cli.Web (React + Vite)..." -ForegroundColor Cyan
if (-not $SkipNpmInstall) {
    npm ci --prefix $webDir
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
}

npm run lint --prefix $webDir
if ($LASTEXITCODE -ne 0) { throw "npm run lint failed" }

npm run build --prefix $webDir
if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }

Write-Host "==> Publishing Mma.Cli.Bff ($Configuration) -> $uiOutDir ..." -ForegroundColor Cyan
if (Test-Path $uiOutDir) {
    Remove-Item -Recurse -Force $uiOutDir
}

dotnet publish $bffProj `
    -c $Configuration `
    -o $uiOutDir `
    --disable-build-servers `
    -p:UseSharedCompilation=false `
    -p:NuGetAudit=false

if ($LASTEXITCODE -ne 0) { throw "dotnet publish Mma.Cli.Bff failed" }

if (-not (Test-Path (Join-Path $uiOutDir 'Mma.Cli.Bff.dll'))) {
    throw "Expected Mma.Cli.Bff.dll in $uiOutDir"
}
if (-not (Test-Path (Join-Path $uiOutDir 'wwwroot\index.html'))) {
    throw "Expected wwwroot\index.html in $uiOutDir"
}

Write-Host "UI bundle ready at $uiOutDir" -ForegroundColor Green
