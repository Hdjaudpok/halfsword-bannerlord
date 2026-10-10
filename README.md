# Half Sword Combat for Bannerlord (experimental)

An experimental Mount & Blade II: Bannerlord mod that tries to bring Half Sword's physics-driven melee to the
player's infantry fights: the mouse moves the arm while a button is held, swings come from mouse motion, and the
body reacts like an active ragdoll. Walking and movement stay Bannerlord's own.

> **Status: research prototype, paused since 2026-10-06.** It runs in a lab setup. It is not a finished, playable
> mod and there is no release build. `STATUS.md` is the running state; `MODLOG.md` is the full journal.

## Approaches tried

| Trial | Idea | Where it stands |
|---|---|---|
| Active ragdoll (step 2) | BEPUphysics v1 ragdoll built from Half Sword's body values, written onto the agent's skeleton every frame | Works in the lab: standing gaps to the animation of 2-11 cm, close to Half Sword's own |
| A. Passthrough | The Half Sword demo runs alongside; a UE4SS Lua mod (`HSLink`) streams the player's pose, aim and weapon; Bannerlord copies them onto the player | Works end to end. Closed: the owner judged the feel not good enough |
| B. Native clips | Half Sword's real strikes become native Bannerlord animation clips, so Bannerlord's own hits, blocks and AI parries work with them | A strike exported as FBX on Bannerlord's `human_skeleton` (round trip within 0.13 cm); the Modding Kit import is next |
| C. Hybrid | Passthrough guard and aim, native clip for the strike | Not started (needs B) |
| D. Rust rewrite | Half Sword's active-ragdoll arm re-implemented headless with rapier3d | First slice follows Half Sword's recorded arm closer than the baseline (hand median 19.0 cm vs 21.9 cm) |

The plan and the rules for each trial are in `docs/ROADMAP-4-TRIALS.md` and `docs/superpowers/plans/`.

## No game data in this repository
- The repository holds no Half Sword or Bannerlord files, extracted assets or decompiled code.
- Half Sword values (body masses, joints, pose atlases) are produced **on your machine, from your own install**, by
  `tools/halfsword/HSDump`, `HSPose`, `make_profile.py` and `make_arm_atlas.py`. They go to
  `%USERPROFILE%\halfsword-decomp`, outside the repository. Without them, the Half Sword physics stays off.
- Game assemblies are referenced in place at build time and never copied.

## Layout
- `src/HalfSwordCombat/`: the Bannerlord module (C#, netstandard2.0, Harmony).
  - `Physics/`: active ragdoll, arm retargeting, grips, pose hook.
  - `Link/`: the passthrough receiver.
  - `Combat/`: Half Sword-style controls.
  - `Lab/`: probes and auto-tests.
- `src/HSClipTest/`: helper module for the Modding Kit import (trial B).
- `module/SubModule.xml`: the module manifest.
- `tools/halfsword/`: UE4SS Lua mods for the Half Sword demo (`HSLink`, `HSPose`, `HSGrip`, `HSInput`, `HSDump`)
  and the Python converters.
- `tools/`: `deploy.ps1`, `run-lab.ps1`, `discord.ps1` (a lab workaround for an overlay deadlock).
- `rust/hs_body/`: the trial D crate.
- `docs/`: passthrough notes, roadmap, plans.

## Requirements (as tested)
- Mount & Blade II: Bannerlord v1.4.8.119303 (Steam), single player.
- Bannerlord.Harmony 2.4.2.225 and BLSE 1.6.2.
- For the passthrough and the data tools: the free Half Sword demo (Steam) with UE4SS (experimental build).
- To build:
  - .NET SDK;
  - Python 3 with numpy and scipy;
  - Rust stable (trial D only).
- [BEPUphysics v1](https://github.com/bepu/bepuphysics1) at commit `88243f8`, cloned and built (Release) in
  `../deps/bepuphysics1`, next to this repository.

## Build and install (lab)
```powershell
dotnet build -c Release src/HalfSwordCombat/HalfSwordCombat.csproj -p:GameDir="<Bannerlord folder>"
powershell -File tools/deploy.ps1 -GameDir "<Bannerlord folder>"
```
`deploy.ps1` adds `Modules\HalfSwordCombat` to the game and touches nothing else. To uninstall, delete that folder.
For the Half Sword side, copy each Lua mod to the demo's `ue4ss\Mods\<Name>\Scripts\main.lua`, with an empty
`enabled.txt` in `ue4ss\Mods\<Name>\`. UE4SS loads a mod that has `enabled.txt` even when `mods.txt` says 0. To remove
UE4SS, delete `dwmapi.dll` and `ue4ss\`.
Back up your saves before a modded launch.

## Credits
- **Half Sword** by Half Sword Games, and **Mount & Blade II: Bannerlord** by TaleWorlds Entertainment. This
  project is not affiliated with either.
- **Libraries and tools:**
  - [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS);
  - BUTR's Bannerlord.Harmony and BLSE;
  - [BEPUphysics v1](https://github.com/bepu/bepuphysics1) (Apache-2.0);
  - [rapier3d](https://rapier.rs) (Apache-2.0);
  - [TpacTool](https://github.com/szszss/TpacTool).
- **References:**
  - trevaintdead's *ai-game-modding-guides* (the passthrough route and the STATUS handoff);
  - lambor590's *Half-Sword-Enhancer* (public Dumper-7 SDK);
  - the [Bannerlord modding docs](https://moddocs.bannerlord.com);
  - Epic's constraint drive documentation;
  - a Half Sword to Dying Light 2 passthrough project that inspired trial A.
- Built with the [universal-modder](https://github.com/rehan-remade/universal-modder) toolkit.

**AI disclosure:** the code and documents were written by AI coding agents, mainly Claude Code (Anthropic's Claude
models). The repository owner directed the work, made the design decisions and did every playtest.

## Contributing and licence
You may use, study and modify the code for any purpose. Updates go back to **this** repository, as pull requests;
please don't republish the project elsewhere. Read [`LICENSE`](LICENSE) for the exact terms and
[`CONTRIBUTING.md`](CONTRIBUTING.md) for how to send a change.
