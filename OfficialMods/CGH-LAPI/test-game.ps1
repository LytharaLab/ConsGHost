[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [string]$CghDistribution = (Join-Path $PSScriptRoot '../../artifacts/CGH'),
    [string]$PackageDirectory = (Join-Path $PSScriptRoot 'out/package'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'out/smoke')
)
$ErrorActionPreference = 'Stop'
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

$files = [ordered]@{
    'garrysmod/lua/bin/gmsv_cgh_lapi_win64.dll' = "$PackageDirectory/GMod/garrysmod/lua/bin/gmsv_cgh_lapi_win64.dll"
    'garrysmod/lua/bin/gmcl_cgh_lapi_win64.dll' = "$PackageDirectory/GMod/garrysmod/lua/bin/gmcl_cgh_lapi_win64.dll"
    'garrysmod/lua/autorun/cgh_lapi.lua' = "$PackageDirectory/GMod/garrysmod/lua/autorun/cgh_lapi.lua"
    'garrysmod/lua/autorun/cgh_lapi_test.lua' = "$PSScriptRoot/tests/cgh_lapi_test.lua"
}

foreach ($relative in $files.Keys) {
    $target = Join-Path $GameDirectory $relative
    if (Test-Path -LiteralPath $target) { throw "The smoke test requires an unoccupied file path: $target" }
    if (!(Test-Path -LiteralPath $files[$relative] -PathType Leaf)) { throw "Missing test input: $($files[$relative])" }
    if (!(Test-Path -LiteralPath (Split-Path $target -Parent) -PathType Container)) { throw "Missing GMod Lua directory for $target" }
}

$installed = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($relative in $files.Keys) {
        $target = Join-Path $GameDirectory $relative
        [IO.File]::Copy($files[$relative], $target, $false)
        $installed.Add($target)
    }

    & dotnet "$testDirectory/build/bin/Runner/release/LapiRunner.dll" $distribution $GameDirectory
    if ($LASTEXITCODE -ne 0) { throw "CGH-LAPI smoke test failed. Logs are under $distribution/Sessions." }
} finally {
    $cleanupErrors = @()
    foreach ($file in $installed) {
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            if (!(Test-Path -LiteralPath $file)) { break }
            try {
                Remove-Item -LiteralPath $file -ErrorAction Stop
                break
            } catch {
                if ($attempt -eq 19) { $cleanupErrors += "$file ($($_.Exception.Message))" }
                else { Start-Sleep -Milliseconds 250 }
            }
        }
    }
    if ($cleanupErrors.Count) { throw "Could not remove smoke-test files: $($cleanupErrors -join ', ')" }
}
