//! Plumbing service-point records needed by the native mesher.

use super::Id;
use crate::plan::Point2;
use serde::Deserialize;

/// A service point's location and catalog kind; routed systems are represented separately.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct PlumbingPoint {
    pub id: Id,
    pub level_id: Id,
    pub kind: i32,
    pub position: Point2,
    /// Sewer connection depth below its level in metres.
    #[serde(default = "default_invert_depth")]
    pub invert_depth: f64,
    /// Design rainfall intensity in millimetres per hour for storm-water sizing.
    #[serde(default = "default_rainfall")]
    pub rainfall_mm_per_hour: f64,
    /// Explicit tank capacity in litres; zero requests the application-defined household default.
    #[serde(default)]
    pub capacity_liters: f64,
    /// Elevation of an elevated tank above its level.
    #[serde(default = "default_tank_height")]
    pub tank_height: f64,
    /// Explicit occupants served by the point; zero uses the project's bedroom count.
    #[serde(default)]
    pub occupants: i32,
    /// Soil absorption in litres per square metre per day for septic design.
    #[serde(default = "default_percolation")]
    pub percolation: f64,
}

/// Matches the original model's default sewer invert depth.
fn default_invert_depth() -> f64 {
    0.6
}

/// Matches the original model's default storm rainfall intensity.
fn default_rainfall() -> f64 {
    150.0
}

/// Matches the original model's default elevated-tank support height.
fn default_tank_height() -> f64 {
    6.0
}

/// Matches the original model's default soil absorption rate.
fn default_percolation() -> f64 {
    40.0
}

/// A routed system connects a source to the identifiers of its served fixtures.
#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct PlumbingSystem {
    pub kind: i32,
    pub source_id: Id,
    pub fixtures: Vec<Id>,
}

/// Plumbing system identifiers stored by the original application.
pub mod plumbing_system_kind {
    pub const COLD_WATER: i32 = 0;
    pub const HOT_WATER: i32 = 1;
    pub const SANITARY: i32 = 2;
    pub const STORM_WATER: i32 = 3;
}

/// Plumbing point identifiers stored by the original application.
pub mod plumbing_point_kind {
    pub const WATER_METER: i32 = 0;
    pub const SEWER_OUTLET: i32 = 1;
    pub const VENT_STACK: i32 = 2;
    pub const SEPTIC_TANK: i32 = 3;
    pub const STORM_OUTLET: i32 = 4;
    pub const CISTERN: i32 = 5;
    pub const ELEVATED_TANK: i32 = 6;
}
