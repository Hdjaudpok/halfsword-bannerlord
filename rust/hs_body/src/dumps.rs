//! Reads the arm's physics values from the user's own UE4SS dumps, at runtime (nothing is stored in this crate):
//! `dumps/pass2/masses.txt` (bone, tab, kg from GetBoneMass) and `dumps/pass1/willie_capsules.txt` (the live physics
//! asset: body shapes in bone space and constraint frames, UE cm).
use std::collections::HashMap;

#[derive(Clone, Debug, PartialEq)]
pub enum ShapeKind {
    /// UE sphyl: radius, length of the cylinder part, centre in bone space (cm).
    Capsule { radius: f32, length: f32, center: [f32; 3] },
    /// UE box: full size along X, Y, Z and centre in bone space (cm).
    Box { size: [f32; 3], center: [f32; 3] },
}

#[derive(Clone, Debug, PartialEq)]
pub struct BodyShape {
    pub bone: String,
    pub kind: ShapeKind,
}

/// `bone<TAB>kg` lines; other lines are ignored.
pub fn parse_masses(text: &str) -> HashMap<String, f32> {
    text.lines()
        .filter_map(|line| {
            let (bone, kg) = line.trim().split_once('\t')?;
            Some((bone.to_owned(), kg.trim().parse().ok()?))
        })
        .collect()
}

/// The first `body[i] bone=NAME ... sphyl[...]` or `box[...]` line of each bone.
pub fn parse_bodies(text: &str) -> Vec<BodyShape> {
    let mut out: Vec<BodyShape> = Vec::new();
    for line in text.lines().map(str::trim).filter(|l| l.starts_with("body[")) {
        let Some(bone) = value(line, "bone=") else { continue };
        if out.iter().any(|b| b.bone == bone) {
            continue;
        }
        let kind = if let Some(inner) = bracket(line, "sphyl[") {
            ShapeKind::Capsule {
                radius: number(inner, "r="),
                length: number(inner, "len="),
                center: vec3(inner, "c="),
            }
        } else if let Some(inner) = bracket(line, "box[") {
            ShapeKind::Box {
                size: [number(inner, "x="), number(inner, "y="), number(inner, "z=")],
                center: vec3(inner, "c="),
            }
        } else {
            continue;
        };
        out.push(BodyShape {
            bone: bone.to_owned(),
            kind,
        });
    }
    out
}

/// For each anatomical constraint (named after its child bone, `joint == bone1`), the child joint's position in the
/// parent's bone space: `pos2` (cm). The first occurrence wins.
pub fn parse_joint_offsets(text: &str) -> HashMap<String, [f32; 3]> {
    let mut out = HashMap::new();
    for line in text.lines().map(str::trim).filter(|l| l.starts_with("constraint[")) {
        let (Some(joint), Some(bone1)) = (value(line, "joint="), value(line, "bone1=")) else {
            continue;
        };
        if joint == bone1 && !out.contains_key(joint) {
            out.insert(joint.to_owned(), vec3(line, "pos2="));
        }
    }
    out
}

/// The token after ` key` (or at the start), up to the next space or `]`.
fn value<'a>(text: &'a str, key: &str) -> Option<&'a str> {
    let start = text
        .match_indices(key)
        .find(|(i, _)| *i == 0 || matches!(text.as_bytes()[i - 1], b' ' | b'['))?
        .0
        + key.len();
    let rest = &text[start..];
    Some(&rest[..rest.find([' ', ']']).unwrap_or(rest.len())])
}

fn bracket<'a>(text: &'a str, open: &str) -> Option<&'a str> {
    let start = text.find(open)? + open.len();
    let end = text[start..].find(']')? + start;
    Some(&text[start..end])
}

fn number(text: &str, key: &str) -> f32 {
    value(text, key).and_then(|v| v.parse().ok()).unwrap_or(f32::NAN)
}

fn vec3(text: &str, key: &str) -> [f32; 3] {
    let parsed = value(text, key).and_then(|v| {
        let mut it = v
            .trim_start_matches('(')
            .trim_end_matches(')')
            .split(',')
            .map(|n| n.parse::<f32>());
        Some([it.next()?.ok()?, it.next()?.ok()?, it.next()?.ok()?])
    });
    parsed.unwrap_or([f32::NAN; 3])
}
