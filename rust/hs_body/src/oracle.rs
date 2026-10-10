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
    Some(Spread {
        n: values.len(),
        median: percentile(values, 50.0)?,
        p95: percentile(values, 95.0)?,
    })
}
