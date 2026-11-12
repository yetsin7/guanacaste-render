//! Device fields needed to render the original electrical catalog.

use super::Id;
use crate::plan::Point2;
use serde::Deserialize;

/// A device's placement, switch state, and controlled elements.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ElectricalDevice {
    #[serde(default)]
    pub id: Id,
    #[serde(default)]
    pub level_id: Id,
    #[serde(default = "unknown_kind")]
    pub kind: i32,
    #[serde(default)]
    pub position: Point2,
    #[serde(default)]
    pub elevation: f64,
    #[serde(default)]
    pub facing: f64,
    #[serde(default = "default_on")]
    pub is_on: bool,
    #[serde(default)]
    pub controls: Vec<Id>,
    #[serde(default)]
    pub circuit_id: Option<Id>,
    #[serde(default)]
    pub outdoor_position: Option<Point2>,
    #[serde(default)]
    pub panel_count: i32,
    #[serde(default)]
    pub tilt: f64,
}

impl ElectricalDevice {
    /// Identifies catalog lights whose output depends on their electrical supply and controls.
    pub fn is_luminaire(&self) -> bool {
        matches!(
            self.kind,
            electrical_device_kind::CEILING_FIXTURE
                | electrical_device_kind::DOWNLIGHT
                | electrical_device_kind::WALL_SCONCE
                | electrical_device_kind::PENDANT
                | electrical_device_kind::CEILING_FAN
        )
    }
}

/// Treats missing kinds as unsupported instead of inventing a device.
fn unknown_kind() -> i32 {
    -1
}

/// Preserves the source application's default switch position.
fn default_on() -> bool {
    true
}

/// Catalog values serialized by the source application.
pub mod electrical_device_kind {
    pub const OUTLET: i32 = 0;
    pub const GFCI_OUTLET: i32 = 1;
    pub const USB_OUTLET: i32 = 2;
    pub const OUTLET_240: i32 = 3;
    pub const SWITCH: i32 = 4;
    pub const THREE_WAY_SWITCH: i32 = 5;
    pub const DIMMER: i32 = 6;
    pub const CEILING_FIXTURE: i32 = 7;
    pub const DOWNLIGHT: i32 = 8;
    pub const WALL_SCONCE: i32 = 9;
    pub const PENDANT: i32 = 10;
    pub const PANEL: i32 = 11;
    pub const CEILING_FAN: i32 = 12;
    pub const MINI_SPLIT: i32 = 13;
    pub const WATER_PUMP: i32 = 14;
    pub const SOLAR_ARRAY: i32 = 15;
    pub const INVERTER: i32 = 16;
    pub const SMOKE_DETECTOR: i32 = 17;
    pub const EMERGENCY_LIGHT: i32 = 18;
    pub const EXIT_SIGN: i32 = 19;
    pub const FIRE_ALARM_PULL_STATION: i32 = 20;
    pub const HORN_STROBE: i32 = 21;
    pub const SPRINKLER: i32 = 22;
    pub const FIRE_EXTINGUISHER: i32 = 23;
    pub const FIRE_HOSE_CABINET: i32 = 24;
}
