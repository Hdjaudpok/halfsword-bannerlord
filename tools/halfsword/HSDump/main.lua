-- HSDump: read-only discovery dump of the Half Sword Demo physics setup.
-- Personal lab use for the HalfSwordCombat Bannerlord mod. Nothing from the game is redistributed.
-- Output goes outside any repository: %USERPROFILE%\halfsword-decomp\dumps\

local OUT = os.getenv("USERPROFILE") .. "\\halfsword-decomp\\dumps\\"

local function W(name, text)
    local f = io.open(OUT .. name, "a")
    if f then
        f:write(text, "\n")
        f:close()
    end
end

local function Log(text) W("hsdump.log", os.date("%H:%M:%S ") .. text) end

local function Try(label, fn)
    local ok, err = pcall(fn)
    if not ok then Log("ERROR in " .. label .. ": " .. tostring(err)) end
    return ok
end

local function N(fname)
    if fname == nil then return "nil" end
    local ok, s = pcall(function() return fname:ToString() end)
    if ok then return s end
    return tostring(fname)
end

local function F(x)
    if type(x) == "number" then return string.format("%.4f", x) end
    return tostring(x)
end

local function V3(v)
    if v == nil then return "nil" end
    local ok, s = pcall(function() return "(" .. F(v.X) .. "," .. F(v.Y) .. "," .. F(v.Z) .. ")" end)
    if ok then return s end
    return "?"
end

local function R3(r)
    if r == nil then return "nil" end
    local ok, s = pcall(function() return "(" .. F(r.Pitch) .. "," .. F(r.Yaw) .. "," .. F(r.Roll) .. ")" end)
    if ok then return s end
    return "?"
end

Log("HSDump loaded")

---------------------------------------------------------------- physics assets
local function DumpBody(i, bs)
    local line = string.format("  body[%d] bone=%s", i, N(bs.BoneName))
    Try("body.physicsType", function() line = line .. " physicsType=" .. tostring(bs.PhysicsType) end)
    Try("body.geometry", function()
        local agg = bs.AggGeom
        agg.SphylElems:ForEach(function(_, e)
            local s = e:get()
            line = line .. string.format(" sphyl[r=%s len=%s c=%s rot=%s]", F(s.Radius), F(s.Length), V3(s.Center), R3(s.Rotation))
        end)
        agg.SphereElems:ForEach(function(_, e)
            local s = e:get()
            line = line .. string.format(" sphere[r=%s c=%s]", F(s.Radius), V3(s.Center))
        end)
        agg.BoxElems:ForEach(function(_, e)
            local s = e:get()
            line = line .. string.format(" box[x=%s y=%s z=%s c=%s rot=%s]", F(s.X), F(s.Y), F(s.Z), V3(s.Center), R3(s.Rotation))
        end)
        agg.TaperedCapsuleElems:ForEach(function(_, e)
            local s = e:get()
            line = line .. string.format(" taperedCapsule[r0=%s r1=%s len=%s c=%s rot=%s]", F(s.Radius0), F(s.Radius1), F(s.Length), V3(s.Center), R3(s.Rotation))
        end)
        line = line .. " convexElems=" .. agg.ConvexElems:GetArrayNum()
    end)
    Try("body.instance", function()
        local bi = bs.DefaultInstance
        line = line .. string.format(" overrideMass=%s massKg=%s massScale=%s linDamp=%s angDamp=%s comNudge=%s inertiaScale=%s",
            tostring(bi.bOverrideMass), F(bi.MassInKgOverride), F(bi.MassScale), F(bi.LinearDamping), F(bi.AngularDamping),
            V3(bi.COMNudge), V3(bi.InertiaTensorScale))
    end)
    Try("body.physicalAnimation", function()
        local profiles = bs.PhysicalAnimationData
        if profiles == nil then return end
        profiles:ForEach(function(_, e)
            local p = e:get()
            local d = p.PhysicalAnimationData
            line = line .. string.format(" physAnim[%s local=%s ori=%s angVel=%s pos=%s vel=%s maxLin=%s maxAng=%s]",
                N(p.ProfileName), tostring(d.bIsLocalSimulation), F(d.OrientationStrength), F(d.AngularVelocityStrength),
                F(d.PositionStrength), F(d.VelocityStrength), F(d.MaxLinearForce), F(d.MaxAngularForce))
        end)
    end)
    W("physics.txt", line)
end

local function DriveText(d)
    return string.format("[pos=%s vel=%s k=%s d=%s max=%s]", tostring(d.bEnablePositionDrive), tostring(d.bEnableVelocityDrive),
        F(d.Stiffness), F(d.Damping), F(d.MaxForce))
end

local function ProfileText(p)
    local s = ""
    Try("profile.limits", function()
        local cone, tw, lin = p.ConeLimit, p.TwistLimit, p.LinearLimit
        s = s .. string.format(" swing1=%s/%s swing2=%s/%s twist=%s/%s coneSoft=%s k=%s d=%s twistSoft=%s k=%s d=%s lin=%s/%s/%s limit=%s",
            tostring(cone.Swing1Motion), F(cone.Swing1LimitDegrees), tostring(cone.Swing2Motion), F(cone.Swing2LimitDegrees),
            tostring(tw.TwistMotion), F(tw.TwistLimitDegrees), tostring(cone.bSoftConstraint), F(cone.Stiffness), F(cone.Damping),
            tostring(tw.bSoftConstraint), F(tw.Stiffness), F(tw.Damping),
            tostring(lin.XMotion), tostring(lin.YMotion), tostring(lin.ZMotion), F(lin.Limit))
    end)
    Try("profile.drive", function()
        local ad = p.AngularDrive
        s = s .. " drive(mode=" .. tostring(ad.AngularDriveMode) .. " slerp" .. DriveText(ad.SlerpDrive) ..
            " swing" .. DriveText(ad.SwingDrive) .. " twist" .. DriveText(ad.TwistDrive) ..
            " target=" .. R3(ad.OrientationTarget) .. " angVelTarget=" .. V3(ad.AngularVelocityTarget) .. ")"
    end)
    Try("profile.flags", function() s = s .. " disableCollision=" .. tostring(p.bDisableCollision) .. " parentDominates=" .. tostring(p.bParentDominates) end)
    return s
end

local function DumpConstraint(i, ct)
    local ci = ct.DefaultInstance
    local line = string.format("  constraint[%d] joint=%s bone1=%s bone2=%s", i, N(ci.JointName), N(ci.ConstraintBone1), N(ci.ConstraintBone2))
    Try("constraint.frames", function()
        line = line .. string.format(" pos1=%s pri1=%s sec1=%s pos2=%s pri2=%s sec2=%s",
            V3(ci.Pos1), V3(ci.PriAxis1), V3(ci.SecAxis1), V3(ci.Pos2), V3(ci.PriAxis2), V3(ci.SecAxis2))
    end)
    line = line .. ProfileText(ci.ProfileInstance)
    W("physics.txt", line)
    Try("constraint.profileHandles", function()
        ct.ProfileHandles:ForEach(function(_, e)
            local h = e:get()
            W("physics.txt", "    profile " .. N(h.ProfileName) .. ":" .. ProfileText(h.ProfileProperties))
        end)
    end)
end

local dumpedAssets = {}

local function DumpPhysicsAssets()
    local assets = FindAllOf("PhysicsAsset")
    local count = assets and #assets or 0
    local fresh = 0
    if assets == nil then return 0 end
    for _, pa in ipairs(assets) do
        local name = pa:GetFullName()
        if not dumpedAssets[name] then
            dumpedAssets[name] = true
            fresh = fresh + 1
            W("physics.txt", "== " .. name)
            Try("asset.profileNames", function()
                local names = {}
                pa.PhysicalAnimationProfiles:ForEach(function(_, e) table.insert(names, N(e:get())) end)
                local cnames = {}
                pa.ConstraintProfiles:ForEach(function(_, e) table.insert(cnames, N(e:get())) end)
                W("physics.txt", "  physicalAnimationProfiles=" .. table.concat(names, ",") .. " constraintProfiles=" .. table.concat(cnames, ","))
            end)
            Try("asset.bodies", function()
                local bodies = pa.SkeletalBodySetups
                W("physics.txt", "  bodies=" .. bodies:GetArrayNum())
                bodies:ForEach(function(i, e) DumpBody(i, e:get()) end)
            end)
            Try("asset.constraints", function()
                local cons = pa.ConstraintSetup
                W("physics.txt", "  constraints=" .. cons:GetArrayNum())
                cons:ForEach(function(i, e) DumpConstraint(i, e:get()) end)
            end)
        end
    end
    if fresh > 0 then Log("physics assets: " .. count .. " loaded, " .. fresh .. " newly dumped") end
    return count
end

---------------------------------------------------------------- characters
local seenComponents = {}

local function DumpCharacters()
    local comps = FindAllOf("SkeletalMeshComponent")
    if comps == nil then return end
    for _, c in ipairs(comps) do
        local name = c:GetFullName()
        if not seenComponents[name] then
            seenComponents[name] = true
            local line = name
            Try("comp.mesh", function()
                local mesh = c.SkeletalMeshAsset
                if mesh == nil or not mesh:IsValid() then mesh = c.SkeletalMesh end
                if mesh ~= nil and mesh:IsValid() then line = line .. " mesh=" .. mesh:GetFullName() end
            end)
            Try("comp.paOverride", function()
                local pa = c.PhysicsAssetOverride
                if pa ~= nil and pa:IsValid() then line = line .. " physicsAssetOverride=" .. pa:GetFullName() end
            end)
            Try("comp.blend", function() line = line .. " physicsBlend=" .. tostring(c.bBlendPhysics) .. " simulatePhysics=" .. tostring(c.BodyInstance.bSimulatePhysics) end)
            W("characters.txt", line)
        end
    end
    local pacs = FindAllOf("PhysicalAnimationComponent")
    if pacs ~= nil then
        for _, p in ipairs(pacs) do
            local name = p:GetFullName()
            if not seenComponents[name] then
                seenComponents[name] = true
                Try("pac", function() W("characters.txt", name .. " strengthMultiplier=" .. F(p.StrengthMultiplyer)) end)
            end
        end
    end
end

local function DumpObjectProperties(obj, file)
    Try("props " .. obj:GetFullName(), function()
        W(file, "#### " .. obj:GetFullName())
        local cls = obj:GetClass()
        while cls ~= nil and cls:IsValid() do
            local className = cls:GetFName():ToString()
            cls:ForEachProperty(function(prop)
                local pname = prop:GetFName():ToString()
                local ptype = prop:GetClass():GetFName():ToString()
                local value = ""
                local ok, v = pcall(function() return obj[pname] end)
                if ok and (type(v) == "number" or type(v) == "boolean" or type(v) == "string") then
                    value = " = " .. F(v)
                end
                W(file, "  " .. className .. "." .. pname .. " : " .. ptype .. value)
            end)
            cls = cls:GetSuperStruct()
        end
    end)
end

local pawnDumped = false

local function DumpPlayerPawn()
    if pawnDumped then return end
    local pc = FindFirstOf("PlayerController")
    if pc == nil or not pc:IsValid() then return end
    local pawn = pc.Pawn
    if pawn == nil or not pawn:IsValid() then return end
    pawnDumped = true
    Log("player pawn: " .. pawn:GetFullName())
    DumpObjectProperties(pawn, "pawn_properties.txt")
    DumpObjectProperties(pc, "controller_properties.txt")
end

---------------------------------------------------------------- runtime hooks (how the game drives the body)
local calls = {}

local function Hook(path, describe)
    local ok, err = pcall(function()
        RegisterHook(path, function(self, ...)
            calls[path] = (calls[path] or 0) + 1
            local n = calls[path]
            if n <= 40 or n % 500 == 0 then
                local args = {...}
                local text = ""
                Try("hook " .. path, function() text = describe(self, args) end)
                W("hooks.txt", os.date("%H:%M:%S ") .. path .. " #" .. n .. " " .. text)
            end
        end)
    end)
    Log((ok and "hooked " or "hook failed ") .. path .. (ok and "" or (": " .. tostring(err))))
end

local function P(a, i) local ok, v = pcall(function() return a[i]:get() end) if ok then return v end return nil end
local function Owner(self) local ok, s = pcall(function() return self:get():GetFullName() end) if ok then return s end return "?" end

local function RegisterHooks()
    local E = "/Script/Engine."
    Hook(E .. "PhysicalAnimationComponent:SetStrengthMultiplyer", function(s, a) return Owner(s) .. " strength=" .. F(P(a, 1)) end)
    Hook(E .. "PhysicalAnimationComponent:ApplyPhysicalAnimationProfileBelow", function(s, a) return Owner(s) .. " body=" .. N(P(a, 1)) .. " profile=" .. N(P(a, 2)) end)
    Hook(E .. "PhysicalAnimationComponent:ApplyPhysicalAnimationSettingsBelow", function(s, a)
        local d = P(a, 2)
        return Owner(s) .. " body=" .. N(P(a, 1)) .. string.format(" ori=%s angVel=%s pos=%s vel=%s maxLin=%s maxAng=%s",
            F(d.OrientationStrength), F(d.AngularVelocityStrength), F(d.PositionStrength), F(d.VelocityStrength), F(d.MaxLinearForce), F(d.MaxAngularForce))
    end)
    Hook(E .. "PhysicalAnimationComponent:ApplyPhysicalAnimationSettings", function(s, a)
        local d = P(a, 2)
        return Owner(s) .. " bone=" .. N(P(a, 1)) .. string.format(" ori=%s angVel=%s pos=%s vel=%s maxLin=%s maxAng=%s",
            F(d.OrientationStrength), F(d.AngularVelocityStrength), F(d.PositionStrength), F(d.VelocityStrength), F(d.MaxLinearForce), F(d.MaxAngularForce))
    end)
    Hook(E .. "SkeletalMeshComponent:SetAllMotorsAngularDriveParams", function(s, a) return Owner(s) .. " spring=" .. F(P(a, 1)) .. " damping=" .. F(P(a, 2)) .. " forceLimit=" .. F(P(a, 3)) end)
    Hook(E .. "SkeletalMeshComponent:SetConstraintProfile", function(s, a) return Owner(s) .. " joint=" .. N(P(a, 1)) .. " profile=" .. N(P(a, 2)) end)
    Hook(E .. "SkeletalMeshComponent:SetConstraintProfileForAll", function(s, a) return Owner(s) .. " profile=" .. N(P(a, 1)) end)
    Hook(E .. "SkeletalMeshComponent:SetAngularLimits", function(s, a) return Owner(s) .. " bone=" .. N(P(a, 1)) .. " swing1=" .. F(P(a, 2)) .. " twist=" .. F(P(a, 3)) .. " swing2=" .. F(P(a, 4)) end)
    Hook(E .. "SkeletalMeshComponent:SetAllBodiesBelowSimulatePhysics", function(s, a) return Owner(s) .. " bone=" .. N(P(a, 1)) .. " simulate=" .. tostring(P(a, 2)) end)
    Hook(E .. "SkeletalMeshComponent:SetAllBodiesBelowPhysicsBlendWeight", function(s, a) return Owner(s) .. " bone=" .. N(P(a, 1)) .. " weight=" .. F(P(a, 2)) end)
    Hook(E .. "SkeletalMeshComponent:SetAllBodiesPhysicsBlendWeight", function(s, a) return Owner(s) .. " weight=" .. F(P(a, 1)) end)
    Hook(E .. "PhysicsHandleComponent:GrabComponentAtLocationWithRotation", function(s, a) return Owner(s) .. " bone=" .. N(P(a, 2)) .. " at=" .. V3(P(a, 3)) end)
    Hook(E .. "PhysicsHandleComponent:SetTargetLocationAndRotation", function(s, a) return Owner(s) .. " loc=" .. V3(P(a, 1)) .. " rot=" .. R3(P(a, 2)) end)
    Hook(E .. "PhysicsHandleComponent:SetTargetLocation", function(s, a) return Owner(s) .. " loc=" .. V3(P(a, 1)) end)
    Hook(E .. "PhysicsHandleComponent:SetLinearStiffness", function(s, a) return Owner(s) .. " k=" .. F(P(a, 1)) end)
    Hook(E .. "PhysicsHandleComponent:SetAngularStiffness", function(s, a) return Owner(s) .. " k=" .. F(P(a, 1)) end)
    Hook(E .. "PhysicsConstraintComponent:SetAngularDriveParams", function(s, a) return Owner(s) .. " spring=" .. F(P(a, 1)) .. " damping=" .. F(P(a, 2)) .. " forceLimit=" .. F(P(a, 3)) end)
    Hook(E .. "PhysicsConstraintComponent:SetLinearDriveParams", function(s, a) return Owner(s) .. " spring=" .. F(P(a, 1)) .. " damping=" .. F(P(a, 2)) .. " forceLimit=" .. F(P(a, 3)) end)
    Hook(E .. "PhysicsConstraintComponent:SetAngularOrientationTarget", function(s, a) return Owner(s) .. " target=" .. R3(P(a, 1)) end)
    Hook(E .. "PhysicsConstraintComponent:SetLinearPositionTarget", function(s, a) return Owner(s) .. " target=" .. V3(P(a, 1)) end)
end

---------------------------------------------------------------- name table and real masses (second pass)
local DECOMP = os.getenv("USERPROFILE") .. "\\halfsword-decomp\\"

local function ResolveNames()
    local f = io.open(DECOMP .. "name_indices.txt", "r")
    if f == nil then return end
    local out = io.open(DECOMP .. "names.tsv", "w")
    local n = 0
    for line in f:lines() do
        local idx = tonumber(line)
        if idx then
            local ok, s = pcall(function() return FName(idx, EFindName.FNAME_Find):ToString() end)
            out:write(idx, "\t", ok and s or ("<error " .. tostring(s) .. ">"), "\n")
            n = n + 1
        end
    end
    f:close()
    out:close()
    Log("names resolved: " .. n)
end

local massesDone = false

local function DumpMasses()
    if massesDone then return end
    local pc = FindFirstOf("PlayerController")
    if pc == nil or not pc:IsValid() then return end
    local pawn = pc.Pawn
    if pawn == nil or not pawn:IsValid() then return end
    local mesh = pawn.Mesh
    if mesh == nil or not mesh:IsValid() then return end
    local pa = mesh.PhysicsAssetOverride
    if pa == nil or not pa:IsValid() then return end
    massesDone = true
    local total = 0
    pa.SkeletalBodySetups:ForEach(function(_, e)
        local bone = e:get().BoneName
        local ok, m = pcall(function() return mesh:GetBoneMass(bone, true) end)
        if ok and type(m) == "number" then total = total + m end
        W("masses.txt", N(bone) .. "\t" .. (ok and F(m) or ("error " .. tostring(m))))
    end)
    W("masses.txt", "TOTAL\t" .. F(total))
    Log("masses dumped for " .. pawn:GetFullName() .. " total=" .. F(total))
end

---------------------------------------------------------------- pass 3: same-session names, BP hooks, motion recorder
-- FName indices of asset names differ between game sessions, so the .jmap and the name table must come from
-- the same run: the script dumps the .jmap, waits for name_indices_req.txt (written by kismet_mem.py from that
-- .jmap), resolves it, then writes names_ready.txt.
local function Exists(p) local f = io.open(p, "r") if f then f:close() return true end return false end

local function ResolveRequestedNames()
    local req = DECOMP .. "name_indices_req.txt"
    if not Exists(req) or Exists(DECOMP .. "names_ready.txt") then return end
    local out = io.open(DECOMP .. "names_session.tsv", "w")
    local n = 0
    for line in io.lines(req) do
        local idx = tonumber(line)
        if idx then
            local ok, s = pcall(function() return FName(idx, EFindName.FNAME_Find):ToString() end)
            out:write(idx, "\t", ok and s or "<error>", "\n")
            n = n + 1
        end
    end
    out:close()
    local done = io.open(DECOMP .. "names_ready.txt", "w")
    done:write(tostring(n))
    done:close()
    Log("session names resolved: " .. n)
end

local function Str(v)
    local t = type(v)
    if t == "number" then return F(v) end
    if t == "boolean" or t == "string" or t == "nil" then return tostring(v) end
    local ok, s = pcall(function() return V3(v) end)
    if ok and s ~= "?" and s ~= "(nil,nil,nil)" then return s end
    ok, s = pcall(function() return v:ToString() end)
    if ok then return s end
    ok, s = pcall(function() return v:GetFullName() end)
    if ok then return s end
    return t
end

local WATCH = { "Health", "Stamina", "All Body Tonus", "Muscle Power", "Constraint Rate", "Fallen", "DED",
    "Aim Vector", "Aim Velocity", "Aim Swing Speed", "Axis Value X", "Axis Value Y",
    "R_Guarding", "L_Guarding", "Get Up Rate", "Leg L Tonus", "Leg R Tonus", "Arm L Tonus", "Arm R Tonus",
    "Upper Body Tonus", "Max Angular Force", "Pain", "Consciousness", "Fallen Rate", "Free Fall Rate",
    "Lower Tilt Upright", "Being Grabbed", "Orientation Strength", "Angular Strength", "Max Linear Force",
    "Distance To Ground Left", "Distance To Ground Right", "Step Height" }

-- Hand targets driven by the mouse: world locations of the IK scene components, sampled with the bones.
local REC_SCENES = { "IK R Hand", "IK L Hand", "Effector Arm R", "Effector Arm L", "Shoulder R IK Scene", "Shoulder_L_IK_Scene" }

local function Snapshot(pawn)
    local parts = {}
    for _, name in ipairs(WATCH) do
        local ok, v = pcall(function() return pawn[name] end)
        if ok then table.insert(parts, name .. "=" .. Str(v)) end
    end
    return table.concat(parts, " ")
end

local BP = "/Game/Character/Blueprints/Willie_BP.Willie_BP_C:"
local bpCalls = {}

local function HookBP(fn, every)
    local path = BP .. fn
    local ok, err = pcall(function()
        RegisterHook(path, function(ctx, ...)
            bpCalls[fn] = (bpCalls[fn] or 0) + 1
            local n = bpCalls[fn]
            if n <= 25 or n % every == 0 then
                local args = {...}
                local text = {}
                for i = 1, #args do
                    local okv, v = pcall(function() return args[i]:get() end)
                    table.insert(text, okv and Str(v) or "?")
                end
                local self = ""
                pcall(function() self = Snapshot(ctx:get()) end)
                W("bp_hooks.txt", os.date("%H:%M:%S ") .. fn .. " #" .. n .. " args=[" .. table.concat(text, " | ") .. "] " .. self)
            end
        end)
    end)
    Log((ok and "bp hooked " or "bp hook failed ") .. fn .. (ok and "" or (": " .. tostring(err))))
end

local function RegisterBPHooks()
    HookBP("Get Damage", 1)
    HookBP("Deal Complex Damage", 1)
    HookBP("Damage Rate", 5)
    HookBP("Dismember Function Initiate", 1)
    HookBP("Set Arm Aim Location", 200)
    HookBP("Set Arm Aim Rotation", 200)
    HookBP("Set Arm IK Relative Rotation", 200)
    HookBP("Set Shoulder Rotation", 200)
    HookBP("Control Physical Animation Str", 300)
    HookBP("Muscle Motor", 300)
    HookBP("Velocity Calculator V", 200)
    HookBP("Draw Weapon", 1)
    HookBP("R Hand Stamina Burn Rate f", 200)
end

-- Motion recorder: 30 Hz, 90 s, starts when the player controls a fighting Willie (not the dress-up dummy).
local REC_BONES = { "pelvis", "spine_03", "spine_05", "Head", "upperarm_r", "lowerarm_r", "hand_r",
    "upperarm_l", "lowerarm_l", "hand_l", "thigh_r", "calf_r", "foot_r", "thigh_l", "calf_l", "foot_l" }
local rec = { state = "waiting", file = nil, t0 = 0, frames = 0 }

local function TransformText(comp, bone)
    local ok, tr = pcall(function() return comp:GetSocketTransform(FName(bone), 0) end) -- 0 = world space
    if not ok or tr == nil then return ",,,,,,," end
    local t, q = tr.Translation, tr.Rotation
    return string.format("%.2f,%.2f,%.2f,%.5f,%.5f,%.5f,%.5f", t.X, t.Y, t.Z, q.X, q.Y, q.Z, q.W)
end

local cachedPC = nil

local function RecordFrame()
    if rec.state == "done" then return end
    if cachedPC == nil or not cachedPC:IsValid() then
        cachedPC = FindFirstOf("PlayerController") -- slow: scans the object array, so only when the cache is empty
    end
    local pc = cachedPC
    if pc == nil or not pc:IsValid() then return end
    local pawn = pc.Pawn
    if pawn == nil or not pawn:IsValid() then return end
    local cls = pawn:GetClass():GetFName():ToString()
    if rec.state == "waiting" then
        if cls ~= "Willie_BP_C" then return end
        rec.state = "recording"
        rec.t0 = os.clock()
        rec.file = io.open(OUT .. "motion.csv", "w")
        local header = { "t" }
        for _, name in ipairs(WATCH) do table.insert(header, (name:gsub(" ", "_"))) end
        for _, b in ipairs(REC_BONES) do
            for _, src in ipairs({ "body", "driver" }) do
                for _, c in ipairs({ "x", "y", "z", "qx", "qy", "qz", "qw" }) do table.insert(header, src .. "_" .. b .. "_" .. c) end
            end
        end
        for _, s in ipairs(REC_SCENES) do
            for _, c in ipairs({ "x", "y", "z" }) do table.insert(header, "scene_" .. (s:gsub(" ", "_")) .. "_" .. c) end
        end
        rec.file:write(table.concat(header, ","), "\n")
        Log("motion recording started on " .. pawn:GetFullName())
    end
    local t = os.clock() - rec.t0
    local row = { string.format("%.3f", t) }
    for _, name in ipairs(WATCH) do
        local ok, v = pcall(function() return pawn[name] end)
        local s = ok and Str(v) or ""
        table.insert(row, '"' .. s .. '"')
    end
    local body, driver = pawn.Mesh, pawn.DriverSkeleton
    for _, b in ipairs(REC_BONES) do
        table.insert(row, TransformText(body, b))
        table.insert(row, TransformText(driver, b))
    end
    for _, s in ipairs(REC_SCENES) do
        local ok, loc = pcall(function() return pawn[s]:K2_GetComponentLocation() end)
        if ok and loc ~= nil then
            table.insert(row, string.format("%.2f,%.2f,%.2f", loc.X, loc.Y, loc.Z))
        else
            table.insert(row, ",,")
        end
    end
    rec.file:write(table.concat(row, ","), "\n")
    rec.frames = rec.frames + 1
    if rec.frames % 300 == 0 then rec.file:flush() end
    if t > 90 then
        rec.file:close()
        rec.state = "done"
        Log("motion recording done: " .. rec.frames .. " frames")
    end
end

---------------------------------------------------------------- schedule
local SKIP_JMAP = true  -- pass 4: session-3 .jmap + names are consistent and kept
local namesDone = true  -- the cross-session ResolveNames is replaced by ResolveRequestedNames
-- Pass 3 crashed the game (EXCEPTION_ACCESS_VIOLATION inside UE4SS, nested Blueprint calls, ~2 s into the fight,
-- most likely the "Get Damage" script hook reading struct arguments). Blueprint script hooks stay OFF: the
-- decompiled bytecode gives the logic, and the 30 Hz recorder samples the state from the game thread instead.
local bpHooksDone = true
local reflectionDone = SKIP_JMAP

LoopAsync(33, function()
    ExecuteInGameThread(function() Try("RecordFrame", RecordFrame) end)
    return rec.state == "done"
end)
local hooksDone = true -- pass 4: no hooks at all during the fight capture
local ticks = 0

LoopAsync(5000, function()
    ticks = ticks + 1
    ExecuteInGameThread(function()
        if not hooksDone then
            hooksDone = true
            RegisterHooks()
        end
        if not namesDone then
            namesDone = true
            Try("ResolveNames", ResolveNames)
        end
        if not bpHooksDone then
            bpHooksDone = true
            RegisterBPHooks()
        end
        Try("ResolveRequestedNames", ResolveRequestedNames)
        Try("DumpMasses", DumpMasses)
        Try("DumpPhysicsAssets", DumpPhysicsAssets)
        Try("DumpCharacters", DumpCharacters)
        Try("DumpPlayerPawn", DumpPlayerPawn)
        if not reflectionDone and ticks >= 4 then
            reflectionDone = true
            Log("DumpJMAP start")
            Try("DumpJMAP", function() DumpJMAP() end)
            Log("DumpJMAP done")
            local ready = io.open(DECOMP .. "jmap_ready.txt", "w")
            ready:write(os.date("%H:%M:%S"))
            ready:close()
        end
        if ticks % 6 == 0 then Log("heartbeat t=" .. (ticks * 5) .. "s") end
    end)
    return ticks >= 240 -- stop after 20 minutes
end)
