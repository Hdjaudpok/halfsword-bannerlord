# Roadmap: one trial of each method (decided 2026-10-06)

**User decision, 2026-10-06 ~12:10:** "comme avant fais un essai de chaque méthode donc natif, passthrough, hybride et
même rewrite rust de half sword ; pour chaque méthode envoie tout ce que tu peux pour le faire ; si pour une des
méthodes tu es bloqué par l'outil, fais une recherche web pour trouver d'autres outils ou autre."
Context: the user felt the passthrough had plateaued ("ça fait depuis hier aucune évolution, tu n'arrives pas à
récupérer et ajuster les physiques de Half Sword", 11:55).

## Rules for every trial
- Never invent: every fact comes from a tool output, a log, decoded data, a source with a link, or the user's words.
- Every method goes through its skill (`universal-modder:*` first, mashup-mods patterns 1-5) and the installed MCP
  tools: bannerlordsage (Bannerlord 1.4.8 source), gabs (launch, console, engine screenshots), rea 4.0.1 and
  ghidra-mcp (native analysis of `TaleWorlds.Native.dll`; ghidra-mcp needs the Ghidra GUI with GhidraMCPPlugin
  enabled), blender.
- Measure, agree a target with the user, then one verified change per test. Stop after 2-3 failed attempts and
  write the handoff (memory `long-session-end-drift`).
- Blocked by a tool: search the web for another tool before giving up.
- Before each trial, write its detailed plan with `superpowers:writing-plans` (`docs/superpowers/plans/`).
- Half Sword data stays in `%USERPROFILE%\halfsword-decomp` (private); nothing from either game is committed.
- STATUS.md and MODLOG.md updated at every milestone.

## What every trial can reuse (all exist today)
- **HSLink stream** (UE4SS Lua in the Half Sword demo): line 1 actor/pelvis/hand; `POSE` (70 features: per arm
  shoulder, elbow, wrist + 4 rotations, plus pelvis and spine rotations), `GRIP`, `AIM` (aim, raw aim, swing speed),
  `WEAPON`; 60-75 samples/s.
- **Pose atlas** recorded by HSPose: DriverSkeleton poses on an aim grid x 8 swing directions x speeds, per class
  (onehand_shield, twohand, polearm, spear), in halfsword-decomp.
- **Extracted Half Sword physics** (private NOTES.md): 22 bodies (80.84 kg), 42 constraints with Motor / Broken / DED
  profiles, muscle logic, a 90 s fight recording with body-to-driver gaps.
- **Bannerlord side:** `ArmRetarget` (per-bone axis maps Half Sword -> human_skeleton), the post-integrate pose hook,
  `LinkMarker` (free-standing weapon copies), the BEPU active ragdoll of step 2 (2026-10-03, gaps pelvis 2.3 cm,
  hands 7.5-10.7 cm, same size as Half Sword's own).
- **Lab tools** (`%USERPROFILE%\tools\gabs`): gabs_call.py, visual_ab.py, input_watch.py, strike_watch.py,
  hs_input_probe.py, hs_grip_trials.py, hs_instance_survey.py, cam_play.py, look_vs_pose.py, grip_presses.py.

## Trial A: passthrough (mashup pattern 2, the current route)
- **Pending, needs the user (~5 min):** play the camera rules deployed 11:42 (`cam_guard` 0.5 = camera keeps half the
  mouse while guarding; `cam_down` 0.5 = a downward move that starts below the horizon counts half). Read with
  `python tools\gabs\cam_play.py <lab log> <HH:MM:SS>`; expected kept/raw 0.5 horizontal while guarding, 0.25 for
  downward moves below the horizon while guarding. Then the user's verdict on the strike height.
- **Torso bend [measured]:** the agent's look pitch equals the camera elevation; below -10 deg the native animation's
  head goes 0.15-0.21 m forward (drawn head 0.24 m). If it still bothers after the camera test, one change: keep the
  look pitch level under Half Sword control (managed side: `MissionMainAgentController` builds `LookDirection` from
  `CameraBearing`/`CameraElevation`, decompiled lines 405-417), or find the native look-at control with
  ghidra-mcp/REA.
- **Right click (half-sword grip) [measured]:** 0/102 scripted trials over 7 fresh instances; it works or fails per
  Half Sword instance (23:34 instance 7/7); aim, driver, fps and re-equip ruled out. Next: decode Willie_BP's
  half-sword search / attach condition (`kismet_mem.py` on a fresh `.jmap`), or place the drawn left hand on the
  drawn blade by IK on the Bannerlord side.

## Trial B: native clips (mashup pattern 1, port the content)
- **Idea:** Half Sword's real strike becomes a native Bannerlord animation clip, so Bannerlord's own combat (hits,
  blocks, AI parries) works with it, clean and firm.
- **Facts:**
  - TaleWorlds docs (https://moddocs.bannerlord.com/asset-management/asset-types/animations/): FBX; 30 FPS optimal or
    60; max 64 bones; Z up; "Use scene name" on; unused bones, and the root for animations, end in `_notused`; no
    constraints or skeleton definitions in the export; then clips in the Resource Browser (id, source1, source2,
    anim_data_name, duration) mapped to actions in `action_sets.xml` (new action types in `action_types.xml`).
  - KB note `knowledge/techniques/editor-free-bannerlord-assets.md` in the uni mod repo (same build 1.4.8.119303): one
    take per FBX (a multi-take import stores the first take under every name, gotcha 5); combat clips in "copy mode"
    from the Native clip of the same action (combat parameter = hit window, flags, `blends_with` / `continue_with`,
    step points, gotchas 10, 12, 21); clip names at most 63 characters (gotcha 11); action set channel 1 drives every
    bone except the `lowerbody`-flagged ones (gotcha 20); clips stay on their own skeleton (gotcha 22). Their writer
    scripts are not published.
  - TpacTool (https://github.com/szszss/TpacTool, last push 2022) exports a rigged model with its skeleton; its
    skeletal-animation export is unfinished. The Modding Kit is installed (the user allows driving it; ask first).
- **Vertical slice:** ONE two-handed overhead strike recorded from Willie (HSLink POSE while the user or
  `hs_input_probe.py` strikes), retargeted to human_skeleton with ArmRetarget's maps, baked in Blender (MCP) to a
  one-take 30 FPS FBX, imported in the Modding Kit, plus a clip copied from the Native two-handed overhead attack clip,
  mapped on the player's action set in a test module.
- **Oracle:** visual A/B in the lab (`visual_ab.py`), the playing action/clip read through `hsc.*` or GABS, and a blow
  registered on a dummy agent.

## Trial C: hybrid (patterns 2 + 1)
- Guard and aim stay passthrough (Half Sword's mouse control); the strike itself is the native clip from trial B,
  triggered when Willie's swing speed (HSLink `AIM`) crosses a threshold, its direction picking the clip; Bannerlord
  resolves hits and parries natively.
- Depends on trial B's clip. First step: log Willie's swing events (speed, yaw) next to the user's mouse to define the
  trigger and the direction mapping before any code.

## Trial D: Rust rewrite of Half Sword's combat body (patterns 4/5, embeddable like pattern 3)
- **Idea:** Willie's active ragdoll reimplemented headless in Rust from the user's extracted data; Bannerlord stays the
  view, later loading it as a native DLL (`cdylib`, `extern "C"`, P/Invoke from the C# mod, which runs on .NET
  Framework 4.8).
- **Facts:** Rapier joints have motors with a target position (quaternion), target velocity, stiffness and damping,
  the building block of UE-style angular drives (https://docs.rs/crate/rapier3d-mjcf; rapier.js `impulse_joint`).
  Step 2 already proved the same idea in C# with BEPU and Half Sword's values.
- **Vertical slice:** a Rust crate kept out of any public repo, reading the data at runtime from halfsword-decomp,
  simulating one arm (upper arm, forearm, hand, weapon) driven to recorded driver poses. Oracle: body-to-driver gaps
  against the 90 s Half Sword fight recording. Skills: `rewrite-rs-skills:port-to-rust`, `rust-testing`, `rust-ffi`.
  Rust stable (cargo 1.99) is installed.

## Order
1. Trial A camera test with the user (short), and its verdict.
2. Trial B vertical slice (the most direct answer to "propre, ferme, parades").
3. Trial C once B's clip exists.
4. Trial D in parallel whenever A/B wait on the user.
Write each trial's results, good or bad, into MODLOG.md and compare them in STATUS.md before choosing.
