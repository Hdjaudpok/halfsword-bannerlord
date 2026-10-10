# STATUS

## NOW 2026-10-06 20:20 (running state; older lines below are history)
- **Running:** Half Sword PID 5332 (20:17, relay PID 39492), Bannerlord lab PID 49796 (20:17:23, log
  `logs\lab-gabs-20261006-201723.log`, loadout 8, build with `lab_rec`), Discord hook OFF. The 11:42 lab crashed at
  12:37:43 in Bannerlord's own UI finalizer (MODLOG 20:20).
- **Trial A closed by the user (20:21):** "test passthrough toujours pareil c pourri passe au prochain test".
- **Trial B (20:45):** the user's last swing is a one-take FBX on TaleWorlds' own `human_skeleton.fbx`
  (`logs\hs_strike_2h_test.fbx`, 49 frames at 30 FPS, round-trip within 0.13 cm; MODLOG 20:45). **Next, needs the
  user:** the Modding Kit import (no command line exists): OK to create a test module `Modules\HSClipTest` in the game
  folder, and who drives the editor (the agent with UI automation while the user does not touch the PC, or the
  user). Then the clip in copy mode from the Native two-handed overhead clip, the action set, the lab oracle.
- **22:10 Modding Kit (user: "essaye de lancer et utilise le modding kit /cua-driver"):** lab and Half Sword closed
  (exact PIDs). Test module created: `<game>\Modules\HSClipTest` (SubModule.xml, no DLL, depends on Native;
  `AssetSources\hs_clips\hs_strike_2h_test.fbx`). The wEditor launcher started directly says "There is no target
  file!"; through Steam (`steam://rungameid/1393600`) it opens (PID 33280). Its window (class `F`, engine-drawn) has
  no usable UIA tree and drops background input; foreground scroll/drag did not move the Mods list. **User took over
  the Mods list** (keep official modules + HSClipTest only), then the agent presses PLAY -> main menu -> Editor
  (or Ctrl+E). `LauncherData.xml` backed up to `Configs\LauncherData.xml.bak-hsclip-20261006` (state at 22:02:37,
  after the launcher's own start-up write): **restore it after the editor session**.
- **22:15 (MODLOG 22:15):** the editor run stopped on 3 blocking RGL warnings (modules using
  `Bannerlord.ModuleLoader.*.dll`) and the game ended; **user: background or 2nd screen only from now on**.
  `LauncherData.xml` restored. Helper `src\HSClipTest` built (opens the editor without a click), **not installed:
  waiting for the user's OK** to put `HSClipTest.dll` in `Modules\HSClipTest\bin\Win64_Shipping_wEditor` and launch
  the editor directly (modules on the command line, window moved to DISPLAY2, warnings closed by background clicks).
- MCP `blender` needs `BLENDER_PATH` (= `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`) in its server
  config; until then Blender is run directly in background mode.

## NOW 2026-10-06 12:27 (fresh session; read this, then the 12:20 handoff below)
- **Session resumed** from the 12:20 handoff with the user's resume prompt: one trial of each method in roadmap order,
  trial A camera test first, a `superpowers:writing-plans` plan before coding each trial, answers in French only.
- **Checks [measured 12:24-12:26]:** MCP bannerlordsage (index 1.4.8.119303, 90 DLLs), gabs (Bannerlord
  `shared-running`: still owned by the previous session's GABS, `games_connect` before sending commands), rea (up, no
  binary open), ghidra-mcp (up, **no Ghidra instance**: open the Ghidra GUI + GhidraMCPPlugin before using it). Skills
  loaded: universal-modder:mod-any-game, universal-modder:mashup-mods, superpowers:writing-plans.
- **Running at 12:26:** Bannerlord lab PID 3848 (11:42:37, log `logs\lab-gabs-20261006-114237.log` written live, CAM
  line every 0.5 s), Half Sword PID 41480 (11:38:41, `hslink_state.txt` live), mouse relay PID 33348, launcher
  PowerShell PID 41352. Loadout 8 (LongSword_T2, left hand attached 11:42:59). Settings unchanged since 11:41:50: no
  `cam_*` lines, so `cam_guard` 0.5, `cam_down` 0.5, `cam_fix` 1.
- **Trial A camera playtest handed to the user at 12:26** (no click / LMB held several seconds / below the horizon /
  normal strikes; verdict on camera dynamics and strike height). Read from 12:25:00 with `cam_play.py`. Expected from
  the code (`HsControl.cs` 164-202): not guarding, bearing 1.0 and downward-below-horizon 0.5; guarding, 0.5 and 0.25.
  Caveat: the CAM `guard` flag is sampled at the end of each 0.5 s window, so a window with a press or release mixes
  both rules.
- **12:42 camera playtest read [measured]** (MODLOG 12:42): clean windows guarding bearing 0.500, downward below the
  horizon 0.250; not guarding 1.000. The rules work as coded. Elevation > 0 = camera low looking up (measured). The
  look pitch stayed >= -3.1 deg while guarding, so the torso-bend mechanism (< -10 deg) did not occur. **Waiting for the
  user's verdict** (camera dynamics, strike height, torso), then plan gate 1a/1b/1c.
- **Plan for trial A:** `docs/superpowers/plans/2026-10-06-trial-a-passthrough.md` (Task 1 done up to the verdict;
  Tasks 2-3 = `look_floor`, only after gate 1c). Execution method not chosen by the user yet.
- **12:45 user: "c'est bien"** (camera verdict, gate 1a) "mais je vois aucun test de rewrite rust ou d'autre
  méthode" -> trials D and B started.
- **Trial D (13:05):** plan `docs/superpowers/plans/2026-10-06-trial-d-rust-arm.md`; crate `rust/hs_body` (4 tests
  pass); baseline on `motion.csv` reproduces NOTES (hand_r 21.8/86.6 cm, 2353 good frames, 257 corrupted rows
  skipped). Next: D2 drive units (web sources), D3 rapier3d arm vs that baseline. Not committed (no OK yet).
- **Trial D slice done (13:40, MODLOG 13:40):** `hs_bench arm` (rapier3d 0.36, both drives in acceleration mode, the
  UE default) follows Half Sword's recorded arm closer than "body = driver": hand_r 19.0 / 70.1 cm vs 21.9 / 86.6
  (median / p95, 2351 frames); force mode for the physical animation is worse (36.5). 7 tests, clippy clean. Next for
  D (one at a time): joint limits, weapon mass; later the `cdylib` + P/Invoke binding. Next overall: trial B plan.
- **Trial B (13:55):** plan `docs/superpowers/plans/2026-10-06-trial-b-native-clip.md`; lab-only `lab_rec` capture
  built (0 errors), **not deployed**. Waiting for the user: (1) OK to restart the Bannerlord lab (PID 3848) to deploy
  it, (2) who does the strike (user or `hs_input_probe.py`), (3) commit OK (asked since 12:26). Trial C needs B's clip.
- **Asked the user:** a checkpoint commit of the mod repo before any trial code (no answer yet).

## HANDOFF 2026-10-06 ~12:20 (superseded by NOW above for the running state and the next step)
- **Decision (user, ~12:10):** one trial of each method: A passthrough, B native Bannerlord clips made from Half
  Sword's real strikes, C hybrid, D Rust rewrite of Half Sword's combat body. Plan, facts with sources, vertical
  slices and oracles: `docs/ROADMAP-4-TRIALS.md`. If a tool blocks a method, search the web for another tool.
- **First step of the next session:** trial A camera playtest with the user (rules deployed 11:42, not yet played),
  read with `python %USERPROFILE%\tools\gabs\cam_play.py logs\lab-gabs-20261006-114237.log <HH:MM:SS>`.
- **Running at 12:20 (PIDs go stale; check with Get-Process first):** Bannerlord lab PID 3848 (started 11:42:37, log
  `logs\lab-gabs-20261006-114237.log`, loadout 8 two-handed sword, new camera rules), Half Sword PID 41480 (started
  11:38:41, minimized, test-13 settings), Discord hook OFF (`tools\discord.ps1 -HookOn` once Bannerlord is closed).
- **Tools:** after the VS Code reload the MCP servers bannerlordsage, gabs, rea and ghidra-mcp were listed in the
  session (check again); ghidra-mcp only works with the Ghidra GUI open and GhidraMCPPlugin enabled once in a
  CodeBrowser. Full list: memory `re-toolchain`. This session's analysis scripts are now in `tools\gabs`
  (`cam_play.py`, `cam_oracle.py` with a hard-coded log path, `look_vs_pose.py`, `grip_presses.py`, `grip_before.py`).
- **Git:** nothing committed in this repo since 2026-10-04; in the uni mod repo the KB index is regenerated and the two
  field notes are untracked (no PR). Ask the user before committing; a checkpoint commit before the trials is advised.

## UPDATE 2026-10-06 ~12:05 (superseded by the handoff above)
- **User (11:55):** "je commence à sentir une limite dans ce que tu peux faire avec le passthrough, ça fait depuis hier
  aucune évolution, tu n'arrives pas à récupérer et ajuster les physiques de Half Sword". Asked to install and assess
  tools, then to update universal-modder.
- **Tools installed and verified** (memory `re-toolchain`): ILSpy 11.1 GUI, Cpp2IL pre-release.21, ghidra-mcp 7.0.0
  (extension built for Ghidra 12.1.4 + bridge MCP at user scope; enable GhidraMCPPlugin once in a CodeBrowser), REA
  4.0.1 (its doctor now passes Ghidra on Windows), Rust + jars, rewrite-rs skills plugin, guides clone. Not installed:
  ida-mcp (needs IDA 9.4+ with idalib). New MCP servers (gabs, bannerlordsage, rea, ghidra-mcp) load at the NEXT
  session start.
- **universal-modder updated to main 6715445** (memory `universal-modder-install`); plugin reinstalled (its skills were
  stale); tests 66/66; KB 60 notes. **Two upstream notes matter here:**
  - `knowledge/techniques/editor-free-bannerlord-assets.md` (Bannerlord 1.4.8.119303, same build): .tpac packages,
    skeletons, **skeletal animations and clips written offline**, action sets, combat-clip rules (copy mode from the
    Native clip, hit window = combat parameter, `blends_with`, 63-char names, channel 1 drives all bones except
    `lowerbody`-flagged ones). Their scripts are NOT published; the note gives formats and gotchas.
  - `knowledge/games/half-sword/super-hammer-ue4ss.md` (full game EA 0.6.1.5, not the demo): `Willie_BP_C:Get Damage`
    signature, public Dumper-7 SDK (lambor590/Half-Sword-Enhancer), BP hooks run after the function.
- **Strategic option to put to the user (not decided):** Half Sword's real strikes as NATIVE Bannerlord clips
  (record Willie's motion from the link, retarget with ArmRetarget, build an FBX in Blender, import one take per file
  through the Modding Kit, or write clips offline per the note), so Bannerlord's own combat (hits, blocks, AI parries)
  works with them; versus continuing the passthrough. A vertical slice would be one two-handed strike.
- **Still pending from the morning:** camera rules (`cam_guard` 0.5, `cam_down` 0.5) deployed, not yet played;
  right click unexplained (0/102). Running: Half Sword PID 41480, Bannerlord lab (log `lab-gabs-20261006-114237.log`),
  Discord hook OFF.

## UPDATE 2026-10-06 (morning session)
- **Nothing running, no code changed** this session. Rules added to memory: `never-invent-use-tools-and-skills`
  (every method through its skill + the installed MCP tools) and `long-session-end-drift` (all 3 long sessions went
  wrong at the end: post-compaction drift, fix loops -> circuit breaker + fresh session from this file).
- **Knowledge base:** field notes written and indexed in the uni mod repo (local only, no PR yet):
  `knowledge/games/mount-blade-ii-bannerlord/half-sword-combat-in-bannerlord-by-passthrough.md` and
  `knowledge/games/half-sword/ue4ss-lua-live-link-to-another-game.md` (`um kb check` PASS, `um kb index` 11 notes).
- **Found in the decompiled 1.4.8 source [read, not yet measured in game]: `CameraPatch` has no effect.**
  `MissionScreen.HandleUserInput` (lines 2796-3288) zeroes `_cameraBearingDelta`/`_cameraElevationDelta` at the start
  (`num8 = 0` outside the editor's smooth mode), adds this frame's mouse, and applies them to `CameraBearing` /
  `CameraElevation` itself (line 3039). Our Harmony **Postfix** scales them after that, so while guarding the camera
  still turns with 100% of the mouse. That fits the measured camera drop during strikes (2.74 -> 1.67 m).
  `MissionMainAgentController` (lines 405-417) sets the main agent's `LookDirection` from CameraBearing/Elevation, so
  the agent's look follows that camera. **Hypothesis (unmeasured):** the native look-at bends the drawn torso with
  it, part of "le perso plie le buste". Decompiled file: `tools\BannerlordSage\dist\games\bannerlord\assets\Source\
  Modules\Native\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.View\...\MissionScreen.cs`.
- **User OK'd (11:00): fix + verify.** Built and deployed 11:06: `CameraPatch` rewritten (prefix/postfix on the angles,
  live `cam_fix = 0` = old behaviour), lab `CAM` line (guard, scale, bearing, elev, raw vs kept degrees, look pitch),
  lab-only `lab_mouse = x y`. All three patches applied (lab log 11:20:41).
  - **Not verified yet:** the scripted oracle (11:22, `cam_oracle.py` in the session scratchpad) got raw = 0 in every
    phase, guard or not: the game applied no mouse at all while its window was in the background (likely the
    `MouseVisible` branch of HandleUserInput, which only edge-scrolls; unverified). It needs the Bannerlord window
    focused: either the user plays (CAM line shows raw vs kept while guarding) or the agent runs the script with
    focus while the user doesn't touch the PC. **Ask which.**
- **Right click [measured]: the half-sword catch is all or nothing per Half Sword instance** (MODLOG 11:20): 23:34
  instance 7/7, every other instance since 0/N (72 scripted trials today, 0 caught). Aim position, `driver` and fps
  are ruled out. Cause unknown; circuit breaker hit, next step to agree with the user.
- **11:45 (MODLOG 11:45):** the user's play confirmed the camera fix (6.5 % kept while guarding) but found it "trop
  fixe"; new user rules deployed: `cam_guard` 0.5 (camera keeps half the mouse while guarding), `cam_down` 0.5
  (downward moves below the horizon count half). **Waiting for the user's playtest**; read it with
  `python <scratchpad>\cam_play.py logs\lab-gabs-20261006-114237.log 11:45:00`.
- **Right click:** 0/102 scripted trials over 7 fresh instances today; aim, driver, fps and re-equip ruled out. Next
  step to choose with the user (IK hand placed by Bannerlord / decode Willie_BP's half-sword search / ask what they did
  at 23:40-00:00 when it worked).
- **Running now (11:45):** Half Sword PID 41480 (minimized, its relay exits with it), Bannerlord lab (log
  `logs\lab-gabs-20261006-114237.log`, loadout 8), Discord hook OFF (`tools\discord.ps1 -HookOn` after Bannerlord
  closes). Settings = test 13 + the new camera defaults: `hslink_tune.txt` maxfps=120 + driver=0.6 (raw mouse),
  Bannerlord `link_input 1`, `lab_pose 1`, no `lab_mouse`/`cam_fix`/`cam_guard`/`cam_down` lines (defaults apply).
- MCP: `gabs`, `bannerlordsage`, `rea` added at **user scope** (11:02, `claude mcp add-json -s user`); they load from
  the next session start. `~/.claude.json` backup: session scratchpad `claude.json.backup-20261006`.

## UPDATE 2026-10-06 00:10 (superseded by the update above; MODLOG 21:58 -> 00:10 has the evidence)
The user stopped for the night: "encore une fois tu passes dans ta phase où tu inventes et on avance jamais".
**Next session: no guessed tweaks.** Measure first, agree a target with the user, one verified change per test
(memory `no-speculative-tuning-loops`).
- **Running: nothing.** Both games and the mouse relay were stopped at 00:12 (exact PIDs); Discord's game hook is
  back ON. Start again: `tools\halfsword\run-hs-link.ps1` (starts the relay too), then `gabs_call.py start lab`
  with `tools\discord.ps1 -HookOff` first, and **no focus_guard when the user is waiting to play**.
- Git: everything since 2026-10-04 is uncommitted (ask before committing).

### Design direction (user, 2026-10-05 23:35)
An adaptation of Half Sword for Bannerlord: "propre et moins flasque, pas comme Half Sword", because enemies will
attack and they must parry. Half Sword stays the engine (controls, aim, physics, weapon data); what Bannerlord draws
must be clean and firm.

### Proven tonight [measured]
- Half Sword crashes were UE4SS (Lua on two threads); HSLink now loops on the game thread: no crash since.
- Two-handed normal grip is fine in Half Sword; the 19:05 "left hand off the grip" was a tool bug.
- Bannerlord's mouse passed on only x 0.77 / y 0.56 (slow moves 0.47): Half Sword now gets the raw counts
  (`hs_mouse_relay.ps1`, Bannerlord-foreground only).
- User-approved state (test 13): "c'est propre", "les 2 clics fonctionnent" = `driver=0.6` + `maxfps=120`
  (hslink_tune.txt), `link_smooth 0.04`, `spine_weight 0.3`, `marker_hsdir 2`, fatigue off. **That is the state left.**

### Open problems (user's words, with what is measured)
1. Left click: "le perso plie le buste et le coup descend vers le sol". Strikes are sideways slashes at Half Sword's
   neutral aim (waist-high guard); at full swing speed the arm extends and the wrist dips ~35 cm mid-swing
   (strike_watch). The camera also orbits/drops during strikes (2.74 -> 1.67 m). Tried and rejected: swing dose,
   side floor, Muscle Power, spine 0, lift (MODLOG 00:10 table).
2. Right click (two-handed): Half Sword's half-sword grip needs Willie's left hand to catch the blade physically;
   it failed in the last two sessions ("full bug").
3. Shield, dagger questions still unanswered; spear double-click thrust built, never playtested.

### Suggested start for the next session (to agree with the user first)
- Ask for a reference of the wanted strike (what a "clean" left-click strike should look like: height, path), then
  measure it with `strike_watch.py` against that target before changing anything.
- For the right click, decide with the user whether Bannerlord should place the left hand on our blade itself
  instead of depending on Half Sword's catch.

## UPDATE 2026-10-05 22:40 (superseded by 00:10 above)
### Running / left on
- **Half Sword** PID 33820 (minimized, HSLink 16 ms game-thread loop, **raw mouse** via `hs_mouse_relay.ps1`, relay
  PID 9788, exits with Half Sword). **Bannerlord lab** started 22:17 (log `logs\lab-gabs-20261005-221715.log`,
  `hsc.loadout 8`). Discord's game hook still OFF.
- Live tuning: `link_pose = 1`, `link_input = 1`, `marker = 1`. Half Sword side live file
  `%USERPROFILE%\halfsword-decomp\hslink_tune.txt` (absent = demo values): `muscle=N`, `blmouse=1`.
### Since 22:25 [measured]
- Willie is at full strength (Muscle Power 35 like every Willie, tonus 1, exhaustion 0).
- Bannerlord's mouse passes on only x 0.77 / y 0.56 (slow 0.47) of the device counts: Half Sword now takes the raw
  counts (relay), as when played directly. Not playtested.
- Big horizontal swings lift the weapon by Half Sword's own design (`Set Arm Aim Rotation`: vertical floor
  -88 deg at the centre to -30 deg at the side; reach grows with swing speed). Muscle Power 35/70/105 does not change it.
- **23:00:** the user chose "weapon low on the sides". The side-floor patch (`hs_bp_patch.py`) lowered everything,
  rest guard included (0.22 -> -0.05 m), and the side rise stayed: **reverted to -30**. A second cause is the
  animation blueprint's swing poses, blended by swing speed (MODLOG 23:00). **Waiting for the user** on the next
  approach (cap the swing speed sent to Half Sword / tune the swing-pose blend / test raw mouse first).
- `hslink_tune.txt` removed (raw mouse on, demo Muscle Power). The Bannerlord mission was paused at 22:37:54.
- **23:10 test series with the user** (they play, then say "fait"; the agent applies the next one with
  `python tools\halfsword\hs_bp_patch.py <HS PID> swing <N>`, Half Sword PID 33820): the swing dose
  `Aim Swing Speed = MapRangeClamped(Swing Velocity proxy, 25, 75, 0, N)` (unique pattern, checked live):
  test 1 = 75 (Half Sword, raw mouse), test 2 = 50, test 3 = 37.5, test 4 = 25. Restore with `swing 75`. The patch
  lives in memory only: a Half Sword restart brings back 75.
  - **Result:** test 1 (Half Sword's 75) "le moins pire" but the problem stays; swing back at 75.
  - Test 5 (measure): our blade vs Willie's (HSLink WEAPON line) within 10 deg 94% of the 2 Hz samples; peaks 30-112.
    Willie's +Z is his blade (152 deg from his right-to-left wrist vector). `marker_hsdir` (0 = off) can align ours.
  - User: "l'arme est trop flasque, ça doit être propre". Test 6: `pose=driver` in `hslink_tune.txt` (DriverSkeleton
    instead of the physical body): "beaucoup trop raide, ça fait même des saccades" -> back to the physical pose.
    `driver=W` blends the two (not tested yet).
  - HSLink loop now 8 ms (`period=8` in hslink_lab.txt): ~75 poses/s instead of ~43 with Bannerlord in front.
  - Test 7 (8 ms, physical pose): "le personnage réagit pas bien, il se baisse": our head went 0.33-0.56 m ahead of
    the pelvis and down to 1.34 m while attacking (Willie throws his torso into swings; the spine is copied).
  - **Test 8 (23:18):** `spine_weight = 0.3` (new live key, ArmRetarget: share of Half Sword's spine on the torso).
    Bannerlord lab restarted 23:17 (log `lab-gabs-20261005-231737.log`).
  - **Bug found 23:20:** the raw mouse relay counted moves made in other windows (Bannerlord ticks unfocused), so the
    user's mouse in VS Code swung Willie's aim between tests (tests 1-8 possibly affected). Fixed: the relay counts only
    while a Bannerlord window is in front, and a restarted relay continues the previous sums.
  - `strike_watch.py` (tools\gabs): per strike, mouse path, Willie's aim (HSLink AIM line), wrist height. 23:29: the
    user's strikes are sideways slashes at neutral height; at full swing speed the wrist dips ~35 cm mid-swing: Half
    Sword's neutral aim is a waist-high guard. Ready, not tested: `lift=N` in hslink_tune.txt (strike start height).
  - User 23:31: better at first, then "il s'est mis à se baisser, peut-être la fatigue, qu'il faut désactiver".
    Exhaustion stayed 0, but Stamina scales ~15 things in Willie_BP. **Test 9 (23:32):** HSLink `calm` keeps
    Stamina 100 and Exhaustion 0. Result: "les mouvements partent dans tous les sens".
  - `maxfps=N` (hslink_tune.txt): Half Sword's frame cap (t.MaxFPS + GameUserSettings; t.MaxFPS alone did nothing).
    At 120: ~118 fps measured (was 190-380). Test 10 not played: the user answered the design question instead.
  - **23:35 design decision (user):** an adaptation of Half Sword for Bannerlord, "propre et moins flasque, pas
    comme Half Sword", since enemies will attack and they must parry (memory note halfsword-bannerlord-design).
  - New Bannerlord key `link_smooth` (s): the drawn pose follows Half Sword's with that time constant.
  - **Test 11 (23:39), "clean" preset:** `driver=0.6` + `maxfps=120` (hslink_tune.txt), `link_smooth = 0.04`,
    `spine_weight = 0.3`, fatigue off, raw mouse. Bannerlord lab 23:38 (log `lab-gabs-20261005-233852.log`),
    Half Sword PID 44148, relay 28972. **Result: "c'est propre, mais le clic droit est bugué"** (two-handed RMB =
    Half Sword's half-sword grip; our left hand ended up to 45 cm from our blade).
  - Test 12: `marker_hsdir = 1` (our blade always along Willie's): "clic droit réparé, mais le clic gauche a perdu
    en propreté" (the physical blade's wobble came back). Turned off.
  - **Test 13 (23:47):** `marker_hsdir = 2`: align only while Willie half-swords (GRIP alt), fading over
    `marker_hsdir_fade` 0.12 s, Willie's direction smoothed over `marker_hsdir_smooth` 0.06 s. Bannerlord lab 23:46
    (log `lab-gabs-20261005-234609.log`). **Result: "c'est mieux, les 2 clics fonctionnent, mais encore une fois
    clic gauche le joueur se baisse"**; asked what exactly: "le perso plie le buste et le coup descend vers le sol".
    (The camera also orbits and drops during strikes: up 2.74 -> 1.67 m; the CameraPatch scale does not hold it.)
  - Test 14 (23:51): `spine_weight 0` + `lift=25`: **"plus rien qui fonctionne, tout est bugué"** (strikes to the
    ground and floppy, right click broken, torso not following). Reverted at 23:56 to the test 13 settings.
    Spine 0 detaches the arms from the torso; lift (forcing Aim Vector_0 at the press) disturbed the strikes.
  - Back to test 13 (23:59): "clic gauche toujours les coups vers le bas, clic droit pas aussi bien qu'avant"
    (same settings; Willie's half-sword grip did catch the blade).
  - The 23:51 lift had no effect: until Willie_BP counts him as guarding it copies Aim Vector (~0) into Aim Vector_0.
    Fixed: both held at the lift height for the first 0.15 s of the press (strike_watch shows it now).
  - Test 15 (00:01): lift 25 -> "clic gauche trop haut, vraiment trop; clic droit full bug" (the only right click:
    Willie's left hand never caught his blade, searching for 3 s).
  - **Test 16 (00:04):** lift 12, and only for a left click alone (never while the left guard is held). Half Sword
    PID 33776, relay 31108; Bannerlord lab from 23:46 still running.
  - Next ideas: cap the spine's forward bend instead of scaling the whole spine; check why the camera drops during
    strikes; for the right click, do not depend on Willie's left hand catching the blade (physics, can fail).

## UPDATE 2026-10-05 22:25
### Running / left on (superseded by 22:40)
- Half Sword PID 23540, Bannerlord lab 22:17.
- Live tuning as for the user: `link_pose = 1`, `link_input = 1`, `marker = 1`, `hands_keep` default 1.
### Done this evening [measured]
- **The 19:05 "two-handed left hand leaves the grip in Half Sword" was a tool bug** (input_watch read the left
  shoulder). Half Sword alone keeps the longsword grip 100% of the time, wrists 0.14-0.15 m apart.
- **Half Sword crashes = UE4SS:** all 8 minidumps fault inside UE4SS.dll at different addresses. HSLink's
  `LoopAsync` + `ExecuteInGameThread` ran Lua on two threads; now `LoopInGameThreadWithDelay`. Stress at 5 ms (which
  crashed 4/4 before): 8.6 min, 342 equips, no crash.
- **HSLink `GRIP` line** (Willie's two-handed grip state) and `HANDS` lab line in Bannerlord (wrist distances, each
  wrist's distance to the drawn blade axis).
- **Two-handed in Bannerlord:** normal grip is fine (the left hand is on the weapon); the IK `hands_keep` trims ~1 cm.
  What breaks is **Half Sword's half-sword grip** (left guard = Bannerlord RMB): Willie's left hand goes onto his blade
  and turns the weapon in his hand, while our copy keeps a fixed grip: the left hand ends 35 cm from our blade.
- **Double-click thrust** built (`thrust_dclick`, 0.3 s): second press of the attack key held = Half Sword's Thrust.
  Not playtested.
### Next
1. HSLink `WEAPON` line (Willie's right weapon relative to `hand_r`); turn the copy by the change from the
   normal-grip reference (MODLOG 22:25 "Next"). Then the same for the shield (left).
2. User playtest: double-click thrust (spear), two-handed sets with and without RMB.
3. Up/down precision (Half Sword has UE mouse smoothing on; HSLink doesn't reproduce it). Redo tests 1, 4, 7.
4. Questions still open: what differs on the shield; the dagger "à 2 mains / de l'autre côté".
5. MCP: GABS/BannerlordSage/REA are local-scope under `C:/...uni mod`, not loaded in VS Code sessions (`c:\...`);
   offer to move them to user scope (needs a session restart).

## UPDATE 2026-10-05 19:15 (superseded by 22:25 above; kept for history)
The previous session ran long and was compacted. Its summary drifted from the facts, and the user stopped it ("tu
commences à halluciner"). Everything below is checked against the code, the logs and the user's own messages. History
is in MODLOG.md: the entries from 2026-10-05 17:55, 18:25 and 19:05 cover this afternoon.

### What is running / left on
- **Bannerlord** lab is running: PID 42812, GABS profile `lab`, log `logs\lab-gabs-20261005-181237.log`. Stop it
  with `gabs_call.py stop` (exact PID).
- **Half Sword** is NOT running: it crashed at 18:47:28. Start it with
  `powershell -File tools\halfsword\run-hs-link.ps1`. It starts minimized, prints the PID and waits for "player found".
- **Discord** stays open (the user is in voice). Its game hook is OFF (`tools\discord.ps1 -HookOff`, the DLL renamed
  `.hsc-off`), because it deadlocks Bannerlord with renodx-dlss. Run `tools\discord.ps1 -HookOn` once Bannerlord is
  closed.
- `%USERPROFILE%\halfsword-decomp\hslink_lab.txt` = `duel calm input`.
- Live tuning: `link_pose = 1`, `link_input = 1`, `marker = 1`, `lab_cam` commented out (normal camera for the user).

### What works [measured]
- **Arms:** both arms come from Half Sword. HSLink's POSE line is applied by ArmRetarget (`link_pose = 1`).
- **Input:** the player's mouse and buttons go to Half Sword (`Link\HsInputLink.cs` writes `hslink_input.txt`). HSLink
  disables Willie's own input and calls Willie_BP's input events once per engine frame, spreading the mouse over the
  frames.
- **Held items:** one free-standing copy per hand (`Link\LinkMarker.cs`), drawn on our hand bone; the natives are
  hidden with `ClearWeaponMeshes`. A new item in the same slot, or `hsc.loadout`, rebuilds the copies.
- **Weapon sync:** `Link\HsWeaponMap.cs` maps the Bannerlord weapon class to a Half Sword family, one fixed weapon each
  (no size matching yet). Shields go by item usage: `hand_shield` -> `Shield_BossGrip`, `shield` -> `Shield_Tagre`.
  HSLink equips Willie through Willie_BP's own Set Up Right/Left Hand Weapon.
- **Test sets:** `hsc.loadout list|next|prev|N` (`Lab\LabLoadouts.cs`) has 13 sets, each checked equipped in both games.

### User playtest of the 13 sets (18:26-18:47), the user's words, summarized
- 1H sword (1) and axe (4): good overall; up/down moves work badly and the aim still lacks precision.
- Shield (2, 3, 5, 6): "le bouclier du jeu n'est pas comme celui de Half Sword".
- Axe or mace with a shield (5, 6): only downward blows, no upward ones.
- Dagger (7): moves too low; the posture is wrong when held with two hands / on the other side (unclear, ask).
- Every two-handed weapon (8, 9, 10, 12, 13): "buggé", the left hand badly placed. For 8 they asked whether Half Sword
  held the right weapon: yes, `ModularWeaponBP_LongSword_T2`, per hslink.log.
- Spear (11): like Half Sword, but it should be firmer, and a double-click should thrust for reach.
- They believe the fists fix made the moves worse. Not proven either way:
  - the fix only acts with an empty left hand while the left guard is held, and that button was unused in the retest;
  - the right wrist peaked higher (p95 0.79 m vs 0.60 m), then Half Sword crashed.
- Caveat: tests 1, 4 and 7 were spoiled by the re-equip bug (fixed 18:37). Redo them.

### Causes found
- **Two-handed — CORRECTED 2026-10-05 21:58 [measured]:** the "0.24-0.70 m" above came from a tool bug
  (input_watch read the left SHOULDER, MODLOG 21:58). Half Sword alone with scripted input keeps the left hand on the
  longsword grip 100% of the time, wrists 0.14-0.15 m apart, even on fast flicks; the left guard switches to the
  half-sword grip (0.16-0.24 m), as Willie_BP says. So the fault is on the Bannerlord side only: the arms copy
  rotations onto other bone lengths and nothing keeps the left wrist at Half Sword's distance from the right one.
- **Shield:** the copy follows the Bannerlord hand bone with Bannerlord's item frame, not Half Sword's shield transform.
  Exactly what looks wrong to the user is unknown (question below).
- **Thrust:** Half Sword binds Thrust to LeftAlt only. The double-click thrust of the 2026-10-03 design must be
  detected in Bannerlord; HsInputLink sends only LeftAlt today.

### Next (proposed to the user, waiting for their OK on the order)
1. **Stream Half Sword's weapon and shield transforms**, plus the left-hand grip target, in the POSE line. Place the
   copies with them, and put the Bannerlord hands on the grips (Half Sword does the same: IK L Hand Location = weapon
   grip component).
2. **Double-click on the attack key -> Half Sword's Thrust**, the press/release pair HSLink already sends for LeftAlt.
3. **Up/down precision:** measure the mouse -> wrist gain, and run a scripted A/B with and without the fists fix
   (`tools\gabs\input_ab.py`, same input both ways).
4. **Redo tests 1, 4, 7.**
- **Questions to the user, not answered yet:**
  - shield: what differs from Half Sword: position in the hand, orientation, size, or how it moves?
  - dagger "à 2 mains ... de l'autre côté": does Willie grab it with both hands (left guard held), or is the
    Bannerlord dagger reversed?

### Do not retry (failed today, see MODLOG 18:25)
- HSLink `LoopAsync` faster than 16 ms: 4 launches at 5 ms, 4 crashes.
- In-game weapon catalogue (lab switch `catalog`): `K2_GetComponentsByClass` gives no array Lua can walk, and fast
  weapon swaps crashed. Sizes must come from extracted assets instead.
- Errors inside UE4SS `ForEach` callbacks escape the outer pcall. Wrap the callback body itself.

### How to run a player test (bounded commands only; the user dislikes commands that never end)
1. Start Half Sword: `run-hs-link.ps1`.
2. Start Bannerlord: `python %USERPROFILE%\tools\gabs\gabs_call.py start lab`, with the Discord hook off.
3. Equip a set: `gabs_call.py cmd "hsc.loadout N"`, then check `hslink.log` for `lab: equip ... -> held ...`.
4. Record in the background: `python %USERPROFILE%\tools\gabs\input_watch.py 1800 out.txt` logs the buttons,
   the mouse and both Half Sword wrists.
- `tools\gabs\focus_guard.ps1` only around launches, NEVER while the user plays: on 2026-10-05 18:37 it stopped the
  user from clicking into the game.

### Git
Most of the passthrough work is untracked or uncommitted: STATUS.md, docs/, `src\HalfSwordCombat\Link\`, Lab files,
HscCommands.cs, and more. Ask the user before committing.

## UPDATE 2026-10-05 11:55
- **Phase 1 works with the real Half Sword [measured]:**
  - with `link_pose = 1` and `marker = 1`, Bannerlord's right arm and the free-standing weapon copy follow Willie's
    real physical arm;
  - Guard Right with aim up -> 118 change cells, the raised arm and axe; noise 1 cell.
- **The user said the weapon sits perfectly in the fist** on our drawn hand bone. That is now the marker's default;
  `marker_link = 1` is only the link test.
- **Discord stays open:** its game hook is turned off with `tools\discord.ps1 -HookOff`. Run `-HookOn` after
  Bannerlord closes.
- **Next:**
  - hide the engine's own held axe (`MBAgentVisuals.ClearWeaponMeshes`), measured with visual_ab;
  - the left arm and the shield;
  - the user's playtest.

## UPDATE 2026-10-05 11:40
- **Test 0 [measured]:** `tools\gabs\visual_ab.py` judges A/B screenshots by pixels.
  - It switches the tested keys several times and keeps only the pixels that follow every switch.
  - One pair proves nothing: about half the screen changes by itself between two shots.
  - Result: the `item_ghost` copy is never drawn. Moving the original item entity is drawn.
- **Phase 0 is visible [measured, fake sender]:** `Link/LinkMarker.cs` (`marker = 1`) draws a free-standing copy
  of the weapon, made like a dropped weapon, at Half Sword's hand.
  - With `link_ab.py` standing in for HSLink, moving the hand +50 cm up or +50 cm right moves the drawn weapon
    (52 and 69 change cells against 0-11 of noise).
  - Details in MODLOG 2026-10-05.
- **Phase 1 is built, not tested in Bannerlord (2026-10-05 11:45):**
  - HSLink's second line `POSE` carries the physical arm pose in the arm atlas's features;
  - `link_pose = 1` makes ArmRetarget take it instead of the atlas;
  - the Half Sword side is measured with the real demo: lab switch `arm` + Guard Right lifts the wrist 0.71 m.
  - Protocol and lab switches: docs/PASSTHROUGH.md 4b.
- **Bannerlord is on hold (user, 2026-10-05):** Discord is open again.
  - Its `discord_clips` deadlocked Bannerlord with renodx-dlss at 11:27:33 (stackdump in MODLOG).
  - Before the next Bannerlord run, decide with the user: close Discord, turn off its overlay and clips, or run
    the lab without renodx-dlss.
- **Next Bannerlord run (one command once the lab is up and Half Sword streams):**
  `link_ab.py OUT --real-arm "0 0 0" "1 0 -60" --setup "link_pose=1 marker=0" --settle 3`. The change map must be
  the player's right arm.
- **Later:** the compositing route. Half Sword renders Willie with Bannerlord's camera, and a ReShade add-on
  depth-tests him into Bannerlord's frame (`uni mod\examples\minecraft-gta5-passthrough`).
- `verdict()` in `visual_ab.py` is left to the user (TODO).

## UPDATE 2026-10-04 23:50
- **Method changed to passthrough:** the real Half Sword runs next to Bannerlord. See `docs/PASSTHROUGH.md`.
- **Proved, by numbers in the lab log (LINK line):**
  - the UE4SS mod `HSLink` streams Half Sword's actor, pelvis and right hand at about 60 Hz into
    `%USERPROFILE%\halfsword-decomp\hslink_state.txt`;
  - Bannerlord (`Link/HsLink.cs`) reads it correctly, checked with a fake sender with known values and with the real
    game while the user moved the arm.
- **NOTHING visible has changed in Bannerlord yet.** The user saw no change all day, and was right.
- **The held-item "copy" method (`item_ghost`) does not work. Proof:** engine screenshots with `copy_offset 0 0 1`
  and with `copy_hide 1` look identical to normal.
  - The copy entity (`GameEntity.CreateEmpty`, `AddMultiMesh` of the item's meshes, `AddChild` to the agent visual
    entity, mobility Dynamic) is **never rendered**.
  - `SetVisibilityExcludeParents(false)` on the engine's own item entity does **not** hide it. Its flag reads false
    while it stays on screen.
  - So every "HELD copy err=0" telemetry line described an invisible object.
- **Lesson:** an engine readback (frame, visibility flag) is not proof of what is drawn. Verify with a test that must
  visibly change the screen (move +1 m, hide), and trust the user's eyes over the agent's numbers.
- **Open, not started:** how the game itself draws and hides held items. In the source: `MBAgentVisuals.AddMultiMesh`
  with `BodyMeshTypes`, and `AddWeaponToAgentEntity`. Also how to pose the Bannerlord arms from the streamed Half Sword
  bones (phase 1).
- **Running at the time of writing:** a Bannerlord lab (GABS profile `lab`) and the Half Sword demo minimized with
  HSLink.
  - Discord was closed for the lab runs; restart it with `tools\discord.ps1 -Start`.
  - `BannerlordConfig.txt` now has `StopGameOnFocusLost=False` (the user changed it in-game for two-window testing).

Written 2026-10-04 17:55 with the STATUS-handoff template from trevaintdead/ai-game-modding-guides, because the chat
went in circles for two days on the hand / weapon placement. Verified facts are marked **[measured]**; everything else
is an assumption.

## Goal
Half Sword's real combat physics and controls for infantry on foot in Mount & Blade II: Bannerlord. Walking stays
Bannerlord's; all combat controls work as in Half Sword:
- LMB guards with the right arm, RMB guards with the left arm;
- the mouse moves the arm while a button is held;
- double click thrusts, Left Alt is a manual thrust.

Design decisions: memory note `halfsword-bannerlord-design`.

## Setup
- **Game:** Bannerlord v1.4.8.119303 (Steam 261550), single player.
- **BUTR stack:** BLSE 1.6.2, Harmony 2.4.2.225, ButterLib 2.10.4, UIExtenderEx 2.13.2, MCM 5.11.3.
- **Source data:** the Half Sword demo (UE5), read through UE4SS Lua dumps. Private data lives in
  `%USERPROFILE%\halfsword-decomp` and is never committed.
- **Agent:** Claude Code.
- **Tools:**
  - BannerlordSage MCP: the decompiled 1.4.8 source;
  - GABS + the Bannerlord.GABS bridge: launch, console, engine screenshots;
  - `tools\gabs\gabs_call.py` and `hsc_shots.py`;
  - REA 3.2.1 (MCP `rea`).
    - Managed-only on this PC: `rea doctor` fails with `spawn EINVAL`.
    - Native analysis rejects DLLs on Windows.
  - Modding Kit (editor installed; the user allows driving it).
- **Paths:**
  - mod: `<repo>`;
  - lab logs: `logs\lab-gabs-*.log`;
  - live tuning file: `Documents\Mount and Blade II Bannerlord\Configs\HalfSwordCombat_tuning.txt`.

## What works (tested, 2026-10-04, before the passthrough)
- **Lab loop [measured]:**
  - `gabs_call.py start lab` launches; `hsc.status`, `hsc.set`, `hsc.get` and `hsc.dump` work;
  - engine screenshots work; `stop` and `kill` use the exact PID; `safely_exited` is restored.
- **GABS bridge crash fixed [measured].**
  - Cause: Lib.GAB's `async void` handler threw an exception when a client disconnected.
  - Fix: `Patches\GabpServerSafetyPatch.cs` in our bridge build.
  - Proof: two connect/disconnect cycles, no unhandled exception.
- **Arm retarget [measured]:** upper arm and forearm directions match the Half Sword atlas within 0.0-0.2 degrees.
  - Tool: `grip_compare.py`, run on `hsc.dump` POSE lines against `port\arm_atlas_onehand_shield*.txt`.
- **Intended weapon frame [measured]:** `ITEM_R` = drawn `r_finger0` x hold.
  - Its long axis (+Z) is 7.9 degrees from Half Sword's blade direction in every pose tested; the edge is 12 degrees off.
  - Weapon vs forearm angle: ours 115, Half Sword 114 (high guard).
- **Item bones [measured]:** `ItemR` / `ItemL` are `r_finger0` / `l_finger0`, with secondary bones `r_foretwist1` /
  `l_foretwist1` (`AnimationSystemData`).
  - `r_finger0` animated local equals its rest local within 0.07 degrees.
  - `l_finger0` differs by 32 degrees (`hsc.grip`).
- **Native engine binding [measured, REA]:** `TaleWorlds.MountAndBlade.dll` has 0 P/Invoke. Its 216 native methods
  are delegates, so item attachment is native code in `TaleWorlds.Native.dll`, invisible from C#.

## What didn't work (2026-10-04, before the passthrough; history)
- **The weapon and shield are not drawn correctly in the hands.**
  - The user reports this repeatedly ("les mains sont toujours pas mieux", "le bouclier agit super mal").
  - The user's screenshot (high guard): the axe shaft runs BESIDE the closed fist, parallel to the forearm.
- **Physics tracking error:** hands are 55-177 degrees off target. Muscle, motor, limit and solver changes made no
  difference.
- **Pose recording is incomplete:** polearm / spear R-N-T-A, and the RL sets.

## Which game owns the player
Since 2026-10-04 23:31 this is a passthrough mod. Half Sword owns the upper body, the arms, the held weapons and the
combat controls. Bannerlord owns the agent's position and walking, and the world. See docs/PASSTHROUGH.md.

## The problem as of 2026-10-04 (history; the current one is at the top)
- **Did:** the lab profile, right guard (`lab_pose 1`, `lab_aim 0 0`) and high guard (`lab_aim 0 -60`), close-up
  engine screenshots.
- **Expected:** the weapon handle passes through the closed right fist and points like Half Sword's blade (the computed
  frame says it does, within 8 degrees).
- **Happened:** on screen the handle is beside the fist (old method), or the fist is empty with a dark shaft behind
  the shoulder ("drawn copy" method, right guard).
- **Unknown:** what that shaft is. It may be our copy or the engine's own weapon still visible.

## Evidence
- `hsc.dump`, drawn-hand to `ITEM_R` distance: 0.085 m (right guard) and 0.086 m (high guard). The computed item
  stays on the hand.
- `grip_compare.py` output: see MODLOG 2026-10-04.
- Close-up screenshots: scratchpad `shots5`, `shots6` (temporary). Before/after image: `logs\avant-apres-20261004.jpg`.
  - The user disagreed with the "after" judgement.
  - **The agent's visual judgements were wrong several times. Do not trust them.**

## What we've already tried
1. Half Sword's measured hold (`hs_grip 1`, `HsGrip.cs`): the axe ended up behind the head and the shield flat. Bad.
2. The base game's hold (`hs_grip 0`, `off_item_rot 0`): looked better to the agent, not to the user.
3. Compensating the engine's native item attachment by setting the item entity's local frame:
   - `item_anchor` / `item_order`, four combinations, screenshot A/B;
   - only `local = drawn x hold x animated^-1` kept the weapon near the fist, and it was still beside it.
4. The item bone's held local frame (`item_grip` 0/1/2): no change for the right hand (rest equals animation).
5. Hiding the engine's item and drawing a copy of its meshes on drawn bone x hold (`item_ghost 1`, `HeldItems.cs`):
   - not verified;
   - the right guard showed an empty fist.

## Ideas not tried yet
- **Telemetry instead of eyes** (guide 5).
  - Log every tick: our copy's global frame read back from the engine against the intended frame, and whether the
    engine's original item is still visible.
  - Then the user playtests and reports with "I did / I expected / what happened".
- **Modding Kit editor** (allowed by the user): Model Viewer / Skeleton Editor. Check the item bone frame and each
  weapon mesh's origin and axes, i.e. which axis is the handle and where the grip is.
- **Ghidra headless directly on `TaleWorlds.Native.dll`** (Ghidra 12.1.4 is installed; REA cannot take DLLs on
  Windows). Find how `AddWeaponToAgentEntity` and skeleton-bone attachment place item meshes.
- **Item Position Editor** (Nexus 13041), downloaded by the user: see how it moves held items.
- **A smaller goal:** one weapon (a sword, not the axe) and one pose until it is right in the user's eyes.

## Files that matter
- `src\HalfSwordCombat\Physics\HeldItems.cs`: places the held items (old compensation and the `item_ghost` copy).
- `src\HalfSwordCombat\Physics\ArmRetarget.cs`: Half Sword poses onto Bannerlord bones; item bone frames
  (`item_grip`).
- `src\HalfSwordCombat\Combat\HsPlayerBehavior.cs`: controls, poses, lab logging, `hsc.status` / `hsc.dump` /
  `hsc.grip`.
- `src\HalfSwordCombat\Physics\ActiveRagdoll.cs`: the physics body; `DrawnEntitial` / `AnimatedEntitial` readback.
- `MODLOG.md` (history), `MODDING_PLAN.md` (recon), `tools\gabs\*` (lab tooling).
