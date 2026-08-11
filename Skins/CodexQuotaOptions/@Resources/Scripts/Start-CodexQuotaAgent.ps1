[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AgentPath,
    [Parameter(Mandatory)] [string] $StatePath,
    [Parameter(Mandatory)] [string] $StopTokenPath,
    [Parameter(Mandatory)] [string] $SyncTokenPath,
    [ValidateRange(10, 300)] [int] $ActiveIntervalSeconds = 20,
    [ValidateRange(3000, 60000)] [int] $TimeoutMs = 12000
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $AgentPath -PathType Leaf)) {
    throw "Dynamic quota agent not found: $AgentPath"
}

$agentDirectory = Split-Path -Parent $AgentPath
$scannerSource = Join-Path $agentDirectory 'SessionEventScanner.cs'
$agentExecutable = Join-Path $agentDirectory 'CodexQuotaAgent.exe'
if (-not (Test-Path -LiteralPath $scannerSource -PathType Leaf)) {
    throw "Session event scanner source not found: $scannerSource"
}

$requiresBuild = -not (Test-Path -LiteralPath $agentExecutable -PathType Leaf)
if (-not $requiresBuild) {
    $executableTime = (Get-Item -LiteralPath $agentExecutable).LastWriteTimeUtc
    $requiresBuild = (Get-Item -LiteralPath $AgentPath).LastWriteTimeUtc -gt $executableTime -or
                     (Get-Item -LiteralPath $scannerSource).LastWriteTimeUtc -gt $executableTime
}

if ($requiresBuild) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
        $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
        throw 'The .NET Framework C# compiler required for the lightweight quota agent was not found.'
    }

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if ($null -eq (Get-Process CodexQuotaAgent -ErrorAction SilentlyContinue)) {
            break
        }
        Start-Sleep -Milliseconds 250
    }

    & $compiler /nologo /target:winexe /optimize+ /reference:System.Web.Extensions.dll "/out:$agentExecutable" $AgentPath $scannerSource
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $agentExecutable -PathType Leaf)) {
        throw 'Unable to compile the lightweight native quota agent.'
    }
}

$rainmeterPid = 0
try {
    $self = Get-CimInstance Win32_Process -Filter "ProcessId=$PID"
    if ($null -ne $self) {
        $parent = Get-Process -Id $self.ParentProcessId -ErrorAction SilentlyContinue
        if ($null -ne $parent -and $parent.ProcessName -eq 'Rainmeter') {
            $rainmeterPid = $parent.Id
        }
    }
}
catch {
    # Fall back to the newest Rainmeter process below.
}

if ($rainmeterPid -eq 0) {
    $rainmeter = Get-Process Rainmeter -ErrorAction SilentlyContinue |
        Sort-Object StartTime -Descending |
        Select-Object -First 1
    if ($null -ne $rainmeter) {
        $rainmeterPid = $rainmeter.Id
    }
}

function Quote-Argument {
    param([string] $Value)
    return '"{0}"' -f ($Value -replace '"', '\"')
}

$arguments = @(
    '--state'
    (Quote-Argument $StatePath)
    '--stop'
    (Quote-Argument $StopTokenPath)
    '--sync'
    (Quote-Argument $SyncTokenPath)
    '--interval'
    [string] $ActiveIntervalSeconds
    '--timeout'
    [string] $TimeoutMs
    '--rainmeter-pid'
    [string] $rainmeterPid
) -join ' '

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $agentExecutable
$startInfo.Arguments = $arguments
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden

$process = [System.Diagnostics.Process]::Start($startInfo)
if ($null -ne $process) {
    $process.Dispose()
}
