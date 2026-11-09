function ConvertTo-CghVersion {
    param([string]$Value)
    if ($Value -cnotmatch '\A(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\z') {
        throw "Invalid CGH version '$Value'. Use major.minor.patch without leading zeros."
    }
    $version = [version]$Value
    if ($version.Major -gt 65534 -or $version.Minor -gt 65534 -or $version.Build -gt 65534) {
        throw "Invalid CGH version '$Value'. Each component must be at most 65534."
    }
    $version
}

function Get-CghVersion {
    param([string]$Repository)
    ConvertTo-CghVersion ([IO.File]::ReadAllText((Join-Path $Repository 'VERSION')).Trim())
}

function Write-CghText {
    param([string]$Path, [string]$Text)
    $temporary = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporary, $Text + "`n")
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
