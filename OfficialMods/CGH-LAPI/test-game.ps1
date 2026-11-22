[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [string]$CghDistribution = (Join-Path $PSScriptRoot '../../artifacts/CGH'),
    [string]$PackageDirectory,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'out/smoke')
)
$ErrorActionPreference = 'Stop'
if (!$PackageDirectory) {
    $metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'cghm_metadata.json') -Raw | ConvertFrom-Json
    $PackageDirectory = Join-Path $PSScriptRoot "out/package-$($metadata.version)"
}
if (Get-Process -Name 'gmod', 'gmod_win64' -ErrorAction SilentlyContinue |
    Where-Object { !$_.WaitForExit(0) }) {
    throw 'Close the existing GMod session before running the smoke test.'
}

$CghDistribution = (Resolve-Path -LiteralPath $CghDistribution).Path
$GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $GameDirectory 'gmod_win64.exe'))) {
    throw "GMod x64 executable missing in $GameDirectory"
}

$testDirectory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([guid]::NewGuid().ToString('N'))
$distribution = Join-Path $testDirectory 'CGH'
New-Item -ItemType Directory -Path "$distribution/Mods" -Force | Out-Null
Get-ChildItem -LiteralPath $CghDistribution -File | Copy-Item -Destination $distribution
Copy-Item -LiteralPath "$PackageDirectory/Mods/CGH-LAPI" -Destination "$distribution/Mods" -Recurse

foreach ($project in 'Probe', 'Runner') {
    & dotnet build "$PSScriptRoot/tests/$project/$project.csproj" -c Release --artifacts-path "$testDirectory/build" "-p:CghDistribution=$CghDistribution" -m:1 -nr:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "Could not build $project" }
}

$probe = "$testDirectory/build/bin/Probe/release/LapiProbe.dll"
foreach ($isolated in $false, $true) {
    $id = if ($isolated) { 'cgh.lapi.test.isolated' } else { 'cgh.lapi.test' }
    $mod = "$distribution/Mods/$id"
    New-Item -ItemType Directory -Path "$mod/Managed" -Force | Out-Null
    Copy-Item -LiteralPath $probe -Destination "$mod/Managed"
    @{
        schemaVersion = 1; id = $id; name = $id; version = '1.0.0'
        loadAsSubprocess = $isolated; dependencies = @(@{ id = 'cgh.lapi' })
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$mod/cghm_metadata.json" -Encoding utf8
}

$testAddon = "$distribution/Mods/cgh.lapi.test/Addons/cgh_lapi_test/lua/autorun"
New-Item -ItemType Directory -Path $testAddon -Force | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/tests/cgh_lapi_test.lua" -Destination $testAddon

& dotnet "$testDirectory/build/bin/Runner/release/LapiRunner.dll" $distribution $GameDirectory
if ($LASTEXITCODE -ne 0) { throw "CGH-LAPI smoke test failed. Logs are under $distribution/Sessions." }
