//! Interior classification used to assign wall-face materials.

use crate::document::{Document, Id};
use crate::plan::Point2;

/// Whether a plan point of one level lies inside the building: enclosed by walls, meaning a ray from it in each
/// of eight directions meets a wall of the level. A terrace open on one side is outside; doors and windows do not
/// let the rays out, because walls are tested along their whole length.
pub(super) struct InteriorTest {
    pub(super) walls: Vec<(Point2, Point2)>,
}

impl InteriorTest {
    /// Collects the walls that can enclose points on the requested level.
    pub(super) fn new(document: &Document, level: Id) -> InteriorTest {
        let walls: Vec<_> = document
            .walls
            .iter()
            .filter(|wall| wall.level_id == level)
            .map(|wall| (wall.start, wall.end))
            .collect();
        // Fewer than three walls cannot enclose anything.
        InteriorTest {
            walls: if walls.len() < 3 { Vec::new() } else { walls },
        }
    }

    /// Uses rays in eight directions to distinguish an interior from an open terrace.
    pub(super) fn contains(&self, point: Point2) -> bool {
        if self.walls.is_empty() {
            return false;
        }
        (0..8).all(|index| {
            let angle = index as f64 * std::f64::consts::PI / 4.0 + 0.1;
            let direction = (angle.cos(), angle.sin());
            self.walls.iter().any(|&(a, b)| ray_hits(point, direction, a, b))
        })
    }
}

/// Tests a bounded forward ray against one wall segment.
fn ray_hits(origin: Point2, direction: (f64, f64), a: Point2, b: Point2) -> bool {
    let (ex, ey) = (b.x - a.x, b.y - a.y);
    let denominator = direction.0 * ey - direction.1 * ex;
    if denominator.abs() < 1e-12 {
        return false;
    }
    let (ox, oy) = (a.x - origin.x, a.y - origin.y);
    let t = (ox * ey - oy * ex) / denominator;
    let u = (ox * direction.1 - oy * direction.0) / denominator;
    t > 0.0 && t < 60.0 && (-1e-6..=1.0 + 1e-6).contains(&u)
}
