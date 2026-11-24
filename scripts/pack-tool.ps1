param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'build-ui.ps1') -Configuration $Configuration -SkipNpmInstall:$SkipNpmInstall

Write-Host "==> Running all .NET test suites..." -ForegroundColor Cyan
dotnet test (Join-Path $repoRoot 'Mma.Cli.Shared.Tests\Mma.Cli.Shared.Tests.csproj') `
    -c $Configuration --disable-build-servers -p:UseSharedCompilation=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "Mma.Cli.Shared.Tests failed" }

dotnet test (Join-Path $repoRoot 'Mma.Cli.Bff.Tests\Mma.Cli.Bff.Tests.csproj') `
    -c $Configuration --disable-build-servers -p:UseSharedCompilation=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "Mma.Cli.Bff.Tests failed" }

Write-Host "==> Packing mma-cli global tool..." -ForegroundColor Cyan
dotnet pack (Join-Path $repoRoot 'Mma.Cli.AppV4\Mma.Cli.AppV4.csproj') `
    -c $Configuration `
    --disable-build-servers `
    -p:UseSharedCompilation=false `
    -p:NuGetAudit=false `
    -p:GeneratePackageOnBuild=false

if ($LASTEXITCODE -ne 0) { throw "dotnet pack Mma.Cli.AppV4 failed" }

$nupkg = Get-ChildItem (Join-Path $repoRoot 'Mma.Cli.AppV4\nupkg\*.nupkg') |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Write-Host "Packed global tool: $($nupkg.FullName)" -ForegroundColor Green
