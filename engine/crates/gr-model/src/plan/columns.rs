//! Plan outlines of columns.

use crate::document::Column;
use crate::plan::{Point2, section_outline};

/// Counter-clockwise outline of a column around its position; `width` and `depth` are its overall extent.
pub fn column_outline(column: &Column) -> Vec<Point2> {
    section_outline(column.shape, column.position, column.width, column.depth)
}
