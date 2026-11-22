[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$BuildDirectory = (Join-Path $PSScriptRoot 'out/build'),
    [string]$OutputDirectory,
    [string]$SdkDirectory = (Join-Path $PSScriptRoot '../../Cpp/include')
)
$ErrorActionPreference = 'Stop'
$metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'cghm_metadata.json') -Raw | ConvertFrom-Json
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "out/package-$($metadata.version)" }
if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'GMod')) {
    throw 'The output directory contains a legacy CGH-LAPI package. Pass a fresh output directory.'
}

function Invoke-CheckedCMake {
    & cmake @args
    if ($LASTEXITCODE -ne 0) { throw "CMake failed with exit code $LASTEXITCODE." }
}

Invoke-CheckedCMake -S $PSScriptRoot -B $BuildDirectory -G 'Visual Studio 17 2022' -A x64 "-DCGH_SDK_DIR=$SdkDirectory"
Invoke-CheckedCMake --build $BuildDirectory --config $Configuration --parallel 1
& ctest --test-dir $BuildDirectory -C $Configuration --output-on-failure
if ($LASTEXITCODE -ne 0) { throw 'CGH-LAPI native tests failed.' }
Invoke-CheckedCMake --install $BuildDirectory --config $Configuration --prefix $OutputDirectory
Write-Host "CGH-LAPI package: $OutputDirectory"
