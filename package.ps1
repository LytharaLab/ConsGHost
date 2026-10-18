[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    if (!$SkipBuild) { & ./build.ps1 }
    $distributionRoot = (Resolve-Path -LiteralPath 'artifacts/CGH').Path
    $archivePath = Join-Path $PSScriptRoot 'artifacts/CGH-0.1.0-win-x64.zip'
    $fileList = Get-ChildItem -LiteralPath $distributionRoot -Recurse -File | Where-Object {
        $relativePath = [IO.Path]::GetRelativePath($distributionRoot, $_.FullName)
        $relativePath -notmatch '^(Sessions|Data)[\\/]' -and $relativePath -ne 'cgh_settings.json'
    } | Sort-Object FullName
    Add-Type -AssemblyName System.IO.Compression
    $archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::Create)
    $archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $fileList) {
            $entryName = [IO.Path]::GetRelativePath($distributionRoot, $file.FullName).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose(); $archiveStream.Dispose() }
    Get-FileHash -LiteralPath $archivePath -Algorithm SHA256 | Format-List
} finally { Pop-Location }
