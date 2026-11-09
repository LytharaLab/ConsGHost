[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$BuildDirectory = (Join-Path $PSScriptRoot 'out/build'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'out/package'),
    [string]$SdkDirectory = (Join-Path $PSScriptRoot '../../Cpp/include')
)
$ErrorActionPreference = 'Stop'

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
