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
            Get-ChildItem -LiteralPath "CSharp/$project/bin/$Configuration/net10.0-windows" -File |
                Where-Object Extension -ne '.pdb' |
                Copy-Item -Destination $Destination -Force
        }
        $hostOutput = "CSharp/ConsGHost.Host/bin/$Configuration/net10.0-windows"
        foreach ($culture in (Get-ChildItem -LiteralPath $hostOutput -Directory)) {
            $satellite = Join-Path $culture.FullName 'ConsGHost.Host.resources.dll'
            if (Test-Path -LiteralPath $satellite -PathType Leaf) {
                $localeFolder = Join-Path $Destination $culture.Name
                New-Item -ItemType Directory -Path $localeFolder -Force | Out-Null
                Copy-Item -LiteralPath $satellite -Destination $localeFolder -Force
            }
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
            Copy-Item -Path "CSharp/Examples/$name/bin/$Configuration/net10.0-windows/$name.*" -Exclude '*.pdb' -Destination "$modRoot/Managed" -Force
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
        $dotnetRoot = Split-Path -Parent (Get-Command dotnet -CommandType Application).Source
        Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination "$Destination/licenses/dotnet-LICENSE.txt" -Force
        Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination "$Destination/licenses/dotnet-ThirdPartyNotices.txt" -Force
        Copy-Item -LiteralPath 'README.md', 'CHANGELOG.md', 'LICENSE', 'NOTICE.md', 'SECURITY.md' -Destination $Destination -Force
        $publicDocs = @(
            'architecture.md', 'cghm_metadata.schema.json', 'mod-development.md', 'verification.md',
            'readme/README.zh-CN.md', 'readme/README.ru.md', 'readme/README.es.md', 'readme/README.fr.md'
        )
        foreach ($doc in $publicDocs) {
            $target = Join-Path $Destination "docs/$doc"
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath "docs/$doc" -Destination $target -Force
        }

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
