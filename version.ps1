[CmdletBinding(DefaultParameterSetName = 'Show')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Set')][string]$Set,
    [Parameter(Mandatory, ParameterSetName = 'Bump')][ValidateSet('Major', 'Minor', 'Patch')][string]$Bump
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/cgh-version.ps1')

switch ($PSCmdlet.ParameterSetName) {
    'Show' { (Get-CghVersion $PSScriptRoot).ToString(); return }
    'Set' { $next = ConvertTo-CghVersion $Set }
    'Bump' {
        $current = Get-CghVersion $PSScriptRoot
        $value = switch ($Bump) {
            'Major' { "$($current.Major + 1).0.0" }
            'Minor' { "$($current.Major).$($current.Minor + 1).0" }
            'Patch' { "$($current.Major).$($current.Minor).$($current.Build + 1)" }
        }
        $next = ConvertTo-CghVersion $value
    }
}
Write-CghText (Join-Path $PSScriptRoot 'VERSION') $next.ToString()
$next.ToString()
