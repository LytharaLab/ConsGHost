[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    & cmake -S Cpp -B Cpp/build -G 'Visual Studio 17 2022' -A x64
    if ($LASTEXITCODE -ne 0) { throw 'CMake configuration failed' }
    & cmake --build Cpp/build --config $Configuration --parallel
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    & dotnet build CSharp/ConsGHost.sln -c $Configuration -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed' }
    $outputRoot = Join-Path $repoRoot 'artifacts/CGH'
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    foreach ($project in @('ConsGHost.Host', 'ConsGHost.Runtime', 'ConsGHost.Bootstrap')) {
        Copy-Item -Path "CSharp/$project/bin/$Configuration/net10.0-windows/*" -Destination $outputRoot -Force
    }
    foreach ($name in @('cgh_mrt_win64.exe', 'cgh_runtime_win64.dll')) {
        Copy-Item -LiteralPath "Cpp/build/$Configuration/$name" -Destination $outputRoot -Force
    }
    foreach ($name in @('BridgeDemo', 'Observer')) {
        $modRoot = Join-Path $outputRoot "Mods/$name"
        New-Item -ItemType Directory -Path "$modRoot/Managed", "$modRoot/Native", "$modRoot/Resources" -Force | Out-Null
        # Preserve edited user metadata and resources when rebuilding.
        if (!(Test-Path -LiteralPath "$modRoot/cghm_metadata.json")) {
            Copy-Item -LiteralPath "examples/Mods/$name/cghm_metadata.json" -Destination $modRoot
        }
        Copy-Item -Path "CSharp/Examples/$name/bin/$Configuration/net10.0-windows/$name.*" -Destination "$modRoot/Managed" -Force
    }
    Copy-Item -LiteralPath "Cpp/build/$Configuration/cgh_example_native.dll" -Destination "$outputRoot/Mods/BridgeDemo/Native" -Force
    Copy-Item -LiteralPath 'examples/Mods/BridgeDemo/Resources/readme.txt' -Destination "$outputRoot/Mods/BridgeDemo/Resources" -Force
    New-Item -ItemType Directory -Path "$outputRoot/SDK", "$outputRoot/licenses" -Force | Out-Null
    Copy-Item -LiteralPath 'Cpp/include/cgh_sdk.h' -Destination "$outputRoot/SDK" -Force
    Copy-Item -LiteralPath "CSharp/ConsGHost.SDK/bin/$Configuration/net10.0-windows/ConsGHost.SDK.dll" -Destination "$outputRoot/SDK" -Force
    Copy-Item -LiteralPath 'Cpp/third_party/minhook-1.3.4/LICENSE.txt' -Destination "$outputRoot/licenses/MinHook.txt" -Force
    Copy-Item -LiteralPath 'README.md' -Destination $outputRoot -Force
    Copy-Item -LiteralPath 'docs' -Destination $outputRoot -Recurse -Force
    Write-Host "CGH ready: $outputRoot/ConsGHost.Host.exe"
} finally { Pop-Location }
