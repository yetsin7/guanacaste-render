//! Plan points and simple polygons: orientation and ear-clipping triangulation.
//!
//! Ported from `geometry/polygon-triangulator.cs`. The mesher's output is compared triangle by triangle with the
//! C# application, so the order in which ears are clipped is part of the contract.

use serde::Deserialize;

/// A position in the plan, in metres.
#[derive(Clone, Copy, Debug, PartialEq, Default, Deserialize)]
pub struct Point2 {
    #[serde(rename = "X")]
    pub x: f64,
    #[serde(rename = "Y")]
    pub y: f64,
}

impl Point2 {
    pub const fn new(x: f64, y: f64) -> Point2 {
        Point2 { x, y }
    }

    pub fn distance_to(self, other: Point2) -> f64 {
        (other.x - self.x).hypot(other.y - self.y)
    }
}

/// Twice the signed area; positive for counter-clockwise order. Computed from the first vertex so that small
/// outlines far from the origin keep their precision.
pub fn signed_double_area(polygon: &[Point2]) -> f64 {
    let Some(&origin) = polygon.first() else { return 0.0 };
    let mut sum = 0.0;
    for index in 1..polygon.len().saturating_sub(1) {
        let (current, next) = (polygon[index], polygon[index + 1]);
        sum += (current.x - origin.x) * (next.y - origin.y) - (next.x - origin.x) * (current.y - origin.y);
    }
    sum
}

/// The polygon in counter-clockwise order.
pub fn counter_clockwise(polygon: &[Point2]) -> Vec<Point2> {
    if signed_double_area(polygon) > 0.0 { polygon.to_vec() } else { polygon.iter().rev().copied().collect() }
}

/// Why a polygon could not be triangulated.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Degenerate;

/// Z component of (b - a) x (c - b).
fn cross(a: Point2, b: Point2, c: Point2) -> f64 {
    (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x)
}

/// Ear-clips a simple polygon, convex or concave, into counter-clockwise triangles that index its points.
pub fn triangulate(polygon: &[Point2]) -> Result<Vec<[usize; 3]>, Degenerate> {
    if polygon.len() < 3 || polygon.iter().any(|point| !point.x.is_finite() || !point.y.is_finite()) {
        return Err(Degenerate);
    }
    let area = signed_double_area(polygon);
    if !area.is_finite() || area.abs() < 1e-12 {
        return Err(Degenerate);
    }
    let mut remaining: Vec<usize> = (0..polygon.len()).collect();
    if area < 0.0 {
        remaining.reverse();
    }
    let mut triangles = Vec::with_capacity(polygon.len() - 2);
    let mut guard = 0usize;
    while remaining.len() > 3 {
        let mut clipped = false;
        for i in 0..remaining.len() {
            let previous = remaining[(i + remaining.len() - 1) % remaining.len()];
            let current = remaining[i];
            let next = remaining[(i + 1) % remaining.len()];
            let turn = cross(polygon[previous], polygon[current], polygon[next]);
            if turn.abs() < 1e-14 {
                // A collinear vertex: removing it does not change the covered area.
                remaining.remove(i);
                clipped = true;
                break;
            }
            if turn < 0.0 || contains_other_vertex(polygon, &remaining, previous, current, next) {
                continue;
            }
            triangles.push([previous, current, next]);
            remaining.remove(i);
            clipped = true;
            break;
        }
        guard += 1;
        if !clipped || guard > polygon.len() * polygon.len() {
            return Err(Degenerate);
        }
    }
    if cross(polygon[remaining[0]], polygon[remaining[1]], polygon[remaining[2]]).abs() > 1e-14 {
        triangles.push([remaining[0], remaining[1], remaining[2]]);
    }
    Ok(triangles)
}

/// Whether any other remaining vertex lies inside or on the candidate ear.
fn contains_other_vertex(polygon: &[Point2], remaining: &[usize], a: usize, b: usize, c: usize) -> bool {
    remaining.iter().any(|&index| {
        if index == a || index == b || index == c {
            return false;
        }
        let p = polygon[index];
        if p == polygon[a] || p == polygon[b] || p == polygon[c] {
            return false;
        }
        cross(polygon[a], polygon[b], p) >= 0.0 && cross(polygon[b], polygon[c], p) >= 0.0 && cross(polygon[c], polygon[a], p) >= 0.0
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn area_of(polygon: &[Point2], triangles: &[[usize; 3]]) -> f64 {
        triangles.iter().map(|t| signed_double_area(&[polygon[t[0]], polygon[t[1]], polygon[t[2]]]) / 2.0).sum()
    }

    #[test]
    fn a_concave_outline_is_covered_exactly_whatever_its_winding() {
        // An L: 4 x 4 with a 2 x 2 corner removed, area 12.
        let l = [(0., 0.), (4., 0.), (4., 2.), (2., 2.), (2., 4.), (0., 4.)].map(|(x, y)| Point2::new(x, y));
        let reversed: Vec<Point2> = l.iter().rev().copied().collect();
        for polygon in [&l[..], &reversed[..]] {
            let triangles = triangulate(polygon).expect("a simple polygon");
            assert_eq!(triangles.len(), 4);
            assert!((area_of(polygon, &triangles) - 12.0).abs() < 1e-12);
            assert!(triangles.iter().all(|t| signed_double_area(&[polygon[t[0]], polygon[t[1]], polygon[t[2]]]) > 0.0));
        }
    }

    #[test]
    fn collinear_vertices_do_not_change_the_area_and_flat_outlines_are_refused() {
        let square = [(0., 0.), (1., 0.), (2., 0.), (2., 2.), (0., 2.)].map(|(x, y)| Point2::new(x, y));
        let triangles = triangulate(&square).expect("a square with one extra point");
        assert!((area_of(&square, &triangles) - 4.0).abs() < 1e-12);
        let flat = [(0., 0.), (1., 0.), (2., 0.)].map(|(x, y)| Point2::new(x, y));
        assert_eq!(triangulate(&flat), Err(Degenerate));
        assert_eq!(triangulate(&[Point2::new(0., 0.), Point2::new(f64::NAN, 1.), Point2::new(1., 1.)]), Err(Degenerate));
    }

    #[test]
    fn area_keeps_precision_far_from_the_origin() {
        let far = [(1e7, 1e7), (1e7 + 0.1, 1e7), (1e7 + 0.1, 1e7 + 0.1), (1e7, 1e7 + 0.1)].map(|(x, y)| Point2::new(x, y));
        assert!((signed_double_area(&far) - 0.02).abs() < 1e-8);
    }
}
