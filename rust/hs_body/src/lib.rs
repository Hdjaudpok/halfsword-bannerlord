//! Headless re-implementation of Half Sword's active-ragdoll arm (trial D, docs/ROADMAP-4-TRIALS.md).
//! No Half Sword data lives in this crate: recordings and physics values are read at runtime from the user's dumps.
pub mod arm;
pub mod dumps;
pub mod motion;
pub mod oracle;
