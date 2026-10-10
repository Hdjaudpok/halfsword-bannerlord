-- HSInput: read-only facts from the player's own Half Sword install, no input sent.
--  - input bindings (engine InputSettings action/axis mappings) -> %USERPROFILE%\halfsword-decomp\input_bindings.txt
--  - weapon blueprint classes the game has loaded (main menu, then after GM Duel) -> ...\weapon_classes.txt
local UEHelpers = require("UEHelpers")
local DIR = os.getenv("USERPROFILE") .. "\\halfsword-decomp\\"

local function Name(fname)
    local ok, s = pcall(function() return fname:ToString() end)
    return ok and s or "?"
end

local function DumpInput()
    local f = io.open(DIR .. "input_bindings.txt", "w")
    if f == nil then return end
    local settings = StaticFindObject("/Script/Engine.Default__InputSettings")
    if settings == nil or not settings:IsValid() then
        f:write("InputSettings not found\n")
        f:close()
        return
    end
    f:write("# actions: name | key | shift ctrl alt\n")
    settings.ActionMappings:ForEach(function(_, element)
        local m = element:get()
        f:write("action | ", Name(m.ActionName), " | ", Name(m.Key.KeyName), " | ",
            tostring(m.bShift), " ", tostring(m.bCtrl), " ", tostring(m.bAlt), "\n")
    end)
    f:write("# axes: name | key | scale\n")
    settings.AxisMappings:ForEach(function(_, element)
        local m = element:get()
        f:write("axis | ", Name(m.AxisName), " | ", Name(m.Key.KeyName), " | ", string.format("%.3f", m.Scale), "\n")
    end)
    f:write("# done\n")
    f:close()
end

local function DumpWeapons(tag)
    local seen, list = {}, {}
    ForEachUObject(function(object)
        local ok, full = pcall(function() return object:GetFullName() end)
        if ok and full and full:find("BlueprintGeneratedClass", 1, true) == 1 and full:find("/Weapons/", 1, true) then
            if not seen[full] then
                seen[full] = true
                table.insert(list, full)
            end
        end
    end)
    table.sort(list)
    local f = io.open(DIR .. "weapon_classes.txt", "a")
    if f == nil then return end
    f:write("# ", tag, ": ", #list, " classes\n")
    for _, s in ipairs(list) do f:write(s, "\n") end
    f:close()
end

local function Safe(label, fn)
    local ok, err = pcall(fn)
    if not ok then
        local f = io.open(DIR .. "weapon_classes.txt", "a")
        if f then f:write("ERROR ", label, ": ", tostring(err), "\n") f:close() end
    end
end

ExecuteWithDelay(3000, function() Safe("input", DumpInput) end)
ExecuteWithDelay(8000, function()
    ExecuteInGameThread(function()
        Safe("menu", function() DumpWeapons("main menu") end)
        Safe("duel", function()
            local world = UEHelpers.GetWorld()
            UEHelpers.GetKismetSystemLibrary():ExecuteConsoleCommand(world, "t.IdleWhenNotForeground 0", UEHelpers.GetPlayerController())
            local gm = UEHelpers.GetGameModeBase()
            gm["GM Duel"](gm)
        end)
    end)
end)
ExecuteWithDelay(16000, function()
    ExecuteInGameThread(function()
        Safe("after duel", function() DumpWeapons("after GM Duel") end)
        local f = io.open(DIR .. "weapon_classes.txt", "a")
        if f then f:write("# done\n") f:close() end
    end)
end)
