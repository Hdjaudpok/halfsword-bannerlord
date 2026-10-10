# Trial A (passthrough): camera verdict and torso look pitch — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the pending parts of trial A: check the user's playtest of the camera rules deployed 2026-10-06 11:42
against the numbers the code predicts, get the user's verdict, and only if the user still sees the torso bend, make
one verified change that stops the agent's look pitch from bending the drawn torso.

**Architecture:** The camera reading changes nothing in the game. It reads the lab log's `CAM` lines, written every
0.5 s by `CameraPatch.Telemetry` (`src\HalfSwordCombat\Combat\HsControl.cs` 204-216), with
`%USERPROFILE%\tools\gabs\cam_play.py`. The torso change is one Harmony postfix on
`MissionMainAgentController.LookTick`. That private method turns the camera angles into `Agent.LookDirection`
(decompiled 1.4.8 `TaleWorlds.MountAndBlade.View.MissionViews\MissionMainAgentController.cs` lines 300-418, called
from `OnPreMissionTick` line 270). The postfix raises the agent's look pitch to a live floor `look_floor` in third
person and never touches the camera. Its oracle holds the camera elevation with a lab-only key `lab_cam_elev`, which
works with the Bannerlord window in the background, and compares the drawn head over five phases.

**Tech Stack:** C# netstandard2.0 mod built with `dotnet build` (Harmony 2.4.2.225, Bannerlord v1.4.8.119303 API,
runtime .NET Framework 4.8), Python 3 scripts in `%USERPROFILE%\tools\gabs`, GABS one-shot client
`gabs_call.py`, Windows PowerShell 5.1.

**Spec:** `docs/ROADMAP-4-TRIALS.md`, section "Trial A". Context: `STATUS.md` (block NOW 2026-10-06 12:27) and
`MODLOG.md` entries 2026-10-06 11:45 and 12:27.

## Global Constraints
- Never invent: every number written to MODLOG/STATUS comes from a tool output, a log line, decoded data or the
  user's words.
- One verified change per test: the oracle passes before the user plays it; revert at once what the user rejects.
- Circuit breaker: after 2-3 failed attempts on the same problem, stop and write the handoff (memory
  `long-session-end-drift`).
- Ask the user before: driving their mouse or keyboard, focusing a game window, restarting a game they may be using,
  installing anything in a game folder, committing, publishing.
- Kill by exact PID only (`gabs_call.py stop` goes through `bannerlord-lifecycle.ps1`); never by name.
- Every command bounded (timeouts); no `focus_guard.ps1` while the user plays or waits to play.
- Discord's game hook stays OFF while Bannerlord runs (`tools\discord.ps1 -HookOn` only after Bannerlord has closed).
- Lab-only code runs only when `SubModule.LabMode` is true (env `HSC_LAB=1`); with the new live keys absent, behaviour
  is exactly today's.
- Half Sword data stays in `%USERPROFILE%\halfsword-decomp`; nothing from either game is committed.
- STATUS.md and MODLOG.md updated at every milestone; every message to the user in French.

## Review Focus
1. **A guard press or release inside a 0.5 s CAM window.** The line's `guard` flag is read at the end of the window,
   so that line mixes both rules. Expected: the reading also reports clean windows (same flag on the previous line).
   Test: Task 1 Step 4 (the synthetic transition line is left out of the clean section).
2. **Looking level or up.** The floor must change nothing above it. Expected: with `look_floor -10`, a camera at
   -10 deg or +10 deg gives the same look pitch and drawn head as without the floor. Test: Task 2 Step 9, phases P4
   and P5.
3. **First-person camera (V key).** The first-person camera hangs on the head bone, and `MissionScreen` reads
   `CustomLookDir` there. Expected: no change in first person. Test: the patch returns when
   `Mission.CameraIsFirstPerson` (Task 2 Step 3); Task 3 Step 2 asks the user to press V once and look down.
4. **The game's own look rules** (conversation or cinematic `CustomLookDir`, `IsLookDirectionLocked`, `FrameLocked`,
   `IsViewingCharacter()`). Expected: they win, no change. Test: the patch uses the same conditions as `LookTick`
   line 415; they cannot be reached in the lab battle, so Task 2 Step 3 checks them in the code.
5. **Another agent** (main agent swapped, dead or AI-controlled). Expected: no change. Test: the patch acts only on
   `HsControl.Agent` when it is `Mission.Current.MainAgent` and active (Task 2 Step 3).

## Out of scope
- Trial A's right click (Half Sword's half-sword grip, 0/102 scripted trials): its own plan, written once the user has
  picked a route (decode Willie_BP's half-sword attach condition, or place the drawn left hand on the drawn blade by
  IK on the Bannerlord side).

---

### Task 1: Read the user's camera playtest (no game change)

**Files:**
- Modify: `%USERPROFILE%\tools\gabs\cam_play.py` (docstring lines 1-5; new clean-window section at the end)
- Create: `%USERPROFILE%\tools\gabs\testdata\cam_synth.log`
- Read: `logs\lab-gabs-20261006-114237.log`

**Interfaces:**
- Consumes: the CAM line format from `HsControl.cs` 204-216:
  `HH:MM:SS.mmm CAM t=T guard=G fix=F scale=SX/SY bearing=B elev=E rawB=RB keptB=KB rawE=RE keptE=KE look=L rawDown=RD keptDown=KD`
  (degrees; `raw*`/`kept*` are sums since the previous line; `rawDown/keptDown` = downward moves that started below
  the horizon).
- Produces: per guard state, kept/raw for bearing, elevation and downward-below-horizon, over all windows and over
  clean windows; a MODLOG entry; the user's verdict.

- [ ] **Step 1: Write the synthetic log with known ratios**

Create `%USERPROFILE%\tools\gabs\testdata\cam_synth.log` with exactly these 4 lines:

```text
12:00:00.000 CAM t=1.000 guard=0 fix=1 scale=0.013/0.014 bearing=0.000 elev=0.000 rawB=10.000 keptB=10.000 rawE=0.000 keptE=0.000 look=0.000 rawDown=0.000 keptDown=0.000
12:00:00.500 CAM t=1.500 guard=0 fix=1 scale=0.013/0.014 bearing=10.000 elev=-20.000 rawB=10.000 keptB=10.000 rawE=4.000 keptE=3.000 look=-20.000 rawDown=2.000 keptDown=1.000
12:00:01.000 CAM t=2.000 guard=1 fix=1 scale=0.013/0.014 bearing=17.000 elev=-20.000 rawB=10.000 keptB=7.000 rawE=0.000 keptE=0.000 look=-20.000 rawDown=0.000 keptDown=0.000
12:00:01.500 CAM t=2.500 guard=1 fix=1 scale=0.013/0.014 bearing=22.000 elev=-21.500 rawB=10.000 keptB=5.000 rawE=4.000 keptE=1.500 look=-21.500 rawDown=2.000 keptDown=0.500
```

Line 3 is a transition window (guard pressed mid-window: 0.7 instead of 0.5); lines 2 and 4 are clean.

- [ ] **Step 2: Run the current script and see the clean section missing**

Run: `python %USERPROFILE%\tools\gabs\cam_play.py %USERPROFILE%\tools\gabs\testdata\cam_synth.log`
Expected: the output ends with
`guard=1 downward moves below the horizon: raw 2.0 deg kept 0.5 deg (0.250)` and has no line starting with
`clean windows`.

- [ ] **Step 3: Fix the docstring and add the clean-window section**

Replace lines 1-5 of `cam_play.py` with:

```python
"""Read the lab CAM lines written while the user plays: camera turn applied vs. turned by the game, guard vs. no guard.

  python cam_play.py LOG [FROM_HH:MM:SS]
Oracle for CameraPatch (HsControl.cs): kept/raw = cam_guard (default 0.5) while guarding, 1 without; a downward move
that starts below the horizon is also scaled by cam_down (default 0.5). scale= is Half Sword's own rule, used only when
cam_guard = -1.
"""
```

Append at the end of the file:

```python


def ratio(kept, raw):
    return "%.3f (raw %.1f deg)" % (kept / raw, raw) if raw > 0.05 else "no move"


# The guard flag is read when a line is written (end of its 0.5 s window): a window with a press or a release mixes both
# rules. Clean = the previous line has the same flag.
print("clean windows (same guard flag on the previous line):")
for guard in (0, 1):
    rs = [r for p, r in zip(rows, rows[1:]) if p[1] == guard and r[1] == guard and (r[7] + r[9]) > 0.05]
    if not rs:
        print("  guard=%d: no clean line with movement" % guard)
        continue
    print("  guard=%d: %d lines | bearing %s | elevation %s | downward below the horizon %s" % (
        guard, len(rs), ratio(sum(r[8] for r in rs), sum(r[7] for r in rs)),
        ratio(sum(r[10] for r in rs), sum(r[9] for r in rs)), ratio(sum(r[13] for r in rs), sum(r[12] for r in rs))))
```

- [ ] **Step 4: Run it on the synthetic log**

Run: `python %USERPROFILE%\tools\gabs\cam_play.py %USERPROFILE%\tools\gabs\testdata\cam_synth.log`
Expected, the last 3 lines exactly:

```text
clean windows (same guard flag on the previous line):
  guard=0: 1 lines | bearing 1.000 (raw 10.0 deg) | elevation 0.750 (raw 4.0 deg) | downward below the horizon 0.500 (raw 2.0 deg)
  guard=1: 1 lines | bearing 0.500 (raw 10.0 deg) | elevation 0.375 (raw 4.0 deg) | downward below the horizon 0.250 (raw 2.0 deg)
```

The all-windows lines above stay as before (`guard=1: 2 lines with movement | bearing raw 20.0 deg kept 12.0 deg
(0.600)` shows the mixing that the clean section removes).

- [ ] **Step 5: Wait for the user's "fait", then read their play**

Run: `python %USERPROFILE%\tools\gabs\cam_play.py logs\lab-gabs-20261006-114237.log 12:25:00`
Expected (the patch multiplies every frame's turn exactly, so clean windows are exact up to rounding):
- clean `guard=1`: bearing `0.500` (±0.01); downward below the horizon `0.250` (±0.01) if the user moved down there;
- clean `guard=0`: bearing `1.000` (±0.01); downward below the horizon `0.500` (±0.01).
If a clean ratio is off by more than 0.01, stop: use superpowers:systematic-debugging on `CameraPatch` and change no
code before the cause is found.

- [ ] **Step 6: Record**

Add a MODLOG.md entry above the newest one, titled `## 2026-10-06 HH:MM Trial A camera playtest [measured]`, with the
`cam_play.py` output lines and the user's words; update the STATUS.md NOW block (verdict, next step).

- [ ] **Step 7: Decision gate with the user**
- (a) Camera and strike height fine: Task 2 is not needed; trial A's camera item is closed. Go to the right-click
  route choice or to trial B.
- (b) The camera feel is off: agree one new number with the user. The keys are live (no restart):
  `python %USERPROFILE%\tools\gabs\gabs_call.py cmd "hsc.set cam_guard 0.7"` or `... "hsc.set cam_down 0.3"`.
  Then repeat Step 5 with a new start time. No code change.
- (c) The torso still bends or strikes still go down: agree the target with the user before Task 2. That means the
  floor in degrees (proposal: -10, from the 11:45 bins where the head moves forward below -10 deg) and the scope
  (always in third person, or only while guarding).

No commit: `tools\gabs` is outside the mod repo.

---

### Task 2: `look_floor`, keeping the look pitch from bending the torso (only after gate 1c)

**Files:**
- Create: `src\HalfSwordCombat\Combat\LookPitchPatch.cs`
- Modify: `src\HalfSwordCombat\Combat\HsControl.cs:199-200`
  (`CameraPatch.Postfix`, before `_bearing = __instance.CameraBearing;`)
- Create: `%USERPROFILE%\tools\gabs\look_phases.py`
- Create: `%USERPROFILE%\tools\gabs\look_floor_oracle.py`
- Create: `%USERPROFILE%\tools\gabs\testdata\look_synth.log`

**Interfaces:**
- Consumes: `HsControl.Agent` (`public static Agent`), `LiveTuning.Get(string cls, string key, float fallback)`,
  `SubModule.LabMode` (`internal static bool`), `CameraPatch.SetElevation` (`Action<MissionScreen, float>`, radians),
  the GEOM line `HH:MM:SS.mmm GEOM t=T cam=(..) ... | head phys=(x,y,z) drv=(x,y,z) skel=(x,y,z) anim=(x,y,z) | r_hand ...`
  (agent frame: y forward, z up; `skel` = drawn head, `anim` = native animation head), written with the CAM line of
  the same `t` every 0.5 s.
- Produces: live key `look_floor` (degrees, absent = off), lab-only live key `lab_cam_elev` (degrees, absent = off),
  `look_phases.py LOG LABEL=HH:MM:SS-HH:MM:SS ...`, `look_floor_oracle.py LOG`.

- [ ] **Step 1: Write the synthetic GEOM/CAM log and see the analysis script missing**

Create `%USERPROFILE%\tools\gabs\testdata\look_synth.log` with exactly:

```text
12:00:00.000 GEOM t=1.000 cam=(0.000,-3.000,2.000) camDist=3.000 eye=(0.000,0.100,1.800) pelvis phys=(0.000,0.000,0.970) drv=(0.000,0.000,0.970) | head phys=(0.000,0.100,1.700) drv=(0.000,0.100,1.700) skel=(0.000,0.100,1.700) anim=(0.000,0.080,1.710) | r_hand phys=(0.050,0.200,1.050) drv=(0.050,0.200,1.050)
12:00:00.001 CAM t=1.000 guard=0 fix=1 scale=0.013/0.014 bearing=0.000 elev=-10.000 rawB=0.000 keptB=0.000 rawE=0.000 keptE=0.000 look=-10.000 rawDown=0.000 keptDown=0.000
12:00:05.000 GEOM t=6.000 cam=(0.000,-3.000,2.000) camDist=3.000 eye=(0.000,0.100,1.800) pelvis phys=(0.000,0.000,0.970) drv=(0.000,0.000,0.970) | head phys=(0.000,0.300,1.650) drv=(0.000,0.300,1.650) skel=(0.000,0.300,1.650) anim=(0.000,0.250,1.660) | r_hand phys=(0.050,0.200,1.050) drv=(0.050,0.200,1.050)
12:00:05.001 CAM t=6.000 guard=0 fix=1 scale=0.013/0.014 bearing=0.000 elev=-30.000 rawB=0.000 keptB=0.000 rawE=0.000 keptE=0.000 look=-30.000 rawDown=0.000 keptDown=0.000
```

Run: `python %USERPROFILE%\tools\gabs\look_phases.py %USERPROFILE%\tools\gabs\testdata\look_synth.log P1=12:00:00-12:00:04 P2=12:00:05-12:00:09`
Expected: FAIL, `can't open file ... look_phases.py`.

- [ ] **Step 2: Write `look_phases.py`**

```python
"""Drawn head per time window: the oracle for look_floor (MODLOG 2026-10-06).

  python look_phases.py LOG LABEL=HH:MM:SS-HH:MM:SS [LABEL=HH:MM:SS-HH:MM:SS ...]
Per window: the GEOM lines (every 0.5 s) joined with the CAM line of the same t: camera elevation, agent look pitch
(degrees), drawn head (skel) and native animation head (anim), forward and up in the agent frame (m).
"""
import re
import statistics
import sys

v = r"\((-?[\d.]+),(-?[\d.]+),(-?[\d.]+)\)"
geo = re.compile(r"(\d\d:\d\d:\d\d)\.\d+ GEOM t=(\S+) .*?\| head phys=" + v + " drv=" + v + " skel=" + v + " anim=" + v)
cam = re.compile(r"(\d\d:\d\d:\d\d)\.\d+ CAM t=(\S+) guard=(\d) .* elev=(\S+) .* look=(\S+)")

windows = []
for arg in sys.argv[2:]:
    label, span = arg.split("=", 1)
    start, end = span.split("-")
    windows.append((label, start, end))
geom, cams = {}, {}
for line in open(sys.argv[1], encoding="utf-8", errors="replace"):
    m = geo.match(line)
    if m:
        f = [float(x) for x in m.groups()[2:]]
        geom[m.group(2)] = (m.group(1), f[6:9], f[9:12])  # wall time, drawn head, native animation head
        continue
    m = cam.match(line)
    if m:
        cams[m.group(2)] = (float(m.group(4)), float(m.group(5)))  # camera elevation, look pitch
print("%-10s %4s | %8s %8s | %-17s | %-17s" % ("window", "n", "cam elev", "look", "drawn head fwd/up", "anim head fwd/up"))
for label, start, end in windows:
    rows = [(cams[t], g) for t, g in geom.items() if t in cams and start <= g[0] <= end]
    if not rows:
        print("%-10s %4d" % (label, 0))
        continue
    mean = lambda f: statistics.mean(f(r) for r in rows)
    print("%-10s %4d | %8.1f %8.1f | %8.3f %8.3f | %8.3f %8.3f" % (
        label, len(rows), mean(lambda r: r[0][0]), mean(lambda r: r[0][1]),
        mean(lambda r: r[1][1][1]), mean(lambda r: r[1][1][2]), mean(lambda r: r[1][2][1]), mean(lambda r: r[1][2][2])))
```

Run the Step 1 command again. Expected, exactly:

```text
window        n | cam elev     look | drawn head fwd/up | anim head fwd/up 
P1            1 |    -10.0    -10.0 |    0.100    1.700 |    0.080    1.710
P2            1 |    -30.0    -30.0 |    0.300    1.650 |    0.250    1.660
```

Then run it on the 11:20 lab log, where look_vs_pose found the bend, to check the regexes on real lines:
`python %USERPROFILE%\tools\gabs\look_phases.py logs\lab-gabs-20261006-112007.log ALL=11:20:00-11:42:00`
Expected: one row with n > 0.

- [ ] **Step 3: Write `LookPitchPatch.cs`**

```csharp
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace HalfSwordCombat.Combat
{
    /// <summary>
    /// Keeps the player's look pitch at or above the live floor "look_floor" (degrees; absent = off, the game's own look).
    /// LookTick (decompiled 1.4.8 MissionMainAgentController lines 300-418, called from OnPreMissionTick) builds
    /// Agent.LookDirection from the camera bearing and elevation. Looking below the horizon makes the native look-at bend
    /// the drawn head and torso forward: below -10 deg of look pitch the native animation's head went 0.15-0.21 m forward
    /// and the drawn head 0.24 m (MODLOG 2026-10-06 11:45). This postfix raises only the pitch, keeps the bearing, and never
    /// touches the camera (CustomLookDir is not used: MissionScreen also reads it). It does nothing in first person, under
    /// the game's own look rules (CustomLookDir set, look locked, frame locked, viewing the character) or for another agent.
    /// </summary>
    [HarmonyPatch(typeof(MissionMainAgentController), "LookTick")]
    internal static class LookPitchPatch
    {
        private const float Deg = 57.29578f;

        private static void Postfix(MissionMainAgentController __instance)
        {
            float floor = LiveTuning.Get(null, "look_floor", -999f);
            if (floor <= -90f)
            {
                return;
            }
            Mission mission = Mission.Current;
            Agent main = HsControl.Agent;
            if (mission == null || main == null || main != mission.MainAgent || !main.IsActive() || mission.CameraIsFirstPerson ||
                __instance.CustomLookDir.IsNonZero || main.IsLookDirectionLocked ||
                main.MovementLockedState == AgentMovementLockedState.FrameLocked ||
                __instance.MissionScreen == null || __instance.MissionScreen.IsViewingCharacter())
            {
                return;
            }
            Vec3 look = main.LookDirection;
            float pitch = (float)System.Math.Asin(MBMath.ClampFloat(look.z, -1f, 1f)) * Deg;
            if (pitch >= floor)
            {
                return;
            }
            Vec2 flat = look.AsVec2;
            if (flat.Length < 0.0001f)
            {
                return; // looking straight down: no bearing to keep
            }
            flat.Normalize();
            float rad = floor / Deg;
            float c = (float)System.Math.Cos(rad);
            main.LookDirection = new Vec3(flat.x * c, flat.y * c, (float)System.Math.Sin(rad));
        }
    }
}
```

If the user chose "only while guarding" at gate 1c, add `|| !HsControl.Guarding` after `mission.CameraIsFirstPerson`
in the early-return condition.

Check the early returns against Review Focus 3-5. They match `LookTick` line 415
(`!IsViewingCharacter() && !IsLookDirectionLocked && MovementLockedState != 2`, where 2 = `FrameLocked`) and line 401
(`CustomLookDir` non-zero = the game's own look target).

- [ ] **Step 4: Add the lab-only camera hold to `CameraPatch.Postfix`**

In `HsControl.cs`, insert before `_bearing = __instance.CameraBearing;` (line 200):

```csharp
            // Lab only: "lab_cam_elev = D" holds the camera elevation at D degrees, the oracle for LookPitchPatch (with the
            // window in the background HandleUserInput applies no mouse, so a script cannot move the camera otherwise).
            if (SubModule.LabMode)
            {
                float labElev = LiveTuning.Get(null, "lab_cam_elev", -999f);
                if (labElev > -90f)
                {
                    SetElevation(__instance, labElev / Deg);
                }
            }
```

- [ ] **Step 5: Build**

Run: `dotnet build -c Release "src\HalfSwordCombat\HalfSwordCombat.csproj"`
Expected: exit code 0 and `0 Erreur(s)` (French dotnet output) / `0 Error(s)`. The build writes to
`src\HalfSwordCombat\bin\Release\`, so the running game is not touched yet.

- [ ] **Step 6: Ask the user, then stop the lab by exact PID**

Ask first: the deploy replaces the mod DLL, which the running Bannerlord holds open. Then run:
`python %USERPROFILE%\tools\gabs\gabs_call.py stop`
and check: `Get-Process -Id <lab PID from STATUS NOW> -ErrorAction SilentlyContinue` prints nothing. Half Sword and its
mouse relay keep running.

- [ ] **Step 7: Deploy, relaunch, equip, check the patches**

Run, in order (each bounded):
1. `powershell -NoProfile -ExecutionPolicy Bypass -File "tools\deploy.ps1"`
   Expected: `deployed -> ...\Modules\HalfSwordCombat` and a SHA256 hash (write it in MODLOG).
2. `python %USERPROFILE%\tools\gabs\gabs_call.py start lab` (Discord hook still OFF). Note the new log name
   `logs\lab-gabs-YYYYMMDD-HHMMSS.log` and the PID.
3. `python %USERPROFILE%\tools\gabs\gabs_call.py cmd "hsc.loadout 8"`, then check
   `%USERPROFILE%\halfsword-decomp\hslink.log` has a new `lab: equip R=ModularWeaponBP_LongSword_T2 ... two-handed=1`.
4. `Select-String -Path <new log> -Pattern 'Harmony patch (applied|FAILED): (LookPitchPatch|CameraPatch)'`
   Expected: `Harmony patch applied: LookPitchPatch` and `Harmony patch applied: CameraPatch`, no `FAILED`.

- [ ] **Step 8: Write `look_floor_oracle.py`**

```python
"""look_floor oracle (MODLOG 2026-10-06): hold the camera elevation with lab_cam_elev, switch look_floor, and compare
the drawn head per phase. Needs the lab running with the LookPitchPatch build; works with Bannerlord in the background.

  python look_floor_oracle.py LOG
"""
import subprocess
import sys
import time

GABS = r"%USERPROFILE%\tools\gabs\gabs_call.py"
PHASES = [  # label, lab_cam_elev (deg), look_floor (deg, None = unset)
    ("P1", -10, None),
    ("P2", -30, None),
    ("P3", -30, -10),
    ("P4", -10, -10),
    ("P5", 10, -10),
]


def cmd(text):
    out = subprocess.run([sys.executable, GABS, "cmd", text], capture_output=True, text=True, timeout=30)
    print("  %s -> %s" % (text, (out.stdout or out.stderr).strip()[:120]))


windows = []
try:
    for label, elev, floor in PHASES:
        cmd("hsc.set lab_cam_elev %d" % elev)
        cmd("hsc.set look_floor %d" % floor if floor is not None else "hsc.unset look_floor")
        time.sleep(2.0)  # settle: the look direction and the animation's look-at follow within a few frames
        start = time.strftime("%H:%M:%S")
        time.sleep(8.0)
        windows.append("%s=%s-%s" % (label, start, time.strftime("%H:%M:%S")))
finally:
    cmd("hsc.unset lab_cam_elev")
    cmd("hsc.unset look_floor")
subprocess.run([sys.executable, r"%USERPROFILE%\tools\gabs\look_phases.py", sys.argv[1]] + windows, timeout=60)
```

- [ ] **Step 9: Run the oracle**

Run: `python %USERPROFILE%\tools\gabs\look_floor_oracle.py <new log>` (about 55 s; the user does not touch
Bannerlord meanwhile).
Pass criteria (each row n >= 12):
- P1: cam elev -10.0 and look -10.0 (±0.5); its drawn head forward = H1.
- P2: cam elev -30.0 and look -30.0 (±0.5); drawn head forward >= H1 + 0.05 m (the bend). **If not, the look pitch
  does not cause the bend in this setup: stop, do not hand `look_floor` to the user, record it and discuss.**
- P3: cam elev -30.0, look -10.0 (±0.5); drawn head forward within 0.03 m of H1.
- P4: same as P1 within 0.03 m (the floor changes nothing at the floor).
- P5: cam elev 10.0 and look 10.0 (±0.5) (looking up is untouched).
After the run, check that `hsc.get look_floor` and `hsc.get lab_cam_elev` are unset (a timed-out `gabs_call.py cmd`
is not cancelled, memory `bannerlord-agent-tools`).

- [ ] **Step 10: Record**

Add a MODLOG.md entry `## 2026-10-06 HH:MM look_floor built and measured [measured]` with the hash, the
`look_phases.py` table, and pass or fail per phase; update STATUS.md NOW.

- [ ] **Step 11: Commit (only if the user agreed to commits)**

```bash
git -C "<repo>" add src/HalfSwordCombat/Combat/LookPitchPatch.cs src/HalfSwordCombat/Combat/HsControl.cs docs/superpowers/plans/2026-10-06-trial-a-passthrough.md
git -C "<repo>" commit -m "feat: look_floor keeps the look pitch from bending the torso; lab_cam_elev oracle key"
```

---

### Task 3: The user's playtest of `look_floor` (only after Task 2 passed)

**Files:**
- Modify (through `hsc.set` only): `Documents\Mount and Blade II Bannerlord\Configs\HalfSwordCombat_tuning.txt`

**Interfaces:**
- Consumes: the live key `look_floor` from Task 2; `cam_play.py` (Task 1); `look_vs_pose.py LOG FROM UNTIL`.
- Produces: the user's verdict; `look_floor` kept or removed.

- [ ] **Step 1: Turn it on and say what the numbers predict**

Run: `python %USERPROFILE%\tools\gabs\gabs_call.py cmd "hsc.set look_floor -10"` (or the value agreed at gate
1c). Tell the user, in French, what Task 2 measured, e.g. "camera at -30: drawn head forward <P3> m instead of <P2> m",
with the real numbers from Step 9.

- [ ] **Step 2: The user plays**

The same strikes as before, looking down as they like, and once: press V (first person), look down, press V again.
Note the start time.

- [ ] **Step 3: Read**

Run: `python %USERPROFILE%\tools\gabs\cam_play.py <log> <start>` and
`python %USERPROFILE%\tools\gabs\look_vs_pose.py <log> <start> <end>`.
Expected: `while guarding: ... look pitch` never below the floor; `look_vs_pose.py` has no row in the `-90..-10` bin.

- [ ] **Step 4: Verdict**

Keep it (the `hsc.set` line stays in the tuning file) or revert it at once
(`python %USERPROFILE%\tools\gabs\gabs_call.py cmd "hsc.unset look_floor"`). Record the user's words and the
numbers in MODLOG.md and STATUS.md.
