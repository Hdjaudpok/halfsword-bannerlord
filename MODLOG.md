# MODLOG — Half Sword combat in Mount & Blade II: Bannerlord

## Goal
Bring Half Sword's physics-driven melee (free mouse-driven weapon control, momentum-based damage,
physical hit reactions / active ragdoll) into Bannerlord single-player battles.

## Route (draft, mashup-mods pattern 1: port the mechanic)
Half Sword runs on Unreal Engine 5 (PhysX-free Chaos physics); its physics cannot be "exported".
Read its behaviour (tuning, damage, constraint setup) from the user's own install, then reimplement it
in Bannerlord's C# mod API (Harmony patches + MissionBehavior). Never ship Half Sword assets or decompiled code.

## Paths
- Bannerlord v1.4.8 (Steam 261550): C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord
- Saves/configs: %USERPROFILE%\Documents\Mount and Blade II Bannerlord
- Decompiled reference (outside any repo, never published): %USERPROFILE%\bannerlord-decomp
- Half Sword: not installed yet (2026-10-02)

## Environment found (2026-10-02)
- Loader stack already installed: Bannerlord.Harmony 2.4.2.225, ButterLib 2.10.4, UIExtenderEx 2.13.2, MCM 5.11.3, BLSE.
- Combat-touching mods installed (conflict risk, disable in a lab setup): RBM 4.3.0 (+RBM_WS), ArtemsCinematicCombat 1.4.9,
  DismembermentPlus 2.0.8.6, BloodMod1313, TAOM 2.0.29.5. ReShade present.
- Anti-cheat: none. Stay out of the Multiplayer module.
- Native physics: PhysX (PhysXGpu_64.dll).

## Log
- 2026-10-02: recon started. KB has no note for Bannerlord or Half Sword.
- 2026-10-02: `um scan` routes: Harmony via loader (dotnet-xna playbook), native hooks as fallback.

## User's real mod setup (from Codex session "Reprendre le correctif Bannerlord", 2026-10-02)
Source: ~/.codex/sessions/2026/10/02/rollout-...01a0fc4d... and Documents/Bannerlord-Mods/DIAGNOSTIC_TAOM_2026-10-02.md
- Main profile: TAOM (The Age of Man) + 36 modules, RBM disabled by the user. Launched through BLSE shortcuts in
  Documents/Bannerlord-Mods (newest: "Bannerlord TAOM 36 mods + sauvegarde.lnk"). Steam launcher crashes in d3d11/GameOverlayRenderer64.
- ButterLib and MCM as separate modules crash TAOM (their libs ship inside TAOM.Dependencies) -> our mod depends on Bannerlord.Harmony only.
- NavalDLC (War Sails), RBM_WS, unlock_ship_upgrade: off (incompatible with TAOM).
- Artem: only ArtemCore + ArtemsCinematicCombat (combat animations: CONFLICT RISK with us) + ArtemsBetterUIVisuals.
- Intro video renamed (*.codex-disabled-20261002-153238) to avoid a hang on logos.
- Codex.RBMSaveRepair: temporary module that drops crafting orders whose item vanished with RBM, so save007 loads. Pending:
  confirm map, save a new game, then remove the module.
- CustomBattle threw repeated NullReferenceException (TaleWorlds.MountAndBlade.CustomBattle.dll) with the 39-module profile: unresolved.

## Decision: lab profile
Develop and test in a separate BLSE lab profile (Native, SandBoxCore, SandBox, CustomBattle, StoryMode + Bannerlord.Harmony + our mod),
via its own shortcut, without touching LauncherData.xml or the TAOM shortcuts. Custom Battle = repeatable oracle.
Integrate into the TAOM profile only once the vertical slice works.

## Half Sword source (2026-10-02)
- Only an official Steam install is used as a source.
- Legit options: the user's own Steam copy, the free official demo, or no Half Sword files at all (reimplement the feel from public footage).

## Bannerlord reverse engineering, first pass (decomp in %USERPROFILE%\bannerlord-decomp, TaleWorlds DLLs target netstandard2.0)
- Melee damage: Mission.MeleeHitCallback (native callback) -> MissionCombatMechanicsHelper.ComputeBlowMagnitudeMelee.
  Swing speed is NOT physical: SpeedGraphFunction(collisionData.AttackProgress, strikeType, direction) = curve over animation progress.
  It then feeds a physical formula (CombatStatCalculator.CalculateStrikeMagnitudeForSwing: weight, inertia, CoM, impact point).
- Replaceable game model: StrikeMagnitudeCalculationModel (CalculateStrikeMagnitudeForSwing/Thrust...) -> momentum damage without Harmony.
- AttackCollisionData exposes CollisionGlobalPosition/Normal, CollisionDistanceOnWeapon, WeaponRotUp, LastBoneSegmentSwingDir,
  AttackProgress, VictimHitBodyPart, CollisionBoneIndex -> edge alignment / glancing blows are computable.
- Bones: Skeleton.SetBoneLocalFrame + ForceUpdateBoneFrames, Agent.GetBoneEntitialFrame, MBSkeletonExtensions.GetBoneEntitialFrame(forceToUpdate).
- Ragdoll: Agent.StartRagdollAsCorpse / EndRagdollAsCorpse, Agent.ApplyForceOnRagdoll(bone, force), SetVelocityLimitsOnRagdoll.
- Our own hits: Agent.RegisterBlow(Blow, in AttackCollisionData) is public.
- Open questions (need in-game oracles): does a live agent survive Start/EndRagdollAsCorpse? do bone overrides persist past the
  animation tick and do they move native weapon collision?

## Lab runs (BLSE lab profile, tools/run-lab.ps1, logs in logs/)
- Run 1 (22:38): AutoTest reached main menu (20 s) and CustomBattleState, then StartGame NRE:
  Sergeant mode requires playerSideGeneralCharacter (GeneralsAndCaptainsAssignmentLogic calls .GetName()). Fixed.
  I killed that instance thinking it was stuck while the user was looking at it -> rule: never close a game window
  without checking idle input / asking (user later said lab instances are mine to manage, they don't play them).
- Run 2 (22:42): battle auto-started (15 agents). Skeleton: rightUpperArm=22 forearm=23 mainHand=26 mainHandItem=27 thorax=11.
  StartRagdollAsCorpse on a LIVE enemy: ragdoll state -> Active, agent stays State=Active, hp=100, AI on, position frozen.
  Same tick ApplyForceOnRagdoll -> System.AccessViolationException (native), then main thread hung 9 s later (freeze).
  Hypothesis: the ragdoll body does not exist until a later tick (NeedsActivation/ActiveFirstTick). EndRagdollAsCorpse untested.
- Game code never calls Start/EndRagdollAsCorpse or ApplyForceOnRagdoll from C#: no usage reference, test one variable per run.
- Run 3 (22:45, HSC_PROBE=ragdoll,bone): StartRagdollAsCorpse on a live enemy -> NeedsActivation -> Active; position frozen.
  EndRagdollAsCorpse at t=6: ragdoll state STAYS Active; the agent's logical position starts moving again at t~11
  (formation advance) while still flagged ragdoll. Engine main thread hung at t~21.5 (log and rgl log stop) -> second freeze.
  Run 2 froze with no bone writes, so the common factor is a live agent in StartRagdollAsCorpse.
  VERDICT: Start/EndRagdollAsCorpse on living agents is unusable (hang 10-20 s later). Only for corpses.
- Run 3 bone test: Skeleton.SetBoneLocalFrame(forearm, rest*80deg) + ForceUpdateBoneFrames from OnMissionTick: hand moved 0.000 m;
  from OnPreDisplayMissionTick: 0.007 m. VERDICT: no effect on agent skeletons (animation output wins / not the render skeleton).
- Lab notes: commander_1 (player) is a mounted hero -> pick an infantry player next time. Blue terrain patches = shaders still
  compiling for the lab module set ("Missing shader from sack"), cosmetic.
- Safe replacements found in the decomp:
  * Knockdown/knockback is native and model-driven: AgentApplyDamageModel.CanWeaponKnockDown / DecideAgentKnockedBackByBlow,
    AgentStatCalculateModel.GetKnockDownResistance / GetKnockBackResistance, BlowFlags.KnockBack/KnockDown.
  * Hand IK exists: Agent.SetHandInverseKinematicsFrameForMissionObjectUsage(localIK, boundEntityGlobalFrame) + ClearHandInverseKinematics
    (used by Ballista, BatteringRam) -> next candidate for free weapon control.
- Run 4 (22:54, HSC_PROBE=puppet): PUPPET ROUTE WORKS.
  Player = commander_2 "Elthild" (on foot; AutoTest now orders heroes with an empty Horse slot first).
  Skeleton "human_skeleton": 22=r_upperarm_twist (Monster.RightUpperArm), 23=r_upperarm_twist1 (child), 26=r_hand, 11=spine2, 0=pelvis.
  Skeleton.Freeze(true) on the main agent's own skeleton -> animation stops; then SetBoneLocalFrame(forearm) +
  UpdateEntitialFramesFromLocalFrames(): first write moved the hand 0.354 m, then 585 rewrites moved 0 and drift 0
  (the pose persists between ticks). Same from OnPreDisplayMissionTick. Screenshot: arm held out horizontally, body frozen
  mid-stride; after Freeze(false) normal animation resumes. Clean QuitGame, no hang.
  Run 3's failure was ForceUpdateBoneFrames (re-evaluates the animation) + no Freeze.
  Note: while frozen the agent's position also stopped changing (locomotion coupling to check next).
- RUNTIME: our DLL runs under ".NET Framework 4.8.9345.0" (BLSE Standalone), NOT the bundled .NET 6 folders.
  -> keep netstandard2.0/net472; physics engine must run on .NET Framework (BepuPhysics 2 is out).
- CapsuleData wraps FtlCapsuleData _globalData/_localData: dump the nested fields next run.

## Unit sorting (user's rules, 2026-10-02) -> Rules/Eligibility.cs
Half Sword mode for any human fighting on foot with a melee weapon, shield or bare hands: archers who draw a sword,
dismounted cavalry and the player on foot included. Riders, and anyone holding a bow/crossbow/throwing weapon, stay vanilla.

## Half Sword source, second check (2026-10-02 23:05)
- User wants Half Sword's REAL physics data and movements ported (not a re-creation). Plan: UE4SS (verified download,
  experimental build 2026-09-29, sha256 af8ea9d8...7252, supports UE <= 5.8) -> DumpJMAP (reflection + CDO values),
  Lua dump of PhysicsAsset bodies/constraints/drives and PhysicalAnimation profiles, function hooks for the control
  logic, pose recordings as oracle traces. Personal use, outputs outside any repo, nothing redistributed.
- BLOCKED: "Half Sword Demo" folder does not match Steam's install (Steam appmanifest SizeOnDisk = 2,665,369,416
  bytes, the folder held extra files). UE4SS NOT installed; nothing read. Needs a clean Steam install of the demo first.

## 2026-10-02 23:15 Half Sword demo clean + UE4SS; lab run 6
- Run 6 (watchdog, physics on main agent): NO hang. 16-body BEPU ragdoll simulated 8 s, released, clean quit.
  Runs 2/3/5 "freezes" all ended with "Missing shader from sack / compile_shader" lines in rgl_log and the lab profile
  had an empty shader cache (blue terrain). Possibly long synchronous shader compiles cut by the 20 s watcher, not
  deadlocks. Unproven: watchdog (insideOurCode + main-thread managed stack) and CPU sampling decide next time.
  The "live ragdoll hangs the engine" verdict is therefore DOWNGRADED to "unproven"; EndRagdollAsCorpse still did not
  clear the ragdoll state (run 3), so native ragdoll stays unusable for recovery anyway.
- Half Sword demo folder cleaned with the user's OK: 53 non-Steam files deleted. Folder now 2,665,369,416 bytes == Steam
  SizeOnDisk exactly. Demo build 18478397: shipping exe 136,401,408 bytes, pakchunk0-Windows.pak 2,324,275,536 bytes.
- UE4SS experimental (sha256 af8ea9d8...7252) installed: Win64/dwmapi.dll + Win64/ue4ss/. Console/GUI off.
  Mod HSDump (copy in tools/halfsword/HSDump/main.lua): DumpJMAP, PhysicsAsset bodies/constraints/profiles,
  SkeletalMesh + PhysicalAnimation components, player pawn/controller properties, hooks on physics UFunctions.
  Output: %USERPROFILE%\halfsword-decomp\dumps\ (never in a repo). Uninstall = delete dwmapi.dll + ue4ss\.

## 2026-10-02 23:40 Half Sword real data extracted (details in %USERPROFILE%\halfsword-decomp\NOTES.md, private)
- tools/halfsword/kismet_mem.py: own disassembler for in-memory UE 5.4 Kismet bytecode from a UE4SS .jmap
  (FScriptName = 12 bytes; pointers resolved via the .jmap addresses; names resolved in game, SAME session only:
  asset FName indices change between runs). 9 Blueprints decoded, 0 decode errors.
- Got: 22-body physics asset (shapes, real masses via GetBoneMass, 80.84 kg), 42 constraints with Motor/Broken/DED
  profiles, muscle logic (driver skeleton -> constraint orientation targets, region tonus x strengths, smoothed global
  multiplier), balance handles, mouse arm-aim placement, 90 s fight recording (oracle metrics: body-driver gaps, hand speed).
- Gotcha: UE4SS RegisterHook on Blueprint functions crashed the game (EXCEPTION_ACCESS_VIOLATION in UE4SS, nested BP
  calls, ~2 s into a fight, likely Get Damage args). Native UFunction hooks were fine (61k calls). Use the 30 Hz
  game-thread recorder instead of BP hooks.
- Policy: all Half Sword data/logic stays in halfsword-decomp (outside any repo). The Bannerlord mod built from it is
  personal use; a shareable build would have to read the values from each player's own Half Sword install.

## 2026-10-02 23:42 Step 1 VALIDATED: Bannerlord animation = Half Sword "driver skeleton", no Freeze
- DriverProbe (lab runs 7-8, HSC_PROBE=driver): every frame, read the animated pose, then SetBoneLocalFrame +
  UpdateEntitialFramesFromLocalFrames (NO Skeleton.Freeze), from OnMissionTick or OnPreDisplayMissionTick.
  Main agent: write moves the hand 0.21-0.40 m and next frame's animated pose is back to the animation
  (gap == moved, e.g. 0.401/0.401, 0.349/0.350): the engine re-evaluates the animation each frame, so the driver
  pose stays clean while the displayed pose is ours. Visual run (HSC_DRIVER_VISUAL=1, upper arms to rest frame):
  arms held out while writing, hanging normally once we stop. No hang.
- AI agents: walking ally shows drift ~2x gap -> animation not re-evaluated every frame (LOD/throttling). On those
  frames the skeleton still holds our last write: detect (animated pose == last written) and reuse the last true
  driver pose.
- Architecture for step 2: driver = Bannerlord animation (read each frame before writing), physics body (BEPU, Half
  Sword values) follows the driver like Muscle Motor, displayed pose = physics bodies written back. Freeze not needed.

## 2026-10-03 09:20 Step 2, lab run 9: Half Sword active ragdoll on the main agent WORKS (numbers)
- 09:14 attempt exited at frame 0, before Mono init: Steam was not running (started 09:14:46). Gotcha: without Steam,
  BLSE Standalone quits right after the "show_imgui" shader step, no error, safely_exited=1.
- Run 9 (HSC_PROBE=active, logs/lab-20261003-091909.log): profile 20 bodies / 19 joints / 2 handles; built 20 bodies,
  80.8 kg, pelvis 0.875 m above a flat ground box.
  - Standing (3-7 s): pelvis 0.866 m (1 cm sag). Body-driver gaps: pelvis 2.3, spine2 2.8, head 4.3, r_hand 7.5,
    l_hand 10.7, r_foot 5.3, l_foot 9.0 cm. Half Sword fight oracle: 0 / 4 / 6 / 22 / 13 / 6 / 6 cm. Same magnitudes.
  - Push 60 N.s on spine2 (7 s): gone before the next 0.25 s sample (handle damping 200/s = 5 ms time constant).
  - Push 227 N.s (11 s): 0.08 s later spine2 10.6, head 14.6 cm; hands up to 55 cm, feet up to 34 cm over 3 s; back
    near the driver in ~0.5 s; no fall.
  - Hand/foot gap spikes at 8.3 / 9.1 / 9.6 s (1-2.5 s after push 1): source unknown (driver animation change or
    physics). Next probe: log driver bone speed next to the gap.
  - DED (15 s, Strength 0, handles off): pelvis 0.85 -> 0.14 m in 0.5 s (near free fall), 0.105 m and head 0.21 m by
    16 s, stays limp.
  - Driver updates 3342 in 17 s (~196 Hz), reuses 0 (main agent re-animated every frame). No hang, no exception.
- Open: flat ground box at the pelvis ground height (slopes: feet float or sink, maybe the l_foot 9 cm); the agent does
  not follow the body, so strong pushes get pulled back to the agent instead of staggering; visual check = run 10.

## 2026-10-03 09:55 Runs 10-16: head bones, engine pose hook, lab freezes = Discord overlay x RenoDX DLSS deadlock
- Run 10 (filmed, bones written from OnMissionTick): standing, pushes (strong push = visible head snap) and the DED
  collapse show on the body, but head + coif stayed at standing height while the body lay down (stretched neck).
  Measured (run 11): no engine pass moves the head bone we write (deviation 0 at the next OnPreDisplayMissionTick and at
  SubModule.OnApplicationTick). The head/face/helmet meshes (MetaMesh.UseHeadBoneFaceGenScaling) render from the
  animation result, not from the skeleton frames we write.
- Frame order (decompiled 1.4.8): Mission.Tick (native) -> OnPreDisplayMissionTick -> UpdateCamera (the main agent's
  animation is evaluated here: our pose is intact at OnPreDisplay, replaced by OnMissionTick) -> OnMissionTick ->
  TickAgentsAndTeamsAsync -> ... -> MBSubModuleBase.OnApplicationTick (runs after the mission tick).
- FIX: engine post-integrate hook. Skeleton.EnableScriptDrivenPostIntegrateCallback() + a ScriptComponentBehavior
  (HscPoseHook) added to the agent's visual entity with CreateAndAddScriptComponent; SkeletonPostIntegrateCallback
  (AnimResult) runs right after the animation is evaluated, on the main thread, once per frame (3263 calls/3263 writes
  in run 12): GetEntitialOutTransform(bone) = Half Sword driver pose; SetOutQuat + SetOutBoneDisplacement = our pose
  (local frames). Run 12 film: whole body including head and helmet lies on the ground in DED.
- OPEN: every bone is off by the same vector (6 cm standing, 91 cm lying): rotations apply but the root (pelvis)
  displacement does not; the pose stays anchored at the animated pelvis. Next: how the root translation is taken.
- FREEZES (runs 11, 12, 13 and control runs 14-15 with NO physics and no bone writes): 18-23 s into the battle.
  Native stacks read from outside the process (dbghelp StackWalk64, no injection; Frida could not attach): loader-lock
  deadlock between two third-party modules injected in the game. Render thread: TaleWorlds -> ReShade dxgi.dll ->
  renodx-dlss.addon64 (holds its SRW lock) -> CreateToolhelp32Snapshot -> waits for the loader lock. Thread 35680:
  holds the loader lock in DiscordHook64.dll thread-attach -> calls a function hooked by renodx-dlss.addon64 -> waits
  for that SRW lock. Not our mod; a race (Discord running since 09:10, runs 9-10 were lucky). Can hit normal play too.
- Watchdog: main-thread stack sampling (Thread.Suspend) is now opt-in (HSC_WATCHDOG_STACK=1); stalls log their end.
- Gotchas: `um win kill` raises UnicodeDecodeError on French Windows (taskkill output in OEM code page) after killing.
  Restore safely_exited=1 in Documents\Mount and Blade II Bannerlord\Configs\engine_config.txt after every kill.

## 2026-10-03 10:06 Runs 17-19: root translation, visual frame, Discord closed = no freeze
- Root (run 17, readback inside the post-integrate callback): after SetOutBoneDisplacement(pelvis, wanted), the anim
  result still returns the ANIMATED pelvis position, every time; rotations (SetOutQuat) apply exactly (r_hand error ==
  root error). The engine ignores root translation through AnimResult. Film (run 19): in DED the body lies flat but
  floats at hip height (pose anchored on the animated pelvis); standing, the 2-6 cm anchoring is invisible.
- MBAgentVisuals.SetFrame: from OnMissionTick it reads back +50 cm right after the call but the agents tick resets it
  before OnApplicationTick (no visible lift, run 18); from OnApplicationTick it reads back 0 immediately (run 19). The
  visual frame follows the agent's frame natively: no visual root offset this way.
- Freezes: DiscordHook64.dll is injected MID-BATTLE (not loaded at battle start, present in the frozen stacks of run 18
  even with the overlay toggled off); the cycle now went through the Steam overlay's LoadLibraryExW hook too. With
  Discord closed (user's choice: close Discord for tests, tools/discord.ps1 -Stop/-Start), run 19 ran to PROBE_DONE and
  quit cleanly.
- Next for the root: Half Sword's own answer is that the capsule follows the body. Move the agent toward the physical
  pelvis (horizontal) and drive falls with Bannerlord's knockdown/get-up animations (vertical); death hands over to the
  native corpse ragdoll. Check first whether SetOutBoneDisplacement works on child bones.

## 2026-10-03 15:27 Runs 20-21: bone positions never come from us; FallDriver puts the fallen body on the ground
- Run 20 (readback in the callback): SetOutBoneDisplacement is ignored for the root's children too (spine sits at its
  rest offset from the animated pelvis, error == root offset). Through AnimResult only ROTATIONS reach the engine.
- Consequence = Half Sword's own design: the driver (agent + animation) must follow the body. Physics/FallDriver.cs:
  when the body is down, play act_death_fall_front/back (chosen from the physical head-vs-pelvis lean along the
  agent's look direction), drive SetCurrentActionProgress from the physical pelvis height (0.97 = lying), and
  TeleportToPosition the agent by the horizontal pelvis gap (deadzone 2 cm).
- Run 21 (filmed, Discord closed, clean quit): root offset during the collapse 4-11 cm (was 80-91 cm); film: whole body
  incl. head and helmet lies on the ground. Pose stays 100% physics (the animation only supplies the pelvis position).
- NEW ISSUE: wielded items (shield, axe) float where the ORIGINAL animation puts the hands (not the physics hands, not
  the post-callback result, not the direct skeleton write: run 10 already showed it). Weapon entities are reachable
  with Agent.GetWeaponEntityFromEquipmentSlot; to test: re-frame them on the physics hands (ties into step 3, where
  the weapon becomes a physics body).

## 2026-10-03 15:36 Runs 22-23: wielded items in the physics hands (Physics/HeldItems.cs)
- Run 22 (diagnostic): each wielded item is an entity under the agent's visual entity with an IDENTITY local frame
  (global frame = agent position); its meshes hang on the item bone (r_finger0 main hand, l_finger0 off hand) of the
  ORIGINAL animation. An entity frame set from OnMissionTick persists to the next tick (not reset by the engine).
- Fix: entity global frame = physicsItemBone x animatedItemBone^-1 x visualFrame, every tick after the pose write
  (mesh = entity x animated bone = physics bone). Items re-resolved each tick (weapon switches); Release() resets the
  entities to identity local frames.
- Run 23 (filmed, clean quit): axe and shield in the hands standing and after the strong push; on the ground with the
  lying body in DED; back to normal after release. Same mechanism will render the step-3 physics weapon anywhere.

## 2026-10-03 15:45 Step 3 research: Half Sword arm control decoded (details in the private NOTES.md)
- kismet_mem.py: KISMET_ALL_OFFSETS=1 labels every statement (ubergraph entry points are not jump targets).
- Mouse -> 2D aim point (degrees, radius <= 90) chased at a stamina-limited speed; its velocity gives swing speed and
  direction. The arm pose is NOT computed by a formula (the Set Arm Aim * functions are dead code): ABP_Willie blends
  174 blend spaces of animation poses (per weapon class, 8 swing directions, guards) from the aim point, swing direction
  and swing speed. The physical arm follows that driver pose with its muscles; the weapon is a physics body in the hand.
- Porting choice to make: record the real driver pose table from the player's own Half Sword (UE4SS, sample the aim
  inputs, read the DriverSkeleton) vs rebuild the arm pose with our own IK (not the real movement).
- **Correction 2026-10-06:** "the Set Arm Aim * functions are dead code" is wrong for `Set Arm Aim Rotation`: on
  2026-10-05 22:50 patching one of its constants in memory moved the measured rest guard 0.22 -> -0.05 m (MODLOG
  23:00 entry), so it runs.

## 2026-10-03 16:28 Step 3a: Half Sword's REAL arm poses on the Bannerlord agent (atlas + retarget), filmed
- HSPose (tools/halfsword/HSPose/main.lua, UE4SS on the user's demo): runs MINIMIZED with no input. `t.IdleWhenNotForeground 0`
  keeps the game ticking in the background (the demo otherwise idles; minimizing also stops capturing the user's mouse,
  which happened once when the demo took focus). Starts a fight with the game mode's own `GM Duel`, invulnerable player,
  enemies destroyed, weapons via `Set Up Right/Left Hand Weapon(class)`. Per sample: guard flags, Aim Vector(_0), Swing
  Direction and Swing Velocity proxy set (the game's tick keeps a set Swing Direction while the aim does not move),
  0.45 s settle, DriverSkeleton bones written (parent-space rotations + component positions). onehand_shield (arming
  sword + Tagre shield): 1921 poses (aim grid 15 deg in a radius-90 disc x {guard, 8 directions x 2 swing speeds}),
  15 min. Reference pose = T-pose with identity bone frames (GetRefPoseTransform). Data stays in halfsword-decomp.
- make_arm_atlas.py: Unreal left-handed component space -> right-handed character frame (x -> -x; quaternion
  (x,y,z,w) -> (x,-y,-z,w)); per pose: shoulder/elbow/wrist, hand/clavicle/upperarm/forearm and pelvis/spine_02..05 rotations.
- Bannerlord rest pose = A-pose (dump: human_skeleton, model frame right +X, forward +Y, up +Z, item bones r_finger0/l_finger0).
  Retarget = per-bone anatomical axis maps (Physics/ArmRetarget.cs), not rest-pose deltas (T vs A pose would add ~33 deg).
- Physics/ArmAtlas.cs (bilinear guard grid, swing = 2 nearest directions x recorded speeds) + ActiveRagdoll.DriverOverride
  (driver = engine animation with the upper body replaced; held items still compensated against the ORIGINAL animation).
- Lab/AimProbe.cs (HSC_PROBE=aim): scripted aim (guard, circle, diagonal swings, vertical swing). Film: wind-up with the
  axe over the head and torso twist, horizontal blade mid-swing, follow-through, shield guard moving with the left arm.
  Driver-body hand gaps up to 1.1 m in fast swings (Half Sword fight p95: 0.87 m).
- Freeze in one filmed run: render thread spinning inside the NVIDIA D3D11 driver under ReShade's dxgi (D3D11On12 bridge)
  while Windows Graphics Capture recorded; the same run unfilmed and a second filmed run were fine.
- User design for step 3b (2026-10-03): disable Bannerlord's native attack for the player; arms move only while a mouse
  button is held (Half Sword: button held = mouse drives the aim, camera nearly frozen; released = aim back to center).

## 2026-10-03 21:15 Step 3b: player controls, rendering fixes, walking left native, Half Sword's real controls decoded
- User's visual report (tests): body not really standing, head staying put while the body spreads, shield through the
  body, weapon floating in the hand, native combat still there. User decisions: walking stays Bannerlord's (the overhaul
  is the combat physics); every combat control as in Half Sword except grab / swap hands / drop (Bannerlord's); weapon
  measures universal (read per item at runtime); disable the old combat system so the two never overlap.
- Head/helmet tearing (GEOM/HOOKDIAG lab logs): with direct bone writes AND the pose hook, the body mesh (skeleton frames)
  and the head/face/helmet (animation result, anchored on the animated pelvis) come from two sources; when the physics
  body trails the agent (user walking: 50-70 cm) they tear apart. Fix: hook only (DirectWrite off for the player).
- Held items: placed on the bone as DRAWN (hook result read back in the callback, `DrawnEntitial`), as a LOCAL frame under
  the agent's visual entity (drawn x animated^-1): no one-frame lag in swings, follows the agents tick moving the entity.
- Walking: the player's agent was moved by the user's own keys (MovementInputVector (0,1), ai=False). Pelvis + legs are
  now kinematic, velocity-driven to the animation every step then snapped (`AnimatedLowerBody`): gap 0.0 cm even at
  3.4 m/s; physics from the pelvis up. A per-foot ground-pad attempt made the body drift 1.6 m/s (penetration recovery
  treadmill, not chased); back to one ground box under the pelvis on the terrain height/normal, frozen during falls.
- Still open: physics head 15-30 cm behind its target standing (Half Sword median 6 cm): muscle tuning.
- Native combat off under Half Sword control (Harmony): attack/defend movement flags and the Kick event flag cleared after
  ControlTick; CrosshairVM.SetArrowProperties zeroed (attack-direction arrows); InputContext.IsGameKeyDown(5) (Show
  Indicators, Left Alt) reads up during a manual thrust only.
- Half Sword input bindings (demo's InputSettings, read at runtime by tools/halfsword/HSInput): Guard Right = LMB,
  Guard Left = RMB (+ axes for triggers), Thrust = Left Alt, Grab Right/Left = E/Q, Swap Hands = X, Run = LShift,
  Crouch = LCtrl, Change Camera = V. Weapon classes listed at runtime (pak index is AES-encrypted: not bypassed).
- Decoded (Willie_BP): press = Down + Guarding, press counter reset 0.5 s after the last press, 2nd press = Thrust
  (R_Thrusting; Extend +0.1 s unless the weapon is stuck; End +0.25 s); release = Down/Thrusting/Extend off, Guarding off
  0.2 s later. Per tick: Thrust Alpha -> 1 when thrusting with a still aim (0 above 15 deg/frame of aim velocity) at
  30/12.5; Thrust Position -> Extend ? alpha : 0.2 at 20..10 by stamina (x2 grips 0/5); Left Alt: position -> Alt Thrust
  = clamp(position - mouse Y axis, 0, 1) at 10. Two-handed: the left hand is a PhysicsConstraint on the weapon's
  "Grip L Hand Off Hand" point (Attach L Hand To Offhand Position), R Two Handed Grip; RMB tap with a 2H weapon toggles
  R Grip Alt Toggle (alternative grip).
- HSPose now records sets class x mode (R, L, RL, N, T = thrust drawn/extended, A = manual thrust 0/0.5/1), skips sets
  with a .done file, HSPOSE_QUICK=1 = coarse check (grid 45). Classes: onehand_shield (ArmingSword_T1 + Shield_Tagre),
  twohand (LongSword_T2, left hand attached: hands 18 cm apart in guard), polearm (Polearm_Mid_Tier), spear (Spear_A).
  Quick checks: every mode moves the right bones (L guard moves the left hand 33-50 cm, thrust extends 84-94 cm, A graded).
- GOTCHA: UE4SS also loads mods that have an enabled.txt even when mods.txt says 0: the old HSPose ran during the input
  reads and overwrote atlas_onehand_shield.csv (restored from atlas_onehand_shield.backup.csv). Disable = rename
  enabled.txt too.
- Bannerlord side: Combat/HsHand.cs (per-arm button logic above), Combat/HsPoseSet.cs (pose sets per class, class from
  the wielded weapon), HsPlayerBehavior (attack key = Guard Right, defend key = Guard Left, Left Alt = manual thrust,
  guard-state fades 0.15 s, thrust pose on top by thrust alpha). make_arm_atlas.py splits _T and _A sets.

## 2026-10-04 16:50 Step 3c: live tuning, Half Sword grip measured, agent tooling (GABS, BannerlordSage, hsc.* commands)
- Live tuning (user's request: small adjustments without restarting): `Configs\HalfSwordCombat_tuning.txt`, `key = n` or
  `key = x y z`, optional class prefix (`twohand.main_item_rot`), re-read on change twice a second (LiveTuning.cs).
  Frames from `<key>_rot` (degrees about x, y, z) + `<key>_pos`. Lab camera `lab_cam = yaw dist height` orbits the body
  facing (SceneView.SetCamera); fake input held by `lab_pose` / `lab_aim`; `lab_dump = n` writes the drawn pose.
- Half Sword grip measured in the demo (tools/halfsword/HSGrip, physics mesh ticked while minimized): the weapon's base
  mesh frame is the hand's weapon socket; blade, edge and grip point per weapon and the Tagre shield frame in hand space
  (values in the private port/hs_grip.txt). HeldItems applies it when `hs_grip >= 0.5` (HsGrip.cs: item +Z blade,
  +X edge; shield centred on its grip).
- Held items hang on the ORIGINAL animation's item bone: with `item_anchor = 1` the weapon followed the native hand when
  the fist was raised. `item_anchor = 0` is the right setting, but the check run was spoiled by the pause menu, so it is
  still unverified.
- Physics tracking error (hands 55-177 deg off target): muscle, motor, limit scales and solver iterations change nothing;
  still open.
- Agent tooling (user's request: a more autonomous environment), outside this repo in `%USERPROFILE%\tools`:
  - BannerlordSage MCP: Bannerlord 1.4.8 decompiled (90 official assemblies), XML and API docs indexed, this repo's
    `src` as mod source.
  - GABS 1.1.2 MCP + the Bannerlord.GABS bridge mod (ported to 1.4.8):
    - profiles: `play` (normal game), `lab` (HSC_LAB, player probe, fake input, kinematic body, no autoquit),
      `lab-physics`;
    - the launcher records the exact game PID; stop and kill go through that PID only, never a process name;
    - one log per launch (`logs/lab-gabs-*.log`).
  - `gabs_call.py`: a one-shot CLI client.
- Console commands (HscCommands.cs), for the developer console or GABS `core/run_command`:
  - `hsc.status`: control, class, buttons, thrust, aim, guard fades, held items;
  - `hsc.get [filter]`;
  - `hsc.set key v [v v]`: writes the tuning file and reloads it at once, keeping the line's comment;
  - `hsc.unset key`;
  - `hsc.dump`.
  - Registered from OnBeforeInitialModuleScreenSetAsRoot with the engine's own two steps (collect, then
    Utilities.AddCommandLineFunction), so other modules' commands are not affected.

## 2026-10-04 17:20 GABS end-to-end check, a lab crash hidden as a freeze, recon (MODDING_PLAN.md)
- GABS end-to-end (lab profile) works:
  - launch, bridge connect (100 tools), `core/get_game_state`, `hsc.set/get/unset`, engine screenshot;
  - hand-over to a second one-shot client;
  - stop through the exact-PID hook, with `safely_exited` restored.
- Fixed: `hsc.status` / `hsc.dump` said "no mission". Mission.AfterStart calls OnBehaviorInitialize on the
  behaviours it already has before it asks the modules for theirs, so ours never got it. Now set in OnCreated.
- Added `lab_calm = 1`: Mission.AllowAiTicking off and DisableDying on; the deployment phase uses the same two
  switches. Reason: in the first long session the AI killed the idle lab player.
- A second lab launch "froze" at 16:56:55.7, two seconds into the battle.
  - Read-only diagnosis (tools\diag\stackdump.ps1, window list):
    - it was a crash: BLSE was opening its "Bannerlord Crash Report" window (GLFW, ButterLib's glfw3.dll);
    - the window deadlocked with ReShade's `renodx-dlss.addon64`. The render thread was in the add-on's Present
      path calling CreateToolhelp32Snapshot and waiting for the loader lock. The crash-window thread held the
      loader lock while loading nvoglv64.dll through the Steam overlay hook, and waited for the add-on's lock.
  - Neither HalfSwordCombat nor the GABS bridge is on the deadlocked stacks.
  - The exception itself is unknown: no report reached disk.
  - Suspects: GABS connecting during the battle (same second), or `lab_calm` (switched on 2 s before).
  - Killed by exact PID through the GABS kill hook.
  - Next: first-chance exception logging in the lab, then reproduce.
- With RenoDX DLSS loaded, any crash will look like a freeze; Discord's overlay hook triggered the same deadlock
  before. Four DLSS add-ons are loaded at once; that decision is the user's.

## 2026-10-04 17:35 Real cause of the "freezes" (GABS bridge), held items fixed with the base game's hold
- **Lab exception log** (`Lab/LabExceptions.cs`, lab only): first-chance and unhandled exceptions are written with the
  throwing thread's full stack before any handler runs. It found the crash at once.
- **Crash cause: the GABS bridge's network library.**
  - Lib.GAB (0.1.0, and still 1.0.5) handles messages in `async void GabpServer.OnMessageReceived`.
  - When a client disconnects mid-request, its catch block calls `SendErrorResponseAsync`, which throws
    "Connection is not active" out of the async void method. The exception is rethrown on the thread pool, so it
    is unhandled and terminates the game.
  - BLSE then opens its crash report window, which deadlocks with RenoDX: the "freeze".
  - Every one-shot `gabs_call.py` call disconnects like this.
  - Fix in our Bannerlord.GABS build: `Patches/GabpServerSafetyPatch.cs`, a Harmony prefix that runs the handler as a
    Task observing every exception.
  - Verified: two connect/disconnect cycles, no unhandled exception.
- **Vanilla shutdown bug (not ours):** `ImageIdentifierTextureProvider.Finalize()` throws a NullReferenceException on
  the finalizer thread while the game closes. BLSE intercepts it and the game still exits.
- **Held items, lab A/B with GABS screenshots** (raised fist, aim 0 -60, two camera angles). New live key
  `item_order`.
  - The engine hangs items on the ORIGINAL animation's bone and composes item = local x bone in model space.
  - So `item_anchor = 0` (now the code default) and `item_order = 0` are right.
  - The other three combinations put the weapon behind the head, at the feet (local = H rotates about the model
    origin) or out of sight.
- **Hold: the base game's (`hs_grip = 0`, `off_item_rot = 0`)** beats Half Sword's measured hold.
  - The axe sits in the raised fist.
  - The kite shield is vertical in the left guard and in front of the face when raised.
  - Before, the shield was flat like a tray and the axe was behind the head.
  - Before/after image: `logs/avant-apres-20261004.jpg`. Tuning file updated.
- **Still open:**
  - right guard aimed right (x = 50): the shield turns horizontal across the waist;
  - check numerically that the weapon points where Half Sword points it (pose dumps against the atlas);
  - physics tracking error.

## 2026-10-04 23:31 Passthrough chosen; phase 0 "one value crosses" works with the real Half Sword
- **Why the change:**
  - after two days in circles, the user found a Half Sword -> Dying Light 2 project (videos in `uni mod`) that runs
    the real Half Sword next to the other game and copies its skeleton and weapon;
  - the user chose this passthrough method;
  - design: `docs/PASSTHROUGH.md`, following SkyCraft's DESIGN.md ("neither game is rewritten"), guide 02 of
    ai-game-modding-guides and the universal-modder `mashup-mods` skill (pattern 2);
  - status file: `STATUS.md` (handoff template).
- **Tools used:**
  - REA 3.2.1, installed and registered as MCP `rea`. Its managed analysis works; `doctor` fails on Windows with
    `spawn EINVAL`.
  - REA measured that `TaleWorlds.MountAndBlade.dll` has 0 P/Invoke: its 216 native methods are delegates, so item
    attachment lives in `TaleWorlds.Native.dll`, which REA cannot open on Windows (it rejects DLLs).
- **Held-item telemetry (HELD line):**
  - our copy is read back exactly on the intended frame (0.000 cm / 0.000 deg);
  - the engine's own item stays hidden;
  - so what is left was the hold itself, which the passthrough now takes from Half Sword.
- **Phase 0:**
  - Half Sword side: UE4SS Lua `HSLink` (read-only, `LoopAsync` 16 ms) writes actor location/yaw, pelvis and hand_r
    to `%USERPROFILE%\halfsword-decomp\hslink_state.txt`. No clock is sent, because Lua's `os.clock` is CPU time.
  - Bannerlord side: `Link/HsLink.cs` reads the line, times samples when their seq changes, has a stale watchdog and
    logs a LINK line.
  - Fake sender first (`tools/halfsword/fake_hslink.py`): a known circle (30 +-10 forward, 20 +-10 right) came back
    exactly, with yaw 90 handled, at 58 samples/s, and STALE showed once the sender stopped.
  - Real Half Sword (`tools/halfsword/run-hs-link.ps1`, minimized; HSPose disabled, enabled.txt renamed): about 60
    samples/s. The idle hand is at 30 cm right of the actor, at hip height.
- **Freeze at 23:27, my mistake:** I forgot to close Discord before the lab run. DiscordHook64.dll loaded into
  Bannerlord and deadlocked with renodx-dlss on the loader lock (stackdump). Fix: always run `discord.ps1 -Stop`
  before Bannerlord labs.
- **Next:**
  - the user moves Half Sword's arm and the LINK values must follow;
  - then a marker in Bannerlord at Half Sword's hand (mashup-mods: "a cube from A drawn in B");
  - then the arms (phase 1).

## 2026-10-05 00:40 Test 0: a machine-judged visible test; the held-item copy is not drawn [measured]
- **Why:** engine readbacks said the copy sat 0 cm from its target while it was never rendered, and the agent's eye
  judgements of screenshots were wrong several times.
- **One pair of screenshots cannot conclude:** between two engine screenshots of the SAME state, about 3,800 of the
  ~8,000 16-pixel cells change (grass in the wind, other agents, banners). The 2026-10-04 one-pair comparisons were all
  inside that noise.
- **Tool:** `%USERPROFILE%\tools\gabs\visual_ab.py` (next to `hsc_shots.py`).
  - It switches the tested live-tuning keys on and off N times (A0 B0 ... An).
  - "change" = pixels that differ at every switch and stay still in every shot of at least one state.
  - "blind" = pixels that move by themselves in both states.
  - Output: a report.json and red maps per test. `verdict()` (the decision rule) is left to the user.
- **Lab 2026-10-05** (lab profile, `lab_cam 20 2.8 1.6`, right guard), change cells at 3 / 6 cycles:
  - `zz_dummy 1` (nothing reads it): 5 (grass) / 0;
  - `lab_aim 0 -60` (arm up): 128 / 115, the map is the player's silhouette;
  - `item_ghost 0` + `main_item_pos 0 0 0.3` (original axe moved 30 cm): 40 / 6, the map is the axe shaft;
  - `copy_offset 0 0 1`: 11 (a tree at the right edge, far from the player) / 0;
  - `copy_hide 1`: 0 / 0.
- **Verdict:**
  - the `item_ghost` copy is not drawn anywhere on screen;
  - moving the ORIGINAL item entity's local frame is drawn.
- **Limit:** the compensated original axe loses cells with more cycles (40 -> 6).
  - Probably a small frame-to-frame wobble (it is computed from the previous frame's animation). Unproven.
  - Use 3 cycles to detect a weapon, 6 to confirm that nothing changed.
- **Next:** a free-standing weapon entity (`GameEntityExtensions.Instantiate`, as for dropped weapons) as the
  phase-0 marker, measured with this tool.

## 2026-10-06 22:15 Modding Kit: launch, blocking warnings, and a click-free way into the editor [measured / read]
- Test module `Modules\HSClipTest` created (SubModule.xml, `AssetSources\hs_clips\hs_strike_2h_test.fbx`). The
  wEditor launcher started directly shows "There is no target file!"; through Steam (`steam://rungameid/1393600`) it
  opens. Its engine-drawn window (class `F`) has no usable UIA tree and drops background input; the user set the Mods
  list by hand: `Command Args: /singleplayer _MODULES_*Native*SandBoxCore*Sandbox*StoryMode*CustomBattle*HSClipTest*
  _MODULES_` (rgl_log_33280).
- **Blocking "RGL WARNING" dialogs at start-up** (stack: ModuleManager x4 -> Engine -> DotNet.AutoGenerated, no text):
  the editor reads the SubModule.xml of EVERY installed module (enabled or not) and stopped after
  `Bannerlord.FamilyTree` (22:07:59), `Bannerlord.GABS` (22:10:57) and `Bannerlord.MBOptionScreen` (22:11:22). These
  three, and `TAOM.Dependencies`, are the only 4 of 50 modules whose SubModule.xml names a
  `Bannerlord.ModuleLoader.<name>.dll`; modules with plain DLLs passed silently. Correlation 3/3, cause not proven. A
  background pixel click on the dialog's OK closed it (a UIA Invoke did not). The game process ended at ~22:11:22
  (the user reported "jeu crash"); no crash report was found.
- User (22:11): "j'aimerais utiliser mon PC, fais tout en arrière-plan ou tout sur le 2ème écran" -> no more
  foreground input. Screens: DISPLAY1 primary 1920x1080 at (0,0), DISPLAY2 1680x1050 at (-1680,26).
- **Click-free editor entry [read]:** the menu's "Editor" option and Ctrl+E both call the public static
  `MBInitialScreenBase.OnEditModeEnterPress()` (decompiled 1.4.8 ViewSubModule 170-175, MBInitialScreenBase 79-83,
  221-224), shown only when `TaleWorlds.Engine.Utilities.EditModeEnabled`. Helper built (not installed):
  `src\HSClipTest` (SubModule: after 2 s with MBInitialScreenBase on top, calls it once; logs to
  `%TEMP%\hsclip_autoeditor.log`), against the wEditor assemblies, 0 errors.
- `LauncherData.xml` restored from the 22:02:37 backup (hash DC5A1BEE... = backup).

## 2026-10-06 20:45 Trial B: the captured swing is a verified one-take FBX on TaleWorlds' human_skeleton [measured]
- Re-reading the capture: the "strike" is several horizontal back-and-forth swings (aim X -34..+25, speed 20-40 over
  2.76 s), not one overhead strike. **A 1.5 s game freeze** happened in it (wall 20:20:03.31 -> 20:20:04.85 while t
  moved 0.16 s); cause not investigated (the 375 KB/s REC logging is a suspect).
- `tools\gabs\rec_to_local.py` (test: synthetic two-bone chain, 45 deg at the half key, passed): last swing
  20:20:02.95-20:20:05.30 -> 49 keys at 30 FPS on 28 bones (`logs\trialB_swing.csv` + `_skeleton.csv`).
- **The Modding Kit ships the skeleton:** `<game>\modding_resources\skeletons\human_skeleton.fbx` (FBX 7400; armature
  `bip01_notused`, bones `bip01_<name>_<index>` = the game's 28 bones in SKEL order + 3 `_notused` nubs).
- MCP `blender`'s `execute_blender_code_for_cli` failed: "Blender executable not found at 'blender'. Set the
  BLENDER_PATH environment variable" (not set for the server). Same thing done directly:
  `Blender 5.1\blender.exe --background --factory-startup --python tools\gabs\bl_bake_clip.py`.
- **`tools\gabs\bl_bake_clip.py`:** fits game rest origins to the FBX rest heads (Kabsch: scale 0.999, no mirror,
  max residual 0.98 cm over 28 bones), derives each bone's axis convention from the rest frames, bakes the local
  rotation changes + pelvis travel on the official armature, exports one take, re-imports and checks.
  - Before export the armature reproduces the game's drawn bones within 0.04-0.13 cm.
  - **Found by the oracle:** Blender's FBX exporter took the CURRENT pose as the bind pose (re-imported pelvis rest
    moved 7.8 cm, every bone off by the same 7.8 cm) and frame 1 came back as frame 2. Fix: a rest key at frame -1,
    current at export; the strike at frames 0..48. After the fix: rest pelvis (0, 0.915, 0) exact, take
    `hs_strike_2h_test` frames 1-49, every checked bone within 0.13 cm.
  - Output: `logs\hs_strike_2h_test.fbx` (not committed: TaleWorlds skeleton inside).
- TaleWorlds' page lists the export rules but not the editor's import clicks; the KB note confirms there is no
  command-line import (editor console: only `export_meta_mesh_as_obj`, `pack_folder`, `compile_external_module_cache`).
  Next: the Modding Kit import (Resource Browser), which needs the user (GUI, a module folder in the game).

## 2026-10-06 20:22 Trial A verdict: "toujours pareil, c'est pourri"; one strike captured for trial B [measured]
- **User (20:21), after playing the passthrough during the capture:** "test passthrough toujours pareil c pourri passe
  au prochain test". Trial A is judged failed by the user; no more passthrough tuning.
- Capture `lab_rec 150` from 20:19:47 (mission t 37.6): LMB holds (`R=1` runs) 3; only one real swing:
  **20:19:57.9-20:20:04.9 (5.67 s of t), peak swing speed 40.2** (above Half Sword's 35 threshold), aim Y -51..13,
  X -58..33, no right click. The other two holds peak at 0.0 and 3.9.
- Next (plan B, Task B3): convert that swing to local rotations at 30 FPS to prove the clip chain end to end; a cleaner
  source motion (e.g. Willie's DriverSkeleton instead of the drawn passthrough pose) can replace it once the chain works.

## 2026-10-06 20:20 Relaunch after a Bannerlord crash; lab_rec deployed and verified [measured]
- **The 11:42 lab crashed at 12:37:43** (found at 20:16): `UNHANDLED terminating=True thread=2` NullReferenceException in
  `TaleWorlds.MountAndBlade.GauntletUI...ImageIdentifierTextureProvider.ReleaseCache()` called from its finalizer
  (Bannerlord's own UI code; our mod is not in the stack). Half Sword's stream had stopped 2 s earlier (12:37:41);
  whether the user closed it is unknown. Side fact for trial D: hslink.log's `body:` line shows
  `R Arm Extra Tonus=1.000` (the value hs_body assumes).
- User (20:15): "lance les tests et dis-moi ce que je dois faire" (go for trial B's capture with the user striking).
- Deployed `lab_rec` (DLL SHA256 5B1C20B1...). Half Sword PID 5332 (relay 39492, 20:17:17 player found, LongSword_T2),
  Discord hook still OFF (`DiscordHook64.dll.hsc-off`), Bannerlord lab PID 49796 (log
  `logs\lab-gabs-20261006-201723.log`), `hsc.loadout 8` (20:19:08, left hand attached).
- **B1 oracle:** `hsc.set lab_rec 2` -> 1 `SKEL: human_skeleton bones=28` header + 28 bone lines, 279 `REC` lines over
  1.999 s of mission time (139 lines/s), 30 blocks per line (28 bones + ITEM_R + ITEM_L), 2.7 KB per line.

## 2026-10-06 13:55 Trial B started: plan, lab_rec capture built (not deployed)
- Plan: `docs/superpowers/plans/2026-10-06-trial-b-native-clip.md` (part 1 = capture one two-handed overhead strike on
  `human_skeleton` through the running passthrough, then convert it to local rotations at 30 FPS; part 2 = Blender
  FBX, Modding Kit import, copy-mode clip, action set, lab oracle, written after part 1). The KB note
  `editor-free-bannerlord-assets.md` confirms its `.tpac` writers are not published, so the route is one-take FBX +
  Modding Kit (GUI: ask the user first).
- Existing lab tools only snapshot one pose (`lab_dump`, max 2 Hz through the tuning file reload). Built a lab-only
  `lab_rec = S` (HsPlayerBehavior: one `SKEL` dump, then a `REC` line per frame with every drawn bone's model-space
  frame for S seconds; `DumpPose` now shares `WritePose`). `dotnet build -c Release`: 0 errors. **Not deployed**:
  needs a Bannerlord restart, asked to the user.

## 2026-10-06 13:40 Trial D vertical slice: the Rust arm beats "body = driver" on Half Sword's own fight [measured]
- **D2 sources:** UE API reference (UE 5.8 page) `FAngularDriveConstraint::bAccelerationMode`: "Whether to use
  acceleration mode for angular drive. The other option is force mode. (default:true)"
  (https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Engine/FAngularDriveConstraint); units of
  `FConstraintDrive` stiffness: kg cm / s^2 in force mode, cm / s^2 in acceleration mode
  (https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/ConstraintDrive). The physical-animation
  drive's mode is not documented: A/B below. Half Sword runs UE 5.4.4, so the 5.8 default is likely, not proven.
- **Runtime body values read from the dumps** (crate reads them at run time): `pass1\willie_capsules.txt` upperarm_r
  capsule r 4.48 len 18.67 cm, lowerarm_r r 3.95 len 21.41, hand_r box 15 x 7.5 x 5; elbow at -27.77 cm, wrist at
  -27.25 cm along the parent bone; `pass2\masses.txt` 1.7597 / 1.5503 / 0.8220 kg.
- **`hs_body::arm`** (rapier3d 0.36.0, `PhysicsWorld`): kinematic root = `T_body(spine_05) x T_driver(spine_05)^-1 x
  T_driver(upperarm_r)` (clavicle not recorded), three dynamic bodies, spherical joints, Muscle Motor drive (parent
  space, k = Muscle Power x 100 x Arm R Tonus x stamina/fall factors, damping 0) + physical-animation drive (world
  space, k = Arm R Tonus x Upper Body Tonus x 1000, d = x 100), per-frame strengths from the recorded scalars, 240 Hz
  substeps, angular damping 10. Not modelled yet: joint limits, the weapon (unknown in this recording), Strength
  Multiplier (not recorded, taken as 1), max force, R Arm Extra Tonus (not recorded, 1).
- `cargo test`: 7 passed (reader 4, arm 3: dump parser, shortest rotation error, drives pull the hand toward a still
  driver and beat no drives); clippy clean; rustfmt clean.
- **`hs_bench arm`, 2351 simulated frames of the 2026-10-02 fight (no Fallen/DED frame in it), median / p95 cm:**

| drives | hand_r e_sim | lowerarm_r e_sim | upperarm_r e_sim |
|---|---|---|---|
| baseline "body = driver" (e_drv) | 21.9 / 86.6 | 14.3 / 49.8 | 8.0 / 23.0 |
| **muscle accel + physical animation accel** | **19.0 / 70.1** | **10.2 / 35.1** | 2.5 / 7.0 |
| muscle accel + physical animation force | 36.5 / 82.4 | 15.8 / 41.9 | 2.5 / 7.0 |
| muscle accel, no physical animation | 42.8 / 84.8 | 20.5 / 44.9 | 2.5 / 7.0 |
| no muscle, physical animation accel | 21.0 / 59.3 | 12.6 / 34.9 | 2.5 / 7.0 |

- **Reading:** with both drives in acceleration mode (the UE default) the Rust arm follows Half Sword's recorded
  physical arm closer than the driver does (hand median -13 %, p95 -19 %): the parity contract's pass condition holds.
  Force mode for the physical animation is clearly worse, so acceleration mode is what the recording supports. The
  upper arm's 2.5 cm is the kinematic shoulder (the same in every row). Far from exact parity: next candidates, one at
  a time against this table: the shoulder/elbow limits of the Motor profile, then the weapon's mass.

## 2026-10-06 13:05 Trial D started: hs_body crate, recording reader, baseline oracle [measured]
- **User (12:45):** "c'est bien mais ... je vois aucun test de rewrite rust ou d'autre méthode". Taken as the camera
  verdict (gate 1a: trial A's camera item closed, `look_floor` not needed for now) and as the go for trials D and B.
- Plan: `docs/superpowers/plans/2026-10-06-trial-d-rust-arm.md` (port-to-rust skill: parity contract, end state B =
  Rust core + binding later). Facts read for it: shoulder `UserConstraint_10` Motor k 500 d 1 (limits 45/75/45), elbow
  `UserConstraint_11` Motor k 250 d 1 (75/45/10), bodies angular damping 10, physical animation world space
  orientation 1000 / angular velocity 100 (`physics.txt` 61-192); `Muscle Motor` position strength = Muscle Power x 100
  x Arm R Tonus x R Arm Extra Tonus x stamina/fall factors, velocity 0, max Max Angular Force (`Willie_BP.txt`
  ~18260-18367).
- Crate `rust/hs_body` (no Half Sword data inside; `motion.csv` read at runtime): reader, oracle, `hs_bench baseline`.
  `cargo test` 4 passed, clippy clean (clippy and rustfmt rustup components installed today).
- **`motion.csv` has 257 corrupted rows of 2610** (file lines 188-444, t ~6.4-15 s): HSDump wrote
  `(TrivialObject: ...)` instead of the scalar values, which adds 32 fields. The reader skips rows whose field count
  differs from the header and reports the count.
- **Baseline ("body = driver") on the 2353 good frames, median / p95 cm:** pelvis 0.0/0.3, spine_05 4.1/14.1, Head
  6.1/21.3, upperarm_r 8.0/23.0, lowerarm_r 14.3/49.8, **hand_r 21.8/86.6**, hand_l 12.7/53.7, feet 5.9-6.1/11.5-12.3.
  Matches NOTES.md (22/87, 13/54, 4/14, 6/21) within 0.4 cm: the reader is right. Next: D2 (drive units in UE 5.4),
  then the rapier3d arm (D3) must beat hand_r 21.8 cm median.

## 2026-10-06 12:42 Trial A camera playtest read: cam_guard and cam_down work as coded [measured]
- Plan written first: `docs/superpowers/plans/2026-10-06-trial-a-passthrough.md` (Task 1 = this reading; Task 2-3 =
  `look_floor`, only if the user still sees the torso bend).
- `tools\gabs\cam_play.py` now also reports **clean windows** (same `guard` flag on the previous line, since the flag is
  read at the end of each 0.5 s window). Checked on `tools\gabs\testdata\cam_synth.log`: the transition line is left
  out (all windows 0.600, clean 0.500).
- **User's play 12:34:15-12:34:47** (43 CAM windows with movement since 12:25:00, loadout 8):
  - clean guard=1 (36 lines): bearing **0.500** (raw 4154 deg), elevation 0.486 (raw 434 deg), downward below the
    horizon **0.250** (raw 25.6 deg);
  - clean guard=0 (7 lines): bearing **1.000** (raw 192 deg), elevation 1.000 (raw 117 deg); no downward move below
    the horizon without guard, so `cam_down` alone was not exercised;
  - all windows: guard=1 bearing 0.505, guard=0 0.885 (the transition windows mix both rules).
- **Sign of the camera elevation [measured]** (GEOM `cam` z vs CAM `elev`, 438 samples since 12:34): elevation > 0 =
  camera LOW looking up (35..90 deg: camera 0.37 m high; 0..10 deg: 2.49 m; -10..0 deg: 2.88 m); the agent's look pitch
  equals it. So `cam_down` ("below the horizon") acts when the camera is high and looks down.
- While guarding the elevation stayed -3.1..52.9 deg (412 of 438 samples at 0..10 deg): the look pitch never went below
  -10 deg, where the 11:45 bins showed the head going forward. The torso-bend mechanism was not triggered in this play.
- **Verdict: pending** (the user wrote "continue").

## 2026-10-06 12:27 Fresh session: checks; trial A camera playtest handed to the user
- Resumed from the STATUS 12:20 handoff. MCP servers bannerlordsage, gabs, rea and ghidra-mcp all answer; ghidra-mcp
  has no Ghidra instance (GUI not open). GABS sees the lab as `shared-running` (owned by the previous session's GABS).
- Running [measured 12:24]: Bannerlord lab PID 3848, Half Sword PID 41480, mouse relay PID 33348; loadout 8
  (Bannerlord `LOADOUT 8` 11:42:58, hslink.log `equip two-handed grip, left hand attached true` 11:42:59).
- **CAM line, read in the code** (`HsControl.cs` 155-216, `HsPlayerBehavior.cs` 481-489): written every 0.5 s of
  mission time; `rawB/keptB` and `rawE/keptE` = sums of |turned| and |kept| degrees since the previous line;
  `rawDown/keptDown` = downward moves that started below the horizon (the ones `cam_down` scales); `guard` =
  `HsControl.Guarding` when the line is written. Expected with the defaults: not guarding 1.0 (downward below the
  horizon 0.5); guarding 0.5 (downward below the horizon 0.25). `scale=` shows Half Sword's own rule, unused while
  `cam_guard` >= 0.
- Mission clock: `t=698.9` at 12:25:00 although the lab started at 11:42:37; t runs at wall speed now (0.505 s per
  0.506 s), so the mission clock was stopped or slowed for ~30 min at some point. Not investigated (no effect on the
  CAM ratios, which compare kept with raw inside each line).
- 12:26: camera playtest handed to the user (protocol in STATUS NOW). Reading pending.

## 2026-10-06 12:20 Tools installed, universal-modder updated, research, decision: one trial of each method
- **User (11:55):** the passthrough feels limited ("aucune évolution depuis hier ... tu n'arrives pas à récupérer et
  ajuster les physiques de Half Sword"); asked to analyse and install Cpp2IL, ILSpy, ida-mcp, ghidra-mcp, the guides,
  REA, jars and rewrite-rs skills, then to update universal-modder. Done and verified (memory `re-toolchain`):
  ILSpy 11.1, Cpp2IL pre-release.21, ghidra-mcp 7.0.0 (built for Ghidra 12.1.4), REA 4.0.1 (doctor passes Ghidra on
  Windows), Rust stable + jars, rewrite-rs-skills, guides clone; ida-mcp not installed (needs IDA 9.4+ with idalib).
  ilspycmd stays 9.1 (11.x needs a newer .NET SDK). MCP gabs / bannerlordsage / rea / ghidra-mcp at user scope.
- **universal-modder** pulled to main 6715445, plugin reinstalled (stale skills), tests 66/66, KB 60 notes. Two new
  upstream notes matter: `knowledge/techniques/editor-free-bannerlord-assets.md` (Bannerlord 1.4.8.119303 skeletons,
  animations, clips, action sets written offline; code not published) and `knowledge/games/half-sword/super-hammer-ue4ss.md`
  (full game EA 0.6.1.5: `Willie_BP_C:Get Damage` signature, Dumper-7 SDK in lambor590/Half-Sword-Enhancer).
- **Research (web, 2026-10-06):** official animation import rules (moddocs.bannerlord.com animations page: FBX, 30/60
  FPS, <= 64 bones, Z up, `_notused` suffix, clips in the Resource Browser -> action_sets.xml); TpacTool exports a
  rigged model with its skeleton (animation export unfinished); Rapier joint motors take a target rotation,
  stiffness and damping; massclown/HalfSwordModdingResources lists physics properties under `WorldSettings`.
- **Decision (user, ~12:10):** "fais un essai de chaque méthode donc natif, passthrough, hybride et même rewrite rust
  de half sword". Roadmap: `docs/ROADMAP-4-TRIALS.md`. Handoff to a fresh session (memory `long-session-end-drift`).

## 2026-10-06 11:45 User playtest of the camera fix; camera rules from the user; right click still unexplained [measured]
- **User (11:33), two-handed sword:** "il est mieux mais il manque quelques ajustements": (1) "j'ai tendance à mettre la
  caméra plus haute et donc les coups partent trop vers le bas"; (2) "quand la souris est cliquée pour faire le
  mouvement, la caméra est trop fixe, pas assez dynamique"; (3) "le clic droit ... ne fonctionne tout simplement pas".
- **Telemetry of that play (lab log 112007, `cam_play.py`):** while guarding the camera kept 6.5 % of the horizontal
  mouse (819 of 12,591 deg) and 12.7 % of the vertical: the fix works, and Half Sword's 1-11 % is what felt too fixed.
  The agent's look pitch equals the camera elevation on every line (-14.7..45.1 deg while guarding). Binned by look
  pitch (`look_vs_pose.py`): below -10 deg the native animation's head goes 0.15-0.21 m forward and the drawn head
  0.24 m (vs 0.01-0.17 m level); looking up ~0. So looking down bends the drawn head/torso forward.
- **User's rules (11:40):** (2) "La moitié (50 %)" of the mouse while guarding; (1) "rendre la sensibilité haut/bas de
  la caméra, quand la caméra est en bas, moins d'impact vers le bas, et garder un coup à hauteur normale". Built:
  live `cam_guard` (default 0.5; -1 = Half Sword's rule) and `cam_down` (default 0.5: a downward move that starts
  below the horizon keeps that share, guarding or not), CAM line `rawDown/keptDown`. Deployed 11:42 (DLL hash
  FB236774...), lab relaunched (log `lab-gabs-20261006-114237.log`), loadout 8. **Not verified yet** (needs the
  user's play: expected kept/raw 0.5 horizontal while guarding, 0.25 for downward moves below the horizon while
  guarding, 0.5 for those when not guarding).
- **Right click, frequency survey (`tools\gabs\hs_instance_survey.py`, 5 fresh instances x 6 trials):** 0/30
  (`logs\evidence\20261006-instance-survey.txt`). Today in all: 7 instances, 0/102.
- **Data lead tested:** the two working instances (23:06, 23:34) had 4 and 6 weapon equips before their first right
  click, the failing ones 2. Re-equipping (loadout 1 then 8) and 6 more trials: 0/6
  (`20261006-grip-trials-after-reequip.txt`). **4 hypotheses refuted (aim, driver, fps, re-equip). Stopped.**

## 2026-10-06 11:20 Right click (half-sword grip): it works or fails per Half Sword INSTANCE [measured]; camera patch found ineffective [read]
- **Camera:** `CameraPatch` (Harmony postfix on `MissionScreen.HandleUserInput`) never scaled anything: the method zeroes
  `_cameraBearingDelta`/`_cameraElevationDelta`, adds the frame's mouse and applies them to `CameraBearing`/`CameraElevation`
  itself (decompiled 1.4.8 lines 2979-3040), before our postfix runs. Rewritten (prefix keeps the angles, postfix keeps
  `CameraScale` x what the call turned while guarding; `cam_fix = 0` = old behaviour) + lab `CAM` line + lab-only
  `lab_mouse = x y` (adds fake mouse to InputContext.GetMouseMoveX/Y). Built and deployed 11:06; oracle below.
- **Right click, new tool `tools\gabs\hs_grip_trials.py`** (Half Sword alone, scripted input, LongSword_T2 two-handed,
  time from the left-guard press to `R Alt Grip` on and to `L Hand In Offhand Attached` while alt = caught on the blade):
  - instance 11:07 (driver=0.6 at load): 18 trials still / moving / left-only: **0 caught**, alt on in 0.03-0.07 s,
    searching 98-99 % of 3 s, normal grip back on release every time (`logs\evidence\20261006-grip-trials.txt`);
  - same instance, 24 trials from 6 aim positions (neutral, ±90 up/down/left/right, (64,-64)): **0 caught**
    (`20261006-grip-trials-aim.txt`) -> **the aim position is not the cause**;
  - instance 11:17 started WITHOUT driver: 6 trials, **0 caught** (`20261006-grip-trials-nodriver.txt`) -> **driver is
    not the cause**.
  - **hslink.log, every left-guard press per instance** (`tools\gabs\grip_presses.py`): 22:36 instance 0/7, 23:06 1/1,
    **23:34 instance 7/7** (the user's tests 11-13, caught within the same second), 00:01 0/1, 00:03 0/2, 11:07 0/42,
    11:17 0/6. **All or nothing per instance.** fps does not separate them (23:34 ~118, 00:01 ~115); FrameRateLimit=120
    is persisted in GameUserSettings.ini. HSLink's main.lua changed at 00:01-00:03 (lift, inert without `lift`), but no
    older copy exists (never committed) to compare. **Circuit breaker:** 3 hypotheses refuted; cause unknown; asked the user.
- **Camera oracle attempt (11:22, lab, Bannerlord in the background):** `lab_mouse 10 0` / `0 2`, phases not guarding,
  guarding with `cam_fix 0`, guarding with `cam_fix 1`: the CAM line's raw turn stayed **0** in every phase and the
  bearing stayed at -138.552, so HandleUserInput applied no mouse at all while the window was unfocused (in the
  decompiled code, with the cursor visible it only edge-scrolls; that this is the background case is likely, not
  checked). The patches are applied and the postfix runs. Verification needs the window focused. Settings restored
  and checked (`lab_mouse`, `cam_fix` removed; `link_input 1`, `lab_pose 1`).

## 2026-10-06 00:10 Playtest series 23:00-00:07 (two-handed sword): what held, what failed, and a process lesson
The user stopped the night with: "encore une fois tu passes dans ta phase où tu inventes et on avance jamais". Most
tests below were guessed tweaks, often two at a time; verdicts also varied between runs with the same settings.
Lesson in memory `no-speculative-tuning-loops`. Facts only:

| test | change | user verdict |
|---|---|---|
| 1-4 | Half Sword swing dose 75 / 50 / 37.5 / 25 (`hs_bp_patch.py swing`) | 75 (Half Sword's) "le moins pire", none fixes it; back to 75 |
| 5 | measure only: our blade vs Willie's (WEAPON line) | "mieux" (nothing had changed) |
| 6 | pure driver pose (`pose=driver`) | "beaucoup trop raide, saccades"; reverted |
| 7 | HSLink loop 8 ms (~75 poses/s) | "il se baisse" (torso bends: our head 0.33-0.56 m ahead, down to 1.34 m) |
| 8 | `spine_weight 0.3` | "pareil mais moins bas, de pas beaucoup" |
| - | **bug:** the raw-mouse relay counted moves made in other windows | fixed (Bannerlord-foreground only) |
| 9 | fatigue off (calm keeps Stamina 100, Exhaustion 0) | "partent dans tous les sens" |
| 10 | Half Sword capped at 120 fps (`maxfps`) | not played; user: "adaptation de Half Sword pour Bannerlord ... propre, moins flasque" |
| 11 | driver 0.6 + `link_smooth 0.04` + 120 fps | **"c'est propre"**, but the right click is buggy |
| 12 | `marker_hsdir 1` (blade always on Willie's) | right click fixed, left click lost its cleanness |
| 13 | `marker_hsdir 2` (only while half-swording, faded) | **"c'est mieux, les 2 clics fonctionnent"**; left click: torso bends, strike goes down |
| 14 | `spine_weight 0` + `lift 25` | "plus rien qui fonctionne"; reverted |
| 15 | lift fixed (held 0.15 s on both aim vectors), 25 | "trop haut, vraiment trop"; right click "full bug" |
| 16 | lift 12, left click alone | "trop haut quand il faut pas, trop bas quand il faut pas"; right click "full bug"; lift off |

- **Measured:** the user's strikes are sideways slashes at Half Sword's neutral aim (a waist-high guard); at full
  swing speed the arm extends and the wrist dips ~35 cm mid-swing (strike_watch, `logs\evidence\2026100*-strike-watch*`).
  The camera orbits and drops during strikes (up 2.74 -> 1.67 m) although CameraPatch should slow it.
- **Right click (two-handed):** Half Sword's half-sword grip needs Willie's left hand to catch the blade physically.
  It caught it in the 23:46-23:59 sessions, not in the two after the 00:01 and 00:03 restarts (searching for 2-3 s).
- **Left as at test 13** (the last state the user approved): `driver=0.6`, `maxfps=120` (hslink_tune.txt),
  `link_smooth 0.04`, `spine_weight 0.3`, `marker_hsdir 2`, fatigue off, raw mouse, HSLink 8 ms; no lift.
- New tools: `tools\gabs\strike_watch.py` (per-strike mouse path, Willie's aim, wrist, blade), HSLink lines `AIM`,
  `WEAPON`, `GRIP`, `body:` log, live `hslink_tune.txt` (`muscle`, `blmouse`, `pose`, `driver`, `lift`, `maxfps`),
  `tools\halfsword\hs_bp_patch.py` (`side`, `swing`), Bannerlord keys `spine_weight`, `link_smooth`, `marker_hsdir`
  (+ `_fade`, `_smooth`), `hands_keep`, `thrust_dclick`.

## 2026-10-05 23:00 Side-floor patch tried and reverted; the swing lift also comes from ABP swing poses [measured]
- The user chose "arme basse sur les côtés" (the weapon's height should follow only the vertical mouse).
- **`tools\halfsword\hs_bp_patch.py PID --side N`:** changes the one constant `-30` of `Set Arm Aim Rotation`
  (`MapRangeClamped(|X|, 0, 90, -88, -30)`) in the running demo's memory; the 19-byte pattern is unique in the whole
  game's bytecode (.jmap 2026-10-02). Written and read back at 0x181b364b218; `--side -30` restores. Nothing on disk.
- **A/B, same scripted horizontal swings** (`logs\evidence\20261005-2250-side-floor-ab.txt`):
  rest guard 0.22 -> **-0.05 m**, slow side 0.36 -> 0.19, crossing peaks 0.47-0.53 -> 0.36-0.48, rise from the centre
  to the side **+0.14 -> +0.24 m**. Everything goes lower, the rest guard included, and the side rise stays: not what
  the user asked. **Reverted to -30** (read back).
- **Other cause, ABP_Willie:** `Aim Swing Speed` (from mouse speed) blends swing poses over the aim:
  `ApplyAdditive_31.Alpha = MapRangeClamped(Aim Swing Speed, 0, 70, 0, 1)`, `TwoWayBlend_108` (0..45),
  `TwoWayBlend_150` (50..70). The faster the move, the more the swing poses lift the weapon, whatever the vertical
  mouse. The body also blocks a low hand at the side (the reason for Half Sword's side floor, likely).
- The Bannerlord lab mission was paused (no tick since 22:37:54, process responding, no crash window): Bannerlord did
  not write input during these probes.

## 2026-10-05 22:40 "Flasque", mouse gain, and why big swings lift the weapon [measured]
- **User, 22:20 (two-handed sword):** better, but "manque une précision ... les mouvements sont trop mollassons comme
  si le personnage était tout flasque pas ferme et sans force". **22:31:** "le personnage fait de grand coup qui part
  dans tous les sens ... plus propre tout en gardant le reste fonctionnel ... sur toutes les armes, quand j'envoie de
  grands coups, l'arme se lève sans que la souris monte".
- **Willie is at full strength [measured]:** HSLink `body:` line (every 5 s): Muscle Power 35 (same as the other
  Willies), All Body Tonus 100, Arm/Upper Body Tonus 1, Exhaustion 0, PhysicalAnimation StrengthMultiplyer 1, demo at
  330-380 fps.
- **Mouse gain [measured]:** `tools\gabs\mouse_gain.ps1` (Raw Input sink, read-only, no focus) while the user played
  150 s: Bannerlord's GetMouseMove passes on x 0.77, **y 0.56** of the device counts; slow moves 0.47, mid 0.67, fast
  0.73. Played directly, Unreal uses the device counts. Evidence `logs\evidence\20261005-mouse-gain.txt`.
  - **Fix:** `tools\halfsword\hs_mouse_relay.ps1` (Raw Input sink, hidden, exits with its Half Sword; started by
    `run-hs-link.ps1`) writes `hslink_mouse.txt` = `seq sumRawX sumRawY END`. HSLink uses it while it is fresh and the
    Bannerlord input line is live (`lab: mouse source raw`); `blmouse=1` in `hslink_tune.txt` (live) or `blmouse` in
    `hslink_lab.txt` goes back to Bannerlord's mouse. Not playtested yet.
- **Big horizontal swings lift the weapon [measured + decoded]:** probe, horizontal mouse only: centre guard wrist
  0.20-0.28 m, the same aim reached slowly at the side 0.36-0.46 m, fast crossing swings peak 0.47-0.68 m. Evidence
  `20261005-2240-swing-rise-probe.txt`, `20261005-2236-muscle-ab.txt`.
  - Muscle Power 35 / 70 / 105 (new live `hslink_tune.txt` `muscle=N`) barely changes it (peaks 0.53 / 0.57 / 0.52):
    not a weak arm.
  - Cause, Willie_BP `Set Arm Aim Rotation`: the vertical aim is clamped to
    `[MapRangeClamped(|X|, 0..90, -88..-30), 75]`: the lower limit rises from -88 deg at the centre to -30 deg at the
    side, so a horizontal swing forces the hand up. `Set Arm Aim Location`: the arm's reach grows with `Aim Swing
    Speed` (35-45 cm up to 75 cm), so fast swings reach far. Half Sword's own design, the same when played directly.
  - Making it "plus propre" for Bannerlord means departing from Half Sword on purpose: the user's call (options asked).
- Also seen: the aim saturates around ±600 Bannerlord counts sideways.

## 2026-10-05 22:25 Game-thread loop survives the stress; two-handed: the half-sword grip is what breaks; double-click thrust built
- **Stress oracle passed [measured]:** HSLink with `LoopInGameThreadWithDelay(5)`, Half Sword alone, scripted input and
  342 equip calls (a switch every 3 s): alive 22:03:52-22:12:28 (8.6 min, 61617 steps), no crash folder. The old
  `LoopAsync` loop at 5 ms crashed 4/4 within ~2 min. Evidence: `logs\evidence\20261005-2205-stress-gamethread-loop-5ms.txt`.
  Back to 16 ms (`hslink_lab.txt` = `duel calm input`). Two of the test's equip paths were wrong
  (`ModularWeaponBP_Axe`, `Shields/Shield_Tagre` don't resolve); HsWeaponMap's own paths are right.
- **Two-handed oracle in Bannerlord [measured]:** lab, `hsc.loadout 8` (LongSword_T2), `link_input 0`, Willie driven by
  `hs_input_probe.py` while Bannerlord reads the pose. Evidence: `logs\evidence\20261005-2218-hands-axis-oracle.txt`.
  - The drawn weapon copy's local box: Z from -0.22 to +0.98 m, X ±0.09 (the guard): **+Z is the blade**, origin at
    the right-hand grip (MARKER `box`).
  - **Normal grip:** without the IK the Bannerlord wrists are already within ~1 cm of Half Sword's distance
    (before 0.11-0.16 vs hs 0.10-0.16 m, direction 1-6 deg off), and the left wrist is 7-11 cm from the blade axis,
    like the right one (7.9 cm): **the left hand is on the weapon.** The IK (`hands_keep`) only trims ~1 cm.
  - **Left guard held (Bannerlord RMB) = Half Sword's half-sword grip:** Willie's wrists 0.53 m apart, the left hand
    on his blade, and the Bannerlord left wrist **35 cm from the Bannerlord blade**: the copy keeps Bannerlord's fixed
    grip in the right hand while Willie's weapon turns in his hand. The user held RMB 35-46% of the two-handed tests,
    so this is the likely "buggé".
  - **Next:** stream Willie's right weapon relative to `hand_r` (HSLink line `WEAPON`), take its normal-grip value as
    the reference once the weapon has settled, and turn the copy in the Bannerlord hand by the change from that
    reference (Hand x delta x Hand^-1 x item). The normal grip the user called perfect stays as it is; every re-grip
    of Half Sword (half-sword, mordhau, impacts) carries over.
- **Double-click thrust (built, deployed, not tested):** `HsInputLink`: a second press of the attack key within
  `thrust_dclick` s (live tuning, default 0.3) holds Half Sword's Thrust (Left Alt, `Alt Thrusting`) until the key is
  released. Willie_BP thrusts with `Alt Thrusting AND R_Guarding`, so it is Half Sword's own thrust.
- **MCP:** GABS / BannerlordSage / REA are local-scope servers under the project key `C:/Users/.../uni mod`; this
  session (VS Code, cwd `c:\...`) loaded only user-scope servers. `gabs_call.py` was used instead (same GABP bridge).

## 2026-10-05 22:15 Half Sword crashes are in UE4SS: the HSLink loop moved to the game thread; two-handed left hand built
- **Crash at 21:58:13** (the user saw "fatal error"), 434 s after the demo started and 12 s after the Bannerlord lab
  launch. Same signature as 18:47: `EXCEPTION_ACCESS_VIOLATION reading 0x0`, a call stack of ntdll only. The user
  closed Bannerlord afterwards (and the 19:06 Bannerlord exit, logged by Windows as an APPCRASH in
  TaleWorlds.Native.dll at shutdown, was also the user closing it).
- **Minidumps read [measured]:** `minidump` (Python), stack scan of the faulting thread (scratchpad `dumpstack.py`,
  `topstack.py`). All 8 Half Sword crashes since 2026-10-02 fault inside UE4SS.dll (+0xc640d8, +0xc634cd, +0xc71c38 x2,
  +0xc71c60 x2, +0x2aca43) or in ucrtbase called from it (18:47), on the engine's main thread. UE4SS.dll has not changed
  since 2026-09-29, so the addresses really differ: memory corruption, not one faulty spot.
- **Likely cause (hypothesis):** HSLink ran `LoopAsync(16)` + `ExecuteInGameThread`: Lua on UE4SS's async thread (the
  loop callback) and on the game thread (Step) in one Lua state, which is not thread-safe. That fits the rate effect:
  at 5 ms the demo crashed 4/4 within ~2 min, at 16 ms after 7-26 min.
- **Change:** HSLink now runs `LoopInGameThreadWithDelay(16, Step)` (UE4SS delayed actions, run from its engine tick
  hook: all Lua on the game thread). `hslink_lab.txt` can hold `period=N` for stress tests only. Logged at load:
  `loop: LoopInGameThreadWithDelay 16 ms, handle 1`.
- **Oracle running:** the same 5 ms that crashed 4/4, with the new loop, scripted input and an equip switch every 3 s,
  8 minutes, Half Sword alone. Evidence: `logs\evidence\20261005-2205-stress-gamethread-loop-5ms.txt`.
- **Two-handed, Bannerlord side (built, deployed, NOT verified in game yet):**
  - HSLink's `GRIP` line is read by `HsLink.TryReadPose` (`HsLink.TwoHanded`, `LeftAttached`);
  - `ArmRetarget.KeepHands`: while Willie is two-handed and the pose comes from the link, the left wrist goes to the
    Bannerlord right wrist plus Willie's right-to-left wrist vector. Two-bone IK on l_upperarm_twist / l_foretwist,
    elbow kept in its current bend plane, the hand and l_finger0 keep Half Sword's rotation and only move;
  - live tuning `hands_keep` (default 1; 0 = off, for A/B);
  - lab line `HANDS t=... twoHanded=.. attached=.. keep hs=<Half Sword wrist distance> before=<Bannerlord, rotations
    only> after=<Bannerlord with the IK>`. The oracle: `after` must equal `hs` (about 0.15 m on the longsword).
  - The 21:59 lab run never reached it: Half Sword had crashed at 21:58, so `twoHanded=False` throughout.

## 2026-10-05 21:58 Two-handed: Half Sword keeps the grip; the 19:05 "left hand off the grip" was a tool bug [measured]
- **Tool bug:** `tools\gabs\input_watch.py` read the left wrist at POSE values 25-27. Each arm has 25 values (shoulder,
  elbow, wrist, then 4 rotations; `ArmAtlas.ReadFeatures`), so 25-27 is the left **shoulder**; the left wrist is 31-33.
  The 19:05 numbers ("wrists 0.24-0.70 m apart", "left wrist height varied 5-7 cm") measured right wrist to left
  shoulder. The conclusion "in Half Sword the left hand leaves the grip" is wrong. Fixed in input_watch.py and
  hs_input_probe.py.
- **HSLink telemetry:** a third state-file line `GRIP seq twoHanded attached altGrip lDown searching releaseRate
  armLTonus END` (Willie_BP's own variables, read-only), and a hslink.log line when the grip state flips. HsLink.cs and
  input_watch read only line 1 and POSE, so nothing else changes.
- **Oracle, Half Sword alone** (no Bannerlord, no user): `hs_input_probe.py` replays scripted Bannerlord input
  (idle, guard, slow and fast moves, left guard) with `ModularWeaponBP_LongSword_T2` two-handed. Evidence:
  `logs\evidence\20261005-2157-grip-probe-longsword-fixed.txt`.
  - left hand attached 100% of the time, release rate peak 0.00, even on 1200-count flicks in 0.25 s;
  - wrists 0.14-0.15 m apart (max 0.18 on a fast move);
  - left guard held: `R Alt Grip` on, the half-sword grip, wrists 0.16-0.24 m apart; back to attached on release.
- **So the two-handed "buggé" is Bannerlord's side:** the arms copy Half Sword's rotations onto Bannerlord's bone
  lengths, and nothing keeps the left wrist at Half Sword's distance from the right one. Next: put the Bannerlord left
  hand at the right wrist plus Half Sword's right-to-left wrist vector (two-bone IK on the left arm), and log the
  drawn distance next to Half Sword's (0.15 m) as the oracle.
- Also seen: Half Sword has UE mouse smoothing on (`smoothing=true`); HSLink does not reproduce it (aim precision,
  plan item 3).

## 2026-10-05 19:05 User playtest of the 13 loadouts, with measurements and causes found in Willie_BP
- **Fixed during the tests:** an empty left hand holds Half Sword's fists (`Weapon_Fists_C`), which differed from "-".
  HSLink then re-equipped the weapon every second (tests 1, 4, 7: 36 to 77 re-equips). `HeldForCheck` now counts
  fists as empty. Installed at 18:37, so tests 8-13 ran with it.
- **Half Sword crash 18:47:28** during the retest of test 1: UE4SS deep-recursion stack, null read. Before today 18:12
  the last crash was on 2026-10-03.
- **User report (summary):**
  - 1H sword and axe: good overall, but up/down moves work badly and the aim lacks precision;
  - every shield test: "le bouclier du jeu n'est pas comme celui de Half Sword";
  - axe or mace with a shield: only downward blows;
  - dagger: too low; the posture is wrong when it is held with two hands;
  - every two-handed weapon (8, 9, 10, 12, 13): "buggé", the left hand badly placed;
  - spear: like Half Sword but should be firmer, with a double-click thrust for reach.
- **Measured (input_watch, Half Sword wrists relative to the pelvis):**
  - two-handed tests: the distance between Willie's wrists ranged 0.24-0.70 m, and the left wrist height varied 5-7 cm
    while the right varied 45-70 cm. So the left hand was not on the grip while the user played, although "left hand
    attached" was logged at equip;
  - the left button (Bannerlord RMB, Half Sword left guard) was held 35-46% of the time in those tests.
- **Why the left hand leaves the grip (Willie_BP):**
  - `L Down` (left guard) differs from `R Alt Grip` on a two-handed weapon, which switches to the weapon's alternate
    grip (half-swording);
  - `Grab L Release Rate` rises while the left-hand constraint force exceeds a limit (arm tonus x body tonus x
    consciousness), then calls `Break L Constraint`. So fast moves pull the hand off;
  - Willie re-attaches by himself (`L Offhand Searching Grip`, then `Attach L Hand To Offhand Position` once the hand is
    near the grip).
- **Bannerlord side:** each copy follows its own Bannerlord hand bone, and the arms copy Half Sword's rotations onto
  other bone lengths. Nothing puts the Bannerlord left hand on the Bannerlord weapon, or gives a weapon or shield Half
  Sword's own orientation. Next: stream Half Sword's weapon and shield transforms, place the copies with them, and put
  the hands on the grips.
- **Thrust:** Half Sword binds Thrust to LeftAlt only (input_bindings.txt). The double-click from the 2026-10-03 design
  has to be detected in Bannerlord (HsInputLink sends only LeftAlt now).

## 2026-10-05 18:25 Shield grip fixed, 13 test loadouts, two HSLink attempts that crashed the demo [measured]
- **User playtest before this:** "les mouvements d'arme manque un peu de précision au contrôle de la souris", "le
  bouclier c'est pas encore sa". They asked for one test per weapon and combination, one at a time.
- **Shield cause:** every Bannerlord shield is class LargeShield (even 70 cm round ones), so all became
  `Shield_Pavise_Light`. The item usage tells the grip:
  - `hand_shield` (centre grip, round) -> Half Sword `Shield_BossGrip` family (BossGrip, Buckler);
  - `shield` (strapped: kite, heater, pavise) -> `Shield_Tagre` family (Tagre, Pavise).
- **`HsWeaponMap` rewritten as families:**
  - each family has a fixed fallback;
  - it can pick the member nearest in size and weight from a measured catalogue (`hs_catalog.txt`);
  - no catalogue exists yet (see below), so every family uses its fallback.
- **`hsc.loadout [list|next|prev|n]`** (`Lab\LabLoadouts.cs`): 13 sets of campaign items, equipped with the game's own
  equip and instant wield.
  - `LinkMarker.NativeReplaced()` makes the marker rebuild its copies and hide the new natives.
  - A new item in the same slot is detected too.
  - Checked in both games, no crash, Half Sword holding each:

    | Bannerlord set | Half Sword |
    |---|---|
    | 1H sword, ± kite, ± round shield | `ArmingSword_T1`, + `Shield_Tagre` / `Shield_BossGrip` |
    | 1H axe | `Axe` |
    | mace + heater | `Mace_Mid_Tier_Avg` + `Shield_Tagre` |
    | dagger | `Dagger_T1` |
    | 2H sword / 2H axe / 2H mace | `LongSword_T2` / `Axe2H` / `Hafted_Mid_Tier_Long`, left hand attached |
    | spear + round shield / spear alone | `Spear_A` + `Shield_BossGrip` / `Spear_A` two-handed |
    | swing polearm | `Polearm_Mid_Tier`, two-handed |
- **Failed, reverted:**
  - HSLink loop at 5 ms (to cut aim latency): 4 launches, 4 crashes (18:12:22, 18:14:59, 18:16:36, 18:18:14;
    EXCEPTION_ACCESS_VIOLATION, "Freed invalid callbacks!" just before). The last two had the catalogue off; 18:16 and
    18:18 crashed within a second of the first weapon equip, 18:18 with a sword alone, so it is not the shield. Back to
    16 ms: no crash from 18:21 to 18:47 (one crash at 18:47:28, see the 19:05 entry).
  - In-game catalogue (lab switch `catalog`):
    - `K2_GetComponentsByClass` gave no array Lua can walk;
    - an error inside a UE4SS `ForEach` callback escapes the outer pcall;
    - and 0.3 s weapon swaps ran into the crashes above.
    - Switched off. Sizes would have to come from the extracted assets instead.
- **Next:** the user tests the 13 sets one by one and reports at the end.

## 2026-10-05 17:55 Input passthrough, both hands, Willie armed like the player [measured]
- **User playtest 17:38, real mouse in Bannerlord:**
  - "sa fonctionne comme half sword sa ce vois";
  - defects reported: on a fast move the arm flies up; the moves don't depend on the weapon or the shield.
  - `tools\gabs\input_watch.py` recorded the chain: buttons and mouse reached Half Sword. The left wrist never moved
    on the right button (Willie had no shield then), and fast flicks lifted the right wrist to 0.79-1.00 m.
- **Fast-move cause [measured]:**
  - minimized, Half Sword runs about 400 frames/s, so ~7 engine frames pass per 16 ms HSLink step (stats line in
    hslink.log);
  - HSLink gave the whole mouse move to the first frame, a 7x per-frame aim velocity for Update Aim();
  - fix: each frame gets its share, and the frame cap went from 8 to 64 (it had clipped steps);
  - same 1767-count flick in 0.25 s: right wrist peak 0.42-0.47 m instead of 0.79-0.86 m. Not a controlled A/B: the
    weapon differs.
- **Weapon sync:**
  - `Link\HsWeaponMap.cs` maps the Bannerlord weapon class to the Half Sword weapon blueprint of the same family
    (one per family);
  - `hslink_equip.txt` carries it to Half Sword;
  - HSLink equips it with Willie_BP's own `Set Up Right/Left Hand Weapon`, plus the two-handed grip as HSPose did,
    re-checked every second;
  - measured: axe + large shield -> `ModularWeaponBP_Axe` + `Shield_Pavise_Light`, held as asked.
- **Both hands:**
  - `LinkMarker` now draws the shield copy on the drawn `l_finger0` too and hides both native items;
  - visual A/B with lab_cam 200 2 1.6 (noise 0 cells): left button + mouse up = shield raised, 301 cells; left
    button + right 163; right button + right 151.
- **Crash 12:11:15 fixed:** `HeldItems` reset a weapon entity that `ClearWeaponMeshes` had freed (AccessViolation).
  - It now drops all handles while marker = 1.
  - It touches a remembered entity only if the agent still owns it.
- **Play settings left for the user:** link_input = 1, link_pose = 1, marker = 1; lab_cam commented out, so the camera is
  normal.
  - In the lab with lab_cam set, the view is the fixed lab orbit, and the mouse seemed to do nothing.
- **Focus:** `tools\gabs\focus_guard.ps1` sends game windows that take the foreground back behind the user's window
  during launches (sends no input).
- **Not done yet:**
  - lengths within a family (one Half Sword weapon per family);
  - the shield size mapping is a guess by Bannerlord class: Pavise for large, Tagre for small;
  - the native-item restore when control ends is not visibly confirmed.

## 2026-10-05 11:40 Phase 0 visible: a link value moves what Bannerlord draws [measured, fake sender]
- **Sources read in full before this** (memory note `passthrough-guides-reference`):
  - trevaintdead/ai-game-modding-guides;
  - SolarFren's Infographic Mega Library;
  - the five Half Sword -> Dying Light 2 clips.
  All of them prove the link with one visible marker before anything native. The DL2 project's first in-game
  object was a free-floating axe.
- **`Link/LinkMarker.cs`**, live tuning `marker = 1`:
  - a copy of the main weapon made the way the game makes dropped weapons: `GameEntityExtensions.Instantiate`
    -> native `CreateFromWeapon`, batched, as in `Mission.SpawnWeaponWithNewEntityAux`;
  - a top-level scene entity, Dynamic, with no physics and no pick-up script;
  - moved every tick (after `ActiveRagdoll.Step`) to Half Sword's right hand relative to our drawn pelvis
    (`HsLink.TryHandFromPelvis`). Without a fresh sample it sits on our drawn `r_finger0`;
  - `marker_offset` moves it, for A/B. Lab log: `MARKER source=link|hand`.
- **Fake Half Sword:** `%USERPROFILE%\tools\gabs\link_ab.py` writes the HSLink line at 60 Hz with the hand
  held at A or B while it takes switched screenshots.
- **Gotcha:** a rename onto `hslink_state.txt` while the game reads it fails with "access denied" and killed the
  sender thread. The first link run was therefore invalid: the log showed `source=hand`. Fix: write in place.
- **Results** (`visual_ab.py` change cells; 0-11 = noise on 2026-10-05):

  | test | cycles | cells | what the change map shows |
  |---|---|---|---|
  | `marker 0/1` | 3 | 10 | at the right hand |
  | `marker_offset 0 0 0.5` | 3 | 46 | a thin diagonal shaft above the head |
  | link: hand 40 25 30 -> 40 25 80 cm (+50 up) | 4 | 52 | the shaft rising above the helmet |
  | link: hand 40 25 30 -> 40 75 30 cm (+50 right) | 4 | 69 | the shaft moving to screen-left (camera in front: the agent's right) |

  In both valid link runs `MARKER source=link` stayed on for the whole run (32 of 32 lines).
- **Verdict:**
  - a value in HSLink's format crosses, and Bannerlord draws an object where it says;
  - the up and right axes are confirmed;
  - forward is not tested yet.
- **Real Half Sword, unattended (2026-10-05 11:26):**
  - HSLink got opt-in lab switches in `%USERPROFILE%\halfsword-decomp\hslink_lab.txt`:
    - `duel` calls the game mode's own `GM Duel` 8 s after load, so a minimized demo streams with nobody at the menu;
    - `calm` makes the player invulnerable and removes the other Willies;
    - `arm` drives the right arm from `hslink_arm.txt` through the Willie_BP inputs HSPose used.
    Without the file HSLink stays read-only.
  - `run-hs-link.ps1` reported the previous session's "player found" while the demo sat in its menu. Fixed: only a
    line after this launch's "# HSLink loaded" counts.
  - Bannerlord read fresh samples (about 60/s) with `MARKER source=link`. The idle hand did not move at all.
- **Freeze 11:27:33, the same deadlock as 2026-10-03:**
  - Discord had been started again at 11:27:04 (not by the agent);
  - `discord_clips` started at 11:27:33, the second the Bannerlord frame stopped (two window captures 5 s apart
    differed by 0 pixels);
  - stackdump: a thread in DiscordHook64.dll holds the loader lock and waits for renodx-dlss.addon64's SRW lock,
    while the render thread holds that lock inside `CreateToolhelp32Snapshot` and waits for the loader lock;
  - killed by exact PID through the GABS hook; `safely_exited` restored.
  - The real-arm A/B (`link_ab.py --real-arm`) did not run.
- **Phase 1 built while Bannerlord is on hold (user's call: Discord stays open):**
  - `ArmAtlas.ReadFeatures` is now shared by the atlas loader and `HsLink.TryReadPose`.
  - HSLink writes a second line `POSE seq <70 features> END` (73 tokens) from the physical mesh's component space,
    converted like make_arm_atlas.py.
  - `HsPlayerBehavior`: `link_pose = 1` replaces the atlas pose with the live one. The lab log's MARKER line shows
    `pose=link|atlas`.
  - Built and deployed; not run in Bannerlord.
- **Half Sword side, measured with the real demo minimized** (lab switches duel, calm, arm), right wrist in m
  (right, forward, up):

  | arm state | right wrist |
  |---|---|
  | released | (0.285, -0.011, 0.002) |
  | Guard Right, aim (0, -60) | (0.109, 0.331, 0.715) |
  | released again, 3 s later | (0.247, 0.078, 0.022), still settling |

  - Physics jitter: 1.4-1.6 cm sd. Upper arm 0.264 m, forearm 0.258 m; the atlas has 0.278 / 0.273 m, recorded
    with another weapon.
  - The atlas pose for the same command lifts the wrist to 1.05 m: the physical body sits lower and more forward
    than the driver target.
- **One minimized instance streamed a POSE line that never moved** (sd 0.0000), while the arm inputs were set.
  - Not a pause: the new unpause check never fired.
  - The next instance moved. Cause unknown.
  - HSLink now also sets `DriverSkeleton.VisibilityBasedAnimTickOption = 0`, as HSGrip did.
- **Discord kept running (the user was in a voice call):**
  - `tools\discord.ps1 -HookOff` renames `DiscordHook64.dll` (in `modules\discord_hook-1`) so nothing gets injected
    into Bannerlord; voice keeps working;
  - `-HookOn` restores it, once Bannerlord has closed;
  - verified: no DiscordHook64 module in the Bannerlord process, and no freeze in the runs below.
- **User report 2026-10-05 11:50:** "la position de la hache est parfaite quand half sword n'a pas lancé le combat
  et est resté dans le menu".
  - With the demo in its menu, the link was stale, so LinkMarker sat on our drawn `r_finger0` (`source=hand`):
    **the free-standing weapon copy sits in the fist**, which two days of compensating the engine's own item never
    achieved.
  - Once HSLink started a duel, the marker went to Half Sword's hand relative to our pelvis (`source=link`). That
    is away from our fist, because our arm was still the atlas pose.
  - Fix: the hand bone is now the default; `marker_link = 1` keeps the link-only test (link_ab.py sets it).
- **Phase 1 measured with the real Half Sword [measured]:**
  - setup: `link_pose = 1`, `marker = 1`; HSLink lab switch `arm` toggles between released and Guard Right with aim
    (0, -60); `link_ab.py --real-arm`, 3 cycles, 3 s settle;
  - change: 118 cells, and the map is the player's right arm with the axe raised above the head;
  - noise run under the same setup: 1 cell;
  - log: `pose=link source=hand` on all 40 lines;
  - so the Bannerlord arm follows the real Half Sword arm, and the weapon copy follows the hand.
- **Not tested:**
  - whether the engine's own axe is still drawn next to the copy (the native item is not hidden);
  - swings, the left arm and the shield, walking;
  - the user playtesting with the real mouse in Half Sword;
  - the marker while walking (it is placed before the agents tick, so it may trail by one frame);
  - the marker's rotation (it follows our hand bone; HSLink sends no rotations yet).
