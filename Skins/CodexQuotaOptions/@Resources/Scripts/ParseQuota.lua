local commandMeasure

local function trim(value)
    value = tostring(value or "")
    return value:match("^%s*(.-)%s*$")
end
local function split(value)
    local fields = {}
    value = tostring(value or ""):gsub("[\r\n]+$", "")
    for field in (value .. "|"):gmatch("(.-)|") do
        table.insert(fields, trim(field))
    end
    return fields
end

local function setVariable(name, value)
    SKIN:Bang("!SetVariable", name, tostring(value or "--"))
end

local function applyPalette(remaining, status)
    if status == "OFFLINE" then
        setVariable("StatusColor", "#ColorOffline#")
        setVariable("ProgressColorA", "#ColorCriticalA#")
        setVariable("ProgressColorB", "#ColorCriticalB#")
    elseif status == "LIMITED" or remaining <= 5 then
        setVariable("StatusColor", "#ColorCriticalA#")
        setVariable("ProgressColorA", "#ColorCriticalA#")
        setVariable("ProgressColorB", "#ColorCriticalB#")
    elseif remaining <= 20 then
        setVariable("StatusColor", "#ColorWarningA#")
        setVariable("ProgressColorA", "#ColorWarningA#")
        setVariable("ProgressColorB", "#ColorWarningB#")
    else
        setVariable("StatusColor", "#ColorOnline#")
        setVariable("ProgressColorA", "#ActiveAccentA#")
        setVariable("ProgressColorB", "#ActiveAccentB#")
    end
end

local function showError(fields)
    setVariable("QuotaRemaining", 0)
    setVariable("QuotaRemainingDisplay", "--")
    setVariable("QuotaUsed", "--")
    setVariable("QuotaWindow", "--")
    setVariable("QuotaReset", "--")
    setVariable("QuotaCountdown", "NO DATA")
    setVariable("QuotaPlan", "--")
    setVariable("QuotaCredits", "--")
    setVariable("QuotaLimitId", "CODEX")
    setVariable("QuotaAux", fields[12] ~= "" and fields[12] or "CHECK CODEX LOGIN")
    setVariable("LastUpdated", fields[10] ~= "" and fields[10] or "--:--:--")
    setVariable("StatusText", "OFFLINE")
    setVariable("ProgressWidth", 0)
    setVariable("ErrorText", fields[13] ~= "" and fields[13] or "Unable to read Codex quota.")
    applyPalette(0, "OFFLINE")
end

function Initialize()
    commandMeasure = SKIN:GetMeasure("MeasureQuotaCommand")
end

function Update()
    return 0
end

function Parse()
    local raw = commandMeasure and commandMeasure:GetStringValue() or ""
    local fields = split(raw)

    if raw == "" or fields[1] ~= "OK" then
        showError(fields)
    else
        local remaining = tonumber(fields[2]) or 0
        local maxWidth = tonumber(SKIN:GetVariable("ProgressMaxWidth", "266")) or 266
        remaining = math.max(0, math.min(100, remaining))

        setVariable("QuotaRemaining", remaining)
        setVariable("QuotaRemainingDisplay", math.floor(remaining + 0.5))
        setVariable("QuotaUsed", fields[3])
        setVariable("QuotaWindow", fields[4])
        setVariable("QuotaReset", fields[5])
        setVariable("QuotaCountdown", fields[6])
        setVariable("QuotaPlan", fields[7])
        setVariable("QuotaCredits", fields[8])
        setVariable("QuotaLimitId", fields[9])
        setVariable("LastUpdated", fields[10])
        setVariable("StatusText", fields[11])
        setVariable("QuotaAux", fields[12])
        setVariable("ErrorText", fields[13] == "--" and "" or fields[13])
        setVariable("ProgressWidth", math.floor(maxWidth * remaining / 100 + 0.5))
        applyPalette(remaining, fields[11])
    end

    SKIN:Bang("!UpdateMeterGroup", "Dynamic")
    SKIN:Bang("!Redraw")
end
