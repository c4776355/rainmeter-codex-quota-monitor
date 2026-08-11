[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $StopTokenPath
)

$ErrorActionPreference = 'SilentlyContinue'
$directory = Split-Path -Parent $StopTokenPath
if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

$token = 'STOP={0}|{1}' -f [Guid]::NewGuid().ToString('N'), [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
[System.IO.File]::WriteAllText($StopTokenPath, $token, (New-Object System.Text.UTF8Encoding($false)))
