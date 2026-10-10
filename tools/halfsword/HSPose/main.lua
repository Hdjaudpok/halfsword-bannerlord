-- HSPose: samples Half Sword's real driver-skeleton poses from the player's own install (personal use).
-- Runs minimized with no input: starts a duel (GM Duel), makes the player invulnerable, removes enemies, equips each
-- class's weapons through the game's own Set Up Right/Left Hand Weapon, then for every sample sets the animation
-- blueprint's inputs (guard flags, aim point, swing direction, swing speed, thrust state), waits for the blends to
-- settle and writes the DriverSkeleton pose (local rotations + component positions) with the values the game used.
-- Several sets (class x control mode) run one after the other; a finished set leaves a .done file and is skipped next
-- time. Output: %USERPROFILE%\halfsword-decomp\pose\atlas_<class>[_<mode>].csv (never in a repository).
local UEHelpers = require("UEHelpers")

local OUT = os.getenv("USERPROFILE") .. "\\halfsword-decomp\\pose\\"
local WEAPONS = "/Game/Assets/Weapons/Blueprints/Built_Weapons/"

-- Weapons per class (paths under Built_Weapons). No left item = the left hand is free or on the weapon.
local CLASSES = {
    onehand_shield = { right = "ModularWeaponBP_ArmingSword_T1", left = "Shield_Tagre" },
    twohand = { right = "ModularWeaponBP_LongSword_T2", twoHanded = true },
    polearm = { right = "Tiers/ModularWeaponBP_Polearm_Mid_Tier", twoHanded = true },
    spear = { right = "Reforged/ModularWeaponBP_Spear_A", twoHanded = true },
}

-- Control modes (Willie_BP): R = left mouse held (Guard Right), L = right mouse held (Guard Left), RL = both,
-- N = none (aim drifting back), T = double-click thrust (R_Thrusting, extended or not), A = Left Alt manual thrust
-- (thrust position alpha 0..1).
local MODES = {
    R = { guardR = true, guardL = false, swings = true },
    L = { guardR = false, guardL = true, swings = true },
    RL = { guardR = true, guardL = true, swings = true },
    N = { guardR = false, guardL = false },
    T = { guardR = true, guardL = false, extend = { false, true } },
    A = { guardR = true, guardL = false, alt = { 0, 0.5, 1 } },
}

-- Order: what the player needs first. onehand_shield R exists already (atlas_onehand_shield.csv).
local SETS = {
    "onehand_shield:L", "onehand_shield:RL", "onehand_shield:N", "onehand_shield:T", "onehand_shield:A",
    "twohand:R", "twohand:N", "twohand:T", "twohand:A",
    "polearm:R", "polearm:N", "polearm:T", "polearm:A",
    "spear:R", "spear:N", "spear:T", "spear:A",
    "twohand:RL", "polearm:RL", "spear:RL",
}

-- HSPOSE_QUICK=1: coarse grid, one swing speed, files prefixed quick_ (checks a mode before the long run).
local QUICK = os.getenv("HSPOSE_QUICK") == "1"
local ONLY = os.getenv("HSPOSE_ONLY") -- e.g. "twohand" to run that class's sets only
local PREFIX = QUICK and "quick_atlas_" or "atlas_"
local HOLD = 0.45          -- seconds per sample before recording (blends settle)
local FIRST_HOLD = 1.5     -- first sample of a set (guard / thrust state changes)
local GRID = QUICK and 45 or 15 -- aim grid step, degrees, inside a disc of radius 90
local DIRS = 8             -- swing directions
local PROXIES = QUICK and { 75 } or { 50, 75 } -- swing velocity proxy: Aim Swing Speed = mapRange(proxy, 25..75 -> 0..75)

local BONES = { "pelvis", "spine_01", "spine_02", "spine_03", "spine_04", "spine_05", "neck_01", "neck_02", "Head",
    "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r", "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l" }

local function Log(text)
    local f = io.open(OUT .. "hspose.log", "a")
    if f then
        f:write(os.date("%H:%M:%S "), text, "\n")
        f:close()
    end
end

local function Try(label, fn)
    local ok, err = pcall(fn)
    if not ok then Log("ERROR " .. label .. ": " .. tostring(err)) end
    return ok
end

local function F(x) return string.format("%.4f", tonumber(x) or 0) end

local function Exists(path)
    local f = io.open(path, "r")
    if f then f:close() return true end
    return false
end

local function Class(rel)
    if rel == nil then return nil end
    local name = rel:match("([^/]+)$")
    local path = WEAPONS .. rel
    local full = path .. "." .. name .. "_C"
    local cls = StaticFindObject(full)
    if cls == nil or not cls:IsValid() then
        LoadAsset(path)
        cls = StaticFindObject(full)
    end
    return cls
end

local function AimPoints()
    local points = {}
    for y = -90, 90, GRID do
        local row = {}
        for x = -90, 90, GRID do
            if x * x + y * y <= 90 * 90 + 1 then table.insert(row, x) end
        end
        if (y / GRID) % 2 == 1 then -- serpentine order: small jumps between samples
            local rev = {}
            for i = #row, 1, -1 do table.insert(rev, row[i]) end
            row = rev
        end
        for _, x in ipairs(row) do table.insert(points, { x, y }) end
    end
    return points
end

-- Sample list of one mode: the aim grid at rest, then (swing modes) every direction and swing speed on the same grid,
-- (T) not yet extended / extended, (A) each thrust position.
local function BuildSamples(mode)
    local s, points = {}, AimPoints()
    local function add(extra)
        for _, p in ipairs(points) do
            local e = { x = p[1], y = p[2], dir = 0, proxy = 0 }
            for k, v in pairs(extra) do e[k] = v end
            table.insert(s, e)
        end
    end
    if mode.extend then
        for _, ext in ipairs(mode.extend) do add({ thrust = true, extend = ext }) end
        return s
    end
    if mode.alt then
        for _, a in ipairs(mode.alt) do add({ alt = a }) end
        return s
    end
    add({})
    if mode.swings then
        for d = 0, DIRS - 1 do
            local yaw = -180 + d * 360 / DIRS
            for _, proxy in ipairs(PROXIES) do add({ dir = yaw, proxy = proxy }) end
        end
    end
    return s
end

local st = { phase = "wait", ticks = 0, set = 0 }

local function Now() return os.clock() end

local function KeepRunning(world, pc)
    Try("keep running", function()
        local gs = UEHelpers.GetGameplayStatics()
        if gs:IsGamePaused(world) then gs:SetGamePaused(world, false) end
        if not st.cvarsSet then
            st.cvarsSet = true
            UEHelpers.GetKismetSystemLibrary():ExecuteConsoleCommand(world, "t.IdleWhenNotForeground 0", pc)
            Log("background idle disabled (t.IdleWhenNotForeground 0)")
        end
    end)
end

local function Apply(pawn, s, mode, cls)
    if CLASSES[cls].twoHanded then
        pawn["R Two Handed Grip"] = true -- left hand on the weapon (physics constraint made at equip)
    end
    pawn["R Down"] = mode.guardR
    pawn["R_Guarding"] = mode.guardR
    pawn["L Down"] = mode.guardL
    pawn["L_Guarding"] = mode.guardL
    pawn["Any_Guarding"] = mode.guardR or mode.guardL
    pawn["Aim Vector_0"] = { X = s.x, Y = s.y, Z = 0 }
    pawn["Aim Vector"] = { X = s.x, Y = s.y, Z = 0 }
    pawn["Swing Direction"] = { Pitch = 0, Yaw = s.dir, Roll = 0 }
    pawn["Swing Velocity proxy"] = s.proxy
    pawn["R_Thrusting"] = s.thrust == true
    pawn["R_Thrusting_Extend"] = s.extend == true
    pawn["Alt Thrusting"] = s.alt ~= nil
    if s.alt ~= nil then
        pawn["R Thrust Position Alpha"] = s.alt
        pawn["Alt Thrust R"] = s.alt
    end
end

local function Header()
    local h = { "i", "mode", "x", "y", "dir", "proxy", "thrust", "extend", "alt", "X", "Y", "aimX", "aimY", "swingYaw",
        "swingSpeed", "gripR", "gripL", "thrustingR", "extendR", "altThrustR", "thrustAlphaR", "thrustPosR" }
    for _, b in ipairs(BONES) do
        for _, c in ipairs({ "qx", "qy", "qz", "qw", "px", "py", "pz" }) do table.insert(h, b .. "_" .. c) end
    end
    return table.concat(h, ",")
end

local function Row(pawn, s)
    local aim, swing = pawn["Aim Vector"], pawn["Swing Direction"]
    local r = { st.i, st.modeName, s.x, s.y, s.dir, s.proxy, tostring(s.thrust == true), tostring(s.extend == true),
        s.alt or "", F(pawn.X), F(pawn.Y), F(aim.X), F(aim.Y), F(swing.Yaw), F(pawn["Aim Swing Speed"]),
        pawn["R_GripType_Current"], pawn["L_GripType_Current"], tostring(pawn["R_Thrusting"]),
        tostring(pawn["R_Thrusting_Extend"]), F(pawn["Alt Thrust R"]), F(pawn["R Thrust Alpha"]),
        F(pawn["R Thrust Position Alpha"]) }
    local driver = pawn.DriverSkeleton
    for _, b in ipairs(BONES) do
        local name = FName(b)
        local ok1, loc = pcall(function() return driver:GetSocketTransform(name, 3) end) -- parent bone space
        local ok2, comp = pcall(function() return driver:GetSocketTransform(name, 2) end) -- component space
        if ok1 and ok2 and loc and comp then
            local q, p = loc.Rotation, comp.Translation
            for _, v in ipairs({ q.X, q.Y, q.Z, q.W, p.X, p.Y, p.Z }) do table.insert(r, F(v)) end
        else
            for _ = 1, 7 do table.insert(r, "") end
        end
    end
    return table.concat(r, ",")
end

-- Next set not done yet; nil when all are.
local function NextSet()
    while true do
        st.set = st.set + 1
        local spec = SETS[st.set]
        if spec == nil then return nil end
        local cls, mode = spec:match("([^:]+):(.+)")
        if not ONLY or ONLY == "" or cls == ONLY then
            local base = OUT .. PREFIX .. cls .. "_" .. mode
            if not Exists(base .. ".done") then return cls, mode, base end
            Log("set " .. spec .. " already done, skipped")
        end
    end
end

local function Equip(pawn, cls)
    local c = CLASSES[cls]
    pawn["Set Up Right Hand Weapon"](pawn, Class(c.right), nil, false, true, {})
    pawn["Set Up Left Hand Weapon"](pawn, Class(c.left), nil, false, true, {})
    pawn.DriverSkeleton.VisibilityBasedAnimTickOption = 0
    st.attachAt = c.twoHanded and Now() + 1 or nil -- the game's own two-hand grip, once the weapon has settled
end

local function Step()
    st.ticks = st.ticks + 1
    local world = UEHelpers.GetWorld()
    local pc = UEHelpers.GetPlayerController()
    KeepRunning(world, pc)
    local pawn = pc and pc:IsValid() and pc.Pawn or nil
    if pawn == nil or not pawn:IsValid() or pawn:GetClass():GetFName():ToString() ~= "Willie_BP_C" then
        st.waitStart = st.waitStart or Now()
        if not st.duelCalled and Now() - st.waitStart > 8 then
            st.duelCalled = true
            Try("GM Duel", function() local gm = UEHelpers.GetGameModeBase() gm["GM Duel"](gm) end)
            Log("GM Duel called")
        end
        return
    end
    Try("calm", function()
        pawn.Invulnerable = true
        if st.ticks % 20 == 0 then
            for _, w in ipairs(FindAllOf("Willie_BP_C") or {}) do
                if w:IsValid() and w:GetAddress() ~= pawn:GetAddress() then w:K2_DestroyActor() end
            end
        end
    end)
    if st.phase == "wait" then
        st.phase, st.t = "next", Now()
        Log("player Willie ready")
        return
    end
    if st.phase == "next" and Now() - st.t > 2 then
        local cls, mode, base = NextSet()
        if cls == nil then
            st.phase = "done"
            Log("ALL SETS DONE")
            return
        end
        st.cls, st.modeName, st.mode, st.base = cls, mode, MODES[mode], base
        if st.equipped ~= cls then
            Try("equip " .. cls, function() Equip(pawn, cls) end)
            st.equipped = cls
        end
        st.phase, st.t = "settle", Now()
        return
    end
    if st.attachAt and Now() >= st.attachAt then
        st.attachAt = nil
        Try("two-hand grip", function()
            pawn["Attach L Hand To Offhand Position"](pawn)
            Log("left hand attached: " .. tostring(pawn["L Hand In Offhand Attached"]) .. " grips " ..
                tostring(pawn["R_GripType_Current"]) .. "/" .. tostring(pawn["L_GripType_Current"]))
        end)
    end
    if st.phase == "settle" and Now() - st.t > 2 then
        st.samples = BuildSamples(st.mode)
        st.file = io.open(st.base .. ".csv", "w")
        st.file:write(Header(), "\n")
        Log(string.format("set %s:%s, %d poses, weapons R=%s L=%s grips %s/%s", st.cls, st.modeName, #st.samples,
            pawn["Weapon R"]:IsValid() and pawn["Weapon R"]:GetClass():GetFName():ToString() or "none",
            pawn["Weapon L"]:IsValid() and pawn["Weapon L"]:GetClass():GetFName():ToString() or "none",
            tostring(pawn["R_GripType_Current"]), tostring(pawn["L_GripType_Current"])))
        st.phase, st.i, st.t = "sample", 1, Now()
    end
    if st.phase == "sample" then
        local s = st.samples[st.i]
        Try("apply", function() Apply(pawn, s, st.mode, st.cls) end)
        if Now() - st.t >= (st.i == 1 and FIRST_HOLD or HOLD) then
            Try("row", function() st.file:write(Row(pawn, s), "\n") end)
            if st.i % 200 == 0 then
                st.file:flush()
                Log(string.format("%s:%s sample %d/%d", st.cls, st.modeName, st.i, #st.samples))
            end
            st.i, st.t = st.i + 1, Now()
            if st.i > #st.samples then
                st.file:close()
                local done = io.open(st.base .. ".done", "w")
                if done then done:write(os.date(), "\n") done:close() end
                Log("SET DONE " .. st.cls .. ":" .. st.modeName .. " grips " .. tostring(pawn["R_GripType_Current"]) .. "/" ..
                    tostring(pawn["L_GripType_Current"]) .. " two-hand " .. tostring(pawn["R Two Handed Grip"]))
                st.phase, st.t = "next", Now()
            end
        end
    end
end

Log("HSPose loaded (" .. #SETS .. " sets" .. (QUICK and ", QUICK" or "") .. ")")
LoopAsync(20, function()
    ExecuteInGameThread(function() Try("Step", Step) end)
    return st.phase == "done"
end)
