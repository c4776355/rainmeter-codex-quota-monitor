[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$agent = Join-Path $root 'Skins\CodexQuotaOptions\@Resources\Scripts\CodexQuotaAgent.cs'
$scanner = Join-Path $root 'Skins\CodexQuotaOptions\@Resources\Scripts\SessionEventScanner.cs'
$testSource = Join-Path $PSScriptRoot 'QuotaPoolSelectionTests.cs'
$output = Join-Path ([System.IO.Path]::GetTempPath()) ('CodexQuotaPoolTests-' + [Guid]::NewGuid().ToString('N') + '.exe')

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The .NET Framework C# compiler was not found.'
}

try {
    & $compiler /nologo /target:exe /optimize+ /main:CodexQuota.QuotaPoolSelectionTests /reference:System.Web.Extensions.dll "/out:$output" $agent $scanner $testSource
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output -PathType Leaf)) {
        throw 'Unable to compile quota-pool regression tests.'
    }

    & $output
    if ($LASTEXITCODE -ne 0) {
        throw "Quota-pool regression tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $output -PathType Leaf) {
        [System.IO.File]::Delete($output)
    }
}
