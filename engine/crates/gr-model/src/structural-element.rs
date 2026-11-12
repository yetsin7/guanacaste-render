//! Structural element records and the geometry properties used by the mesher.

use super::Id;
use crate::plan::Point2;
use serde::Deserialize;

/// `StructuralElementKind`.
pub mod element_kind {
    pub const BEAM: i32 = 0;
    pub const BRACE: i32 = 1;
    pub const TRUSS: i32 = 2;
    pub const ISOLATED_FOUNDATION: i32 = 4;
    pub const COMPONENT: i32 = 5;
    pub const OPENING: i32 = 8;
    pub const SHAFT: i32 = 9;
    pub const DORMER: i32 = 11;
    pub const STAIR: i32 = 12;
    pub const RAILING: i32 = 13;
    pub const RAMP: i32 = 14;
    pub const GUTTER: i32 = 15;
    pub const DOWNSPOUT: i32 = 16;
    pub const CURTAIN_WALL: i32 = 17;
    pub const SKYLIGHT: i32 = 18;
    pub const WALL_SWEEP: i32 = 23;
    pub const WALL_REVEAL: i32 = 24;
    pub const PLANTING: i32 = 28;
    pub const ELEVATOR: i32 = 29;
    pub const FLEX_DUCT: i32 = 46;
    pub const CONDUIT: i32 = 48;
    pub const FLEX_PIPE: i32 = 49;
    pub const BOLT: i32 = 55;
    pub const REBAR: i32 = 57;
}

/// `ElementStyle`: a variant of the element's kind, such as the growth form of a planting or the infill of a railing.
pub mod element_style {
    pub const DEFAULT: i32 = 0;
    pub const ROUND: i32 = 1;
    pub const I_SHAPE: i32 = 2;
    pub const TEE: i32 = 3;
    pub const PICKETS: i32 = 4;
    pub const GLASS_PANEL: i32 = 5;
    pub const CABLE: i32 = 6;
    pub const SOLID_PARAPET: i32 = 7;
    pub const OPEN: i32 = 8;
    pub const GUANACASTE: i32 = 9;
    pub const PALM: i32 = 10;
    pub const CONIFER: i32 = 11;
    pub const SHRUB: i32 = 12;
    pub const L_SHAPED: i32 = 13;
    pub const U_SHAPED: i32 = 14;
    pub const SPIRAL: i32 = 15;
    pub const BREEZE_BLOCK: i32 = 16;
}

/// A structural element: beams, stairs, railings, furniture, plantings and the like. Only the fields the mesher
/// reads. An element drawn at a point has `start` equal to `end`.
#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct StructuralElement {
    pub id: Id,
    pub level_id: Id,
    pub kind: i32,
    pub start: Point2,
    pub end: Point2,
    pub width: f64,
    pub height: f64,
    /// Metres from the level (or the ground, for a planting) to the underside of the element.
    pub offset: f64,
    /// Plan depth of a point element; zero means as deep as it is wide.
    pub depth: f64,
    /// `ResidentialComponentShape`: zero is a plain box.
    pub component_shape: i32,
    /// Turn of a point element in the plan, in radians.
    pub rotation: f64,
    /// Spacing of the mullions and transoms of a curtain wall; zero uses the default.
    pub grid_width: f64,
    pub grid_height: f64,
    /// Requested riser height of a stair; zero uses the default.
    pub riser_height: f64,
    pub tread_depth: f64,
    /// Rise of a railing from its start to its end, when it follows a stair or a ramp.
    pub rise: f64,
    pub style: i32,
    /// The component family whose mesh the element draws instead of its procedural geometry.
    pub family_id: Option<Id>,
    /// Cuts of a structural member (copes, mitres); only their presence matters here.
    pub cuts: Option<Vec<serde::de::IgnoredAny>>,
}

impl StructuralElement {
    /// Kinds without a solid of their own: drafting marks, and the openings and shafts that only cut slabs.
    const WITHOUT_SOLID: [i32; 21] = [
        7, 8, 9, 10, 19, 20, 21, 22, 30, 31, 32, 33, 35, 36, 37, 38, 39, 40, 41, 42, 43,
    ];
    /// Kinds whose footprint is the rectangle between their two points rather than a strip along them.
    const RECTANGULAR: [i32; 11] = [8, 9, 11, 18, 22, 29, 31, 32, 34, 35, 54];

    /// Whether this kind emits visible geometry rather than only annotations or cuts.
    pub fn has_solid(&self) -> bool {
        !Self::WITHOUT_SOLID.contains(&self.kind)
    }

    /// Distance between the endpoints in plan coordinates.
    pub fn length(&self) -> f64 {
        self.start.distance_to(self.end)
    }

    /// Whether member cuts require geometry the current mesher cannot reproduce.
    pub fn has_cuts(&self) -> bool {
        self.cuts.as_ref().is_some_and(|cuts| !cuts.is_empty())
    }

    /// A planting grown procedurally, rather than drawn from the mesh of a component family.
    pub fn is_procedural_plant(&self) -> bool {
        self.kind == element_kind::PLANTING && self.family_id.is_none()
    }

    /// The rectangle between the two points of the element.
    pub fn plan_extent(&self) -> crate::plan::PlanRectangle {
        crate::plan::PlanRectangle {
            min_x: self.start.x.min(self.end.x),
            min_y: self.start.y.min(self.end.y),
            max_x: self.start.x.max(self.end.x),
            max_y: self.start.y.max(self.end.y),
        }
    }

    /// Plan outline of the element: the rectangle between its points for area-like kinds, a turned rectangle
    /// around a point element, and otherwise a strip as wide as the element along its two points.
    pub fn footprint(&self) -> Vec<Point2> {
        let half = self.width / 2.0;
        let half_depth = (if self.depth > 0.0 {
            self.depth
        } else {
            self.width
        }) / 2.0;
        let length = self.length();
        if Self::RECTANGULAR.contains(&self.kind) && length >= 1e-9 {
            return self.plan_extent().corners().to_vec();
        }
        if length < 1e-9 {
            let (cos, sin) = (self.rotation.cos(), self.rotation.sin());
            return [
                (-half, -half_depth),
                (half, -half_depth),
                (half, half_depth),
                (-half, half_depth),
            ]
            .into_iter()
            .map(|(x, y)| {
                Point2::new(
                    self.start.x + x * cos - y * sin,
                    self.start.y + x * sin + y * cos,
                )
            })
            .collect();
        }
        let normal_x = -(self.end.y - self.start.y) / length * half;
        let normal_y = (self.end.x - self.start.x) / length * half;
        vec![
            Point2::new(self.start.x - normal_x, self.start.y - normal_y),
            Point2::new(self.end.x - normal_x, self.end.y - normal_y),
            Point2::new(self.end.x + normal_x, self.end.y + normal_y),
            Point2::new(self.start.x + normal_x, self.start.y + normal_y),
        ]
    }
}
