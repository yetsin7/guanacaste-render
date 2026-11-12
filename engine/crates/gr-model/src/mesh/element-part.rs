//! Representation and triangulation of closed element solids.

use super::prism::{PrismShape, add_prism, to_f32};
use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::plan::{Point2, signed_double_area};
use glam::Vec3;

/// One closed solid of an element: a vertical prism over `footprint` from `bottom` to `top`, both measured from
/// the element's level. With `end_top`, the footprint is a quad whose second and third corners rise to that height
/// instead, which gives ramps and sloped rails their slope.
#[derive(Clone, Debug, PartialEq)]
pub struct ElementPart {
    pub footprint: Vec<Point2>,
    pub bottom: f64,
    pub top: f64,
    pub kind: SurfaceKind,
    pub end_top: Option<f64>,
    /// Underside at the end corners; `None` keeps the underside level at `bottom`.
    pub end_bottom: Option<f64>,
}

impl ElementPart {
    /// Creates a level solid with no slope at its far end.
    pub fn block(footprint: Vec<Point2>, bottom: f64, top: f64, kind: SurfaceKind) -> ElementPart {
        ElementPart {
            footprint,
            bottom,
            top,
            kind,
            end_top: None,
            end_bottom: None,
        }
    }
}

/// Uses the standard structural surface for an element solid.
pub(super) fn structure(footprint: Vec<Point2>, bottom: f64, top: f64) -> ElementPart {
    ElementPart::block(footprint, bottom, top, SurfaceKind::Structure)
}

/// Adds one part as a prism, or as a sloped solid when it carries an end height. `datum` is the elevation of the
/// element's level.
pub fn add_part(mesh: &mut SceneMesh, part: &ElementPart, datum: f64) {
    let outline = to_f32(&part.footprint);
    let Some(end_top) = part.end_top.filter(|_| outline.len() == 4) else {
        add_prism(
            mesh,
            &outline,
            (datum + part.bottom) as f32,
            (datum + part.top) as f32,
            part.kind,
            &PrismShape::default(),
        );
        return;
    };
    // Corners 0 and 3 sit at the start (bottom, top); corners 1 and 2 at the end (end_bottom, end_top).
    let bottom = (datum + part.bottom) as f32;
    let end_bottom = (datum + part.end_bottom.unwrap_or(part.bottom)) as f32;
    let bottoms = [bottom, end_bottom, end_bottom, bottom];
    let (start_top, end_top) = ((datum + part.top) as f32, (datum + end_top) as f32);
    let tops = [start_top, end_top, end_top, start_top];
    let plan: Vec<Point2> = outline
        .iter()
        .map(|point| Point2::new(point.x as f64, point.y as f64))
        .collect();
    let order = if signed_double_area(&plan) > 0.0 {
        [0, 1, 2, 3]
    } else {
        [3, 2, 1, 0]
    };
    let top = |index: usize| {
        Vec3::new(
            outline[order[index]].x,
            outline[order[index]].y,
            tops[order[index]],
        )
    };
    let under = |index: usize| {
        Vec3::new(
            outline[order[index]].x,
            outline[order[index]].y,
            bottoms[order[index]],
        )
    };
    mesh.add_quad(top(0), top(1), top(2), top(3), part.kind);
    mesh.add_quad(under(3), under(2), under(1), under(0), part.kind);
    for index in 0..4 {
        let next = (index + 1) % 4;
        mesh.add_quad(under(index), under(next), top(next), top(index), part.kind);
    }
}
