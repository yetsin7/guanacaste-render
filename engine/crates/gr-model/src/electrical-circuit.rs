//! Circuit fields used by the source application's live electrical simulation.

use super::Id;
use serde::Deserialize;

/// A branch circuit attached to a panel and protected by a breaker.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ElectricalCircuit {
    pub id: Id,
    pub panel_id: Id,
    #[serde(default)]
    pub number: i32,
    #[serde(default = "default_breaker_amps")]
    pub breaker_amps: f64,
    #[serde(default = "default_voltage")]
    pub voltage: f64,
    #[serde(default = "default_breaker_on")]
    pub breaker_on: bool,
}

/// Uses the source model's default branch-breaker rating.
fn default_breaker_amps() -> f64 {
    15.0
}

/// Uses the source model's default branch voltage.
fn default_voltage() -> f64 {
    120.0
}

/// New circuit breakers are closed by default.
fn default_breaker_on() -> bool {
    true
}
