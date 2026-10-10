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
    /// Rows whose field count differs from the header's (recorder glitches), left out of `frames`.
    pub skipped_rows: usize,
}

impl Recording {
    pub fn load(path: &Path) -> Result<Self, Box<dyn Error>> {
        Self::from_reader(std::fs::File::open(path)?)
    }

    pub fn from_reader<R: std::io::Read>(reader: R) -> Result<Self, Box<dyn Error>> {
        let mut csv = csv::ReaderBuilder::new()
            .has_headers(true)
            .flexible(true)
            .from_reader(reader);
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
                *slot = *index
                    .get(name.as_str())
                    .ok_or_else(|| format!("missing column {name}"))?;
            }
            Ok(cols)
        };
        let body_cols = bones
            .iter()
            .map(|b| pose_columns("body", b))
            .collect::<Result<Vec<_>, _>>()?;
        let driver_cols = bones
            .iter()
            .map(|b| pose_columns("driver", b))
            .collect::<Result<Vec<_>, _>>()?;
        let t_col = *index.get("t").ok_or("missing column t")?;
        let scalar_cols: Vec<usize> = (0..header.len())
            .filter(|&i| i != t_col && !["body_", "driver_", "scene_"].iter().any(|p| header[i].starts_with(p)))
            .collect();
        let scalar_names = scalar_cols.iter().map(|&i| header[i].clone()).collect();
        let mut frames = Vec::new();
        let mut skipped_rows = 0;
        for record in csv.records() {
            let record = record?;
            if record.len() != header.len() {
                skipped_rows += 1;
                continue;
            }
            let num = |i: usize| -> Result<f64, Box<dyn Error>> {
                let field = &record[i];
                Ok(field
                    .trim()
                    .parse::<f64>()
                    .map_err(|e| format!("column {}: {field:?}: {e}", header[i]))?)
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
        Ok(Recording {
            bones,
            scalar_names,
            frames,
            skipped_rows,
        })
    }

    pub fn bone(&self, name: &str) -> Option<usize> {
        self.bones.iter().position(|b| b == name)
    }

    /// A Willie_BP scalar of one frame: numbers as written, booleans as 1 / 0, vectors (not parsed) as NaN.
    pub fn scalar(&self, frame: &Frame, name: &str) -> Option<f64> {
        self.scalar_names
            .iter()
            .position(|n| n == name)
            .map(|i| frame.scalars[i])
    }
}

fn scalar(field: &str) -> f64 {
    match field.trim() {
        "true" => 1.0,
        "false" => 0.0,
        other => other.parse().unwrap_or(f64::NAN),
    }
}
