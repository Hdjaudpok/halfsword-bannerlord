# Mount & Blade II: Bannerlord modding plan (HalfSwordCombat)

Recon 2026-10-04 (game-recon skill). The step-by-step history is in MODLOG.md; this file is the current answer.

## The game

- **Install:** `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord` (Steam 261550),
  **v1.4.8.119303**. The NavalDLC module is present.
- **Modding Kit:** Steam app 1393600, build 24575232, installed 2026-10-04 into the same folder.
  - Its editor lives in `bin\Win64_Shipping_wEditor` (Qt): Model Viewer, Skeleton Editor.
  - User data goes to `Documents\Mount and Blade II Bannerlord\Editor Files`.
- **Engine:** TaleWorlds engine. The native C++ part is `TaleWorlds.Native.dll`. The game logic is managed
  .NET Framework 4.8 (`TaleWorlds.*.dll`), 64-bit.
- **Anti-cheat / online:** `um scan` found none. HalfSwordCombat is single-player only and is never loaded with
  the Multiplayer module.

## Paths

- **Saves:** `Documents\Mount and Blade II Bannerlord\Game Saves`.
- **Config:** `Documents\Mount and Blade II Bannerlord\Configs`:
  - `engine_config.txt`, whose `safely_exited` flag must go back to 1 after any kill;
  - `HalfSwordCombat_tuning.txt`, the live tuning file.
- **Logs:**

  | Source | Location |
  |---|---|
  | Engine | `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_<pid>.txt` (plus `rgl_log_errors_<pid>.txt`) |
  | BLSE / ButterLib | `Documents\...\Configs\ModLogs` |
  | ReShade | `bin\Win64_Shipping_Client\ReShade.log` |
  | This mod | `Documents\mods\halfsword-bannerlord\logs` (one `lab-gabs-*.log` per GABS lab launch) |

## Community route

The BUTR stack, all installed and loading on 1.4.8:

| Component | Version |
|---|---|
| BLSE (`Bannerlord.BLSE.Standalone.exe`) | 1.6.2 |
| Bannerlord.Harmony | 2.4.2.225 |
| ButterLib | 2.10.4 |
| UIExtenderEx | 2.13.2 |
| MCM (`Bannerlord.MBOptionScreen`) | 5.11.3 |

- Documentation: moddocs.bannerlord.com (official, editor included) and the BUTR GitHub organisation.
- Version symbols go in `supported-game-versions.txt`.
- **Prior art for the idea** (search 2026-10-04): no Bannerlord mod does Half Sword-style active-ragdoll combat.
  - The nearest are RBM (installed, v4.3.0), BEST, and Realistic Medieval Combat Physics.
  - All of them tune animations, damage and AI; none of them adds physical bodies.

## Chosen route

The idea: Half Sword's physics combat for infantry on foot. The route is **managed patching (Harmony) plus engine
APIs, from a BUTR-stack module**:
- native attack, defend and kick are switched off under Half Sword control;
- a physics body made of engine physics shapes follows Half Sword's real controller (decoded Willie_BP logic and
  poses recorded from the local Half Sword demo);
- the drawn skeleton comes from the engine pose hook.

**Why this route:**
- no loader API covers skeletal physics;
- a data or asset mod can change neither the controls nor the physics;
- managed patching reaches input, missions, agents and skeleton frames.

**Half Sword data:**
- It stays private in `%USERPROFILE%\halfsword-decomp` (personal use).
- It is never committed or shipped.
- No AES, DRM or ownership check is bypassed.

## Lab

- **Launch:** through GABS (`~/.gabs/config.json`, game `bannerlord`):
  - profiles `lab` (fake input from the tuning file, kinematic body) and `lab-physics`;
  - `launch-bannerlord.ps1` starts BLSE Standalone with an explicit module list, so `LauncherData.xml` and the
    user's TAOM shortcuts stay untouched;
  - stop and kill target the exact game PID only (`bannerlord-lifecycle.ps1`).
- **Observe and adjust:**
  - `hsc.*` console commands through GABS `core/run_command`;
  - engine screenshots through `ui/take_screenshot`, which need no window focus but show HDR colours off under
    RenoDX;
  - `lab_cam` orbits the camera, `lab_calm` switches AI ticking and dying off.
  - `tools\gabs\hsc_shots.py` takes pose/aim/camera series and makes a contact sheet.
- **Read the game:** the BannerlordSage MCP holds the decompiled 1.4.8 source and XML. Plain files also sit in
  `tools\BannerlordSage\dist\games\bannerlord\assets\Source\...`.
- **Safety:**
  - so far only custom battles run with the mod;
  - `um backup` the saves before any campaign play-test;
  - close Discord before lab runs (its overlay hook deadlocks with the RenoDX DLSS add-on) and restart it after;
  - never drive the user's mouse.

## Unknowns to resolve first

1. **Crash at 16:56:55.7 on 2026-10-04 in a lab battle.** The exception is unknown.
   - Context:
     - GABS was connecting to the Bannerlord.GABS bridge;
     - `lab_calm` had just switched `Mission.AllowAiTicking` off; the engine does the same during deployment, so
       this is a normal state;
     - the previous lab run, without `lab_calm` and with GABS connecting before the battle, ran for minutes.
   - The crash showed up as a **freeze**: BLSE's "Bannerlord Crash Report" window (ImGui/GLFW, ButterLib's
     `glfw3.dll`) deadlocked with ReShade's `renodx-dlss.addon64`:
     - the render thread, inside the add-on, holds the add-on's lock and waits for the loader lock
       (`CreateToolhelp32Snapshot` from Present);
     - the crash-window thread, loading `nvoglv64.dll` through the Steam overlay's LoadLibrary hook, holds the
       loader lock and waits for the add-on's lock.
   - Next: log first-chance exceptions in the lab, reproduce, then try `lab_calm` without GABS and GABS without
     `lab_calm`.
2. **With these ReShade add-ons, any crash looks like a freeze.** Discord's overlay hook triggered the same
   deadlock earlier.
   - Four DLSS add-ons load at once: `renodx-dlss`, `renodx-dlss5` (DLSS5-Feeder), `dlss5-bridge` and
     `dlss5-dx11-bridge`.
   - The user decides: lab runs without ReShade, or updating or trimming the add-ons. Newer DLSS5 tool releases
     dropped per-Present module enumeration.
3. **Hand and weapon placement:**
   - `item_anchor = 0` is still unverified (items hang on the original animation's item bone);
   - the hand axis maps still need checking against the Half Sword atlas (pose dumps of 2026-10-03 ready).
4. **Physics tracking error:** hands are 55-177 degrees off target, and muscle, motor, limit and solver changes
   do nothing.
5. **Pose recording is incomplete:** polearm and spear (R/N/T/A), and the RL sets of twohand, polearm and spear.
6. **Item Position Editor** (Nexus 13041): Nexus blocks automated access, so it needs the user's download. Then
   check whether its item-frame editing can replace hand-tuned offsets.
7. **Modding Kit editor:** the Model Viewer and Skeleton Editor are GUI tools. Using them means driving the mouse
   and keyboard, so ask the user first, or the user drives them.
