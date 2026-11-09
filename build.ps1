[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
. (Join-Path $repoRoot 'scripts/cgh-version.ps1')
. (Join-Path $repoRoot 'scripts/cgh-distribution.ps1')
$version = Get-CghVersion $repoRoot
Push-Location -LiteralPath $repoRoot
try {
    & cmake -S Cpp -B Cpp/build -G 'Visual Studio 17 2022' -A x64
    if ($LASTEXITCODE -ne 0) { throw 'CMake configuration failed' }
    & cmake --build Cpp/build --config $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    & dotnet build CSharp/ConsGHost.sln -c $Configuration -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed' }
    if (!$BuildOnly) {
        $outputRoot = Join-Path $repoRoot 'artifacts/CGH'
        Copy-CghDistribution $repoRoot $outputRoot $Configuration $version
        Write-Host "CGH $version ready at $outputRoot/ConsGHost.Host.exe"
    }
} finally { Pop-Location }
