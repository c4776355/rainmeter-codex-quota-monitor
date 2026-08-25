[CmdletBinding()]
param(
    [string] $SourceRoot = $PSScriptRoot,
    [string] $OutputFile = (Join-Path $PSScriptRoot 'CodexQuotaOptions_1.5.0.rmskin')
)

$ErrorActionPreference = 'Stop'
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$manifest = Join-Path $SourceRoot 'RMSKIN.ini'
$skins = Join-Path $SourceRoot 'Skins'

if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "Missing package manifest: $manifest"
}
if (-not (Test-Path -LiteralPath $skins -PathType Container)) {
    throw "Missing Skins directory: $skins"
}

$outputDirectory = Split-Path -Parent $OutputFile
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    $outputDirectory = (Get-Location).Path
    $OutputFile = Join-Path $outputDirectory $OutputFile
}
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('codex-quota-options-' + [Guid]::NewGuid().ToString('N'))
$zipPath = [System.IO.Path]::ChangeExtension($OutputFile, '.zip')

try {
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Copy-Item -LiteralPath $manifest -Destination (Join-Path $staging 'RMSKIN.ini')
    Copy-Item -LiteralPath $skins -Destination (Join-Path $staging 'Skins') -Recurse

    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    if (Test-Path -LiteralPath $OutputFile) { Remove-Item -LiteralPath $OutputFile -Force }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $staging,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )
    Move-Item -LiteralPath $zipPath -Destination $OutputFile

    $stream = [System.IO.File]::Open($OutputFile, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write)
    try {
        $writer = New-Object System.IO.BinaryWriter($stream, [System.Text.Encoding]::ASCII, $true)
        try {
            $writer.Write([long] $stream.Length)
            $writer.Write([byte] 0)
            $writer.Write([System.Text.Encoding]::ASCII.GetBytes("RMSKIN`0"))
        }
        finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }

    Get-Item -LiteralPath $OutputFile
}
finally {
    if (Test-Path -LiteralPath $staging) {
        $resolvedTemp = [System.IO.Path]::GetFullPath($staging)
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if ($resolvedTemp.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
        }
    }
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
}
