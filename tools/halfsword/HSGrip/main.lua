-- HSGrip: measures how Half Sword holds its weapons (player's own install, read-only, no input): with the sword and
-- shield equipped and a few guard states held, writes the world transforms of the hands, the hand sockets, each
-- weapon's base mesh and grip components, and the base mesh bounds. Output: %USERPROFILE%\halfsword-decomp\grip.txt
local UEHelpers = require("UEHelpers")
local OUT = os.getenv("USERPROFILE") .. "\\halfsword-decomp\\grip.txt"
local WEAPONS = "/Game/Assets/Weapons/Blueprints/Built_Weapons/"
local RIGHT, LEFT = "ModularWeaponBP_ArmingSword_T1", "Shield_Tagre"

local function W(...)
    local f = io.open(OUT, "a")
    if f then f:write(...) f:write("\n") f:close() end
end

local function F(x) return string.format("%.4f", tonumber(x) or 0) end

local function T(tr)
    local q, p = tr.Rotation, tr.Translation
    return "p=" .. F(p.X) .. "," .. F(p.Y) .. "," .. F(p.Z) .. " q=" .. F(q.X) .. "," .. F(q.Y) .. "," .. F(q.Z) .. "," .. F(q.W)
end

local function Class(name)
    local full = WEAPONS .. name .. "." .. name .. "_C"
    local cls = StaticFindObject(full)
    if cls == nil or not cls:IsValid() then
        LoadAsset(WEAPONS .. name)
        cls = StaticFindObject(full)
    end
    return cls
end

local function Try(label, fn)
    local ok, err = pcall(fn)
    if not ok then W("ERROR ", label, ": ", tostring(err)) end
end

local STATES = {
    { name = "R guard centre", r = true, l = false, x = 0, y = 0 },
    { name = "R guard up", r = true, l = false, x = 0, y = -60 },
    { name = "L guard centre", r = false, l = true, x = 0, y = 0 },
    { name = "both centre", r = true, l = true, x = 0, y = 0 },
    { name = "none", r = false, l = false, x = 0, y = 0 },
}

local st = { phase = "wait", i = 0 }
local function Now() return os.clock() end

local function Apply(pawn, s)
    pawn["R Down"] = s.r
    pawn["R_Guarding"] = s.r
    pawn["L Down"] = s.l
    pawn["L_Guarding"] = s.l
    pawn["Any_Guarding"] = s.r or s.l
    pawn["Aim Vector_0"] = { X = s.x, Y = s.y, Z = 0 }
    pawn["Aim Vector"] = { X = s.x, Y = s.y, Z = 0 }
    pawn["Swing Velocity proxy"] = 0
end

local function Record(pawn, s)
    W("== ", s.name)
    for _, sock in ipairs({ "hand_r", "hand_l", "weapon_rSocket", "weapon_lSocket", "lowerarm_r", "lowerarm_l" }) do
        Try("mesh " .. sock, function() W("mesh ", sock, " ", T(pawn.Mesh:GetSocketTransform(FName(sock), 0))) end)
        Try("driver " .. sock, function() W("driver ", sock, " ", T(pawn.DriverSkeleton:GetSocketTransform(FName(sock), 0))) end)
    end
    for _, side in ipairs({ "R", "L" }) do
        Try("weapon " .. side, function()
            local w = pawn["Weapon " .. side]
            if w == nil or not w:IsValid() then W("weapon ", side, " none") return end
            W("weapon ", side, " class ", w:GetClass():GetFName():ToString())
            W("weapon ", side, " BaseMesh ", T(w.BaseMesh:K2_GetComponentToWorld()))
            Try("bounds " .. side, function()
                local box = w.BaseMesh.StaticMesh:GetBoundingBox()
                W("weapon ", side, " BaseMesh bounds min=", F(box.Min.X), ",", F(box.Min.Y), ",", F(box.Min.Z),
                    " max=", F(box.Max.X), ",", F(box.Max.Y), ",", F(box.Max.Z))
            end)
            local list = {}
            for _, name in ipairs({ "Head", "Guard", "Grip", "Pommel", "Blade", "Mesh", "Shield", "ShieldMesh", "StaticMesh",
                                    "Grip R Hand Default", "Grip R Hand Default Pos", "Grip L Hand Off Hand", "Grip L Hand Alt Pos" }) do
                local ok, comp = pcall(function() return w[name] end)
                if ok and type(comp) == "userdata" and comp:IsValid() then table.insert(list, comp) end
            end
            for _, comp in ipairs(list) do
                Try("component", function()
                    local line = "weapon " .. side .. " comp " .. comp:GetFName():ToString() .. " " .. T(comp:K2_GetComponentToWorld())
                    local okMesh, mesh = pcall(function() return comp.StaticMesh end)
                    if okMesh and mesh ~= nil and mesh:IsValid() then
                        local box = mesh:GetBoundingBox()
                        line = line .. " mesh=" .. mesh:GetFName():ToString() .. " min=" .. F(box.Min.X) .. "," .. F(box.Min.Y) .. "," ..
                            F(box.Min.Z) .. " max=" .. F(box.Max.X) .. "," .. F(box.Max.Y) .. "," .. F(box.Max.Z)
                    end
                    W(line)
                end)
            end
        end)
    end
end

local function Step()
    local pc = UEHelpers.GetPlayerController()
    local world = UEHelpers.GetWorld()
    if not st.cvars then
        st.cvars = true
        Try("cvar", function() UEHelpers.GetKismetSystemLibrary():ExecuteConsoleCommand(world, "t.IdleWhenNotForeground 0", pc) end)
    end
    local pawn = pc and pc:IsValid() and pc.Pawn or nil
    if pawn == nil or not pawn:IsValid() or pawn:GetClass():GetFName():ToString() ~= "Willie_BP_C" then
        st.waitStart = st.waitStart or Now()
        if not st.duel and Now() - st.waitStart > 8 then
            st.duel = true
            Try("duel", function() local gm = UEHelpers.GetGameModeBase() gm["GM Duel"](gm) end)
        end
        return
    end
    Try("calm", function()
        pawn.Invulnerable = true
        for _, w in ipairs(FindAllOf("Willie_BP_C") or {}) do
            if w:IsValid() and w:GetAddress() ~= pawn:GetAddress() then w:K2_DestroyActor() end
        end
    end)
    if st.phase == "wait" then
        st.phase, st.t = "equip", Now()
        return
    end
    if st.phase == "equip" and Now() - st.t > 2 then
        Try("equip", function()
            pawn["Set Up Right Hand Weapon"](pawn, Class(RIGHT), nil, false, true, {})
            pawn["Set Up Left Hand Weapon"](pawn, Class(LEFT), nil, false, true, {})
            pawn.Mesh.VisibilityBasedAnimTickOption = 0 -- minimized: keep the physical body's bones updated
            pawn.DriverSkeleton.VisibilityBasedAnimTickOption = 0
        end)
        st.phase, st.t, st.i = "state", Now(), 1
        return
    end
    if st.phase == "state" then
        local s = STATES[st.i]
        Apply(pawn, s)
        if Now() - st.t > 2.5 then
            Record(pawn, s)
            st.i, st.t = st.i + 1, Now()
            if st.i > #STATES then
                st.phase = "done"
                W("# done")
            end
        end
    end
end

W("# HSGrip ", os.date())
LoopAsync(20, function()
    ExecuteInGameThread(function() Try("Step", Step) end)
    return st.phase == "done"
end)
