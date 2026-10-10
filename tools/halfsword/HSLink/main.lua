-- HSLink: passthrough step 2 (ai-game-modding-guides guide 02; universal-modder mashup-mods pattern 2, "start small").
-- Read-only: every ~16 ms on the game thread, the Half Sword player's actor location + yaw and its pelvis and right hand
-- world positions (physical body; UE units: cm, X forward, Y right, Z up) go to one line of
-- %USERPROFILE%\halfsword-decomp\hslink_state.txt:
--   seq actorX actorY actorZ actorYaw pelvisX pelvisY pelvisZ handX handY handZ END
--   POSE seq <arm pose features, see PoseLine> END          (phase 1, second line)
-- No clock is sent: Lua's os.clock is CPU time, not wall time (the GTA passthrough note's timing gotcha). The receiver
-- times samples by when their seq changes. Bannerlord (HalfSwordCombat, Link\HsLink.cs) reads the line.
-- Nothing in Half Sword's gameplay is changed. The only settings touched keep a minimized window ticking and its bones
-- updated (as HSGrip needed).
local UEHelpers = require("UEHelpers")
local BASE = os.getenv("USERPROFILE") .. "\\halfsword-decomp\\"
local OUT = BASE .. "hslink_state.txt"
local LOG = BASE .. "hslink.log"

local function Log(...)
    local f = io.open(LOG, "a")
    if f then f:write(os.date("%H:%M:%S "), ...) f:write("\n") f:close() end
end

local function F(x) return string.format("%.3f", tonumber(x) or 0) end

local st = { seq = 0, setup = false, steps = 0, loadedAt = os.time() }

-- Lab only, opt-in: %USERPROFILE%\halfsword-decomp\hslink_lab.txt holding the words "duel" and/or "calm".
--   duel: no player pawn 8 s after load -> the game mode's own "GM Duel" (as HSPose did), so a minimized demo streams
--         without anyone clicking through the menu;
--   calm: the player is invulnerable and the other Willies are removed, so an unattended duel doesn't end the test;
--   arm:  the right arm follows hslink_arm.txt ("guard aimX aimY", e.g. "1 0 -60" = Guard Right, aim up), set every
--         tick through the same Willie_BP inputs HSPose used (R Down, R_Guarding, Aim Vector), so a test can move
--         the real physical arm without anyone at the mouse (tools\gabs\link_ab.py --real-arm).
-- Without the file HSLink stays read-only.
local LAB = (function()
    local f = io.open(BASE .. "hslink_lab.txt", "r")
    if not f then return {} end
    local s = f:read("*a") or ""
    f:close()
    return { duel = s:find("duel") ~= nil, calm = s:find("calm") ~= nil, arm = s:find("arm") ~= nil,
             input = s:find("input") ~= nil, catalog = s:find("catalog") ~= nil, blmouse = s:find("blmouse") ~= nil,
             period = tonumber(s:match("period=(%d+)")) } -- loop period in ms, for stress tests only (default 16)
end)()

-- Passthrough phase 3 (input), lab switch "input": Bannerlord has the focus, so the player's mouse and combat buttons
-- come from %USERPROFILE%\halfsword-decomp\hslink_input.txt, written by Bannerlord (Link\HsInputLink.cs) as
--   seq sumMouseX sumMouseY guardRight guardLeft alt END
-- (Bannerlord's raw mouse moves summed since the start, Bannerlord's y down; buttons 0/1). Nothing of Half Sword's
-- control logic is rewritten: the engine's own input dispatch is replaced one for one. Willie's input is disabled
-- (DisableInput) and, once per elapsed engine frame, every input event Willie_BP binds is called the way UE's input
-- system calls it: action/key events on press and release, then every axis event with that frame's value (0 for the
-- axes Bannerlord keeps: walking, gamepad). Update Aim() uses the frame's delta seconds on every call, so extra calls
-- on top of the engine's would change Half Sword's aim and swing speeds.
-- Axis values as UE's PlayerInput makes them: raw mouse x the AxisConfig sensitivity of MouseX/MouseY, x the mapping
-- scale, x FOVScale * FOV when bEnableFOVScaling; all read from this game's InputSettings at start, logged. Mouse
-- smoothing is not reproduced (logged when the game has it on).
-- Names: Willie_BP's input functions (dump bp/Willie_BP.txt); presses: InputActionEvent_7 / InputKeyEvent_0, releases:
-- _8 / _1 (Thrust_7 sets Alt Thrusting = true, Thrust_8 sets it false).
local INPUT = BASE .. "hslink_input.txt"
local EV = {
    turnMouse = "InpAxisEvt_Turn Right / Left Mouse_K2Node_InputAxisEvent_16",
    lookMouse = "InpAxisEvt_Look Up / Down Mouse_K2Node_InputAxisEvent_17",
    guardR = "InpAxisEvt_Right Guard Axis_K2Node_InputAxisEvent_6",
    guardL = "InpAxisEvt_Left Guard Axis_K2Node_InputAxisEvent_7",
    thrustDown = "InpActEvt_Thrust_K2Node_InputActionEvent_7",
    thrustUp = "InpActEvt_Thrust_K2Node_InputActionEvent_8",
    rmbDown = "InpActEvt_RightMouseButton_K2Node_InputKeyEvent_0",
    rmbUp = "InpActEvt_RightMouseButton_K2Node_InputKeyEvent_1",
}
-- Every axis event in Willie_BP, in binding-node order; the ones not in EV get 0 each frame.
local AXIS_ORDER = {
    "InpAxisEvt_Turn Right / Left Gamepad_K2Node_InputAxisEvent_0",
    "InpAxisEvt_Look Up / Down Gamepad_K2Node_InputAxisEvent_1",
    "InpAxisEvt_Right Arm Axis_K2Node_InputAxisEvent_2",
    "InpAxisEvt_Left Arm Axis_K2Node_InputAxisEvent_3",
    "InpAxisEvt_Key Forward_K2Node_InputAxisEvent_4",
    "InpAxisEvt_Key Right_K2Node_InputAxisEvent_5",
    EV.guardR, EV.guardL,
    "InpAxisEvt_Move Forward / Backward_K2Node_InputAxisEvent_14",
    EV.turnMouse, EV.lookMouse,
    "InpAxisEvt_Move Right / Left_K2Node_InputAxisEvent_19",
}

-- UE PlayerInput's mouse axis scaling, from this game's InputSettings.
local function MouseScaling(pc)
    local m = { sensX = nil, sensY = nil, scaleTurn = nil, scaleLook = nil, fov = 1 }
    local settings = StaticFindObject("/Script/Engine.Default__InputSettings")
    settings.AxisConfig:ForEach(function(_, element)
        local c = element:get()
        local key = c.AxisKeyName:ToString()
        local p = c.AxisProperties
        local s = p.Sensitivity * (p.bInvert and -1 or 1)
        if key == "MouseX" then m.sensX, m.expX = s, p.Exponent end
        if key == "MouseY" then m.sensY, m.expY = s, p.Exponent end
    end)
    settings.AxisMappings:ForEach(function(_, element)
        local a = element:get()
        local name, key = a.AxisName:ToString(), a.Key.KeyName:ToString()
        if name == "Turn Right / Left Mouse" and key == "MouseX" then m.scaleTurn = a.Scale end
        if name == "Look Up / Down Mouse" and key == "MouseY" then m.scaleLook = a.Scale end
    end)
    if settings.bEnableFOVScaling then
        m.fov = settings.FOVScale * pc.PlayerCameraManager:GetFOVAngle()
    end
    Log("lab: input scaling MouseX sens=", tostring(m.sensX), " exp=", tostring(m.expX), " MouseY sens=", tostring(m.sensY),
        " exp=", tostring(m.expY), " turn scale=", tostring(m.scaleTurn), " look scale=", tostring(m.scaleLook),
        " fov factor=", F(m.fov), " smoothing=", tostring(settings.bEnableMouseSmoothing))
    if m.sensX == nil or m.sensY == nil or m.scaleTurn == nil or m.scaleLook == nil then
        error("mouse axis config or mapping missing in InputSettings")
    end
    return m
end

local function ReadInputLine()
    local f = io.open(INPUT, "r")
    if not f then return nil end
    local line = f:read("*l")
    f:close()
    if not line then return nil end
    local seq, x, y, r, l, alt = line:match("^(%d+) (%S+) (%S+) (%d) (%d) (%d) END")
    if not seq then return nil end
    return { seq = tonumber(seq), x = tonumber(x), y = tonumber(y), r = r == "1", l = l == "1", alt = alt == "1" }
end

-- The player's raw mouse (tools\halfsword\hs_mouse_relay.ps1, Windows Raw Input): "seq sumRawX sumRawY END", device
-- counts, y down. Played directly, Unreal makes MouseX/MouseY from these same counts; Bannerlord's GetMouseMoveX/Y passes
-- on only part of them (x 0.77, y 0.56, slow moves 0.47: tools\gabs\mouse_gain.ps1, 2026-10-05 22:29), which made
-- Willie's moves slow, weak and imprecise. Used while the relay writes and Bannerlord's input line is live (the player
-- is in a mission); lab switch "blmouse" goes back to Bannerlord's mouse for A/B tests.
local MOUSE = BASE .. "hslink_mouse.txt"

local function ReadMouseLine()
    local f = io.open(MOUSE, "r")
    if not f then return nil end
    local line = f:read("*l")
    f:close()
    if not line then return nil end
    local seq, x, y = line:match("^(%d+) (%-?%d+) (%-?%d+) END")
    if not seq then return nil end
    return { seq = tonumber(seq), x = tonumber(x), y = tonumber(y) }
end

local function Call(pawn, name, value)
    pawn[name](pawn, value)
end

local function LabInput(pawn, pc)
    local world = UEHelpers.GetWorld()
    local now = UEHelpers.GetGameplayStatics():GetRealTimeSeconds(world)
    local frame = UEHelpers.GetKismetSystemLibrary():GetFrameCount()
    local inp = st.inp
    if inp == nil or inp.pawn ~= pawn:GetAddress() then
        pawn:DisableInput(pc)
        inp = { pawn = pawn:GetAddress(), scaling = MouseScaling(pc), frame = frame, r = false, l = false, alt = false,
                sx = nil, sy = nil, px = 0, py = 0, seq = -1, newAt = -1 }
        st.inp = inp
        Log("lab: input from Bannerlord, Willie's own input disabled")
    end
    local s = ReadInputLine()
    local blDx, blDy = 0, 0
    if s and s.seq ~= inp.seq then
        if inp.sx ~= nil then
            blDx, blDy = s.x - inp.sx, s.y - inp.sy
        end
        inp.sx, inp.sy, inp.seq, inp.newAt, inp.last = s.x, s.y, s.seq, now, s
    end
    local live = inp.last ~= nil and now - inp.newAt <= 0.5
    local rawOff = LAB.blmouse or (st.tune and st.tune.blmouse)
    local r = not rawOff and ReadMouseLine() or nil -- nil also when caught mid-write: the next step catches up
    local rawDx, rawDy = 0, 0
    if rawOff then
        inp.rx, inp.ry, inp.rseq, inp.rNewAt = nil, nil, nil, nil -- a new baseline when raw comes back: no jump
    elseif r and r.seq ~= inp.rseq then
        if inp.rx ~= nil then
            rawDx, rawDy = r.x - inp.rx, r.y - inp.ry
        end
        inp.rx, inp.ry, inp.rseq, inp.rNewAt = r.x, r.y, r.seq, now
    end
    local rawLive = inp.rNewAt ~= nil and now - inp.rNewAt <= 0.25
    local source = rawLive and "raw" or "bannerlord"
    if source ~= inp.source then
        inp.source = source
        Log("lab: mouse source ", source)
    end
    if live then -- mouse moves not yet given to a frame; raw ones only while the player is in a mission
        inp.px = inp.px + (rawLive and rawDx or blDx)
        inp.py = inp.py + (rawLive and rawDy or blDy)
    end
    local want = live and inp.last or { r = false, l = false, alt = false }
    local frames = frame - inp.frame
    if frames <= 0 then return end -- no new engine frame: the engine would not have dispatched input either
    inp.frame = frame
    -- Minimized, Half Sword runs about 400 frames a second: ~7 per 16 ms step (lab 2026-10-05). The cap only guards a
    -- stall; it must stay above the normal count or the dt-based parts of Update Aim() lose time.
    if frames > 64 then frames = 64 end
    -- Stats for the log: engine frames per step and the largest mouse axis value given to one frame.
    inp.statSteps, inp.statFrames = (inp.statSteps or 0) + 1, (inp.statFrames or 0) + frames
    inp.statMaxFrames = math.max(inp.statMaxFrames or 0, frames)
    local key = function(k) return { KeyName = FName(k) } end
    local m = inp.scaling
    for i = 1, frames do
        -- Actions and keys first, then axes (UE fires key/action delegates before axis delegates each frame).
        if i == 1 then
            -- Live tuning "lift=N" (hslink_tune.txt): a strike starts with the aim at least N above neutral (+Y = mouse
            -- down; the raw aim Aim Vector_0 is the mouse summed in a disc of radius 90). Half Sword's neutral is a
            -- waist-high guard: the user's sideways strikes (2026-10-05 23:29, strike_watch) kept the aim level, and at
            -- full swing speed the extended arm dipped ~35 cm mid-swing ("le coup part totalement par terre").
            -- First try (23:51) set Aim Vector_0 once at the press: no effect, since until Willie_BP counts him as
            -- guarding it copies the smoothed Aim Vector (~0) into Aim Vector_0. So both are held at that height for
            -- the first 0.15 s of the press; an aim already higher is left alone.
            -- Not while the left guard is held: on a two-handed weapon that is the half-sword grip, whose left hand has
            -- to catch the blade (2026-10-06 00:02, lift 25: it never caught it and the user found the right click
            -- "full bug").
            local lift = st.tune and st.tune.lift
            if lift and lift > 0 and want.r and not inp.r and not want.l then
                inp.liftUntil = now + 0.15
            end
            if not want.r or want.l then inp.liftUntil = nil end
            if want.alt ~= inp.alt then Call(pawn, want.alt and EV.thrustDown or EV.thrustUp, key("LeftAlt")) end
            if want.l ~= inp.l then Call(pawn, want.l and EV.rmbDown or EV.rmbUp, key("RightMouseButton")) end
            inp.alt, inp.l, inp.r = want.alt, want.l, want.r
        end
        -- The mouse moved during all the elapsed frames, so each gets its share, as the real device's moves arrive frame
        -- by frame. (Until 2026-10-05 the first frame got everything: on fast moves that multiplied the per-frame aim
        -- velocity Update Aim() sees, and Half Sword swung harder than for the same move with a real mouse.)
        -- Bannerlord's mouse y grows downward, UE's MouseY grows upward.
        local mx = inp.px / frames
        local my = -inp.py / frames
        inp.statMaxAxis = math.max(inp.statMaxAxis or 0, math.abs(mx * m.sensX), math.abs(my * m.sensY))
        local values = {
            [EV.turnMouse] = mx * m.sensX * m.scaleTurn * m.fov,
            [EV.lookMouse] = my * m.sensY * m.scaleLook * m.fov,
            [EV.guardR] = want.r and 1 or 0,
            [EV.guardL] = want.l and 1 or 0,
        }
        for _, name in ipairs(AXIS_ORDER) do Call(pawn, name, values[name] or 0) end
    end
    local lift = st.tune and st.tune.lift
    if lift and lift > 0 and inp.liftUntil and now <= inp.liftUntil then
        for _, name in ipairs({ "Aim Vector_0", "Aim Vector" }) do
            local ok, v = pcall(function() return pawn[name] end)
            if ok and v and v.Y > -lift then
                pawn[name] = { X = v.X, Y = -lift, Z = 0 }
            end
        end
    end
    inp.px, inp.py = 0, 0
    if now - (inp.statAt or now) >= 10 then
        Log("lab: input ", inp.statSteps, " steps, ", inp.statFrames, " frames (max ", inp.statMaxFrames,
            " per step), largest mouse axis value per frame ", F(inp.statMaxAxis or 0))
        inp.statSteps, inp.statFrames, inp.statMaxFrames, inp.statMaxAxis = 0, 0, 0, 0
    end
    if inp.statAt == nil or now - inp.statAt >= 10 then inp.statAt = now end
end

-- Willie armed like the Bannerlord player (lab switch "input"): Bannerlord (Link\HsWeaponMap.cs) writes
-- hslink_equip.txt as "rightPath leftPath twoHanded END" (paths under Weapons/Blueprints/Built_Weapons, "-" = empty
-- hand). Equipped through Willie_BP's own Set Up Right/Left Hand Weapon, as HSPose did; two-handed: R Two Handed Grip
-- and the game's own Attach L Hand To Offhand Position one second later, once the weapon has settled (HSPose). Checked
-- once a second, so a duel that re-arms Willie is corrected.
local WEAPONS = "/Game/Assets/Weapons/Blueprints/Built_Weapons/"

local function WeaponClass(rel)
    if rel == nil or rel == "-" then return nil end
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

local function HeldName(pawn, side)
    local ok, w = pcall(function() return pawn["Weapon " .. side] end)
    if ok and w ~= nil and w:IsValid() then return w:GetClass():GetFName():ToString() end
    return "-"
end

-- What the loadout check compares. Willie's empty hand can hold Half Sword's own fists (Weapon_Fists_C): that is still
-- an empty hand. Before this, a free left hand with fists differed from "-", so the weapon was equipped again every
-- second (2026-10-05 tests 1, 4, 7: 36 to 77 re-equips each).
local function HeldForCheck(pawn, side)
    local name = HeldName(pawn, side)
    if name:find("^Weapon_Fists") then return "-" end
    return name
end

-- Weapon catalogue (lab switch "catalog"): the real size and mass of every Half Sword weapon and shield, so Bannerlord
-- (Link\HsWeaponMap.cs) picks, within a family, the one nearest to the player's item instead of a fixed guess. The list
-- is the demo's own (halfsword-decomp\weapon_classes.txt, blueprints under Built_Weapons; improvised items, feet, fists
-- and farm tools left out). Each is equipped for a moment through Set Up Right/Left Hand Weapon and measured: every
-- static mesh component's box corners, projected on the weapon's BaseMesh axes (X, Y, Z; +Z = along the blade), give
-- its extent per axis (cm). Mass: BaseMesh:GetMass() (kg, 0 when the game has no body there). Output, once per launch:
-- halfsword-decomp\hs_catalog.txt, "path sizeX sizeY sizeZ mass".
local function CatalogList()
    local list, seen = {}, {}
    local f = io.open(BASE .. "weapon_classes.txt", "r")
    if not f then return list end
    for line in f:lines() do
        local rel = line:match("/Built_Weapons/(.-)%.[^/]+$")
        if rel and not seen[rel] and not rel:find("Improvized") and not rel:find("Weapon_Feet") and not rel:find("Weapon_Fists")
            and (not rel:find("Tools/") or rel:find("Tool_Axe") or rel:find("Tool_Hammer") or rel:find("Tool_Knife")) then
            seen[rel] = true
            list[#list + 1] = rel
        end
    end
    f:close()
    return list
end

local function Rotate(q, v)
    local x, y, z, w = q.X, q.Y, q.Z, q.W
    local tx, ty, tz = 2 * (y * v[3] - z * v[2]), 2 * (z * v[1] - x * v[3]), 2 * (x * v[2] - y * v[1])
    return { v[1] + w * tx + (y * tz - z * ty), v[2] + w * ty + (z * tx - x * tz), v[3] + w * tz + (x * ty - y * tx) }
end

local function MeasureHeld(pawn, side)
    local w = pawn["Weapon " .. side]
    if w == nil or not w:IsValid() then error("no weapon in hand " .. side) end
    local base = w.BaseMesh:K2_GetComponentToWorld()
    local axes = { Rotate(base.Rotation, { 1, 0, 0 }), Rotate(base.Rotation, { 0, 1, 0 }), Rotate(base.Rotation, { 0, 0, 1 }) }
    local lo, hi = { math.huge, math.huge, math.huge }, { -math.huge, -math.huge, -math.huge }
    local smc = StaticFindObject("/Script/Engine.StaticMeshComponent")
    local firstErr
    w:K2_GetComponentsByClass(smc):ForEach(function(_, element)
        -- UE4SS runs this callback in its own lua_pcall and throws a C++ exception on an error, which the outer pcall
        -- does not stop: 2026-10-05 18:12:14 one error here ("A function requiring userdata as param #1"), then the demo
        -- crashed 7 s later. So nothing may leave this function as an error.
        local ok, err = pcall(function()
            local c = element:get()
            if c == nil or not c:IsValid() then return end
            local mesh = c.StaticMesh
            if mesh == nil or not mesh:IsValid() then return end
            local box, t = mesh:GetBoundingBox(), c:K2_GetComponentToWorld()
            for _, cx in ipairs({ box.Min.X, box.Max.X }) do
                for _, cy in ipairs({ box.Min.Y, box.Max.Y }) do
                    for _, cz in ipairs({ box.Min.Z, box.Max.Z }) do
                        local p = Rotate(t.Rotation, { cx * t.Scale3D.X, cy * t.Scale3D.Y, cz * t.Scale3D.Z })
                        p = { p[1] + t.Translation.X, p[2] + t.Translation.Y, p[3] + t.Translation.Z }
                        for a = 1, 3 do
                            local d = p[1] * axes[a][1] + p[2] * axes[a][2] + p[3] * axes[a][3]
                            if d < lo[a] then lo[a] = d end
                            if d > hi[a] then hi[a] = d end
                        end
                    end
                end
            end
        end)
        if not ok and firstErr == nil then firstErr = tostring(err) end
    end)
    if lo[1] == math.huge then error(firstErr or "no static mesh") end
    local okMass, mass = pcall(function() return w.BaseMesh:GetMass() end)
    return string.format("%.1f %.1f %.1f %.2f", hi[1] - lo[1], hi[2] - lo[2], hi[3] - lo[3], okMass and mass or 0)
end

-- One step of the catalogue; true while it runs (the loadout sync waits for it).
local function LabCatalog(pawn)
    local now = UEHelpers.GetGameplayStatics():GetRealTimeSeconds(UEHelpers.GetWorld())
    local c = st.cat
    -- Each line is written as soon as it is measured: if the demo stops halfway, the file says how far it got.
    local function Write(line, mode)
        local f = io.open(BASE .. "hs_catalog.txt", mode or "a")
        if f then f:write(line, "\n"); f:close() end
    end
    if c == nil then
        c = { list = CatalogList(), i = 0, phase = "equip", n = 0 }
        st.cat = c
        Write("# HSLink weapon catalogue " .. os.date() .. ": path sizeX sizeY sizeZ (cm, BaseMesh axes, Z = along the blade) mass (kg)", "w")
        Log("lab: catalog of ", #c.list, " weapons")
    end
    if c.done then return false end
    if c.wait and now < c.wait then return true end
    if c.phase == "equip" then
        c.i = c.i + 1
        local rel = c.list[c.i]
        if rel == nil then
            Write("# done")
            c.done, st.equipLine = true, nil
            Log("lab: catalog written, ", c.n, " measured of ", #c.list)
            return false
        end
        c.side = rel:find("Shield_") and "L" or "R"
        local okCls, cls = pcall(WeaponClass, rel)
        if not okCls or cls == nil then
            Write(rel .. " FAILED no_class")
            return true
        end
        pawn["R Two Handed Grip"] = false
        pawn["Set Up " .. (c.side == "L" and "Left" or "Right") .. " Hand Weapon"](pawn, cls, nil, false, true, {})
        c.phase, c.wait = "measure", now + 0.3
    else
        local ok, line = pcall(MeasureHeld, pawn, c.side)
        if ok then c.n = c.n + 1 end
        Write(c.list[c.i] .. " " .. (ok and line or ("FAILED " .. tostring(line):gsub("%s+", "_"))))
        c.phase = "equip"
    end
    return true
end

local function LabEquip(pawn)
    local world = UEHelpers.GetWorld()
    local now = UEHelpers.GetGameplayStatics():GetRealTimeSeconds(world)
    if LAB.catalog and LabCatalog(pawn) then return end
    if st.attachAt and now >= st.attachAt then
        st.attachAt = nil
        pawn["R Two Handed Grip"] = true
        pawn["Attach L Hand To Offhand Position"](pawn)
        Log("lab: equip two-handed grip, left hand attached ", tostring(pawn["L Hand In Offhand Attached"]))
    end
    if now - (st.equipCheck or 0) < 1 then return end
    st.equipCheck = now
    local f = io.open(BASE .. "hslink_equip.txt", "r")
    if not f then return end
    local line = f:read("*l") or ""
    f:close()
    local right, left, two = line:match("^(%S+) (%S+) (%d) END")
    if not right then return end
    local wantR = right == "-" and "-" or right:match("([^/]+)$") .. "_C"
    local wantL = left == "-" and "-" or left:match("([^/]+)$") .. "_C"
    if line == st.equipLine and HeldForCheck(pawn, "R") == wantR and HeldForCheck(pawn, "L") == wantL then return end
    pawn["R Two Handed Grip"] = false
    pawn["Set Up Right Hand Weapon"](pawn, WeaponClass(right), nil, false, true, {})
    pawn["Set Up Left Hand Weapon"](pawn, WeaponClass(left), nil, false, true, {})
    st.equipLine = line
    st.attachAt = (two == "1") and now + 1 or nil
    Log("lab: equip R=", right, " L=", left, " two-handed=", two, " -> held R=", HeldName(pawn, "R"), " L=", HeldName(pawn, "L"))
end

local function LabArm(pawn)
    if st.steps % 6 == 0 or st.arm == nil then -- re-read the command about ten times a second
        local f = io.open(BASE .. "hslink_arm.txt", "r")
        if f then
            local g, x, y = (f:read("*l") or ""):match("^%s*(%S+)%s+(%S+)%s+(%S+)")
            f:close()
            if g then
                local arm = { guard = tonumber(g) == 1, x = tonumber(x) or 0, y = tonumber(y) or 0 }
                if st.arm == nil or arm.guard ~= st.arm.guard or arm.x ~= st.arm.x or arm.y ~= st.arm.y then
                    Log("lab: arm guard=", tostring(arm.guard), " aim=", F(arm.x), ",", F(arm.y))
                end
                st.arm = arm
            end
        end
    end
    local a = st.arm
    if a == nil then return end
    pawn["R Down"] = a.guard
    pawn["R_Guarding"] = a.guard
    pawn["Any_Guarding"] = a.guard
    pawn["Aim Vector_0"] = { X = a.x, Y = a.y, Z = 0 }
    pawn["Aim Vector"] = { X = a.x, Y = a.y, Z = 0 }
end

-- Passthrough phase 1: the second line, "POSE seq <features> END", is the physical body's arm pose in the arm atlas's
-- features and frame (tools/halfsword/make_arm_atlas.py), so Bannerlord's ArmRetarget takes it as it takes an atlas
-- pose. Per arm r then l: shoulder, elbow, wrist positions (upperarm, lowerarm, hand); hand, clavicle, upper-arm,
-- forearm rotations; then pelvis and spine_02..05 rotations. Character frame: right +X (= -X of Unreal's component
-- space, where Willie faces +Y), forward +Y, up +Z, metres, pelvis-relative; a rotation (x, y, z, w) -> (x, -y, -z, w).
-- Read from the mesh component space (RTS_Component = 2) of the physical mesh, i.e. what Half Sword draws.
local ARM_BONES = { r = { "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r" }, l = { "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l" } }
local TRUNK = { "pelvis", "spine_02", "spine_03", "spine_04", "spine_05" }

local function Q(t, out)
    local q = t.Rotation
    out[#out + 1] = string.format("%.5f %.5f %.5f %.5f", q.X, -q.Y, -q.Z, q.W)
end

-- Live tuning "pose=driver" (hslink_tune.txt): the pose comes from Willie's DriverSkeleton, the kinematic target his
-- physical body follows (the aim, the swing animations), instead of the physical mesh with its lag, inertia and wobble.
-- The user, 2026-10-05 23:10: "l'arme est trop flasque dans les mouvements, ça doit être propre". Half Sword's own data
-- either way; its physics still runs in Half Sword.
-- Tried 2026-10-05 23:12: the pure DriverSkeleton is "beaucoup trop raide, ça fait même des saccades". So "driver=W"
-- (0..1, hslink_tune.txt) blends the two: W of the driver's pose, 1-W of the physical body's (positions lerped,
-- rotations nlerped). "pose=driver" = W 1.
local function DriverWeight(pawn)
    local w = st.tune and (st.tune.pose == "driver" and 1 or st.tune.driver) or 0
    if w <= 0 then return 0, nil end
    local ok, d = pcall(function() return pawn.DriverSkeleton end)
    if not ok or d == nil or not d:IsValid() then return 0, nil end
    return math.min(1, w), d
end

local function Blend(a, b, w)
    if b == nil or w <= 0 then return a end
    local pa, pb, qa, qb = a.Translation, b.Translation, a.Rotation, b.Rotation
    local s = (qa.X * qb.X + qa.Y * qb.Y + qa.Z * qb.Z + qa.W * qb.W) < 0 and -1 or 1 -- same hemisphere
    local q = { X = qa.X + (s * qb.X - qa.X) * w, Y = qa.Y + (s * qb.Y - qa.Y) * w,
                Z = qa.Z + (s * qb.Z - qa.Z) * w, W = qa.W + (s * qb.W - qa.W) * w }
    local n = math.sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W)
    q.X, q.Y, q.Z, q.W = q.X / n, q.Y / n, q.Z / n, q.W / n
    return { Translation = { X = pa.X + (pb.X - pa.X) * w, Y = pa.Y + (pb.Y - pa.Y) * w, Z = pa.Z + (pb.Z - pa.Z) * w },
             Rotation = q }
end

local function PoseLine(pawn)
    local w, driver = DriverWeight(pawn)
    local mesh, comp = pawn.Mesh, {}
    local function get(b)
        if comp[b] == nil then
            local name = FName(b)
            comp[b] = Blend(mesh:GetSocketTransform(name, 2), driver and driver:GetSocketTransform(name, 2) or nil, w)
        end
        return comp[b]
    end
    local p0 = get("pelvis").Translation
    local out = {}
    for _, side in ipairs({ "r", "l" }) do
        local b = ARM_BONES[side]
        for _, bone in ipairs({ b[2], b[3], b[4] }) do
            local p = get(bone).Translation
            out[#out + 1] = string.format("%.4f %.4f %.4f", -(p.X - p0.X) / 100, (p.Y - p0.Y) / 100, (p.Z - p0.Z) / 100)
        end
        for _, bone in ipairs({ b[4], b[1], b[2], b[3] }) do Q(get(bone), out) end
    end
    for _, bone in ipairs(TRUNK) do Q(get(bone), out) end
    return table.concat(out, " ")
end

-- Third line, "GRIP seq twoHanded attached altGrip lDown searching releaseRate armLTonus END" (read-only telemetry): the
-- left hand's two-handed grip as Willie_BP keeps it (NOTES.md "Two-handed grip"). attached = L Hand In Offhand Attached,
-- altGrip = R Alt Grip (half-swording), lDown = L Down (left guard), searching = L Offhand Searching Grip (re-attach
-- under way), releaseRate = Grab L Release Rate (the left hand lets go at 1), armLTonus = Arm L Tonus. The 2026-10-05
-- playtest had Willie's wrists 0.24-0.70 m apart with every two-handed weapon, with or without the left guard held, so
-- whether the hand was pulled off or never re-attached has to be read here. hslink.log gets a line when the state flips.
local function GripLine(pawn)
    local function b(name) return pawn[name] and 1 or 0 end
    local g = { two = b("R Two Handed Grip"), att = b("L Hand In Offhand Attached"), alt = b("R Alt Grip"),
                ldown = b("L Down"), search = b("L Offhand Searching Grip"),
                rate = tonumber(pawn["Grab L Release Rate"]) or -1, tonus = tonumber(pawn["Arm L Tonus"]) or -1 }
    local key = g.two .. g.att .. g.alt .. g.ldown
    st.gripPeak = math.max(st.gripPeak or 0, g.rate)
    if key ~= st.gripKey then
        if g.two == 1 or (st.gripKey or ""):sub(1, 1) == "1" then
            Log("lab: grip two=", g.two, " attached=", g.att, " alt=", g.alt, " lDown=", g.ldown, " searching=", g.search,
                " release=", F(g.rate), " (peak since last ", F(st.gripPeak), ") tonusL=", F(g.tonus))
        end
        st.gripKey, st.gripPeak = key, g.rate
    end
    return string.format("%d %d %d %d %d %.3f %.3f", g.two, g.att, g.alt, g.ldown, g.search, g.rate, g.tonus)
end

-- Read-only, every 5 s in hslink.log: what makes Willie strong or limp (Willie_BP): joint motors get Muscle Power x
-- All Body Tonus (0-100) and the arms x Arm Tonus x Upper Body Tonus; the physical animation's StrengthMultiplyer
-- follows All Body Tonus. The user, 2026-10-05 22:30: the moves feel "flasque, pas ferme et sans force". Also engine
-- frames per second (the demo runs minimized, ~380 fps), and once per other Willie its Muscle Power, for comparison.
local BODY_VARS = { "Muscle Power", "All Body Tonus", "Upper Body Tonus", "Arm R Tonus", "Arm L Tonus", "R Arm Extra Tonus",
                    "Exhaustion", "Stamina", "Consciousness", "Health", "Angular Strength", "Orientation Strength", "Max Angular Force" }

local function BodyLog(pawn)
    local now = os.time()
    if now - (st.bodyAt or 0) < 5 then return end
    local frame = UEHelpers.GetKismetSystemLibrary():GetFrameCount()
    local fps = st.bodyFrame and (frame - st.bodyFrame) / math.max(1, now - st.bodyAt) or 0
    st.bodyAt, st.bodyFrame = now, frame
    local out = {}
    for _, name in ipairs(BODY_VARS) do
        local ok, v = pcall(function() return pawn[name] end)
        out[#out + 1] = name .. "=" .. (ok and F(v) or "?")
    end
    local okS, strength = pcall(function() return pawn.PhysicalAnimation.StrengthMultiplyer end)
    out[#out + 1] = "PhysAnimStrength=" .. (okS and F(strength) or "?")
    Log("body: ", table.concat(out, " "), " fps=", string.format("%.0f", fps))
end

local function OtherWillies(pawn)
    st.seenWillies = st.seenWillies or {}
    for _, w in ipairs(FindAllOf("Willie_BP_C") or {}) do
        local okA, addr = pcall(function() return w:GetAddress() end)
        if okA and w:IsValid() and addr ~= pawn:GetAddress() and not st.seenWillies[addr] then
            st.seenWillies[addr] = true
            local ok, mp = pcall(function() return w["Muscle Power"] end)
            local okT, t = pcall(function() return w["All Body Tonus"] end)
            Log("body: other Willie Muscle Power=", ok and F(mp) or "?", " All Body Tonus=", okT and F(t) or "?")
        end
    end
end

-- Live tuning of Willie's own stats, opt-in: %USERPROFILE%\halfsword-decomp\hslink_tune.txt, "key=value" per line, read
-- once a second. muscle = Muscle Power (Willie_BP: joint motor strength = Muscle Power x All Body Tonus; never written
-- by Willie_BP itself, 35 for every Willie of the demo duel). Probe 2026-10-05 22:40: a fast horizontal swing lifts the
-- wrist to 0.66-0.68 m while the same aim reached slowly holds 0.38-0.42 m: the weapon's momentum carries the arm up,
-- which the user saw as "l'arme se lève sans que la souris monte". Without the file nothing is touched.
local function LiveTune(pawn)
    if st.steps % 60 ~= 0 and st.tune ~= nil then
        if st.tune.muscle then pawn["Muscle Power"] = st.tune.muscle end
        return
    end
    local tune = {}
    local f = io.open(BASE .. "hslink_tune.txt", "r")
    if f then
        local s = f:read("*a") or ""
        f:close()
        tune.muscle = tonumber(s:match("muscle%s*=%s*([%d%.]+)"))
        tune.blmouse = s:match("blmouse%s*=%s*1") ~= nil -- live A/B: Bannerlord's mouse instead of the raw relay
        tune.pose = s:match("pose%s*=%s*(%a+)")            -- "driver": stream the DriverSkeleton (PoseLine)
        tune.driver = tonumber(s:match("driver%s*=%s*([%d%.]+)")) -- 0..1: blend of driver and physical pose
        tune.lift = tonumber(s:match("lift%s*=%s*([%d%.]+)"))     -- aim units: strike start height (LabInput)
        tune.maxfps = tonumber(s:match("maxfps%s*=%s*(%d+)"))     -- Half Sword's frame cap (t.MaxFPS), 0 = none
    end
    -- Minimized, the demo runs 300-380 frames a second; played directly, about the monitor's rate. Willie's physics
    -- and parts of his aim logic run once per frame, so the frame rate may change how nervous he is (the user,
    -- 2026-10-05 23:33: "les mouvements partent dans tous les sens"). t.MaxFPS is the engine's own cap.
    local fps = tune.maxfps or 0
    if fps ~= (st.maxfps or 0) then
        st.maxfps = fps
        local ok, err = pcall(function()
            UEHelpers.GetKismetSystemLibrary():ExecuteConsoleCommand(UEHelpers.GetWorld(), "t.MaxFPS " .. fps,
                UEHelpers.GetPlayerController())
        end)
        -- 2026-10-05 23:33: t.MaxFPS alone left the demo at ~190 fps: the game's own frame rate limit (GameUserSettings,
        -- applied by the engine) wins, so that one is set too.
        local okG, errG = pcall(function()
            local gus = UEHelpers.GetEngine().GameUserSettings
            gus:SetFrameRateLimit(fps)
            gus:ApplyNonResolutionSettings()
        end)
        Log("lab: tune maxfps=", fps, ok and "" or (" console FAILED " .. tostring(err)),
            okG and " (GameUserSettings set)" or (" GameUserSettings FAILED " .. tostring(errG)))
    end
    if (st.tune and st.tune.lift) ~= tune.lift then
        Log("lab: tune lift=", tostring(tune.lift))
    end
    if (st.tune and st.tune.driver) ~= tune.driver then
        Log("lab: tune driver=", tostring(tune.driver))
    end
    if st.tune ~= nil and tune.blmouse ~= st.tune.blmouse then
        Log("lab: tune blmouse=", tostring(tune.blmouse))
    end
    if (st.tune and st.tune.pose) ~= tune.pose then
        Log("lab: tune pose=", tostring(tune.pose or "mesh"))
    end
    if st.tune == nil or tune.muscle ~= st.tune.muscle then
        if st.tune ~= nil or tune.muscle then
            Log("lab: tune muscle=", tostring(tune.muscle), " (Muscle Power was ", F(pawn["Muscle Power"]), ")")
        end
        if tune.muscle == nil and st.tune ~= nil and st.tune.muscle and st.muscle0 then
            pawn["Muscle Power"] = st.muscle0 -- file removed: the demo's own value again
        end
    end
    if st.muscle0 == nil then st.muscle0 = pawn["Muscle Power"] end
    st.tune = tune
    if tune.muscle then pawn["Muscle Power"] = tune.muscle end
end

-- Fourth line, "WEAPON seq zx zy zz xx xy xz END": the right weapon's BaseMesh +Z and +X axes in the POSE line's frame
-- (Willie's mesh component space, then x -> -x: right +X, forward +Y, up +Z). Willie holds his weapon by a physics
-- constraint, so during a swing the blade can trail the hand; Bannerlord's copy is fixed to its hand bone. The user,
-- 2026-10-05 23:05: on big swings "l'arme se lève sans que la souris monte"; Bannerlord compares its copy's blade with
-- this one (LinkMarker).
local function WeaponLine(pawn)
    local w = pawn["Weapon R"]
    if w == nil or not w:IsValid() then return nil end
    local qw = w.BaseMesh:K2_GetComponentToWorld().Rotation
    local qm = pawn.Mesh:K2_GetComponentToWorld().Rotation
    local inv = { X = -qm.X, Y = -qm.Y, Z = -qm.Z, W = qm.W }
    local z = Rotate(inv, Rotate(qw, { 0, 0, 1 }))
    local x = Rotate(inv, Rotate(qw, { 1, 0, 0 }))
    return string.format("%.4f %.4f %.4f %.4f %.4f %.4f", -z[1], z[2], z[3], -x[1], x[2], x[3])
end

-- Fifth line, "AIM seq aimX aimY rawX rawY swingSpeed END" (read-only): Willie_BP's Aim Vector (the aim the arm uses,
-- which follows the raw one at a limited speed), Aim Vector_0 (mouse moves summed, clamped to a disc of radius 90) and
-- Aim Swing Speed. The user, 2026-10-05 23:24: strikes moved the mouse mostly sideways, yet Willie's wrist dropped
-- 40-52 cm and his blade pointed 33-44 deg down.
local function AimLine(pawn)
    local a, r = pawn["Aim Vector"], pawn["Aim Vector_0"]
    return string.format("%.2f %.2f %.2f %.2f %.2f", a.X, a.Y, r.X, r.Y, tonumber(pawn["Aim Swing Speed"]) or -1)
end

local function Step()
    st.steps = st.steps + 1
    local pc = UEHelpers.GetPlayerController()
    local pawn = pc and pc:IsValid() and pc.Pawn or nil
    if pawn == nil or not pawn:IsValid() or pawn:GetClass():GetFName():ToString() ~= "Willie_BP_C" then
        if LAB.duel and not st.duelCalled and os.time() - st.loadedAt > 8 then
            st.duelCalled = true
            local ok, err = pcall(function() local gm = UEHelpers.GetGameModeBase() gm["GM Duel"](gm) end)
            Log("lab: GM Duel called", ok and "" or (" FAILED " .. tostring(err)))
        end
        return
    end
    if LAB.duel and st.steps % 30 == 0 then
        -- Unattended: a minimized demo can sit paused (the 2026-10-05 POSE line did not move at all, sd 0, while the arm
        -- inputs were set); HSPose kept it running the same way.
        pcall(function()
            local world, gs = UEHelpers.GetWorld(), UEHelpers.GetGameplayStatics()
            if gs:IsGamePaused(world) then
                gs:SetGamePaused(world, false)
                st.unpaused = (st.unpaused or 0) + 1
                if st.unpaused <= 3 then Log("lab: game was paused, unpaused") end
            end
        end)
    end
    local okT, errT = pcall(LiveTune, pawn)
    if not okT and not st.tuneErr then
        st.tuneErr = true
        Log("lab: tune FAILED ", tostring(errT))
    end
    pcall(BodyLog, pawn)
    if LAB.calm then
        -- No fatigue either (the user, 2026-10-05 23:31: better at first, "après certains coups il s'est mis à se
        -- baisser, peut-être la fatigue, qu'il faut désactiver"). Willie_BP scales ~15 things by Stamina (0-100: five
        -- factors 1 -> 0.5, the aim speed 175 -> 100...) and caps the arm tonus at 1 - Exhaustion.
        pcall(function()
            if st.staminaLogged == nil then
                st.staminaLogged = true
                Log("lab: calm keeps Stamina 100 (was ", F(pawn.Stamina), ") and Exhaustion 0")
            end
            pawn.Stamina = 100
            pawn.Exhaustion = 0
        end)
        pcall(function()
            pawn.Invulnerable = true
            if st.steps % 20 == 0 then
                pcall(OtherWillies, pawn)
                for _, w in ipairs(FindAllOf("Willie_BP_C") or {}) do
                    if w:IsValid() and w:GetAddress() ~= pawn:GetAddress() then w:K2_DestroyActor() end
                end
            end
        end)
    end
    if LAB.input then
        local ok, err = pcall(LabInput, pawn, pc)
        if not ok then
            st.inputErrs = (st.inputErrs or 0) + 1
            if st.inputErrs <= 3 then Log("lab: input FAILED ", tostring(err)) end
        end
        local okE, errE = pcall(LabEquip, pawn)
        if not okE then
            st.equipErrs = (st.equipErrs or 0) + 1
            if st.equipErrs <= 3 then Log("lab: equip FAILED ", tostring(errE)) end
        end
    elseif LAB.arm then
        local ok, err = pcall(LabArm, pawn)
        if not ok and not st.armErr then
            st.armErr = true
            Log("lab: arm FAILED ", tostring(err))
        end
    end
    if not st.setup then
        st.setup = true
        pcall(function()
            UEHelpers.GetKismetSystemLibrary():ExecuteConsoleCommand(UEHelpers.GetWorld(), "t.IdleWhenNotForeground 0", pc)
        end)
        pcall(function() pawn.Mesh.VisibilityBasedAnimTickOption = 0 end)
        -- The driver skeleton too, as HSGrip did: the physical arm follows it. (2026-10-05: one minimized run streamed a
        -- POSE line that never moved; the next run moved without this. Cause unknown, so keep both updated.)
        pcall(function() pawn.DriverSkeleton.VisibilityBasedAnimTickOption = 0 end)
        Log("player found, streaming to ", OUT)
    end
    local actor = pawn:K2_GetActorLocation()
    local yaw = pawn:K2_GetActorRotation().Yaw
    local pelvis = pawn.Mesh:GetSocketTransform(FName("pelvis"), 0).Translation
    local hand = pawn.Mesh:GetSocketTransform(FName("hand_r"), 0).Translation
    local okPose, pose = pcall(PoseLine, pawn)
    if not okPose and not st.poseErr then
        st.poseErr = true
        Log("POSE line FAILED ", tostring(pose))
    end
    local okGrip, grip = pcall(GripLine, pawn)
    if not okGrip and not st.gripErr then
        st.gripErr = true
        Log("GRIP line FAILED ", tostring(grip))
    end
    local okAim, aim = pcall(AimLine, pawn)
    if not okAim and not st.aimErr then
        st.aimErr = true
        Log("AIM line FAILED ", tostring(aim))
    end
    local okWeapon, weapon = pcall(WeaponLine, pawn)
    if not okWeapon and not st.weaponErr then
        st.weaponErr = true
        Log("WEAPON line FAILED ", tostring(weapon))
    end
    st.seq = st.seq + 1
    local f = io.open(OUT, "w")
    if f then
        f:write(st.seq, " ", F(actor.X), " ", F(actor.Y), " ", F(actor.Z), " ", F(yaw), " ",
            F(pelvis.X), " ", F(pelvis.Y), " ", F(pelvis.Z), " ", F(hand.X), " ", F(hand.Y), " ", F(hand.Z), " END\n")
        if okPose then
            f:write("POSE ", st.seq, " ", pose, " END\n")
        end
        if okGrip then
            f:write("GRIP ", st.seq, " ", grip, " END\n")
        end
        if okWeapon and weapon then
            f:write("WEAPON ", st.seq, " ", weapon, " END\n")
        end
        if okAim then
            f:write("AIM ", st.seq, " ", aim, " END\n")
        end
        f:close()
    end
end

Log("# HSLink loaded ", os.date(), LAB.duel and " lab:duel" or "", LAB.calm and " lab:calm" or "", LAB.arm and " lab:arm" or "",
    LAB.input and " lab:input" or "", LAB.catalog and " lab:catalog" or "")
-- Every 16 ms, all on the game thread: UE4SS's delayed actions run from its engine tick hook. Until 2026-10-05 22:20
-- this was LoopAsync(16) + ExecuteInGameThread, so Lua ran on UE4SS's async thread (the loop) and on the game thread
-- (Step) in the same Lua state, which is not thread-safe. Every Half Sword crash since 2026-10-02 (8 minidumps) faults
-- inside UE4SS.dll or the C runtime it calls, each time at a different address: memory corruption, not one bug. The
-- async loop at 5 ms crashed 4 times out of 4 within ~2 minutes, at 16 ms after 7-26 minutes (MODLOG).
-- The period can be set in hslink_lab.txt ("period=5") for stress tests only.
local PERIOD = LAB.period or 16
local handle = LoopInGameThreadWithDelay(PERIOD, function()
    local ok, err = pcall(Step)
    if not ok and not st.errLogged then
        st.errLogged = true
        Log("ERROR ", tostring(err))
    end
end)
Log("loop: LoopInGameThreadWithDelay ", PERIOD, " ms, handle ", tostring(handle))
