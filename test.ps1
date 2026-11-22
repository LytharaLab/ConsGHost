[CmdletBinding()]
param([switch]$RealGame, [switch]$SkipBuild, [string]$GameDirectory)
$ErrorActionPreference = 'Stop'
if ($RealGame) {
    if ([string]::IsNullOrWhiteSpace($GameDirectory)) { throw 'Pass -GameDirectory with the path to the Garry''s Mod installation.' }
    $GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).ProviderPath
    if (!(Test-Path -LiteralPath (Join-Path $GameDirectory 'gmod_win64.exe') -PathType Leaf)) {
        throw 'GameDirectory must contain gmod_win64.exe.'
    }
}
Push-Location -LiteralPath $PSScriptRoot
try {
    if (!$SkipBuild) { & ./build.ps1 }
    & ctest --test-dir Cpp/build -C Release --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed' }
    $testArgs = @($PSScriptRoot)
    if ($RealGame) { $testArgs += @('--real-game', $GameDirectory) }
    & dotnet CSharp/ConsGHost.Tests/bin/Release/net10.0-windows/ConsGHost.Tests.dll @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'CGH acceptance tests failed' }
    $uiOutput = Join-Path $PSScriptRoot 'artifacts/ui-check'
    $uiProcess = Start-Process -FilePath (Join-Path $PSScriptRoot 'artifacts/CGH/ConsGHost.Host.exe') -ArgumentList @('--ui-check', ('"' + $uiOutput + '"')) -PassThru -Wait -WindowStyle Hidden
    if ($uiProcess.ExitCode -ne 0) { throw "Host UI check failed; see $uiOutput/failure.txt" }
    Get-Content -LiteralPath (Join-Path $uiOutput 'result.txt')
} finally { Pop-Location }
