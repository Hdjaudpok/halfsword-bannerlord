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
fn rows_with_a_different_field_count_are_skipped_and_counted() {
    // HSDump wrote 257 rows of the 2026-10-02 fight with "(TrivialObject: ...)" vectors instead of numbers, which
    // adds fields: those rows cannot be aligned with the header.
    let glitch = "0.020,\"(TrivialObject: 1,TrivialObject: 1)\",x,y,\"false\",0,0,0,0,0,0,1,3,4,0,0,0,0,1,7\n";
    let text = format!("{MINI}{glitch}");
    let rec = Recording::from_reader(text.as_bytes()).unwrap();
    assert_eq!(rec.frames.len(), 2);
    assert_eq!(rec.skipped_rows, 1);
}

#[test]
fn gaps_and_percentiles() {
    let rec = Recording::from_reader(MINI.as_bytes()).unwrap();
    let gaps: Vec<f64> = rec.frames.iter().map(|f| distance(&f.body[0], &f.driver[0])).collect();
    assert_eq!(gaps, vec![5.0, 12.0]);
    assert_eq!(percentile(&gaps, 50.0), Some(5.0));
    assert_eq!(
        spread(&gaps),
        Some(Spread {
            n: 2,
            median: 5.0,
            p95: 12.0
        })
    );
    assert_eq!(percentile(&[], 50.0), None);
}
