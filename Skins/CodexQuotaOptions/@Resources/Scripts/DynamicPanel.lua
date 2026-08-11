local statePath
local startedAt = os.time()
local lastLauncherRequest = 0
local variableCache = {}

local function trim(value)
    value = tostring(value or "")
    return value:match("^%s*(.-)%s*$")
end

local function readState()
    local result = {}
    local file = io.open(statePath, "r")
    if not file then
        return result
    end

    for line in file:lines() do
        local key, value = line:match("^([^=]+)=(.*)$")
        if key then
            result[trim(key)] = trim(value)
        end
    end
    file:close()
    return result
end

local function setVariable(name, value)
    value = tostring(value or "--")
    if variableCache[name] ~= value then
        variableCache[name] = value
        SKIN:Bang("!SetVariable", name, value)
    end
end

local function number(value, fallback)
    local parsed = tonumber(value)
    if parsed == nil then
        return fallback or 0
    end
    return parsed
end

local function formatTimer(seconds)
    seconds = math.max(0, math.floor(seconds + 0.5))
    local minutes = math.floor(seconds / 60)
    local remainder = seconds % 60
    return string.format("%02d:%02d", minutes, remainder)
end

local function formatReset(epoch, now)
    if epoch <= 0 then
        return "--"
    end

    local seconds = epoch - now
    if seconds <= 0 then
        return "RESETTING"
    end

    local days = math.floor(seconds / 86400)
    local hours = math.floor((seconds % 86400) / 3600)
    local minutes = math.floor((seconds % 3600) / 60)
    local remainingSeconds = seconds % 60

    if days > 0 then
        return string.format("%dD %02dH", days, hours)
    elseif hours > 0 then
        return string.format("%02dH %02dM", hours, minutes)
    end
    return string.format("%02dM %02dS", minutes, remainingSeconds)
end

local function splitReset(value)
    value = trim(value)
    if value == "" or value == "--" then
        return "--", "--:--"
    end

    local date, time = value:match("^(%S+)%s+(%S+)$")
    if date and time then
        return date, time
    end
    return value, "--:--"
end

local function applyQuotaPalette(remaining, quotaState)
    if quotaState == "LIMITED" or remaining <= 5 then
        setVariable("ProgressColorA", "#ColorCriticalA#")
        setVariable("ProgressColorB", "#ColorCriticalB#")
    elseif remaining <= 20 then
        setVariable("ProgressColorA", "#ColorWarningA#")
        setVariable("ProgressColorB", "#ColorWarningB#")
    else
        setVariable("ProgressColorA", "#ActiveAccentA#")
        setVariable("ProgressColorB", "#ActiveAccentB#")
    end
end

local function applyMode(mode, activeCount, nextSyncAt, now, state)
    local statusText = state.StatusText or mode
    local statusDetail = state.StatusDetail or "LOCAL WATCH"
    local syncLabel = "REMOTE QUERY"
    local syncCountdown = "--:--"

    if mode == "ACTIVE" then
        statusText = "TASK ACTIVE"
        if activeCount == 1 then
            statusDetail = "1 LIVE TURN"
        else
            statusDetail = string.format("%d LIVE TURNS", activeCount)
        end
        syncLabel = "NEXT QUERY"
        syncCountdown = formatTimer(nextSyncAt - now)
        setVariable("StatusColor", "#ColorOnline#")
        setVariable("PulseColor", "166,247,255")
        setVariable("PulseMin", "72")
        setVariable("PulseRange", "150")
        setVariable("PulseSoftMin", "28")
        setVariable("PulseSoftRange", "72")
        setVariable("PanelWashMin", "3")
        setVariable("PanelWashRange", "19")
        setVariable("PulseRadiusBase", "5.6")
        setVariable("PulseRadiusRange", "3.2")
        setVariable("ScanAlpha", "125")
    elseif mode == "SYNCING" then
        statusText = "SYNCING"
        statusDetail = state.StatusDetail or "READING LIMITS"
        syncLabel = "QUERY STATUS"
        syncCountdown = "00:00"
        setVariable("StatusColor", "#ColorSync#")
        setVariable("PulseColor", "168,216,255")
        setVariable("PulseMin", "95")
        setVariable("PulseRange", "150")
        setVariable("PulseSoftMin", "38")
        setVariable("PulseSoftRange", "84")
        setVariable("PanelWashMin", "5")
        setVariable("PanelWashRange", "22")
        setVariable("PulseRadiusBase", "5.8")
        setVariable("PulseRadiusRange", "3.6")
        setVariable("ScanAlpha", "150")
    elseif mode == "ERROR" then
        statusText = state.StatusText or "RETRYING"
        statusDetail = state.StatusDetail or "CHECK CONNECTION"
        syncLabel = nextSyncAt > now and "RETRY IN" or "REMOTE QUERY"
        syncCountdown = nextSyncAt > now and formatTimer(nextSyncAt - now) or "PAUSED"
        setVariable("StatusColor", "#ColorOffline#")
        setVariable("PulseColor", "255,116,145")
        setVariable("PulseMin", "20")
        setVariable("PulseRange", "0")
        setVariable("PulseSoftMin", "8")
        setVariable("PulseSoftRange", "0")
        setVariable("PanelWashMin", "0")
        setVariable("PanelWashRange", "0")
        setVariable("PulseRadiusBase", "6.2")
        setVariable("PulseRadiusRange", "0")
        setVariable("ScanAlpha", "0")
    elseif mode == "SILENT" then
        statusText = "SILENT"
        statusDetail = "NO ACTIVE TASK"
        syncLabel = "REMOTE QUERY"
        syncCountdown = "--:--"
        setVariable("StatusColor", "#ColorSilent#")
        setVariable("PulseColor", "128,190,220")
        setVariable("PulseMin", "15")
        setVariable("PulseRange", "0")
        setVariable("PulseSoftMin", "7")
        setVariable("PulseSoftRange", "0")
        setVariable("PanelWashMin", "0")
        setVariable("PanelWashRange", "0")
        setVariable("PulseRadiusBase", "5.4")
        setVariable("PulseRadiusRange", "0")
        setVariable("ScanAlpha", "0")
    else
        statusText = "STARTING"
        statusDetail = "SCANNING TASKS"
        syncLabel = "MONITOR LINK"
        syncCountdown = "BOOT"
        setVariable("StatusColor", "#ColorSync#")
        setVariable("PulseColor", "168,216,255")
        setVariable("PulseMin", "20")
        setVariable("PulseRange", "0")
        setVariable("PulseSoftMin", "8")
        setVariable("PulseSoftRange", "0")
        setVariable("PanelWashMin", "0")
        setVariable("PanelWashRange", "0")
        setVariable("PulseRadiusBase", "5.4")
        setVariable("PulseRadiusRange", "0")
        setVariable("ScanAlpha", "0")
    end

    setVariable("StatusText", statusText)
    setVariable("StatusDetail", statusDetail)
    setVariable("SyncLabel", syncLabel)
    setVariable("SyncCountdown", syncCountdown)
end

function Initialize()
    statePath = SELF:GetOption("StateFile")
end

function Update()
    local state = readState()
    local now = os.time()
    local heartbeat = number(state.AgentHeartbeat, 0)
    local stateMissing = next(state) == nil
    local stale = heartbeat <= 0 or (now - heartbeat) > 35

    if (stateMissing or stale) and (now - startedAt) >= 4 and (now - lastLauncherRequest) >= 20 then
        lastLauncherRequest = now
        SKIN:Bang("!CommandMeasure", "MeasureAgentLauncher", "Run")
    end

    local mode = state.Mode or "STARTING"
    if stale and mode ~= "SILENT" then
        mode = "STARTING"
    end

    local remaining = number(state.Remaining, 0)
    local remainingDisplay = state.Remaining or "--"
    if remainingDisplay == "--" then
        remaining = 0
    else
        remaining = math.max(0, math.min(100, remaining))
        remainingDisplay = tostring(math.floor(remaining + 0.5))
    end

    local maxWidth = number(SKIN:GetVariable("ProgressMaxWidth", "338"), 338)
    local activeCount = number(state.ActiveCount, 0)
    local nextSyncAt = number(state.NextSyncAt, 0)
    local resetEpoch = number(state.ResetEpoch, 0)
    local resetText = state.Reset or "--"
    local resetDate, resetTime = splitReset(resetText)

    setVariable("QuotaRemaining", remaining)
    setVariable("QuotaRemainingDisplay", remainingDisplay)
    setVariable("QuotaUsed", state.Used or "--")
    setVariable("QuotaWindow", state.Window or "--")
    setVariable("QuotaReset", resetText)
    setVariable("QuotaResetDate", resetDate)
    setVariable("QuotaResetTime", resetTime)
    setVariable("QuotaCountdown", formatReset(resetEpoch, now))
    setVariable("QuotaPlan", state.Plan or "--")
    setVariable("QuotaCredits", state.Credits or "--")
    setVariable("QuotaLimitId", state.LimitId or "CODEX")
    setVariable("QuotaAux", state.Aux or "PRIMARY LIMIT")
    setVariable("LastUpdated", state.LastUpdated or "--:--:--")
    setVariable("ErrorText", state.LastError == "--" and "" or (state.LastError or ""))
    setVariable("ProgressWidth", math.floor(maxWidth * remaining / 100 + 0.5))

    applyQuotaPalette(remaining, state.QuotaState or "UNKNOWN")
    applyMode(mode, activeCount, nextSyncAt, now, state)

    SKIN:Bang("!UpdateMeterGroup", "Dynamic")
    SKIN:Bang("!Redraw")
    return 0
end
