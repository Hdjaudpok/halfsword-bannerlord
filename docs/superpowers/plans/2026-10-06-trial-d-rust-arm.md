# Trial D (Rust rewrite): Half Sword's right arm body, headless — Implementation Plan, part 1

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-implement Willie's physical right arm (upper arm, forearm, hand) in Rust, driven by the recorded
DriverSkeleton poses, and measure it against Half Sword's own recorded arm (the 90 s fight, `dumps/motion.csv`).

**Architecture:** A private Rust crate `rust/hs_body` in the mod repo (no git remote; no Half Sword data inside: every
value is read at runtime from `%USERPROFILE%\halfsword-decomp`). Part 1 builds the recording reader and the
characterization oracle (Task D1), then reads the drive semantics from their sources (Task D2). Part 2, written as an
amendment once D2 has fixed the units, adds the rapier3d arm and its differential run against the recording (Task D3).
End state (port-to-rust): **B, a Rust core with a binding as the product**. Bannerlord will later load it as a
`cdylib` through P/Invoke from the C# mod (.NET Framework 4.8). No binding in part 1.

**Tech Stack:** Rust stable 1.99 (cargo), crate `csv` 1.x; part 2: `rapier3d`. Data: Half Sword demo build 18478397
(UE 5.4.4) dumps.

**Spec:** `docs/ROADMAP-4-TRIALS.md`, section "Trial D". Facts: `%USERPROFILE%\halfsword-decomp\NOTES.md` (private),
`dumps\pass1\physics.txt` section `Willie_PhysicsAsset_Capsules1` (lines 61-192), `bp\Willie_BP.txt` function
`Muscle Motor` (lines ~18260-18367) and the physical-animation setup (lines ~1367-1466).

## Global Constraints
- Never invent: every gain, mass, limit and unit comes from the dumps, the decoded Blueprints or a cited source.
- Half Sword data stays in `%USERPROFILE%\halfsword-decomp`. The crate, its tests and its fixtures contain no values
  copied from it; tests use synthetic numbers.
- The crate is never pushed to a public remote (the mod repo has none).
- One change at a time against the oracle. After 2-3 failed attempts on the same gap, stop and write the handoff.
- Commits only with the user's OK. STATUS.md and MODLOG.md updated at each milestone. Messages to the user in French.

## Parity contract (port-to-rust)
- **Unit of parity:** per recorded frame (29 Hz, 2610 frames), the world position of each simulated body's origin
  (upperarm_r, lowerarm_r, hand_r), given the recorded DriverSkeleton poses and the recorded chest.
- **Measured by:** `e_sim` = |simulated origin − recorded BODY origin| against the baseline `e_drv` =
  |recorded DRIVER origin − recorded BODY origin| (what "body = driver", i.e. no physics, would give). Report n,
  median and p95 of both, per bone. Frames with `Fallen` or `DED` are reported separately.
- **The slice works when:** median `e_sim` < median `e_drv` for hand_r. The physics then explains part of
  Half Sword's own lag. Exact parity is impossible: Chaos is not available to run side by side.
- **Acceptable differences:** solver and integration details, collision with the world (not simulated), the weapon
  (its mass is unknown for this recording; a known, reported difference).
- **Not ported:** gore, the left arm and legs (part 1-2), balance handles.

## Review Focus
1. **Quoted CSV fields with commas** (`"(0.0000,0.0000,0.0000)"` vectors): the reader must not shift columns. Test:
   D1 Step 1 `reads_bones_poses_and_scalars`.
2. **Boolean fields** (`"true"`/`"false"`): read as 1/0, so `Fallen`/`DED` frames can be split out. Same test.
3. **A missing column** (another recording layout): fail with the column's name, not a panic. Test: D1 Step 1
   `missing_driver_column_is_an_error`.
4. **Empty input**: percentiles of nothing return None, not a panic. Test: D1 Step 1 `gaps_and_percentiles`.
5. **The baseline must match the recorded characterization** (NOTES.md: hand_r 22/87 cm, hand_l 13/54, spine_05
   4/14, Head 6/21, pelvis 0/0.3): if the reader is wrong, every later comparison is wrong. Test: D1 Step 5.

---

### Task D1: Recording reader and characterization oracle

**Files:**
- Create: `rust/hs_body/Cargo.toml`, `rust/hs_body/src/lib.rs`, `rust/hs_body/src/motion.rs`,
  `rust/hs_body/src/oracle.rs`, `rust/hs_body/src/bin/hs_bench.rs`, `rust/hs_body/tests/motion_reader.rs`,
  `rust/hs_body/PARITY-CONTRACT.md` (copy of the section above), `rust/hs_body/.gitignore` (`/target`)

**Interfaces:**
- Produces: `motion::Pose { pos: [f64; 3], rot: [f64; 4] }` (UE cm; quaternion x y z w),
  `motion::Recording::{load(&Path), from_reader(R), bone(&str) -> Option<usize>, scalar(&Frame, &str) -> Option<f64>}`,
  `Recording.bones: Vec<String>`, `Recording.frames: Vec<Frame>`, `Frame { t, body: Vec<Pose>, driver: Vec<Pose> }`,
  `oracle::{distance(&Pose, &Pose) -> f64, percentile(&[f64], f64) -> Option<f64>, spread(&[f64]) -> Option<Spread>}`,
  `Spread { n, median, p95 }`; binary `hs_bench baseline <motion.csv>`.

- [ ] **Step 1: Write the failing tests** (`tests/motion_reader.rs`)

```rust
use hs_body::motion::Recording;
use hs_body::oracle::{distance, percentile, spread, Spread};

const MINI: &str = "t,Health,Aim_Vector,Fallen,body_hand_r_x,body_hand_r_y,body_hand_r_z,body_hand_r_qx,body_hand_r_qy,body_hand_r_qz,body_hand_r_qw,driver_hand_r_x,driver_hand_r_y,driver_hand_r_z,driver_hand_r_qx,driver_hand_r_qy,driver_hand_r_qz,driver_hand_r_qw,scene_IK_R_Hand_x
0.001,\"100.0000\",\"(0.0000,0.0000,0.0000)\",\"false\",0,0,0,0,0,0,1,3,4,0,0,0,0,1,7
0.035,\"90.0000\",\"(1.0000,2.0000,0.0000)\",\"true\",10,0,0,0,0,0,1,10,0,12,0,0,0,1,7
";

#[test]
fn reads_bones_poses_and_scalars() {
    let rec = Recording::from_reader(MINI.as_bytes()).unwrap();
    assert_eq!(rec.bones, vec!["hand_r".to_string()]);
    assert_eq!(rec.frames.len(), 2);
    let hand = rec.bone("hand_r").unwrap();
    assert_eq!(rec.frames[0].driver[hand].pos, [3.0, 4.0, 0.0]);
    assert_eq!(rec.frames[1].body[hand].rot, [0.0, 0.0, 0.0, 1.0]);
    assert_eq!(rec.scalar(&rec.frames[1], "Health"), Some(90.0));
    assert_eq!(rec.scalar(&rec.frames[1], "Fallen"), Some(1.0));
    assert!(rec.scalar(&rec.frames[0], "Aim_Vector").unwrap().is_nan());
    assert_eq!(rec.scalar(&rec.frames[0], "scene_IK_R_Hand_x"), None);
}

#[test]
fn missing_driver_column_is_an_error() {
    let broken = MINI.replacen("driver_hand_r_qw", "driver_hand_r_qq", 1);
    let err = Recording::from_reader(broken.as_bytes()).unwrap_err().to_string();
    assert!(err.contains("driver_hand_r_qw"), "{err}");
}

#[test]
fn gaps_and_percentiles() {
    let rec = Recording::from_reader(MINI.as_bytes()).unwrap();
    let gaps: Vec<f64> = rec.frames.iter().map(|f| distance(&f.body[0], &f.driver[0])).collect();
    assert_eq!(gaps, vec![5.0, 12.0]);
    assert_eq!(percentile(&gaps, 50.0), Some(5.0));
    assert_eq!(spread(&gaps), Some(Spread { n: 2, median: 5.0, p95: 12.0 }));
    assert_eq!(percentile(&[], 50.0), None);
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `cargo test --manifest-path rust/hs_body/Cargo.toml` (from the mod repo root).
Expected: compile error, `unresolved import hs_body::motion` (the modules do not exist yet).

- [ ] **Step 3: Write the crate**

`Cargo.toml`:

```toml
[package]
name = "hs_body"
version = "0.1.0"
edition = "2021"
publish = false
description = "Half Sword's active-ragdoll arm re-implemented headless (private; reads the user's own dumps at runtime)"

[dependencies]
csv = "1"
```

`src/lib.rs`:

```rust
//! Headless re-implementation of Half Sword's active-ragdoll arm (trial D, docs/ROADMAP-4-TRIALS.md).
//! No Half Sword data lives in this crate: recordings and physics values are read at runtime from the user's dumps.
pub mod motion;
pub mod oracle;
```

`src/motion.rs`:

```rust
//! Reader for the 29 Hz fight recording written by HSDump (`dumps/motion.csv`): per frame, Willie_BP scalars and the
//! world transform (UE cm, quaternion x y z w) of each recorded bone's physical body and of its DriverSkeleton bone.
use std::collections::HashMap;
use std::error::Error;
use std::path::Path;

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Pose {
    pub pos: [f64; 3],
    pub rot: [f64; 4],
}

#[derive(Debug)]
pub struct Frame {
    pub t: f64,
    scalars: Vec<f64>,
    pub body: Vec<Pose>,
    pub driver: Vec<Pose>,
}

#[derive(Debug)]
pub struct Recording {
    pub bones: Vec<String>,
    scalar_names: Vec<String>,
    pub frames: Vec<Frame>,
}

impl Recording {
    pub fn load(path: &Path) -> Result<Self, Box<dyn Error>> {
        Self::from_reader(std::fs::File::open(path)?)
    }

    pub fn from_reader<R: std::io::Read>(reader: R) -> Result<Self, Box<dyn Error>> {
        let mut csv = csv::ReaderBuilder::new().has_headers(true).from_reader(reader);
        let header: Vec<String> = csv.headers()?.iter().map(str::to_owned).collect();
        let index: HashMap<&str, usize> = header.iter().enumerate().map(|(i, h)| (h.as_str(), i)).collect();
        let bones: Vec<String> = header
            .iter()
            .filter_map(|h| h.strip_prefix("body_").and_then(|rest| rest.strip_suffix("_x")))
            .map(str::to_owned)
            .collect();
        let pose_columns = |prefix: &str, bone: &str| -> Result<[usize; 7], Box<dyn Error>> {
            let mut cols = [0usize; 7];
            for (slot, suffix) in cols.iter_mut().zip(["x", "y", "z", "qx", "qy", "qz", "qw"]) {
                let name = format!("{prefix}_{bone}_{suffix}");
                *slot = *index.get(name.as_str()).ok_or_else(|| format!("missing column {name}"))?;
            }
            Ok(cols)
        };
        let body_cols = bones.iter().map(|b| pose_columns("body", b)).collect::<Result<Vec<_>, _>>()?;
        let driver_cols = bones.iter().map(|b| pose_columns("driver", b)).collect::<Result<Vec<_>, _>>()?;
        let t_col = *index.get("t").ok_or("missing column t")?;
        let scalar_cols: Vec<usize> = (0..header.len())
            .filter(|&i| {
                i != t_col && !["body_", "driver_", "scene_"].iter().any(|p| header[i].starts_with(p))
            })
            .collect();
        let scalar_names = scalar_cols.iter().map(|&i| header[i].clone()).collect();
        let mut frames = Vec::new();
        for record in csv.records() {
            let record = record?;
            let num = |i: usize| -> Result<f64, Box<dyn Error>> {
                let field = &record[i];
                Ok(field.trim().parse::<f64>().map_err(|e| format!("column {}: {field:?}: {e}", header[i]))?)
            };
            let pose = |cols: &[usize; 7]| -> Result<Pose, Box<dyn Error>> {
                Ok(Pose {
                    pos: [num(cols[0])?, num(cols[1])?, num(cols[2])?],
                    rot: [num(cols[3])?, num(cols[4])?, num(cols[5])?, num(cols[6])?],
                })
            };
            frames.push(Frame {
                t: num(t_col)?,
                scalars: scalar_cols.iter().map(|&i| scalar(&record[i])).collect(),
                body: body_cols.iter().map(pose).collect::<Result<_, _>>()?,
                driver: driver_cols.iter().map(pose).collect::<Result<_, _>>()?,
            });
        }
        Ok(Recording { bones, scalar_names, frames })
    }

    pub fn bone(&self, name: &str) -> Option<usize> {
        self.bones.iter().position(|b| b == name)
    }

    /// A Willie_BP scalar of one frame: numbers as written, booleans as 1 / 0, vectors (not parsed) as NaN.
    pub fn scalar(&self, frame: &Frame, name: &str) -> Option<f64> {
        self.scalar_names.iter().position(|n| n == name).map(|i| frame.scalars[i])
    }
}

fn scalar(field: &str) -> f64 {
    match field.trim() {
        "true" => 1.0,
        "false" => 0.0,
        other => other.parse().unwrap_or(f64::NAN),
    }
}
```

`src/oracle.rs`:

```rust
//! Oracle metrics shared by the baseline and the simulation: distances between pose origins, and percentiles.
use crate::motion::Pose;

/// Distance between two pose origins, in the recording's units (UE cm).
pub fn distance(a: &Pose, b: &Pose) -> f64 {
    let d = [a.pos[0] - b.pos[0], a.pos[1] - b.pos[1], a.pos[2] - b.pos[2]];
    (d[0] * d[0] + d[1] * d[1] + d[2] * d[2]).sqrt()
}

/// Nearest-rank percentile (p in 0..=100); None when there are no values.
pub fn percentile(values: &[f64], p: f64) -> Option<f64> {
    if values.is_empty() {
        return None;
    }
    let mut sorted = values.to_vec();
    sorted.sort_by(f64::total_cmp);
    let rank = ((p / 100.0) * sorted.len() as f64).ceil().max(1.0) as usize;
    Some(sorted[rank.min(sorted.len()) - 1])
}

#[derive(Debug, PartialEq)]
pub struct Spread {
    pub n: usize,
    pub median: f64,
    pub p95: f64,
}

pub fn spread(values: &[f64]) -> Option<Spread> {
    Some(Spread { n: values.len(), median: percentile(values, 50.0)?, p95: percentile(values, 95.0)? })
}
```

`src/bin/hs_bench.rs`:

```rust
//! hs_bench baseline <motion.csv>: the recorded body-to-driver gap per bone (median / p95, UE cm), the
//! characterization the arm simulation is measured against.
use hs_body::motion::Recording;
use hs_body::oracle::{distance, spread};
use std::path::Path;
use std::process::ExitCode;

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().collect();
    let [_, cmd, path] = args.as_slice() else {
        eprintln!("usage: hs_bench baseline <motion.csv>");
        return ExitCode::from(2);
    };
    if cmd != "baseline" {
        eprintln!("usage: hs_bench baseline <motion.csv>");
        return ExitCode::from(2);
    }
    let rec = match Recording::load(Path::new(path)) {
        Ok(rec) => rec,
        Err(e) => {
            eprintln!("{path}: {e}");
            return ExitCode::FAILURE;
        }
    };
    println!("frames {}", rec.frames.len());
    for (b, name) in rec.bones.iter().enumerate() {
        let gaps: Vec<f64> = rec.frames.iter().map(|f| distance(&f.body[b], &f.driver[b])).collect();
        if let Some(s) = spread(&gaps) {
            println!("{name:<12} n {:>5}  gap median {:>6.1} cm  p95 {:>6.1} cm", s.n, s.median, s.p95);
        }
    }
    ExitCode::SUCCESS
}
```

- [ ] **Step 4: Run the tests**

Run: `cargo test --manifest-path rust/hs_body/Cargo.toml`
Expected: `test result: ok. 3 passed`. Then `cargo clippy --manifest-path rust/hs_body/Cargo.toml --all-targets` and
`cargo fmt --manifest-path rust/hs_body/Cargo.toml --check`: no warnings, no diff (run `cargo fmt` once if needed).

- [ ] **Step 5: Characterize the real recording**

Run: `cargo run --release --manifest-path rust/hs_body/Cargo.toml --bin hs_bench -- baseline "%USERPROFILE%\halfsword-decomp\dumps\motion.csv"`
Expected: `frames 2610`, 15 bones, and within ±1 cm of NOTES.md: pelvis 0 / 0.3, spine_05 4 / 14, Head 6 / 21,
hand_r 22 / 87, hand_l 13 / 54, feet 6 / 12. A larger difference means the reader or the method differs: stop and
find out why before Task D3.

- [ ] **Step 6: Record and commit**

MODLOG.md entry `## 2026-10-06 HH:MM Trial D: hs_body reader and baseline [measured]` with the bench output;
STATUS.md NOW. Commit only with the user's OK:
`git add rust/hs_body docs/superpowers/plans/2026-10-06-trial-d-rust-arm.md` then
`git commit -m "feat(trial D): hs_body crate, motion.csv reader and baseline oracle"`.

---

### Task D2: Drive semantics from their sources (no code)

**Files:**
- Modify: `%USERPROFILE%\halfsword-decomp\NOTES.md` (private: the values), `rust/hs_body/PARITY-CONTRACT.md` (the
  equations and units only, no values)

- [ ] **Step 1: Values from the dumps** (already read on 2026-10-06, re-check on the file):
  - shoulder `UserConstraint_10` (upperarm_r ↔ clavicle_r) profile Motor: limits swing1 45 / swing2 75 / twist 45
    (limited), soft k 50 d 5, drive mode 1 (twist and swing), position drive k 500 d 1;
  - elbow `UserConstraint_11` (lowerarm_r ↔ upperarm_r) profile Motor: swing1 75 / swing2 45 / twist 10, drive k 250 d 1;
  - bodies: linear damping 0.01, angular damping 10; physical animation PA: world space (`local=false`),
    orientation 1000, angular velocity 100;
  - masses from `dumps\pass2\masses.txt` (NOTES: upperarm 1.76, lowerarm 1.55, hand 0.82 kg); the hand body's shape
    and the clavicle frame from the asset section (find which asset holds `hand_r` for the `Capsules` override).
- [ ] **Step 2: Runtime overrides from `bp\Willie_BP.txt`**:
  - `Muscle Motor`: target = DriverSkeleton bone rotation in parent-bone space (`GetSocketTransform(..., 3)`);
    position strength for the right arm = `Muscle Power × MapRangeClamped(All Body Tonus, 25→0, 100→x) × Arm R Tonus ×
    R Arm Extra Tonus × MapRangeClamped(Stamina, 100→0, 1→0.5) × (Fallen ? Get Up Rate × 0.5 : 1) × (1 − Free Fall
    Rate)`, velocity strength 0, max force `Max Angular Force`. Confirm the region switch picks `Arm R Tonus ×
    R Arm Extra Tonus` for upperarm/lowerarm/hand (decode `Temp_byte_Variable_9`), and which `UpperBody` value the
    body mesh call passes;
  - physical animation (clavicle_r and below): orientation = `Arm R Tonus × Upper Body Tonus × Orientation Strength`,
    angular velocity = same × `Angular Strength`, max angular force `Max Angular Force`, scaled by the global
    `SetStrengthMultiplyer` (FInterpTo, line 1367).
- [ ] **Step 3: Units and drive model from UE 5.4 sources** (web search; cite each link in PARITY-CONTRACT.md):
  whether Chaos constraint drives and `UPhysicalAnimationComponent` drives run in acceleration mode (stiffness
  independent of mass/inertia) or force mode; how angular damping 10 is applied per step; how swing1/swing2 cone
  limits and soft limits (k, d) act. If a source does not settle a point, write "unknown" and plan an A/B in D3.
- [ ] **Step 4: Write part 2 of this plan** (Task D3, the rapier3d arm and its differential run) with exact code,
  from the D2 facts; ask the user to look at it.

### Task D3 (part 2, after D2): what it must deliver
- A kinematic chest frame per recorded frame: `T_root = T_body(spine_05) × T_driver(spine_05)⁻¹ × T_driver(upperarm_r)`
  (the clavicle is not recorded: it is assumed to follow its driver relative to the chest, a stated approximation).
- Dynamic upperarm_r, lowerarm_r, hand_r bodies (masses and shapes from D2), spherical joints with the Motor
  profile's limits, the constraint drive (parent space) plus the physical-animation drive (world space), each with
  the D2 strengths per frame from the recorded scalars, substepped between the 29 Hz frames with interpolated targets.
- `hs_bench arm <motion.csv>`: per bone, `e_sim` and `e_drv` (n, median, p95), all frames and without Fallen/DED.
- Pass: median `e_sim` < median `e_drv` for hand_r (parity contract). Report the counts, never only pass/fail.
