[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $StatePath,

    [Parameter(Mandatory)]
    [string] $PoolTokenPath
)

$ErrorActionPreference = 'Stop'

$current = 'FIVE_HOUR'
if (Test-Path -LiteralPath $StatePath -PathType Leaf) {
    foreach ($line in [System.IO.File]::ReadAllLines($StatePath, (New-Object System.Text.UTF8Encoding($false)))) {
        if ($line.StartsWith('PoolMode=', [System.StringComparison]::OrdinalIgnoreCase)) {
            $value = $line.Substring('PoolMode='.Length).Trim()
            if ($value -eq 'WEEKLY') {
                $current = 'WEEKLY'
            }
            break
        }
    }
}

$next = if ($current -eq 'WEEKLY') { 'FIVE_HOUR' } else { 'WEEKLY' }
$directory = Split-Path -Parent $PoolTokenPath
if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

$token = 'POOL={0}|{1}|{2}' -f $next, [Guid]::NewGuid().ToString('N'), [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
[System.IO.File]::WriteAllText($PoolTokenPath, $token, (New-Object System.Text.UTF8Encoding($false)))
