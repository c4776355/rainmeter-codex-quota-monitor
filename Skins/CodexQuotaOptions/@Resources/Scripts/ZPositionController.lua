local lastPosition = nil

local function currentPosition()
    return tonumber(SKIN:GetVariable("CURRENTCONFIGZPOS", "0")) or 0
end

local function applyVisual(position)
    local topmost = position >= 1
    if topmost then
        SKIN:Bang("!SetVariable", "ZPositionLabel", "TOP ON")
        SKIN:Bang("!SetVariable", "ZPositionTextColor", "105,255,240,255")
        SKIN:Bang("!SetVariable", "ZPositionFill", "42,194,224,68")
        SKIN:Bang("!SetVariable", "ZPositionOutline", "166,247,255,220")
        SKIN:Bang("!SetVariable", "ZPositionTip", "TOP ON - panel stays above other apps; click to allow covering")
    else
        SKIN:Bang("!SetVariable", "ZPositionLabel", "TOP OFF")
        SKIN:Bang("!SetVariable", "ZPositionTextColor", "164,221,243,242")
        SKIN:Bang("!SetVariable", "ZPositionFill", "5,42,88,118")
        SKIN:Bang("!SetVariable", "ZPositionOutline", "128,190,220,90")
        SKIN:Bang("!SetVariable", "ZPositionTip", "TOP OFF - other apps and fullscreen games can cover this panel")
    end

    SKIN:Bang("!UpdateMeter", "MeterZToggle")
    SKIN:Bang("!UpdateMeter", "MeterZToggleLabel")
    SKIN:Bang("!Redraw")
end

function Initialize()
    lastPosition = nil
end

function Update()
    local position = currentPosition()
    if position ~= lastPosition then
        lastPosition = position
        applyVisual(position)
    end
    return position
end

function Toggle()
    local target = currentPosition() >= 1 and 0 or 2
    SKIN:Bang("!ZPos", tostring(target))
    lastPosition = target
    applyVisual(target)
end
