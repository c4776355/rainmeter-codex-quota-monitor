[CmdletBinding()]
param(
    [string] $ScriptsDirectory = (Join-Path $PSScriptRoot 'Skins\CodexQuotaOptions\@Resources\Scripts')
)

$ErrorActionPreference = 'Stop'
$ScriptsDirectory = (Resolve-Path -LiteralPath $ScriptsDirectory).Path
$agentSource = Join-Path $ScriptsDirectory 'CodexQuotaAgent.cs'
$scannerSource = Join-Path $ScriptsDirectory 'SessionEventScanner.cs'
$output = Join-Path $ScriptsDirectory 'CodexQuotaAgent.exe'

foreach ($required in @($agentSource, $scannerSource)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Missing build input: $required"
    }
}

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The .NET Framework C# compiler was not found.'
}

& $compiler /nologo /target:winexe /optimize+ /reference:System.Web.Extensions.dll "/out:$output" $agentSource $scannerSource
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output -PathType Leaf)) {
    throw 'Unable to compile CodexQuotaAgent.exe.'
}

Get-Item -LiteralPath $output
