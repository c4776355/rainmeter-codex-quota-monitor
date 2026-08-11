[CmdletBinding()]
param(
    [string] $Version = '1.4.1',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

& (Join-Path $PSScriptRoot 'Build-Agent.ps1') | Out-Null

$packageName = "CodexQuotaOptions_$Version.rmskin"
$package = Join-Path $OutputDirectory $packageName
& (Join-Path $PSScriptRoot 'Build-Rmskin.ps1') -SourceRoot $PSScriptRoot -OutputFile $package | Out-Null

$hash = Get-FileHash -LiteralPath $package -Algorithm SHA256
$checksum = '{0} *{1}' -f $hash.Hash.ToLowerInvariant(), $packageName
$checksum | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding Ascii

Get-Item -LiteralPath $package, (Join-Path $OutputDirectory 'SHA256SUMS.txt')
