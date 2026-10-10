//! Willie's right arm (upperarm_r, lowerarm_r, hand_r) re-implemented with rapier3d and driven the way Half Sword
//! drives it (decoded Willie_BP, MODLOG 2026-10-06 13:05):
//! - "Muscle Motor": each joint's drive pulls the child's rotation, in its parent's space, toward the DriverSkeleton's;
//!   position strength = Muscle Power x 100 x Arm R Tonus x R Arm Extra Tonus x stamina and fall factors, velocity
//!   strength 0;
//! - physical animation (world space): orientation strength = Arm R Tonus x Upper Body Tonus x Orientation Strength,
//!   angular velocity strength = the same x Angular Strength, toward the DriverSkeleton bone's world rotation.
//!
//! The chest is not simulated: a kinematic root holds the shoulder where the recorded chest puts it
//! (`T_body(spine_05) x T_driver(spine_05)^-1 x T_driver(upperarm_r)`; the clavicle is not recorded and is assumed to
//! follow its driver). Units: metres, kg, s (the recording's UE centimetres are divided by 100). See PARITY-CONTRACT.md.
use crate::dumps::{parse_bodies, parse_joint_offsets, parse_masses, ShapeKind};
use crate::motion::{Frame, Pose, Recording};
use rapier3d::math::{Pose as RPose, Rotation, Vector};
use rapier3d::prelude::*;
use std::error::Error;

pub const SEGMENTS: [&str; 3] = ["upperarm_r", "lowerarm_r", "hand_r"];

/// How a drive turns its stiffness and damping into torque. UE 5.8's API reference gives acceleration mode as the
/// default for constraint drives (FAngularDriveConstraint::bAccelerationMode); the physical-animation drive's mode is
/// not documented, so both are kept for an A/B against the recording.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum DriveMode {
    /// Torque = effective inertia x (stiffness x angle error - damping x velocity error).
    Acceleration,
    /// Torque = stiffness x angle error - damping x velocity error, with UE's kg cm^2 / s^2 converted to N m.
    Force,
    Off,
}

#[derive(Clone, Debug)]
pub struct Segment {
    pub mass: f32,
    pub mprops: MassProperties,
    /// The next joint's position in this bone's frame (m); zero for the hand.
    pub child_joint: Vector,
}

#[derive(Clone, Debug)]
pub struct ArmConfig {
    pub segments: Vec<Segment>,
    pub muscle: DriveMode,
    pub physical_animation: DriveMode,
    /// Body damping from the physics asset (willie_capsules.txt: angDamp 10, linDamp 0.01).
    pub angular_damping: f32,
    pub linear_damping: f32,
    /// Willie_BP "R Arm Extra Tonus": not in the recording; 1 unless measured.
    pub extra_tonus: f32,
    pub substep_hz: f32,
    /// A time gap larger than this (s) between two kept frames restarts the arm from the recorded bodies.
    pub max_frame_gap: f64,
}

const CM: f32 = 0.01;

impl ArmConfig {
    /// Shapes and joint offsets from `willie_capsules.txt`, masses from `masses.txt`.
    pub fn from_dumps(capsules: &str, masses: &str) -> Result<Self, Box<dyn Error>> {
        let bodies = parse_bodies(capsules);
        let offsets = parse_joint_offsets(capsules);
        let masses = parse_masses(masses);
        let mut segments = Vec::new();
        for (i, name) in SEGMENTS.iter().enumerate() {
            let body = bodies
                .iter()
                .find(|b| b.bone == *name)
                .ok_or_else(|| format!("no body for {name}"))?;
            let mass = *masses.get(*name).ok_or_else(|| format!("no mass for {name}"))?;
            let child_joint = match SEGMENTS.get(i + 1) {
                Some(child) => {
                    let o = offsets
                        .get(*child)
                        .ok_or_else(|| format!("no joint offset for {child}"))?;
                    Vector::new(o[0], o[1], o[2]) * CM
                }
                None => Vector::ZERO,
            };
            let mprops = match &body.kind {
                // The capsule axis is taken along the bone (towards the next joint); the asset's rotator is not
                // decoded yet. The hand's capsule fallback uses the bone's X axis.
                ShapeKind::Capsule { radius, length, center } => {
                    let c = Vector::new(center[0], center[1], center[2]) * CM;
                    let axis = if child_joint.length() > 0.0 {
                        child_joint.normalize()
                    } else {
                        Vector::X
                    };
                    let half = axis * (length * CM * 0.5);
                    let unit = MassProperties::from_capsule(1.0, c - half, c + half, radius * CM);
                    MassProperties::from_capsule(mass / unit.mass(), c - half, c + half, radius * CM)
                }
                ShapeKind::Box { size, center } => {
                    let half = Vector::new(size[0], size[1], size[2]) * (CM * 0.5);
                    let unit = MassProperties::from_cuboid(1.0, half);
                    MassProperties::from_cuboid(mass / unit.mass(), half).transform_by(&RPose::from_translation(
                        Vector::new(center[0], center[1], center[2]) * CM,
                    ))
                }
            };
            segments.push(Segment {
                mass,
                mprops,
                child_joint,
            });
        }
        Ok(ArmConfig {
            segments,
            muscle: DriveMode::Acceleration,
            physical_animation: DriveMode::Acceleration,
            angular_damping: 10.0,
            linear_damping: 0.01,
            extra_tonus: 1.0,
            substep_hz: 240.0,
            max_frame_gap: 0.2,
        })
    }
}

/// The shortest rotation (scaled axis, world) that turns `current` into `target`.
pub fn rotation_error(current: Rotation, target: Rotation) -> Vector {
    let mut d = target * current.inverse();
    if d.w < 0.0 {
        d = -d;
    }
    d.to_scaled_axis()
}

fn to_rpose(p: &Pose) -> RPose {
    RPose::from_parts(
        Vector::new(p.pos[0] as f32, p.pos[1] as f32, p.pos[2] as f32) * CM,
        Rotation::from_xyzw(p.rot[0] as f32, p.rot[1] as f32, p.rot[2] as f32, p.rot[3] as f32).normalize(),
    )
}

fn from_rpose(p: &RPose) -> Pose {
    let t = p.translation / CM;
    let r = p.rotation;
    Pose {
        pos: [t.x as f64, t.y as f64, t.z as f64],
        rot: [r.x as f64, r.y as f64, r.z as f64, r.w as f64],
    }
}

fn compose(a: &RPose, b: &RPose) -> RPose {
    RPose::from_parts(a.translation + a.rotation * b.translation, a.rotation * b.rotation)
}

fn lerp_pose(a: &RPose, b: &RPose, s: f32) -> RPose {
    RPose::from_parts(a.translation.lerp(b.translation, s), a.rotation.slerp(b.rotation, s))
}

/// World angular velocity that turns `a` into `b` in `dt` seconds.
fn angular_velocity(a: Rotation, b: Rotation, dt: f32) -> Vector {
    rotation_error(a, b) / dt
}

/// Per-frame drive strengths from the recorded Willie_BP scalars (missing scalars count as Half Sword's defaults of
/// a fresh, standing Willie: tonus 1, stamina 100, not fallen).
#[derive(Clone, Copy, Debug)]
struct Strengths {
    muscle_k: f32,
    pa_k: f32,
    pa_d: f32,
}

fn strengths(rec: &Recording, f: &Frame, extra_tonus: f32) -> Strengths {
    let s = |name: &str, default: f64| rec.scalar(f, name).filter(|v| v.is_finite()).unwrap_or(default) as f32;
    let map = |v: f32, in0: f32, in1: f32, out0: f32, out1: f32| {
        let t = ((v - in0) / (in1 - in0)).clamp(0.0, 1.0);
        out0 + (out1 - out0) * t
    };
    let arm = s("Arm_R_Tonus", 1.0);
    // MapRangeClamped(All Body Tonus, 25 -> 0, 100 -> 0) for the body mesh (UpperBody = false).
    let tonus_gate = map(s("All_Body_Tonus", 100.0), 25.0, 0.0, 100.0, 0.0);
    let stamina = map(s("Stamina", 100.0), 100.0, 0.0, 1.0, 0.5);
    let fallen = if s("Fallen", 0.0) > 0.5 {
        s("Get_Up_Rate", 0.0) * 0.5
    } else {
        1.0
    };
    let free_fall = 1.0 - s("Free_Fall_Rate", 0.0);
    let pa = arm * s("Upper_Body_Tonus", 1.0);
    Strengths {
        muscle_k: s("Muscle_Power", 35.0) * tonus_gate * arm * extra_tonus * stamina * fallen * free_fall,
        pa_k: pa * s("Orientation_Strength", 1000.0),
        pa_d: pa * s("Angular_Strength", 100.0),
    }
}

/// UE stiffness in force mode is kg cm^2 / s^2 per radian; rapier works in kg m^2 / s^2.
const FORCE_MODE_TO_SI: f32 = 1.0e-4;

struct World {
    physics: PhysicsWorld,
    root: RigidBodyHandle,
    bodies: Vec<RigidBodyHandle>,
}

impl World {
    fn new(cfg: &ArmConfig, root: &RPose, start: &[RPose]) -> Self {
        let mut physics = PhysicsWorld::new();
        physics.gravity = Vector::new(0.0, 0.0, -9.81); // UE is Z up
        let root_handle = physics.insert_body(RigidBodyBuilder::kinematic_position_based().pose(*root));
        let mut bodies = Vec::new();
        for (seg, pose) in cfg.segments.iter().zip(start) {
            let body = RigidBodyBuilder::dynamic()
                .pose(*pose)
                .additional_mass_properties(seg.mprops)
                .angular_damping(cfg.angular_damping)
                .linear_damping(cfg.linear_damping)
                .can_sleep(false);
            bodies.push(physics.insert_body(body));
        }
        let mut parent = (root_handle, Vector::ZERO);
        for (i, body) in bodies.iter().enumerate() {
            let joint = SphericalJointBuilder::new()
                .local_anchor1(parent.1)
                .local_anchor2(Vector::ZERO);
            physics.insert_impulse_joint(parent.0, *body, joint);
            parent = (*body, cfg.segments[i].child_joint);
        }
        World {
            physics,
            root: root_handle,
            bodies,
        }
    }
}

/// World inverse inertia applied to `v`.
fn inv_inertia_mul(mp: &MassProperties, rot: Rotation, v: Vector) -> Vector {
    let frame = rot * mp.principal_inertia_local_frame;
    frame * ((frame.inverse() * v) * mp.inv_principal_inertia)
}

/// World inertia applied to `v`.
fn inertia_mul(mp: &MassProperties, rot: Rotation, v: Vector) -> Vector {
    let frame = rot * mp.principal_inertia_local_frame;
    let inv = mp.inv_principal_inertia;
    let local = frame.inverse() * v;
    let div = |a: f32, b: f32| if b > 0.0 { a / b } else { 0.0 };
    frame * Vector::new(div(local.x, inv.x), div(local.y, inv.y), div(local.z, inv.z))
}

pub struct ArmSim;

impl ArmSim {
    /// Simulates the arm over the recording. Returns, per recorded frame, the three bodies' poses (UE cm), or None
    /// for the frames used to (re)start the arm.
    pub fn run(rec: &Recording, cfg: &ArmConfig) -> Result<Vec<Option<[Pose; 3]>>, Box<dyn Error>> {
        let idx = |name: &str| rec.bone(name).ok_or_else(|| format!("recording has no bone {name}"));
        let spine = idx("spine_05")?;
        let seg_idx: Vec<usize> = SEGMENTS.iter().map(|n| idx(n)).collect::<Result<_, _>>()?;
        let root_of = |f: &Frame| {
            let body_chest = to_rpose(&f.body[spine]);
            let drv_chest = to_rpose(&f.driver[spine]);
            compose(
                &compose(&body_chest, &drv_chest.inverse()),
                &to_rpose(&f.driver[seg_idx[0]]),
            )
        };
        let drivers = |f: &Frame| -> Vec<RPose> { seg_idx.iter().map(|&b| to_rpose(&f.driver[b])).collect() };

        let mut out: Vec<Option<[Pose; 3]>> = vec![None; rec.frames.len()];
        let mut world: Option<World> = None;
        for (n, f) in rec.frames.iter().enumerate() {
            let restart = n == 0 || f.t - rec.frames[n - 1].t > cfg.max_frame_gap || world.is_none();
            if restart {
                let start: Vec<RPose> = seg_idx.iter().map(|&b| to_rpose(&f.body[b])).collect();
                world = Some(World::new(cfg, &root_of(f), &start));
                continue;
            }
            let prev = &rec.frames[n - 1];
            let w = world.as_mut().expect("world exists after a restart");
            let dt_frame = (f.t - prev.t).max(1e-4) as f32;
            let substeps = (dt_frame * cfg.substep_hz).ceil().max(1.0) as usize;
            let dt = dt_frame / substeps as f32;
            w.physics.integration_parameters.dt = dt;
            let (root0, root1) = (root_of(prev), root_of(f));
            let (drv0, drv1) = (drivers(prev), drivers(f));
            let (st0, st1) = (
                strengths(rec, prev, cfg.extra_tonus),
                strengths(rec, f, cfg.extra_tonus),
            );
            for s in 1..=substeps {
                let a = s as f32 / substeps as f32;
                let root = lerp_pose(&root0, &root1, a);
                let drv: Vec<RPose> = drv0.iter().zip(&drv1).map(|(p, q)| lerp_pose(p, q, a)).collect();
                let st = Strengths {
                    muscle_k: st0.muscle_k + (st1.muscle_k - st0.muscle_k) * a,
                    pa_k: st0.pa_k + (st1.pa_k - st0.pa_k) * a,
                    pa_d: st0.pa_d + (st1.pa_d - st0.pa_d) * a,
                };
                w.physics.bodies[w.root].set_next_kinematic_position(root);
                apply_drives(w, cfg, &root, &drv, &drv0, &drv1, dt_frame, st);
                w.physics.step();
            }
            let mut poses = [Pose {
                pos: [0.0; 3],
                rot: [0.0, 0.0, 0.0, 1.0],
            }; 3];
            for (slot, h) in poses.iter_mut().zip(&w.bodies) {
                *slot = from_rpose(w.physics.bodies[*h].position());
            }
            out[n] = Some(poses);
        }
        Ok(out)
    }
}

#[allow(clippy::too_many_arguments)]
fn apply_drives(
    w: &mut World,
    cfg: &ArmConfig,
    root: &RPose,
    drv: &[RPose],
    drv0: &[RPose],
    drv1: &[RPose],
    dt_frame: f32,
    st: Strengths,
) {
    let n = w.bodies.len();
    let rot: Vec<Rotation> = w.bodies.iter().map(|h| *w.physics.bodies[*h].rotation()).collect();
    let angvel: Vec<Vector> = w.bodies.iter().map(|h| w.physics.bodies[*h].angvel()).collect();
    let mut torque = vec![Vector::ZERO; n];

    // Muscle Motor: child rotation in its parent's space toward the driver's (the shoulder's parent is the root,
    // whose rotation already is the driver upper arm's, so its target is the identity).
    if cfg.muscle != DriveMode::Off {
        for i in 0..n {
            let (parent_rot, target_rel) = if i == 0 {
                (root.rotation, Rotation::IDENTITY)
            } else {
                (rot[i - 1], drv[i - 1].rotation.inverse() * drv[i].rotation)
            };
            let current_rel = parent_rot.inverse() * rot[i];
            let err = parent_rot * rotation_error(current_rel, target_rel);
            let len = err.length();
            if len < 1e-9 {
                continue;
            }
            let axis = err / len;
            let tau = match cfg.muscle {
                DriveMode::Acceleration => {
                    let mut inv = axis.dot(inv_inertia_mul(&cfg.segments[i].mprops, rot[i], axis));
                    if i > 0 {
                        inv += axis.dot(inv_inertia_mul(&cfg.segments[i - 1].mprops, rot[i - 1], axis));
                    }
                    err * (st.muscle_k / inv.max(1e-9))
                }
                DriveMode::Force => err * (st.muscle_k * FORCE_MODE_TO_SI),
                DriveMode::Off => Vector::ZERO,
            };
            torque[i] += tau;
            if i > 0 {
                torque[i - 1] -= tau;
            }
        }
    }

    // Physical animation: each body's world rotation toward the driver bone's, velocity toward the driver's.
    if cfg.physical_animation != DriveMode::Off {
        for i in 0..n {
            let err = rotation_error(rot[i], drv[i].rotation);
            let target_vel = angular_velocity(drv0[i].rotation, drv1[i].rotation, dt_frame);
            let accel = err * st.pa_k - (angvel[i] - target_vel) * st.pa_d;
            torque[i] += match cfg.physical_animation {
                DriveMode::Acceleration => inertia_mul(&cfg.segments[i].mprops, rot[i], accel),
                DriveMode::Force => accel * FORCE_MODE_TO_SI,
                DriveMode::Off => Vector::ZERO,
            };
        }
    }

    for (h, t) in w.bodies.iter().zip(torque) {
        let body = &mut w.physics.bodies[*h];
        body.reset_torques(true);
        body.add_torque(t, true);
    }
}
