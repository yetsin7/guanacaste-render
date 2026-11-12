//! Geometry in the plan: polygons, wall joints, the solids of a wall, small shapes and slab openings. All in `f64`
//! metres.

mod columns;
mod polygon;
mod shapes;
mod slab_openings;
mod wall_joins;
mod wall_solids;

pub use columns::column_outline;
pub use polygon::{Degenerate, Point2, counter_clockwise, signed_double_area, triangulate};
pub use shapes::{lerp, point_footprint, section_outline, strip};
pub use slab_openings::{PlanRectangle, contains_rectangle, slab_pieces};
pub use wall_joins::WallJoins;
pub use wall_solids::{WallSolid, wall_solids};
