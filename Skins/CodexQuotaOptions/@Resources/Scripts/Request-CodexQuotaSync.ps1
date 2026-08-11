[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SyncTokenPath
)

$ErrorActionPreference = 'Stop'
$directory = Split-Path -Parent $SyncTokenPath
if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

$token = 'SYNC={0}|{1}' -f [Guid]::NewGuid().ToString('N'), [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
[System.IO.File]::WriteAllText($SyncTokenPath, $token, (New-Object System.Text.UTF8Encoding($false)))
