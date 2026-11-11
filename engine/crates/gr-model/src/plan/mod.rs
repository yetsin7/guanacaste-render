//! Geometry in the plan: polygons, wall joints, the solids of a wall and column outlines. All in `f64` metres.

mod columns;
mod polygon;
mod wall_joins;
mod wall_solids;

pub use columns::column_outline;
pub use polygon::{Degenerate, Point2, counter_clockwise, signed_double_area, triangulate};
pub use wall_joins::WallJoins;
pub use wall_solids::{WallSolid, wall_solids};
