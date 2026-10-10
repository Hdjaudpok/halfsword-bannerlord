use hs_body::arm::{rotation_error, ArmConfig, ArmSim, DriveMode};
use hs_body::dumps::{parse_bodies, parse_joint_offsets, parse_masses, ShapeKind};
use hs_body::motion::Recording;
use rapier3d::math::{Rotation, Vector};

// Synthetic dump lines in the UE4SS dump format (values invented for the test, not Half Sword's).
const BODIES: &str = "  body[1] bone=upperarm_r physicsType=1 sphyl[r=4.0000 len=20.0000 c=(-15.0000,0.0000,0.0000) rot=(0.0000,0.0000,0.0000)] massKg=100.0000
  body[2] bone=lowerarm_r physicsType=1 sphyl[r=3.0000 len=20.0000 c=(-14.0000,0.0000,0.0000) rot=(0.0000,0.0000,0.0000)] massKg=1.5000
  body[3] bone=hand_r physicsType=1 box[x=10.0000 y=6.0000 z=4.0000 c=(-5.0000,0.0000,0.0000) rot=(0.0000,0.0000,0.0000)] massKg=1.0000
  constraint[1] joint=lowerarm_r bone1=lowerarm_r bone2=upperarm_r pos1=(0.0000,0.0000,0.0000) pos2=(-30.0000,0.0000,0.0000) swing1=0/75.0000
  constraint[2] joint=hand_r bone1=hand_r bone2=lowerarm_r pos1=(0.0000,0.0000,0.0000) pos2=(-28.0000,0.0000,0.0000) swing1=0/75.0000
  constraint[3] joint=UserConstraint_11 bone1=lowerarm_r bone2=upperarm_r pos1=(0.0000,0.0000,0.0000) pos2=(-99.0000,0.0000,0.0000)
";
const MASSES: &str = "upperarm_r\t2.0000\nlowerarm_r\t1.5000\nhand_r\t0.8000\nTOTAL\t4.3000\n";

#[test]
fn parses_shapes_masses_and_joint_offsets() {
    let bodies = parse_bodies(BODIES);
    assert_eq!(bodies.len(), 3);
    assert_eq!(bodies[0].bone, "upperarm_r");
    match &bodies[0].kind {
        ShapeKind::Capsule { radius, length, center } => {
            assert_eq!((*radius, *length, *center), (4.0, 20.0, [-15.0, 0.0, 0.0]));
        }
        other => panic!("{other:?}"),
    }
    match &bodies[2].kind {
        ShapeKind::Box { size, center } => assert_eq!((*size, *center), ([10.0, 6.0, 4.0], [-5.0, 0.0, 0.0])),
        other => panic!("{other:?}"),
    }
    let masses = parse_masses(MASSES);
    assert_eq!(masses.get("lowerarm_r"), Some(&1.5));
    let offsets = parse_joint_offsets(BODIES);
    // The anatomical joint named after the child wins; UserConstraint duplicates are ignored.
    assert_eq!(offsets.get("lowerarm_r"), Some(&[-30.0, 0.0, 0.0]));
    assert_eq!(offsets.get("hand_r"), Some(&[-28.0, 0.0, 0.0]));
}

#[test]
fn rotation_error_is_the_shortest_world_rotation() {
    let current = Rotation::IDENTITY;
    let target = Rotation::from_rotation_z(std::f32::consts::FRAC_PI_2);
    let e = rotation_error(current, target);
    assert!(
        (e - Vector::new(0.0, 0.0, std::f32::consts::FRAC_PI_2)).length() < 1e-5,
        "{e:?}"
    );
    // The same target given with a negated quaternion must give the same short rotation.
    let e2 = rotation_error(current, -target);
    assert!((e2 - e).length() < 1e-5, "{e2:?}");
}

/// Bone name, body position and rotation, driver position and rotation (UE cm, quaternion x y z w).
type BonePoses = (&'static str, [f64; 3], [f64; 4], [f64; 3], [f64; 4]);

/// A recording whose driver holds the arm still while the recorded bodies start 30 degrees off: the drives must pull
/// the simulated hand toward the driver.
fn still_driver_recording() -> String {
    let mut text = String::from("t,Arm_R_Tonus,Upper_Body_Tonus,Orientation_Strength,Angular_Strength,Muscle_Power,All_Body_Tonus,Stamina,Fallen,Get_Up_Rate,Free_Fall_Rate");
    for bone in ["spine_05", "upperarm_r", "lowerarm_r", "hand_r"] {
        for kind in ["body", "driver"] {
            for c in ["x", "y", "z", "qx", "qy", "qz", "qw"] {
                text.push_str(&format!(",{kind}_{bone}_{c}"));
            }
        }
    }
    text.push('\n');
    // Driver: arm straight along -X from a shoulder at (0, 20, 140) cm. Bodies: the upper arm turned 30 deg about Z.
    let (s, c) = (15f64.to_radians().sin(), 15f64.to_radians().cos());
    for i in 0..60 {
        let t = i as f64 / 30.0;
        let mut row = format!("{t:.4},1,1,1000,100,35,100,100,false,0,0");
        let poses: [BonePoses; 4] = [
            (
                "spine_05",
                [0.0, 0.0, 140.0],
                [0.0, 0.0, 0.0, 1.0],
                [0.0, 0.0, 140.0],
                [0.0, 0.0, 0.0, 1.0],
            ),
            (
                "upperarm_r",
                [0.0, 20.0, 140.0],
                [0.0, 0.0, s, c],
                [0.0, 20.0, 140.0],
                [0.0, 0.0, 0.0, 1.0],
            ),
            (
                "lowerarm_r",
                [-30.0 * (1.0 - 2.0 * s * s), 20.0 - 30.0 * 2.0 * s * c, 140.0],
                [0.0, 0.0, s, c],
                [-30.0, 20.0, 140.0],
                [0.0, 0.0, 0.0, 1.0],
            ),
            (
                "hand_r",
                [-58.0 * (1.0 - 2.0 * s * s), 20.0 - 58.0 * 2.0 * s * c, 140.0],
                [0.0, 0.0, s, c],
                [-58.0, 20.0, 140.0],
                [0.0, 0.0, 0.0, 1.0],
            ),
        ];
        for (_, bp, bq, dp, dq) in poses {
            for v in bp.iter().chain(bq.iter()).chain(dp.iter()).chain(dq.iter()) {
                row.push_str(&format!(",{v}"));
            }
        }
        text.push_str(&row);
        text.push('\n');
    }
    text
}

fn final_hand_gap(rec: &Recording, cfg: &ArmConfig) -> f64 {
    let hand = rec.bone("hand_r").unwrap();
    let poses = ArmSim::run(rec, cfg).unwrap();
    let last = poses.last().unwrap().as_ref().unwrap();
    hs_body::oracle::distance(&last[2], &rec.frames.last().unwrap().driver[hand])
}

#[test]
fn drives_pull_the_hand_toward_a_still_driver() {
    let rec = Recording::from_reader(still_driver_recording().as_bytes()).unwrap();
    let hand = rec.bone("hand_r").unwrap();
    let start = hs_body::oracle::distance(&rec.frames[0].body[hand], &rec.frames[0].driver[hand]);
    assert!(start > 25.0, "start gap {start}");
    let cfg = ArmConfig::from_dumps(BODIES, MASSES).unwrap();
    let driven = final_hand_gap(&rec, &cfg);
    let mut limp = cfg.clone();
    limp.muscle = DriveMode::Off;
    limp.physical_animation = DriveMode::Off;
    let hanging = final_hand_gap(&rec, &limp);
    // Without drives the arm hangs under gravity; with them it must end nearer the driver than it started.
    assert!(driven < start, "driven gap {start:.1} -> {driven:.1} cm");
    assert!(driven < hanging, "driven {driven:.1} cm vs no drives {hanging:.1} cm");
}
