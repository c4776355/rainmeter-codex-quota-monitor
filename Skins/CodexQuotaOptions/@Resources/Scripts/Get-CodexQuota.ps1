[CmdletBinding()]
param(
    [ValidateRange(3000, 60000)]
    [int] $TimeoutMs = 12000,

    [string] $CodexPath
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

function ConvertTo-SafeField {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) {
        return '--'
    }

    $text = [string] $Value
    $text = $text -replace '[\r\n|]+', ' '
    $text = $text.Trim()
    if ($text.Length -gt 96) {
        $text = $text.Substring(0, 96)
    }
    if ([string]::IsNullOrWhiteSpace($text)) {
        return '--'
    }
    return $text
}

function Write-QuotaPayload {
    param([object[]] $Fields)

    $safe = foreach ($field in $Fields) {
        ConvertTo-SafeField $field
    }
    [Console]::Out.WriteLine(($safe -join '|'))
}

function Get-WindowLabel {
    param([AllowNull()] [long] $Minutes)

    if ($null -eq $Minutes -or $Minutes -le 0) {
        return 'RATE WINDOW'
    }
    if (($Minutes % 10080) -eq 0) {
        return ('{0}W WINDOW' -f [int] ($Minutes / 10080))
    }
    if (($Minutes % 1440) -eq 0) {
        return ('{0}D WINDOW' -f [int] ($Minutes / 1440))
    }
    if (($Minutes % 60) -eq 0) {
        return ('{0}H WINDOW' -f [int] ($Minutes / 60))
    }
    return ('{0}M WINDOW' -f $Minutes)
}

function Get-CountdownLabel {
    param([AllowNull()] $ResetAt)

    if ($null -eq $ResetAt) {
        return '--'
    }

    $remaining = $ResetAt - [DateTimeOffset]::Now
    if ($remaining.TotalSeconds -le 0) {
        return 'RESETTING'
    }
    if ($remaining.TotalDays -ge 1) {
        return ('{0}D {1}H' -f [int] [Math]::Floor($remaining.TotalDays), $remaining.Hours)
    }
    if ($remaining.TotalHours -ge 1) {
        return ('{0}H {1}M' -f [int] [Math]::Floor($remaining.TotalHours), $remaining.Minutes)
    }
    return ('{0}M' -f [Math]::Max(1, [int] [Math]::Ceiling($remaining.TotalMinutes)))
}

function ConvertTo-WindowInfo {
    param(
        [Parameter(Mandatory)] [object] $Window,
        [Parameter(Mandatory)] [string] $Name
    )

    $used = [Math]::Max(0, [Math]::Min(100, [int] $Window.usedPercent))
    $reset = $null
    if ($null -ne $Window.resetsAt) {
        $reset = [DateTimeOffset]::FromUnixTimeSeconds([long] $Window.resetsAt).ToLocalTime()
    }

    [pscustomobject]@{
        Name      = $Name
        Used      = $used
        Remaining = 100 - $used
        Duration  = if ($null -eq $Window.windowDurationMins) { $null } else { [long] $Window.windowDurationMins }
        ResetAt   = $reset
    }
}

$process = $null

try {
    if ([string]::IsNullOrWhiteSpace($CodexPath)) {
        $command = Get-Command codex -CommandType Application -ErrorAction Stop | Select-Object -First 1
        $CodexPath = $command.Source
    }
    if (-not (Test-Path -LiteralPath $CodexPath -PathType Leaf)) {
        throw "Codex executable not found: $CodexPath"
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $CodexPath
    $startInfo.Arguments = 'app-server'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $startInfo.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Unable to start Codex App Server.'
    }

    $stderrTask = $process.StandardError.ReadToEndAsync()
    $deadline = [DateTimeOffset]::UtcNow.AddMilliseconds($TimeoutMs)

    $initialize = @{
        method = 'initialize'
        id = 0
        params = @{
            clientInfo = @{
                name = 'rainmeter_quota_monitor'
                title = 'Rainmeter Codex Quota Monitor'
                version = '1.1.0'
            }
        }
    } | ConvertTo-Json -Compress -Depth 8

    $process.StandardInput.WriteLine($initialize)
    $process.StandardInput.Flush()

    $result = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $remainingMs = [Math]::Max(1, [int] ($deadline - [DateTimeOffset]::UtcNow).TotalMilliseconds)
        $readTask = $process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait($remainingMs)) {
            throw "Codex App Server timed out after $TimeoutMs ms."
        }

        $line = $readTask.Result
        if ($null -eq $line) {
            break
        }
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $message = $line | ConvertFrom-Json
        if ($message.id -eq 0) {
            if ($null -ne $message.error) {
                throw "Codex initialization failed: $($message.error.message)"
            }
            $process.StandardInput.WriteLine('{"method":"initialized","params":{}}')
            $process.StandardInput.WriteLine('{"method":"account/rateLimits/read","id":1}')
            $process.StandardInput.Flush()
            continue
        }

        if ($message.id -eq 1) {
            if ($null -ne $message.error) {
                throw "Quota request failed: $($message.error.message)"
            }
            $result = $message.result
            break
        }
    }

    if ($null -eq $result) {
        throw 'Codex App Server returned no quota data. Confirm that Codex is signed in with ChatGPT.'
    }

    $snapshot = $null
    if ($null -ne $result.rateLimitsByLimitId -and
        $null -ne $result.rateLimitsByLimitId.PSObject.Properties['codex']) {
        $snapshot = $result.rateLimitsByLimitId.codex
    }
    if ($null -eq $snapshot) {
        $snapshot = $result.rateLimits
    }
    if ($null -eq $snapshot) {
        throw 'The Codex quota bucket is not available for this account.'
    }

    $windows = @()
    if ($null -ne $snapshot.primary) {
        $windows += ConvertTo-WindowInfo -Window $snapshot.primary -Name 'PRIMARY'
    }
    if ($null -ne $snapshot.secondary) {
        $windows += ConvertTo-WindowInfo -Window $snapshot.secondary -Name 'SECONDARY'
    }
    if ($windows.Count -eq 0) {
        throw 'The Codex quota response did not contain a rate-limit window.'
    }

    # Display the most constrained active window when more than one is present.
    $selected = $windows |
        Sort-Object -Property @{ Expression = { $_.Remaining }; Ascending = $true },
                               @{ Expression = { $_.Duration }; Descending = $true } |
        Select-Object -First 1
    $other = $windows | Where-Object { $_.Name -ne $selected.Name } | Select-Object -First 1

    $windowLabel = Get-WindowLabel $selected.Duration
    $resetLabel = if ($null -eq $selected.ResetAt) { '--' } else { $selected.ResetAt.ToString('MM-dd HH:mm') }
    $countdownLabel = Get-CountdownLabel $selected.ResetAt

    $plan = if ($null -eq $snapshot.planType) { 'UNKNOWN' } else { ([string] $snapshot.planType).ToUpperInvariant() }
    $credits = '--'
    if ($null -ne $snapshot.credits) {
        if ($snapshot.credits.unlimited) {
            $credits = 'UNLIMITED'
        }
        elseif ($null -ne $snapshot.credits.balance) {
            $credits = [string] $snapshot.credits.balance
        }
        elseif ($snapshot.credits.hasCredits) {
            $credits = 'AVAILABLE'
        }
        else {
            $credits = '0'
        }
    }

    $status = if ($null -ne $snapshot.rateLimitReachedType) { 'LIMITED' } else { 'ONLINE' }
    $aux = if ($null -ne $other) {
        '{0} {1}% // {2}' -f $other.Name, $other.Remaining, (Get-WindowLabel $other.Duration)
    }
    else {
        '{0} // {1}' -f $selected.Name, ([string] $snapshot.limitId).ToUpperInvariant()
    }

    Write-QuotaPayload @(
        'OK',
        $selected.Remaining,
        $selected.Used,
        $windowLabel,
        $resetLabel,
        $countdownLabel,
        $plan,
        $credits,
        ([string] $snapshot.limitId).ToUpperInvariant(),
        ([DateTimeOffset]::Now.ToString('HH:mm:ss')),
        $status,
        $aux,
        $snapshot.rateLimitReachedType
    )
}
catch {
    Write-QuotaPayload @(
        'ERROR', 0, '--', '--', '--', '--', '--', '--', 'CODEX',
        ([DateTimeOffset]::Now.ToString('HH:mm:ss')), 'OFFLINE', 'NO DATA', $_.Exception.Message
    )
}
finally {
    if ($null -ne $process) {
        try {
            $process.StandardInput.Close()
        }
        catch {
            # Ignore cleanup errors from a process that already exited.
        }
        try {
            if (-not $process.WaitForExit(750)) {
                $process.Kill()
                $process.WaitForExit(750) | Out-Null
            }
        }
        catch {
            # The child process is owned by this script and may already be gone.
        }
        $process.Dispose()
    }
}
