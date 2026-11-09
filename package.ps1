[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/cgh-version.ps1')
. (Join-Path $PSScriptRoot 'scripts/cgh-distribution.ps1')

$version = Get-CghVersion $PSScriptRoot
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -BuildOnly }
$output = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$output = (Resolve-Path -LiteralPath $output).ProviderPath
$archiveName = "CGH-$version-win-x64.zip"
$archivePath = Join-Path $output $archiveName
$stage = Join-Path $output ('.package-' + [guid]::NewGuid().ToString('N'))
$temporaryArchive = "$stage.zip"
try {
    Copy-CghDistribution $PSScriptRoot $stage 'Release' $version
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryArchive, 'Optimal', $false)
    $hash = (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Move-Item -LiteralPath $temporaryArchive -Destination $archivePath -Force
    Write-CghText "$archivePath.sha256" "$hash  $archiveName"
    Get-Item -LiteralPath $archivePath, "$archivePath.sha256"
} finally {
    if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive -Force }
    if (Test-Path -LiteralPath $stage) {
        $resolvedStage = (Resolve-Path -LiteralPath $stage).ProviderPath
        if (!$resolvedStage.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Package staging path is outside artifacts: $resolvedStage"
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
