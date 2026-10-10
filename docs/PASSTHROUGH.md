# Half Sword in Bannerlord: passthrough design

Draft v0.1, 2026-10-04. Built on SkyCraft's `docs/DESIGN.md` (chasmlol/SkyCraft), ai-game-modding-guides guide 02 and
the universal-modder `mashup-mods` skill (pattern 2). It was decided after the user found a Half Sword -> Dying Light 2
project doing the same (videos in the `uni mod` folder): the real Half Sword runs next to the other game, and its
skeleton and weapon are copied live.

## 1. Core principle
**Neither game is rewritten.**
- Half Sword (demo, UE5) runs its own player: physics body, controls, grips, weapon physics.
- Bannerlord runs its own world: terrain, NPCs, AI, battles, campaign, walking.

The two mods only **translate** between them. If we find ourselves re-implementing a Half Sword mechanic in C#, or a
Bannerlord one in Lua, the design has gone wrong. That is what the previous approach did: recorded pose atlases, our own
active ragdoll, measured grips. It went in circles for two days.

## 2. Who is authoritative for what

| Thing | Authority | Why |
|---|---|---|
| Player's upper body pose, arms, hands | **Half Sword** | Its real physics and controls are the point of the mod |
| Weapon and shield transforms in the hands | **Half Sword** | The real grip, no guessing per weapon |
| Player position and walking | **Bannerlord** | User decision 2026-10-03: walking stays Bannerlord's |
| World, NPCs, AI, damage to NPCs | **Bannerlord** | It is the host game |
| Mouse and combat keys | Forwarded to **Half Sword** (later phase) | Combat controls must be Half Sword's |

## 3. Components

```
Half Sword demo (UE5, minimized)                      Bannerlord 1.4.8 (BLSE)
  UE4SS Lua mod HSLink                                  HalfSwordCombat module (C#, Harmony)
    reads Willie's bones every 16 ms         ---->    Link\HsLink: reads the line and the POSE line
    replays Bannerlord's mouse/buttons        <----    Link\HsInputLink: hslink_input.txt (mouse, buttons)
    equips Willie like the player             <----    Link\HsWeaponMap via hslink_equip.txt
                                                       poses the player's arms (pose hook, ArmRetarget)
                                                       Link\LinkMarker: held item copies, natives hidden
  transport: file drop %USERPROFILE%\halfsword-decomp\hslink_state.txt (phase 0)
             -> shared memory or UDP on 127.0.0.1 when per-frame rate matters
```

The pieces already measured to work on the Bannerlord side are reused:
- the pose hook and arm retarget (directions within 0.2 degrees of Half Sword's);
- the held item copy placed exactly where computed (0 cm / 0 degrees readback, lab 2026-10-04);
- the GABS lab loop.

## 4. Coordinate mapping
- Half Sword (UE) is left-handed: cm, X forward, Y right, Z up.
- Bannerlord is right-handed: m, x right, y forward, z up.
- Positions are sent relative to the Half Sword actor (its location and yaw). Bannerlord applies them relative to the
  player agent's frame:
  - `bl.x = hs.right / 100`
  - `bl.y = hs.forward / 100`
  - `bl.z = hs.up / 100`
- The mapping gets a test in phase 0, with a known value from a fake sender.

## 5. Phased plan

Each phase ends in something checked in the game, by measurement and by the user's playtest.

| # | Phase | Done when |
|---|---|---|
| 0 | **Link** | One value crosses: Bannerlord's LINK log shows Half Sword's right hand moving relative to its actor, at about 60 samples/s, with a working watchdog. Then a marker is drawn in Bannerlord where Half Sword's hand is |
| 1 | **Arms** | The player's arms in Bannerlord follow Half Sword's arms live |
| 2 | **Weapon** | The held weapon and shield copies sit at Half Sword's weapon transforms |
| 3 | **Input** | Bannerlord has focus; mouse and combat keys are forwarded to Half Sword (SkyCraft's InputBridge idea) |
| 4 | **Hits** | Half Sword weapon sweeps hit Bannerlord NPCs (hit detection in Bannerlord, damage through its own pipeline) |

### State (2026-10-05 19:15; details in STATUS.md and MODLOG)

| # | Phase | State |
|---|---|---|
| 0 | Link | **Done, visible** (`link_ab.py`, MODLOG 2026-10-05 11:40). |
| 1 | Arms | **Done for both arms [measured, user playtest].** POSE is applied by ArmRetarget (`link_pose = 1`). Visual A/B: shield raised 301 cells, weapon 151. |
| 2 | Weapon | **Partly done.** One copy per hand on our drawn hand bone (`LinkMarker`), natives hidden. Willie is armed like the player (`HsWeaponMap`, `hslink_equip.txt`). **Missing:** the copies do not use Half Sword's weapon and shield transforms, and nothing puts the left hand on a two-handed grip. This is the next step, from the user's playtest of 13 sets (MODLOG 19:05). |
| 3 | Input | **Done [measured, user playtest].** Mouse and buttons go through `hslink_input.txt` into Willie_BP's own input events. **Missing:** the double-click thrust (Half Sword binds Thrust to LeftAlt only). |
| 4 | Hits | Not started. |

## 4b. HSLink protocol (file drop, `%USERPROFILE%\halfsword-decomp\hslink_state.txt`, rewritten every ~16 ms)
```
seq actorX actorY actorZ actorYaw pelvisX pelvisY pelvisZ handX handY handZ END        Half Sword world, cm
POSE seq <70 features> END                                                             arm atlas layout and frame
```
- **POSE features** are those of `tools/halfsword/make_arm_atlas.py`, read with `ArmAtlas.ReadFeatures`:
  - per arm, r then l: shoulder, elbow, wrist (xyz), then hand, clavicle, upper-arm and forearm rotations (xyzw);
  - then pelvis and spine_02..05 rotations.
- **POSE frame:** character frame, right +X, forward +Y, up +Z, metres, pelvis-relative. They are taken from the
  physical mesh's component space, i.e. what Half Sword draws.
- **Bannerlord -> Half Sword files** (same folder):
  - `hslink_input.txt`, from `Link\HsInputLink.cs` with `link_input = 1`, rewritten every mission tick:
    `seq sumMouseX sumMouseY guardRight guardLeft alt END`.
    - The mouse is Bannerlord's raw move summed since the start, y down.
    - guardRight is the attack key (LMB) and guardLeft the defend key (RMB). Half Sword gets Right/Left Guard Axis,
      plus the RightMouseButton key events for the left guard.
    - alt is Left Alt, sent as Thrust.
  - `hslink_equip.txt`, from `HsInputLink.TickEquip` with `link_pose = 1`, on change and once a second:
    `rightPath leftPath twoHanded END`. Paths are under `Weapons/Blueprints/Built_Weapons`, `-` = empty hand.
- **Lab switches**, opt-in, in `hslink_lab.txt`; without the file HSLink only reads:
  - `duel`: start a duel by the game mode's own `GM Duel`, and keep the game unpaused;
  - `calm`: the player is invulnerable and the other Willies are removed;
  - `arm`: `hslink_arm.txt` ("guard aimX aimY") drives the right arm through the Willie_BP inputs;
  - `input`: Willie's own input is disabled and `hslink_input.txt` is replayed through Willie_BP's input events, once
    per engine frame, with the mouse spread over the frames. `hslink_equip.txt` is applied, plus the two-handed grip
    1 s after equipping. An empty hand holding Half Sword's fists counts as empty;
  - `catalog`: **do not use.** It crashed the demo (MODLOG 18:25).
- **Loop:** `LoopAsync(16)` + `ExecuteInGameThread`. 5 ms crashed the demo 4 times out of 4.
- **Lab tools:**
  - `tools\halfsword\run-hs-link.ps1` starts the demo minimized and waits for this launch's "player found";
  - `link_ab.py --real-arm "0 0 0" "1 0 -60" --setup "link_pose=1 marker=0"` is the phase-1 visible test.

## 6. Rules kept
- Single player only. Everything is read from the user's own installs; Half Sword data is never committed.
- No UE4SS Blueprint hooks: `LoopAsync` + `ExecuteInGameThread`. The only calls are Willie_BP's own functions and
  input events (input, equip, duel).
- Wrap the body of every UE4SS callback (`ForEach`, ...) in pcall: an error there escapes the outer pcall.
- Kill by exact PID. Don't take the user's mouse; Half Sword stays minimized. `focus_guard.ps1` only around launches,
  never while the user plays.
