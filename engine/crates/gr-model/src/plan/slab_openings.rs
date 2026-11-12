//! Cuts rectangular openings out of slabs by splitting their plan into convex pieces. Ported from
//! `geometry/slab-openings.cs`.

use crate::plan::{Degenerate, Point2, signed_double_area, triangulate};

/// An axis-aligned rectangle in the plan, in metres.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PlanRectangle {
    pub min_x: f64,
    pub min_y: f64,
    pub max_x: f64,
    pub max_y: f64,
}

impl PlanRectangle {
    /// The smallest rectangle that contains every point; `None` for no points.
    pub fn of(points: &[Point2]) -> Option<PlanRectangle> {
        let first = points.first()?;
        let start = PlanRectangle {
            min_x: first.x,
            min_y: first.y,
            max_x: first.x,
            max_y: first.y,
        };
        Some(points.iter().fold(start, |r, p| PlanRectangle {
            min_x: r.min_x.min(p.x),
            min_y: r.min_y.min(p.y),
            max_x: r.max_x.max(p.x),
            max_y: r.max_y.max(p.y),
        }))
    }

    /// The four corners in counter-clockwise order.
    pub fn corners(&self) -> [Point2; 4] {
        [
            Point2::new(self.min_x, self.min_y),
            Point2::new(self.max_x, self.min_y),
            Point2::new(self.max_x, self.max_y),
            Point2::new(self.min_x, self.max_y),
        ]
    }
}

/// A half-plane to keep.
#[derive(Clone, Copy)]
enum Side {
    XAtMost(f64),
    XAtLeast(f64),
    YAtMost(f64),
    YAtLeast(f64),
}

impl Side {
    /// Signed distance: not positive for points that are kept.
    fn distance(self, point: Point2) -> f64 {
        match self {
            Side::XAtMost(x) => point.x - x,
            Side::XAtLeast(x) => x - point.x,
            Side::YAtMost(y) => point.y - y,
            Side::YAtLeast(y) => y - point.y,
        }
    }
}

/// One Sutherland-Hodgman step.
fn clip(polygon: &[Point2], side: Side) -> Vec<Point2> {
    let mut output = Vec::new();
    for index in 0..polygon.len() {
        let current = polygon[index];
        let next = polygon[(index + 1) % polygon.len()];
        let (current_distance, next_distance) = (side.distance(current), side.distance(next));
        if current_distance <= 0.0 {
            output.push(current);
        }
        if (current_distance < 0.0 && next_distance > 0.0)
            || (current_distance > 0.0 && next_distance < 0.0)
        {
            let t = current_distance / (current_distance - next_distance);
            output.push(Point2::new(
                current.x + t * (next.x - current.x),
                current.y + t * (next.y - current.y),
            ));
        }
    }
    output
}

/// Rejects degenerate polygons after a half-plane cut.
fn has_area(polygon: &[Point2]) -> bool {
    polygon.len() >= 3 && signed_double_area(polygon).abs() > 1e-12
}

/// Convex counter-clockwise pieces that cover the outline minus every opening: the outline is ear-clipped and each
/// rectangle is then subtracted from every piece with half-plane clips.
pub fn slab_pieces(
    outline: &[Point2],
    openings: &[PlanRectangle],
) -> Result<Vec<Vec<Point2>>, Degenerate> {
    let mut pieces: Vec<Vec<Point2>> = triangulate(outline)?
        .into_iter()
        .map(|[a, b, c]| vec![outline[a], outline[b], outline[c]])
        .collect();
    for opening in openings {
        pieces = pieces
            .iter()
            .flat_map(|piece| {
                let middle = clip(
                    &clip(piece, Side::XAtLeast(opening.min_x)),
                    Side::XAtMost(opening.max_x),
                );
                [
                    clip(piece, Side::XAtMost(opening.min_x)),
                    clip(piece, Side::XAtLeast(opening.max_x)),
                    clip(&middle, Side::YAtMost(opening.min_y)),
                    clip(&middle, Side::YAtLeast(opening.max_y)),
                ]
            })
            .filter(|piece| has_area(piece))
            .collect();
    }
    Ok(pieces)
}

/// Whether the whole rectangle is covered by a simple polygon, boundary included. Comparing areas also detects
/// concave notches that lie between four interior corners.
pub fn contains_rectangle(
    outline: &[Point2],
    rectangle: &PlanRectangle,
) -> Result<bool, Degenerate> {
    let (width, height) = (
        rectangle.max_x - rectangle.min_x,
        rectangle.max_y - rectangle.min_y,
    );
    if !width.is_finite() || !height.is_finite() || width <= 0.0 || height <= 0.0 {
        return Ok(false);
    }
    let sides = [
        Side::XAtLeast(rectangle.min_x),
        Side::XAtMost(rectangle.max_x),
        Side::YAtLeast(rectangle.min_y),
        Side::YAtMost(rectangle.max_y),
    ];
    let mut covered = 0.0;
    for [a, b, c] in triangulate(outline)? {
        let mut piece = vec![outline[a], outline[b], outline[c]];
        for side in sides {
            piece = clip(&piece, side);
        }
        if piece.len() >= 3 {
            covered += signed_double_area(&piece).abs() / 2.0;
        }
    }
    let area = width * height;
    Ok((area - covered).abs() <= (area * 1e-9).max(1e-10))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Provides a simple slab boundary for opening checks.
    fn square(size: f64) -> Vec<Point2> {
        vec![
            Point2::new(0.0, 0.0),
            Point2::new(size, 0.0),
            Point2::new(size, size),
            Point2::new(0.0, size),
        ]
    }

    #[test]
    /// Checks that cutting an opening preserves the expected remaining area.
    fn pieces_cover_the_slab_minus_the_opening() {
        let hole = PlanRectangle {
            min_x: 1.0,
            min_y: 1.0,
            max_x: 2.0,
            max_y: 3.0,
        };
        let pieces = slab_pieces(&square(4.0), &[hole]).expect("a simple outline");
        let area: f64 = pieces
            .iter()
            .map(|piece| signed_double_area(piece) / 2.0)
            .sum();
        assert!((area - 14.0).abs() < 1e-12);
        assert!(pieces.iter().all(|piece| signed_double_area(piece) > 0.0));
    }

    #[test]
    /// Checks containment against slab edges and concave notches.
    fn a_rectangle_is_contained_only_when_wholly_inside() {
        let slab = square(4.0);
        let inside = PlanRectangle {
            min_x: 0.0,
            min_y: 1.0,
            max_x: 2.0,
            max_y: 3.0,
        };
        let across_the_edge = PlanRectangle {
            min_x: 3.0,
            min_y: 1.0,
            max_x: 5.0,
            max_y: 2.0,
        };
        assert_eq!(contains_rectangle(&slab, &inside), Ok(true));
        assert_eq!(contains_rectangle(&slab, &across_the_edge), Ok(false));
        // A C-shaped slab: the four corners of this rectangle are inside it, but it spans the notch.
        let c = [
            (0., 0.),
            (4., 0.),
            (4., 1.),
            (1., 1.),
            (1., 3.),
            (4., 3.),
            (4., 4.),
            (0., 4.),
        ]
        .map(|(x, y)| Point2::new(x, y));
        let over_the_notch = PlanRectangle {
            min_x: 0.5,
            min_y: 0.5,
            max_x: 3.5,
            max_y: 3.5,
        };
        assert_eq!(contains_rectangle(&c, &over_the_notch), Ok(false));
    }

    #[test]
    /// Keeps opening walls consistently oriented.
    fn corners_run_counter_clockwise() {
        let rectangle =
            PlanRectangle::of(&[Point2::new(3.0, 1.0), Point2::new(1.0, 2.0)]).expect("two points");
        assert!(signed_double_area(&rectangle.corners()) > 0.0);
        assert_eq!(rectangle.corners()[0], Point2::new(1.0, 1.0));
    }
}
