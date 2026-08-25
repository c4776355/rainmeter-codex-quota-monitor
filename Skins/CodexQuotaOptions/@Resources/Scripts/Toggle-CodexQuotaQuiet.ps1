[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $StatePath,

    [Parameter(Mandatory)]
    [string] $QuietTokenPath
)

$ErrorActionPreference = 'Stop'

$state = @{}
if (Test-Path -LiteralPath $StatePath -PathType Leaf) {
    foreach ($line in [System.IO.File]::ReadAllLines($StatePath, (New-Object System.Text.UTF8Encoding($false)))) {
        $separator = $line.IndexOf('=')
        if ($separator -gt 0) {
            $state[$line.Substring(0, $separator)] = $line.Substring($separator + 1)
        }
    }
}

$activeCount = 0
[void] [int]::TryParse([string] $state['ActiveCount'], [ref] $activeCount)
if ($activeCount -le 0) {
    return
}

$enable = if ([string] $state['ManualSilent'] -eq '1') { 0 } else { 1 }
$directory = Split-Path -Parent $QuietTokenPath
if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

$token = 'QUIET={0}|{1}|{2}' -f $enable, [Guid]::NewGuid().ToString('N'), [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
[System.IO.File]::WriteAllText($QuietTokenPath, $token, (New-Object System.Text.UTF8Encoding($false)))
