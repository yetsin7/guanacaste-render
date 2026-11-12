//! Floor and roof records used by the building mesher.

use super::Id;
use crate::plan::Point2;
use serde::Deserialize;

/// A surveyed plan coordinate and its elevation relative to the site level.
#[derive(Clone, Copy, Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct TopographyPoint {
    pub x: f64,
    pub y: f64,
    pub elevation: f64,
}

/// `FloorKind`, `RoofShape` and `FinishKind`.
pub mod floor {
    pub const KIND_FLOOR: i32 = 0;
    pub const KIND_ROOF: i32 = 2;
    pub const KIND_WATER: i32 = 3;
    pub const KIND_TOPOSOLID: i32 = 4;
    pub const SHAPE_FLAT: i32 = 0;
    pub const SHAPE_SHED: i32 = 1;
    pub const SHAPE_GABLE: i32 = 2;
    pub const SHAPE_HIP: i32 = 3;
    pub const FINISH_PAINT: i32 = 1;
    pub const FINISH_EXPOSED_CONCRETE: i32 = 4;
    pub const FINISH_ASPHALT: i32 = 5;
    pub const FINISH_GRASS: i32 = 6;
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct FloorSlab {
    pub id: Id,
    pub level_id: Id,
    pub boundary: Vec<Point2>,
    pub thickness: f64,
    #[serde(default)]
    pub shape: i32,
    #[serde(default)]
    pub pitch: f64,
    #[serde(default)]
    pub finish: i32,
    #[serde(default)]
    pub kind: i32,
    #[serde(default)]
    pub elevation_offset: f64,
    #[serde(default)]
    pub overhang: f64,
    /// Whether the original roof includes walls beneath its rising edges.
    #[serde(default = "default_gable_walls")]
    pub gable_walls: bool,
    /// Survey points constraining a toposolid's surface; absent on ordinary slabs.
    #[serde(default)]
    pub topo_points: Option<Vec<TopographyPoint>>,
}

/// Matches the source model's default when older files omit the gable-wall setting.
fn default_gable_walls() -> bool {
    true
}

impl FloorSlab {
    /// Identifies site terrain, which uses a separate surface representation.
    pub fn is_toposolid(&self) -> bool {
        self.kind == floor::KIND_TOPOSOLID
    }

    /// A roof that rises above its eave.
    pub fn is_pitched(&self) -> bool {
        self.kind == floor::KIND_ROOF && self.shape != floor::SHAPE_FLAT && self.pitch > 0.0
    }
}
