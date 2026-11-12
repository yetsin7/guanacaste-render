//! Small plan shapes that elements are assembled from. Ported from `ArchitecturalShapes` in
//! `architecture/architectural-shapes.cs`.

use crate::document::column_shape;
use crate::plan::Point2;

/// Sides of the polygon that stands for a circle.
const ROUND_SIDES: usize = 24;

/// Counter-clockwise outline of a column-like section of the given overall extent around `center`.
pub fn section_outline(shape: i32, center: Point2, width: f64, depth: f64) -> Vec<Point2> {
    let (hw, hd) = (width / 2.0, depth / 2.0);
    let arm = 0.35 * width.min(depth);
    let local: Vec<(f64, f64)> = match shape {
        column_shape::ROUND => (0..ROUND_SIDES)
            .map(|index| {
                let angle = 2.0 * std::f64::consts::PI * index as f64 / ROUND_SIDES as f64;
                (hw * angle.cos(), hd * angle.sin())
            })
            .collect(),
        column_shape::OCTAGONAL => {
            // The rectangle with each corner cut at 29 % of its short side.
            let cut = 0.29 * 2.0 * hw.min(hd);
            vec![
                (-hw + cut, -hd),
                (hw - cut, -hd),
                (hw, -hd + cut),
                (hw, hd - cut),
                (hw - cut, hd),
                (-hw + cut, hd),
                (-hw, hd - cut),
                (-hw, -hd + cut),
            ]
        }
        column_shape::L_SHAPED => {
            vec![
                (-hw, -hd),
                (hw, -hd),
                (hw, -hd + arm),
                (-hw + arm, -hd + arm),
                (-hw + arm, hd),
                (-hw, hd),
            ]
        }
        column_shape::T_SHAPED => vec![
            (-arm / 2.0, -hd),
            (arm / 2.0, -hd),
            (arm / 2.0, hd - arm),
            (hw, hd - arm),
            (hw, hd),
            (-hw, hd),
            (-hw, hd - arm),
            (-arm / 2.0, hd - arm),
        ],
        _ => vec![(-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)],
    };
    local.into_iter().map(|(x, y)| Point2::new(center.x + x, center.y + y)).collect()
}

/// A square or round footprint of the given size around a point.
pub fn point_footprint(center: Point2, size: f64, round: bool) -> Vec<Point2> {
    section_outline(if round { column_shape::ROUND } else { 0 }, center, size, size)
}

/// A rectangle along the segment `from`..`to`, `width` wide, its centreline shifted `lateral` to the left.
pub fn strip(from: Point2, to: Point2, lateral: f64, width: f64) -> Vec<Point2> {
    let length = from.distance_to(to);
    let (normal_x, normal_y) = (-(to.y - from.y) / length, (to.x - from.x) / length);
    let (near, far) = (lateral - width / 2.0, lateral + width / 2.0);
    vec![
        Point2::new(from.x + normal_x * near, from.y + normal_y * near),
        Point2::new(to.x + normal_x * near, to.y + normal_y * near),
        Point2::new(to.x + normal_x * far, to.y + normal_y * far),
        Point2::new(from.x + normal_x * far, from.y + normal_y * far),
    ]
}

/// The point a fraction of the way from one point to another.
pub fn lerp(from: Point2, to: Point2, fraction: f64) -> Point2 {
    Point2::new(from.x + (to.x - from.x) * fraction, from.y + (to.y - from.y) * fraction)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::plan::signed_double_area;

    #[test]
    /// Checks orientation and dimensions of supported column sections.
    fn every_section_is_counter_clockwise_and_fits_its_extent() {
        for shape in 0..=4 {
            let outline = section_outline(shape, Point2::new(5.0, -3.0), 0.6, 0.4);
            assert!(signed_double_area(&outline) > 0.0, "shape {shape} is not counter-clockwise");
            assert!(
                outline
                    .iter()
                    .all(|p| (p.x - 5.0).abs() <= 0.3 + 1e-12 && (p.y + 3.0).abs() <= 0.2 + 1e-12)
            );
        }
    }

    #[test]
    /// Checks the sign convention for lateral offsets along a segment.
    fn a_shifted_strip_lies_to_the_left_of_its_segment() {
        // Heading east, left is north.
        let outline = strip(Point2::new(0.0, 0.0), Point2::new(4.0, 0.0), 1.0, 0.5);
        assert_eq!(outline[0], Point2::new(0.0, 0.75));
        assert_eq!(outline[2], Point2::new(4.0, 1.25));
        assert!((signed_double_area(&outline) - 4.0).abs() < 1e-12);
    }
}
