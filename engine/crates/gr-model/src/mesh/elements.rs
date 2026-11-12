//! Structural and architectural elements as the solids they are drawn with: stairs, railings, ramps, curtain
//! walls and breeze-block screens, beams, gutters, downspouts, skylights and plain blocks.
//!
//! Ported from `ElementGeometry` (`architecture/element-geometry.cs`), `CirculationGeometry`
//! (`circulation-geometry.cs`) and `AddPart` in the mesher. Kinds whose geometry lives elsewhere and is not ported
//! yet (elevators and members with cuts) are reported as unsupported, not approximated.

use super::element_part::{ElementPart, structure};
use super::residential_components::residential_parts;
use super::scene_mesh::SurfaceKind;
use super::stairs::{composite_parts, is_composite, riser_count};
use crate::document::component_shape as catalog;
use crate::document::{StructuralElement, element_kind as kind, element_style as style};
use crate::plan::{Point2, lerp, point_footprint, strip};

/// Side of the square post of a railing when the railing itself is thinner.
const RAIL_POST: f64 = 0.06;

/// The solids of an element, or `None` when its geometry is not ported yet.
pub fn element_parts(element: &StructuralElement) -> Option<Vec<ElementPart>> {
    if !element.has_solid() {
        return Some(Vec::new());
    }
    if element.has_cuts() || element.kind == kind::ELEVATOR {
        return None;
    }
    if element.kind == kind::COMPONENT && element.component_shape != catalog::BOX {
        return residential_parts(element);
    }
    let round_run = matches!(element.kind, kind::CONDUIT | kind::FLEX_DUCT | kind::FLEX_PIPE | kind::REBAR);
    let (bottom, top) = (element.offset, element.offset + element.height);
    Some(match element.kind {
        kind::STAIR => stair_parts(element),
        kind::RAILING => railing_parts(element),
        kind::RAMP => ramp_parts(element),
        kind::WALL_SWEEP | kind::WALL_REVEAL => wall_sweep_parts(element),
        kind::GUTTER => gutter_parts(element),
        kind::CURTAIN_WALL => curtain_wall_parts(element),
        kind::SKYLIGHT => skylight_parts(element),
        // A round pipe or conduit as two crossed boxes, which together read as an octagon.
        _ if round_run && element.length() >= 0.05 => vec![
            structure(strip(element.start, element.end, 0.0, element.width), element.offset + element.height * 0.15, element.offset + element.height * 0.85),
            structure(strip(element.start, element.end, 0.0, element.width * 0.7), bottom, top),
        ],
        kind::DOWNSPOUT | kind::COMPONENT | kind::BOLT | kind::ISOLATED_FOUNDATION if element.style == style::ROUND => {
            vec![structure(point_footprint(element.start, element.width, true), bottom, top)]
        }
        kind::BEAM | kind::BRACE | kind::TRUSS if matches!(element.style, style::I_SHAPE | style::TEE) && element.length() >= 0.05 => {
            sectioned_member_parts(element)
        }
        _ => vec![structure(element.footprint(), bottom, top)],
    })
}

/// Straight stairs: solid steps by default, or treads on stepped stringers when open.
fn stair_parts(element: &StructuralElement) -> Vec<ElementPart> {
    if is_composite(element) {
        return composite_parts(element);
    }
    if element.length() < 0.05 {
        return Vec::new();
    }
    let count = riser_count(element);
    let mut parts = Vec::new();
    if element.style != style::OPEN {
        // Each step is a solid from the foot of the stair up to its own tread.
        for index in 0..count {
            let step = StructuralElement {
                start: lerp(element.start, element.end, index as f64 / count as f64),
                end: lerp(element.start, element.end, (index + 1) as f64 / count as f64),
                height: element.height * (index + 1) as f64 / count as f64,
                ..element.clone()
            };
            parts.push(structure(step.footprint(), step.offset, step.offset + step.height));
        }
        return parts;
    }
    let tread_thickness = 0.04f64.min(element.height / count as f64 / 2.0);
    const STRINGER_WIDTH: f64 = 0.06;
    const STRINGER_DEPTH: f64 = 0.20;
    for index in 0..count {
        let from = lerp(element.start, element.end, index as f64 / count as f64);
        let to = lerp(element.start, element.end, (index + 1) as f64 / count as f64);
        let top = element.offset + element.height * (index + 1) as f64 / count as f64;
        parts.push(structure(strip(from, to, 0.0, element.width), top - tread_thickness, top));
        for side in [-1.0, 1.0] {
            parts.push(structure(
                strip(from, to, side * (element.width / 2.0 - STRINGER_WIDTH / 2.0), STRINGER_WIDTH),
                element.offset.max(top - tread_thickness - STRINGER_DEPTH),
                top - tread_thickness,
            ));
        }
    }
    parts
}

/// Guards: posts and two rails by default, or balusters, glass, cables, or a solid parapet.
fn railing_parts(element: &StructuralElement) -> Vec<ElementPart> {
    let length = element.length();
    if length < 0.05 {
        return Vec::new();
    }
    if element.rise != 0.0 {
        return sloped_railing_parts(element);
    }
    let (bottom, top) = (element.offset, element.offset + element.height);
    let mut parts = Vec::new();
    match element.style {
        style::DEFAULT => {
            // A post every 1.2 m, then a rail at mid height and one at the top.
            let intervals = (length / 1.20).ceil() as i32;
            for index in 0..=intervals {
                let position = lerp(element.start, element.end, index as f64 / intervals as f64);
                let post = StructuralElement { start: position, end: position, ..element.clone() };
                parts.push(structure(post.footprint(), post.offset, post.offset + post.height));
            }
            for fraction in [0.5, 1.0] {
                let rail_bottom = element.offset + element.height * fraction - 0.05;
                parts.push(structure(element.footprint(), rail_bottom, rail_bottom + 0.05));
            }
            return parts;
        }
        style::SOLID_PARAPET => {
            const THICKNESS: f64 = 0.15;
            parts.push(structure(strip(element.start, element.end, 0.0, THICKNESS), bottom, top - 0.05));
            parts.push(structure(strip(element.start, element.end, 0.0, THICKNESS + 0.06), top - 0.05, top));
            return parts;
        }
        _ => {}
    }

    // Posts every 1.8 m (1.2 m for cables), always at both ends, then a top rail and the infill of the style.
    let post_spacing = if element.style == style::CABLE { 1.2 } else { 1.8 };
    let intervals = (length / post_spacing).ceil() as i32;
    for index in 0..=intervals {
        let position = lerp(element.start, element.end, index as f64 / intervals as f64);
        parts.push(structure(point_footprint(position, element.width.max(RAIL_POST), false), bottom, top));
    }
    parts.push(structure(strip(element.start, element.end, 0.0, element.width + 0.02), top - 0.05, top));
    match element.style {
        style::PICKETS => {
            parts.push(structure(strip(element.start, element.end, 0.0, element.width), bottom + 0.08, bottom + 0.13));
            const PICKET: f64 = 0.02;
            let spacing = 0.11f64.max(length / 2000.0);
            let count = (length / spacing).floor() as i32;
            for index in 1..count {
                let position = lerp(element.start, element.end, index as f64 * spacing / length);
                parts.push(structure(point_footprint(position, PICKET, false), bottom + 0.13, top - 0.05));
            }
        }
        style::GLASS_PANEL => {
            parts.push(ElementPart::block(strip(element.start, element.end, 0.0, 0.012), bottom + 0.10, top - 0.05, SurfaceKind::Glass));
        }
        style::CABLE => {
            for index in 1..=5 {
                let height = bottom + 0.12 + (element.height - 0.22) * (index - 1) as f64 / 4.0;
                parts.push(structure(strip(element.start, element.end, 0.0, 0.012), height, height + 0.012));
            }
        }
        _ => {}
    }
    parts
}

/// A guard that follows a rising stair or ramp: level posts at the height of the stair under them, sloped rails
/// and infill.
fn sloped_railing_parts(element: &StructuralElement) -> Vec<ElementPart> {
    let length = element.length();
    let z = |fraction: f64| element.offset + element.rise * fraction;
    let sloped = |lateral: f64, width: f64, lower: f64, upper: f64, surface: SurfaceKind| ElementPart {
        footprint: strip(element.start, element.end, lateral, width),
        bottom: z(0.0) + lower,
        top: z(0.0) + upper,
        kind: surface,
        end_bottom: Some(z(1.0) + lower),
        end_top: Some(z(1.0) + upper),
    };
    let height = element.height;
    let mut parts = Vec::new();
    if element.style == style::SOLID_PARAPET {
        parts.push(sloped(0.0, 0.15, 0.0, height - 0.05, SurfaceKind::Structure));
        parts.push(sloped(0.0, 0.21, height - 0.05, height, SurfaceKind::Structure));
        return parts;
    }
    let spacing = if element.style == style::CABLE { 1.2 } else { 1.8 };
    let intervals = ((length / spacing).ceil() as i32).max(1);
    for index in 0..=intervals {
        let fraction = index as f64 / intervals as f64;
        let position = lerp(element.start, element.end, fraction);
        parts.push(structure(point_footprint(position, element.width.max(RAIL_POST), false), z(fraction), z(fraction) + height));
    }
    parts.push(sloped(0.0, element.width + 0.02, height - 0.05, height, SurfaceKind::Structure));
    match element.style {
        style::DEFAULT => parts.push(sloped(0.0, element.width, height / 2.0 - 0.05, height / 2.0, SurfaceKind::Structure)),
        style::GLASS_PANEL => parts.push(sloped(0.0, 0.012, 0.10, height - 0.05, SurfaceKind::Glass)),
        style::CABLE => {
            for index in 0..5 {
                let level = (height - 0.22) * index as f64 / 4.0;
                parts.push(sloped(0.0, 0.012, 0.12 + level, 0.132 + level, SurfaceKind::Structure));
            }
        }
        style::PICKETS => {
            parts.push(sloped(0.0, element.width, 0.08, 0.13, SurfaceKind::Structure));
            const SPACING: f64 = 0.11;
            let count = (length / SPACING).floor().min(2000.0) as i32;
            for index in 1..count {
                let fraction = index as f64 * SPACING / length;
                let position = lerp(element.start, element.end, fraction);
                parts.push(structure(point_footprint(position, 0.02, false), z(fraction) + 0.13, z(fraction) + height - 0.05));
            }
        }
        _ => {}
    }
    parts
}

/// A profile on both faces of the host wall: proud by 2.5 cm for a sweep, 4 mm for a reveal line.
fn wall_sweep_parts(element: &StructuralElement) -> Vec<ElementPart> {
    if element.length() < 0.05 {
        return Vec::new();
    }
    let reveal = element.kind == kind::WALL_REVEAL;
    let proud = if reveal { 0.004 } else { 0.025 };
    let surface = if reveal { SurfaceKind::OpeningFrame } else { SurfaceKind::Wall };
    [-1.0, 1.0]
        .into_iter()
        .map(|side| {
            ElementPart::block(
                strip(element.start, element.end, side * (element.width / 2.0 + proud / 2.0), proud),
                element.offset,
                element.offset + element.height,
                surface,
            )
        })
        .collect()
}

/// A wedge rising from the start (thin) to the end (full height).
fn ramp_parts(element: &StructuralElement) -> Vec<ElementPart> {
    if element.length() < 0.05 {
        return Vec::new();
    }
    let toe = 0.08f64.min(element.height / 2.0);
    vec![ElementPart {
        end_top: Some(element.offset + element.height),
        ..structure(strip(element.start, element.end, 0.0, element.width), element.offset, element.offset + toe)
    }]
}

/// A U-shaped channel: a thin bottom and two thin sides, 8 mm thick.
fn gutter_parts(element: &StructuralElement) -> Vec<ElementPart> {
    if element.length() < 0.05 {
        return Vec::new();
    }
    const SHEET: f64 = 0.008;
    let mut parts = vec![structure(strip(element.start, element.end, 0.0, element.width), element.offset, element.offset + SHEET)];
    for side in [-1.0, 1.0] {
        parts.push(structure(
            strip(element.start, element.end, side * (element.width / 2.0 - SHEET / 2.0), SHEET),
            element.offset,
            element.offset + element.height,
        ));
    }
    parts
}

/// Wide-flange and tee members as stacked flange and web boxes along the axis.
fn sectioned_member_parts(element: &StructuralElement) -> Vec<ElementPart> {
    let flange = (element.height * 0.12).clamp(0.01, element.height / 3.0);
    let web = (element.width * 0.12).max(0.01);
    let (bottom, top) = (element.offset, element.offset + element.height);
    let i_shape = element.style == style::I_SHAPE;
    let mut parts = Vec::new();
    if i_shape {
        parts.push(structure(strip(element.start, element.end, 0.0, element.width), bottom, bottom + flange));
    }
    parts.push(structure(strip(element.start, element.end, 0.0, web), if i_shape { bottom + flange } else { bottom }, top - flange));
    parts.push(structure(strip(element.start, element.end, 0.0, element.width), top - flange, top));
    parts
}

/// A pane of glass in a grid of mullions (every 1.5 m) and transoms (every 1.2 m), or a breeze-block screen.
fn curtain_wall_parts(element: &StructuralElement) -> Vec<ElementPart> {
    let length = element.length();
    if length < 0.05 {
        return Vec::new();
    }
    let (bottom, top) = (element.offset, element.offset + element.height);
    let breeze_block = element.style == style::BREEZE_BLOCK;
    // A breeze-block screen is thin masonry webs on a square module through the full depth of the screen, leaving
    // real holes so sunlight passes through the cells; a curtain wall is framing members over a pane of glass.
    let (member, surface) = if breeze_block { (0.04, SurfaceKind::Wall) } else { (0.06, SurfaceKind::OpeningFrame) };
    let grid = |spacing: f64, standard: f64| if spacing > 0.0 { spacing } else { standard };
    let (columns, rows) = if breeze_block {
        (
            ((length / grid(element.grid_width, 0.20)).round_ties_even() as i32).max(1),
            ((element.height / grid(element.grid_height, 0.20)).round_ties_even() as i32).max(1),
        )
    } else {
        (
            ((length / grid(element.grid_width, 1.5)).ceil() as i32).max(1),
            ((element.height / grid(element.grid_height, 1.2)).ceil() as i32).max(1),
        )
    };
    let mut parts = Vec::new();
    if !breeze_block {
        parts.push(ElementPart::block(strip(element.start, element.end, 0.0, 0.012), bottom, top, SurfaceKind::Glass));
    }
    for index in 0..=columns {
        let fraction = index as f64 / columns as f64;
        let from = lerp(element.start, element.end, (fraction - member / 2.0 / length).max(0.0));
        let to = lerp(element.start, element.end, (fraction + member / 2.0 / length).min(1.0));
        parts.push(ElementPart::block(strip(from, to, 0.0, element.width), bottom, top, surface));
    }
    for index in 0..=rows {
        let center = bottom + element.height * index as f64 / rows as f64;
        parts.push(ElementPart::block(
            strip(element.start, element.end, 0.0, element.width),
            bottom.max(center - member / 2.0),
            top.min(center + member / 2.0),
            surface,
        ));
    }
    parts
}

/// A curb of four bars around a glass pane.
fn skylight_parts(element: &StructuralElement) -> Vec<ElementPart> {
    let extent = element.plan_extent();
    const BAR: f64 = 0.08;
    let (width, depth) = (extent.max_x - extent.min_x, extent.max_y - extent.min_y);
    if width <= 2.0 * BAR + 0.05 || depth <= 2.0 * BAR + 0.05 {
        return Vec::new();
    }
    let (bottom, top) = (element.offset, element.offset + element.height);
    let rectangle = |x0: f64, y0: f64, x1: f64, y1: f64| vec![Point2::new(x0, y0), Point2::new(x1, y0), Point2::new(x1, y1), Point2::new(x0, y1)];
    vec![
        structure(rectangle(extent.min_x, extent.min_y, extent.max_x, extent.min_y + BAR), bottom, top),
        structure(rectangle(extent.min_x, extent.max_y - BAR, extent.max_x, extent.max_y), bottom, top),
        structure(rectangle(extent.min_x, extent.min_y + BAR, extent.min_x + BAR, extent.max_y - BAR), bottom, top),
        structure(rectangle(extent.max_x - BAR, extent.min_y + BAR, extent.max_x, extent.max_y - BAR), bottom, top),
        ElementPart::block(
            rectangle(extent.min_x + BAR, extent.min_y + BAR, extent.max_x - BAR, extent.max_y - BAR),
            top - 0.03,
            top - 0.015,
            SurfaceKind::Glass,
        ),
    ]
}

#[cfg(test)]
#[path = "element-tests.rs"]
mod tests;
