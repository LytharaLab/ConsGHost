function Copy-CghDistribution {
    param(
        [string]$Repository,
        [string]$Destination,
        [string]$Configuration,
        [version]$Version
    )
    Push-Location -LiteralPath $Repository
    try {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
        foreach ($project in @('ConsGHost.Host', 'ConsGHost.Runtime', 'ConsGHost.Bootstrap')) {
            Copy-Item -Path "CSharp/$project/bin/$Configuration/net10.0-windows/*" -Destination $Destination -Force
        }
        foreach ($name in @('cgh_mrt_win64.exe', 'cgh_runtime_win64.dll')) {
            Copy-Item -LiteralPath "Cpp/build/$Configuration/$name" -Destination $Destination -Force
        }
        foreach ($name in @('BridgeDemo', 'Observer')) {
            $modRoot = Join-Path $Destination "Mods/$name"
            New-Item -ItemType Directory -Path "$modRoot/Managed", "$modRoot/Native", "$modRoot/Resources" -Force | Out-Null
            # Preserve edited user metadata and resources in an existing distribution.
            if (!(Test-Path -LiteralPath "$modRoot/cghm_metadata.json")) {
                Copy-Item -LiteralPath "examples/Mods/$name/cghm_metadata.json" -Destination $modRoot
            }
            $defaultConfig = "examples/Mods/$name/config.default.json"
            if (Test-Path -LiteralPath $defaultConfig) {
                Copy-Item -LiteralPath $defaultConfig -Destination $modRoot -Force
            }
            Copy-Item -Path "CSharp/Examples/$name/bin/$Configuration/net10.0-windows/$name.*" -Destination "$modRoot/Managed" -Force
        }
        Copy-Item -LiteralPath "Cpp/build/$Configuration/cgh_example_native.dll" -Destination "$Destination/Mods/BridgeDemo/Native" -Force
        $resource = "$Destination/Mods/BridgeDemo/Resources/readme.txt"
        if (!(Test-Path -LiteralPath $resource)) {
            Copy-Item -LiteralPath 'examples/Mods/BridgeDemo/Resources/readme.txt' -Destination $resource
        }
        New-Item -ItemType Directory -Path "$Destination/SDK", "$Destination/licenses" -Force | Out-Null
        Copy-Item -LiteralPath 'Cpp/include/cgh_sdk.h' -Destination "$Destination/SDK" -Force
        Copy-Item -LiteralPath "$Destination/ConsGHost.SDK.dll" -Destination "$Destination/SDK" -Force
        Copy-Item -LiteralPath 'Cpp/third_party/minhook-1.3.4/LICENSE.txt' -Destination "$Destination/licenses/MinHook.txt" -Force
        Copy-Item -LiteralPath 'README.md', 'CHANGELOG.md', 'LICENSE' -Destination $Destination -Force
        Copy-Item -LiteralPath 'docs' -Destination $Destination -Recurse -Force

        $runtime = Join-Path $Destination 'ConsGHost.Runtime.exe'
        $expected = $Version.ToString()
        $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($runtime).ProductVersion
        if ($actual -cne $expected) {
            throw "CGH Runtime product version '$actual' does not match VERSION ($expected). Rebuild CGH first."
        }
        $verified = & $runtime --verify-version $Destination
        if ($LASTEXITCODE -ne 0 -or $verified -cne $expected) {
            throw "Could not verify CGH $expected distribution. Rebuild CGH first."
        }
        $manifest = [ordered]@{ version = $expected; platform = 'win-x64'; configuration = $Configuration }
        Write-CghText (Join-Path $Destination 'version.json') ($manifest | ConvertTo-Json)
    } finally { Pop-Location }
}
