//! The parts of a project the mesher reads, as stored in the JSON of schema 15.
//!
//! Field names and enumeration numbers follow the C# model (`BuildingDocument` and the records in
//! `bim-elements.cs`, `WallOpenings.cs`, `families/opening-types.cs`). Fields the mesher does not use are not
//! declared; `serde` skips them. Enumerations are stored as their numbers, so they are constants here rather than
//! Rust enums: an unknown number from a newer application must not make the whole file unreadable.

#[path = "component-shapes.rs"]
pub mod component_shape;
#[path = "floor-slab.rs"]
mod floor_slab;
#[path = "plumbing-point.rs"]
mod plumbing_point;
#[path = "structural-element.rs"]
mod structural_element;

pub use floor_slab::{FloorSlab, TopographyPoint, floor};
pub use plumbing_point::{PlumbingPoint, PlumbingSystem, plumbing_point_kind, plumbing_system_kind};
pub use structural_element::{StructuralElement, element_kind, element_style};

use crate::container::{Container, ContainerError, DocumentKind};
use crate::plan::Point2;
use serde::Deserialize;
use std::collections::HashMap;
use std::fmt;

/// The only schema this reader understands. Older files need the migrations of the C# application.
pub const SCHEMA_VERSION: i32 = 15;

/// Identifier of an element: a GUID kept as the 128-bit number its text spells.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, Default)]
pub struct Id(pub u128);

impl Id {
    /// Parses the canonical `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` form.
    /// Reads and verifies a native project already held in memory.
    pub fn parse(text: &str) -> Option<Id> {
        let bytes = text.as_bytes();
        if bytes.len() != 36 || [8, 13, 18, 23].iter().any(|&at| bytes[at] != b'-') {
            return None;
        }
        let mut value = 0u128;
        for (at, &byte) in bytes.iter().enumerate() {
            if [8, 13, 18, 23].contains(&at) {
                continue;
            }
            value = value << 4 | (byte as char).to_digit(16)? as u128;
        }
        Some(Id(value))
    }
}

impl fmt::Display for Id {
    /// Formats this value using its canonical representation.
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let v = self.0;
        write!(
            f,
            "{:08x}-{:04x}-{:04x}-{:04x}-{:012x}",
            (v >> 96) as u32,
            (v >> 80) as u16,
            (v >> 64) as u16,
            (v >> 48) as u16,
            v & 0xffff_ffff_ffff
        )
    }
}

impl<'de> Deserialize<'de> for Id {
    /// Rejects malformed identifiers while reading project JSON.
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Id, D::Error> {
        let text = <&str>::deserialize(deserializer)?;
        Id::parse(text).ok_or_else(|| serde::de::Error::custom("an element identifier is not a GUID"))
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Level {
    pub id: Id,
    pub elevation: f64,
    #[serde(default)]
    pub role: i32,
}

/// `WallJoinSetting`: how one end of a wall treats the wall it meets.
pub mod join_setting {
    pub const MITER: i32 = 0;
    pub const BUTT: i32 = 1;
    pub const SQUARE_OFF: i32 = 2;
    pub const DISALLOWED: i32 = 3;
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Wall {
    pub id: Id,
    pub level_id: Id,
    pub start: Point2,
    pub end: Point2,
    pub thickness: f64,
    pub height: f64,
    pub wall_type_id: Id,
    #[serde(default)]
    pub base_offset: f64,
    #[serde(default)]
    pub start_join: i32,
    #[serde(default)]
    pub end_join: i32,
    /// Points of (distance along the wall, height); `None` leaves the top flat.
    #[serde(default)]
    pub top_profile: Option<Vec<Point2>>,
}

impl Wall {
    /// Measures the wall centerline in plan coordinates.
    pub fn length(&self) -> f64 {
        self.start.distance_to(self.end)
    }

    /// Unit vector from the start towards the end.
    pub fn direction(&self) -> (f64, f64) {
        let length = self.length();
        ((self.end.x - self.start.x) / length, (self.end.y - self.start.y) / length)
    }

    /// Unit normal on the left of the start-to-end direction.
    pub fn left_normal(&self) -> (f64, f64) {
        let (x, y) = self.direction();
        (-y, x)
    }

    /// The profile when it has at least two points, which is when it shapes the top.
    pub fn shaping_profile(&self) -> Option<&[Point2]> {
        self.top_profile.as_deref().filter(|profile| profile.len() > 1)
    }

    /// Height of the top at a distance along the wall, following its profile and never above its height.
    pub fn top_height_at(&self, along: f64) -> f64 {
        let Some(profile) = self.shaping_profile() else { return self.height };
        let on_profile = if along <= profile[0].x {
            profile[0].y
        } else {
            profile
                .windows(2)
                .find(|pair| along <= pair[1].x)
                .map(|pair| pair[0].y + (pair[1].y - pair[0].y) * (along - pair[0].x) / (pair[1].x - pair[0].x))
                .unwrap_or(profile[profile.len() - 1].y)
        };
        self.height.min(on_profile)
    }
}

/// `OpeningKind`.
pub mod opening_kind {
    pub const DOOR: i32 = 0;
    pub const WINDOW: i32 = 1;
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct WallOpening {
    pub id: Id,
    pub wall_id: Id,
    pub kind: i32,
    /// Metres from the wall start to the centre of the opening.
    pub offset: f64,
    pub width: f64,
    pub sill: f64,
    pub height: f64,
    pub opening_type_id: Id,
    #[serde(default)]
    pub flip_hand: bool,
}

impl WallOpening {
    /// Returns the opening station nearest the wall start.
    pub fn begin(&self) -> f64 {
        self.offset - self.width / 2.0
    }

    /// Returns the opening station farthest from the wall start.
    pub fn end(&self) -> f64 {
        self.offset + self.width / 2.0
    }
}

/// `WallFunction` and `WallConstruction`.
pub mod wall {
    pub const FUNCTION_INTERIOR: i32 = 0;
    pub const PLASTER: i32 = 0;
    pub const GLASS: i32 = 2;
    pub const BRICK: i32 = 3;
    pub const BLOCK: i32 = 4;
    pub const CONCRETE: i32 = 5;
    pub const STONE: i32 = 6;
    pub const WOOD: i32 = 7;
    pub const METAL: i32 = 8;
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct WallType {
    pub id: Id,
    pub function: i32,
    #[serde(default)]
    pub construction: i32,
}

/// `OpeningOperation`.
pub mod operation {
    pub const STANDARD: i32 = 0;
    pub const SLIDING: i32 = 1;
    pub const FOLDING: i32 = 2;
    pub const POCKET: i32 = 3;
    pub const OVERHEAD: i32 = 4;
    pub const REVOLVING: i32 = 5;
    pub const CASEMENT: i32 = 6;
    pub const AWNING: i32 = 7;
    pub const HUNG: i32 = 8;
    pub const LOUVERED: i32 = 9;
    pub const VOID: i32 = 10;
}

/// `DoorLeafStyle`.
pub mod leaf_style {
    pub const FLUSH: i32 = 1;
    pub const SIX_PANEL: i32 = 2;
    pub const LOUVERED: i32 = 3;
    pub const FULL_GLASS: i32 = 4;
    pub const SECURITY: i32 = 5;
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct OpeningType {
    pub id: Id,
    #[serde(default)]
    pub depth: f64,
    #[serde(default = "default_frame_width")]
    pub frame_width: f64,
    #[serde(default)]
    pub glazing_light_count: i32,
    #[serde(default)]
    pub operation: i32,
    #[serde(default)]
    pub glazed_leaf: bool,
    #[serde(default)]
    pub leaf_style: i32,
    #[serde(default)]
    pub security_bars: bool,
}

/// Matches the application default for older opening types.
fn default_frame_width() -> f64 {
    0.06
}

/// `ColumnShape`.
pub mod column_shape {
    pub const ROUND: i32 = 1;
    pub const L_SHAPED: i32 = 2;
    pub const T_SHAPED: i32 = 3;
    pub const OCTAGONAL: i32 = 4;
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Column {
    pub id: Id,
    pub level_id: Id,
    pub position: Point2,
    pub width: f64,
    pub depth: f64,
    pub height: f64,
    #[serde(default)]
    pub shape: i32,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Document {
    pub schema_version: i32,
    #[serde(default)]
    pub levels: Vec<Level>,
    #[serde(default)]
    pub walls: Vec<Wall>,
    #[serde(default)]
    pub openings: Vec<WallOpening>,
    #[serde(default)]
    pub wall_types: Vec<WallType>,
    #[serde(default)]
    pub opening_types: Vec<OpeningType>,
    #[serde(default)]
    pub floors: Vec<FloorSlab>,
    #[serde(default)]
    pub columns: Vec<Column>,
    #[serde(default)]
    pub structural_elements: Vec<StructuralElement>,
    #[serde(default)]
    pub electrical_devices: Vec<serde::de::IgnoredAny>,
    #[serde(default)]
    pub plumbing_points: Vec<PlumbingPoint>,
    #[serde(default)]
    pub plumbing_systems: Vec<PlumbingSystem>,
    #[serde(default)]
    pub in_place_families: Vec<serde::de::IgnoredAny>,
    #[serde(default)]
    pub render_materials: Vec<serde::de::IgnoredAny>,
    #[serde(default)]
    pub render_setup: Option<serde::de::IgnoredAny>,
    #[serde(default)]
    pub render_scenes: Vec<serde::de::IgnoredAny>,
    #[serde(default)]
    pub extensions: HashMap<String, serde::de::IgnoredAny>,
}

#[derive(Debug)]
pub enum DocumentError {
    Container(ContainerError),
    /// The file is valid but holds something this reader does not load.
    Unsupported(String),
    Json(serde_json::Error),
}

impl fmt::Display for DocumentError {
    /// Formats this value using its canonical representation.
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            DocumentError::Container(error) => error.fmt(f),
            DocumentError::Unsupported(reason) => write!(f, "unsupported project: {reason}"),
            DocumentError::Json(error) => write!(f, "the project contents are invalid: {error}"),
        }
    }
}

impl std::error::Error for DocumentError {}

impl Document {
    /// Identifies saved render features that the native scene builder does not yet reproduce.
    pub fn has_scene_customizations(&self) -> bool {
        !self.render_materials.is_empty()
            || self.render_setup.is_some()
            || !self.render_scenes.is_empty()
            || self.extensions.keys().any(|key| key.starts_with("render.") && key != "render.modelsync")
    }

    /// Reads and verifies a native project from disk.
    pub fn load(path: impl AsRef<std::path::Path>) -> Result<Document, DocumentError> {
        Document::from_container(Container::read(path).map_err(DocumentError::Container)?)
    }

    /// Reads and verifies a native project already held in memory.
    pub fn parse(bytes: &[u8]) -> Result<Document, DocumentError> {
        Document::from_container(Container::parse(bytes).map_err(DocumentError::Container)?)
    }

    /// Accepts only project containers whose header and schema agree.
    fn from_container(container: Container) -> Result<Document, DocumentError> {
        if container.kind != DocumentKind::Project {
            return Err(DocumentError::Unsupported("the file is a family or a template, not a project".into()));
        }
        if container.schema_version != SCHEMA_VERSION {
            return Err(DocumentError::Unsupported(format!(
                "schema {} needs the migrations of the application (this reader loads schema {SCHEMA_VERSION})",
                container.schema_version
            )));
        }
        let document: Document = serde_json::from_slice(&container.json).map_err(DocumentError::Json)?;
        if document.schema_version != container.schema_version {
            return Err(DocumentError::Unsupported("the header and the contents disagree on the schema".into()));
        }
        Ok(document)
    }
}

#[cfg(test)]
#[path = "document-tests.rs"]
mod tests;
