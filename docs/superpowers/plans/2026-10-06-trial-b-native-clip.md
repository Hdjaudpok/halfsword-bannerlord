# Trial B (native clips): one Half Sword two-handed strike as a Bannerlord clip — Implementation Plan, part 1

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture one real Half Sword two-handed overhead strike on Bannerlord's own `human_skeleton` (through the
running passthrough, which already retargets Willie onto it), so that it can become a native Bannerlord clip that
Bannerlord's combat (hits, blocks, AI parries) uses.

**Architecture:** Part 1 records. A lab-only live key `lab_rec = S` makes `HsPlayerBehavior` write, for S seconds,
one `REC` line per frame with every drawn bone's model-space frame (the same serialization as `lab_dump`), plus one
`SKEL` dump of the player's skeleton (names, parents, rest frames). Then one strike is played and a Python converter
turns the `REC` lines into per-bone local rotations at 30 FPS. Part 2, written once part 1's data exist and the user
has agreed the GUI steps, covers the rest: the FBX bake in Blender (MCP `blender`), the Modding Kit import (one take
per FBX), the clip in copy mode from the Native two-handed overhead clip, the action set in a test module, and the lab
oracle.

**Tech Stack:** C# netstandard2.0 mod (Bannerlord 1.4.8.119303), Python 3, later Blender (MCP) and the Modding Kit.

**Spec:** `docs/ROADMAP-4-TRIALS.md`, section "Trial B". Sources: TaleWorlds animation import rules
(https://moddocs.bannerlord.com/asset-management/asset-types/animations/: FBX, 30 or 60 FPS, at most 64 bones, Z up,
"Use scene name", no constraints in the export); KB note `knowledge/techniques/editor-free-bannerlord-assets.md` in
the uni mod repo (one take per FBX, gotcha 5; combat clips in copy mode with their combat parameter, gotchas 10 and
21; names of at most 63 characters; a clip stays on its own skeleton, gotcha 22).

## Global Constraints
- Never invent: bone names, parents and rest frames come from the game's own skeleton (`SKEL` lines).
- Lab-only code runs only with `SubModule.LabMode`; `lab_rec` absent or 0 = no change.
- Ask the user before restarting Bannerlord (deploy), before driving the Modding Kit editor, before installing a test
  module into the game's Modules folder.
- Kill by exact PID; bounded commands; no `focus_guard.ps1` while the user plays.
- Nothing from either game is committed (recordings stay in `logs\`, which is not committed, or in halfsword-decomp).
- STATUS.md and MODLOG.md at every milestone; messages to the user in French.

## Review Focus
1. **Frame rate of the capture**: one `REC` line per lab tick; at ~60-120 fps a 3 s capture is 180-360 lines, about
   1 MB of log. Expected: no hitch the user can see. Test: Task B1 Step 5 counts lines per second.
2. **Bones not driven by Half Sword** (legs, from Bannerlord's walk): recorded too, so the clip is complete. Test:
   Step 5 checks the `REC` line has as many bones as the `SKEL` dump.
3. **A strike that does not happen** (no guard, no swing): the capture must show Willie's swing. Test: Task B2 reads
   the HSLink `AIM` swing speed over the capture window.
4. **Rest frames** must match between `SKEL` and a still `REC` frame for the bones the passthrough does not drive.
   Test: Task B3 Step 2.
5. **Model space vs local space**: the clip needs local rotations; the converter divides by the parent. Test: Task B3
   Step 1 on a synthetic two-bone chain.

---

### Task B1: `lab_rec`, a per-frame capture of the drawn skeleton (lab only)

**Files:**
- Modify: `src\HalfSwordCombat\Combat\HsPlayerBehavior.cs` (`DumpPose` at 427-445 takes a header; new fields next to
  `_lastDump` at 456; `LabLog` at 470-475)

- [ ] **Step 1: Generalize `DumpPose`** so the bone serialization can carry another header:

```csharp
        private void DumpPose(float id)
        {
            WritePose("POSE " + ProbeLog.F(id) + " X=" + ProbeLog.F(_aim.X) + " Y=" + ProbeLog.F(_aim.Y) +
                      " yaw=" + ProbeLog.F(_aim.SwingYaw) + " speed=" + ProbeLog.F(_aim.SwingSpeed) +
                      " R=" + (_right.Guarding ? 1 : 0) + " L=" + (_left.Guarding ? 1 : 0));
        }

        // Every bone's drawn model-space frame (origin, s, f, u) and each held item's final frame, after `header`.
        private void WritePose(string header)
        {
            var sb = new System.Text.StringBuilder(header);
            foreach (var bone in _body.Bones)
            {
                Append(sb, bone.Key, _body.DrawnEntitial(bone.Value));
            }
            for (int side = 0; side < 2; side++)
            {
                sbyte b = _body.BoneIndex(side == 0 ? "r_finger0" : "l_finger0");
                if (b >= 0)
                {
                    Append(sb, side == 0 ? "ITEM_R" : "ITEM_L", _body.DrawnEntitial(b).TransformToParent(_body.ItemOffset(side)));
                }
            }
            ProbeLog.Write(sb.ToString());
        }
```

- [ ] **Step 2: Add the capture** (fields after `private float _lastDump;`, logic after the `lab_dump` block):

```csharp
        private float _lastRec, _recUntil = -1f;
```

```csharp
            // Lab: "lab_rec = S" (change S) records the drawn skeleton every frame for S seconds (trial B: a Half Sword
            // strike captured on human_skeleton), after one SKEL dump of the skeleton's bones, parents and rest frames.
            float rec = LiveTuning.Get(null, "lab_rec", 0f);
            if (rec != _lastRec)
            {
                _lastRec = rec;
                if (rec > 0f)
                {
                    Lab.SkeletonDump.Write(main);
                    _recUntil = t + rec;
                    ProbeLog.Write("REC start t=" + ProbeLog.F(t) + " seconds=" + ProbeLog.F(rec));
                }
            }
            if (t <= _recUntil)
            {
                WritePose("REC t=" + ProbeLog.F(t) + " X=" + ProbeLog.F(_aim.X) + " Y=" + ProbeLog.F(_aim.Y) +
                          " speed=" + ProbeLog.F(_aim.SwingSpeed) + " R=" + (_right.Guarding ? 1 : 0) +
                          " L=" + (_left.Guarding ? 1 : 0));
            }
```

- [ ] **Step 3: Build** `dotnet build -c Release src\HalfSwordCombat\HalfSwordCombat.csproj`: 0 errors.
- [ ] **Step 4: Ask the user, stop the lab (`gabs_call.py stop`), deploy (`tools\deploy.ps1`), start the lab
  (`gabs_call.py start lab`), `hsc.loadout 8`.** Check `Harmony patch applied` lines and the hslink.log equip line.
- [ ] **Step 5: Oracle without a strike:** `hsc.set lab_rec 2`, wait 3 s, then count in the new log: one `SKEL:`
  header and N `SKEL i ...` lines (N = bone count), one `REC start`, and `REC t=` lines over 2 s of `t` with as many
  ` | ` bone blocks as there are bones in `_body.Bones` plus the items. Write the counts and the lines per second in
  MODLOG.

### Task B2: one two-handed overhead strike, captured

- [ ] **Step 1: Choose with the user who strikes:** the user (they hold LMB, raise the sword high, swing down; the
  agent sets `hsc.set lab_rec 3` on their "go"), or the script (`hs_input_probe.py` with `hsc.set link_input 0`, then
  `link_input 1` again; the script's options are read from its `--help` first).
- [ ] **Step 2: Check the strike happened:** in the lab log, the `REC` lines' `speed=` rises above 35 (Half Sword's
  swing threshold that freezes the swing direction, NOTES.md) and `R=1` while it does; the HSLink `AIM` lines in
  hslink.log over the same seconds agree.

### Task B3: REC lines to local rotations at 30 FPS

**Files:**
- Create: `%USERPROFILE%\tools\gabs\rec_to_local.py`, `%USERPROFILE%\tools\gabs\testdata\rec_synth.log`

- [ ] **Step 1:** a converter reading the `SKEL` and `REC` lines of one capture, writing a CSV with, per 1/30 s key
  (linear interpolation of origins, normalized lerp of rotations between the two nearest `REC` lines), each bone's
  local rotation (parent^-1 x child, model space) as a quaternion and the root's origin. Test first on a synthetic
  log of a two-bone chain whose child turns 90 deg about the parent's up axis between t=0 and t=1: the key at
  t=0.5 must give 45 deg.
- [ ] **Step 2:** on the real capture, the first key's local rotations of the bones the passthrough does not drive
  must equal their rest local rotations from `SKEL` within the walk animation's own motion (report the angles).
- [ ] **Step 3:** write part 2 of this plan (Blender bake through MCP, Modding Kit import, copy-mode clip, action
  set, lab oracle), with each GUI step marked "ask the user first".
