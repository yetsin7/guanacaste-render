//! Plan outlines of columns. Ported from `ArchitecturalShapes.Outline` in `architecture/architectural-shapes.cs`.

use crate::document::{Column, column_shape};
use crate::plan::Point2;

/// Sides of the polygon that stands for a round column.
const ROUND_SIDES: usize = 24;

/// Counter-clockwise outline of a column around its position; `width` and `depth` are its overall extent.
pub fn column_outline(column: &Column) -> Vec<Point2> {
    let (hw, hd) = (column.width / 2.0, column.depth / 2.0);
    let arm = 0.35 * column.width.min(column.depth);
    let local: Vec<(f64, f64)> = match column.shape {
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
                (-hw + cut, -hd), (hw - cut, -hd), (hw, -hd + cut), (hw, hd - cut),
                (hw - cut, hd), (-hw + cut, hd), (-hw, hd - cut), (-hw, -hd + cut),
            ]
        }
        column_shape::L_SHAPED => {
            vec![(-hw, -hd), (hw, -hd), (hw, -hd + arm), (-hw + arm, -hd + arm), (-hw + arm, hd), (-hw, hd)]
        }
        column_shape::T_SHAPED => vec![
            (-arm / 2.0, -hd), (arm / 2.0, -hd), (arm / 2.0, hd - arm), (hw, hd - arm),
            (hw, hd), (-hw, hd), (-hw, hd - arm), (-arm / 2.0, hd - arm),
        ],
        _ => vec![(-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)],
    };
    local.into_iter().map(|(x, y)| Point2::new(column.position.x + x, column.position.y + y)).collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::document::Id;
    use crate::plan::signed_double_area;

    #[test]
    fn every_shape_is_counter_clockwise_and_fits_its_extent() {
        for shape in 0..=4 {
            let column = Column { id: Id(1), level_id: Id(2), position: Point2::new(5.0, -3.0), width: 0.6, depth: 0.4, height: 3.0, shape };
            let outline = column_outline(&column);
            assert!(signed_double_area(&outline) > 0.0, "shape {shape} is not counter-clockwise");
            assert!(outline.iter().all(|p| (p.x - 5.0).abs() <= 0.3 + 1e-12 && (p.y + 3.0).abs() <= 0.2 + 1e-12));
        }
    }
}
