//! The more detailed seating recipes from the residential component catalog.

use super::element_part::ElementPart;
use super::residential_components::ComponentGeometry;
use super::scene_mesh::SurfaceKind as Surface;
use crate::document::component_shape as catalog;
use std::f64::consts::PI;

/// Selects supported task chairs and bar stools using the catalog's stored identifiers.
pub(super) fn parts(
    geometry: &ComponentGeometry<'_>,
    component_shape: i32,
) -> Option<Vec<ElementPart>> {
    match component_shape {
        catalog::SECTIONAL => Some(sectional(geometry)),
        catalog::OFFICE_CHAIR => Some(office_chair(geometry)),
        catalog::BAR_STOOL => Some(bar_stool(geometry)),
        catalog::OTTOMAN => Some(ottoman(geometry)),
        _ => None,
    }
}

/// Builds an L-shaped sectional, its chaise, arms, legs, and separate seat and back cushions.
fn sectional(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let seat = 0.95_f64.min(depth * 0.55);
    let chaise = 0.95_f64.min(width * 0.4);
    let arm = 0.16;
    let back = 0.26;
    let mut parts = vec![
        geometry.soft(
            (0.0, depth / 2.0 - seat / 2.0),
            (width, seat),
            (0.12, 0.38),
            0.04,
            Surface::Upholstery,
        ),
        geometry.soft(
            (-width / 2.0 + chaise / 2.0, -seat / 2.0),
            (chaise, depth - seat),
            (0.12, 0.38),
            0.04,
            Surface::Upholstery,
        ),
        geometry.soft(
            (arm / 2.0, depth / 2.0 - back / 2.0),
            (width - arm, back),
            (0.32, height),
            0.06,
            Surface::Upholstery,
        ),
        geometry.soft(
            (width / 2.0 - arm / 2.0, depth / 2.0 - seat / 2.0),
            (arm, seat),
            (0.20, height * 0.72),
            0.04,
            Surface::Upholstery,
        ),
        geometry.soft(
            (-width / 2.0 + arm / 2.0, 0.0),
            (arm, depth),
            (0.20, height * 0.72),
            0.04,
            Surface::Upholstery,
        ),
    ];
    for center in geometry.corners(0.08) {
        parts.push(geometry.block(center, (0.04, 0.04), (0.0, 0.12), Surface::DoorLeaf));
    }
    parts.push(geometry.soft(
        (
            -width / 2.0 + arm + (chaise - arm) / 2.0,
            -seat / 2.0 + 0.02,
        ),
        (chaise - arm - 0.02, depth - seat - 0.02),
        (0.38, 0.52),
        0.05,
        Surface::Upholstery,
    ));
    let run_start = -width / 2.0 + chaise;
    let run_length = width / 2.0 - arm - run_start;
    let count = ((run_length / 0.70).round_ties_even() as i32).clamp(1, 4);
    let cushion = (run_length - 0.03 * (count - 1) as f64) / count as f64;
    for index in 0..count {
        let x = run_start + cushion / 2.0 + index as f64 * (cushion + 0.03);
        parts.push(geometry.soft(
            (x, depth / 2.0 - back - (seat - back) / 2.0 + 0.02),
            (cushion, seat - back - 0.02),
            (0.38, 0.52),
            0.05,
            Surface::Upholstery,
        ));
    }
    let back_count = (((width - 2.0 * arm) / 0.75).round_ties_even() as i32).clamp(1, 5);
    let back_width = (width - 2.0 * arm - 0.03 * (back_count - 1) as f64) / back_count as f64;
    for index in 0..back_count {
        let x = -width / 2.0 + arm + back_width / 2.0 + index as f64 * (back_width + 0.03);
        parts.push(geometry.soft(
            (x, depth / 2.0 - back - 0.07),
            (back_width, 0.16),
            (0.50, height - 0.04),
            0.05,
            Surface::Upholstery,
        ));
    }
    parts
}

/// Builds an upholstered footstool on four short legs.
fn ottoman(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let mut parts = vec![geometry.soft(
        (0.0, 0.0),
        (geometry.width(), geometry.depth()),
        (0.06, geometry.element.height),
        0.06,
        Surface::Upholstery,
    )];
    for center in geometry.corners(0.07) {
        parts.push(geometry.block(center, (0.035, 0.035), (0.0, 0.06), Surface::DoorLeaf));
    }
    parts
}

/// Builds a five-star base, casters, upholstered seat and back, and armrests.
fn office_chair(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let radius = geometry.width().min(geometry.depth()) / 2.0 * 0.92;
    let mut parts = Vec::new();
    for index in 0..5 {
        let angle = PI / 2.0 + index as f64 * 2.0 * PI / 5.0;
        let point = (angle.cos() * radius, angle.sin() * radius);
        parts.push(geometry.bar((0.0, 0.0), point, 0.04, (0.05, 0.09), Surface::Metal));
        parts.push(geometry.round(point, 0.05, (0.0, 0.05), Surface::OpeningFrame));
    }
    parts.push(geometry.round((0.0, 0.0), 0.06, (0.09, 0.42), Surface::Metal));
    let seat = (geometry.width() * 0.8).min(0.52);
    parts.push(geometry.soft(
        (0.0, -0.02),
        (seat, seat * 0.95),
        (0.42, 0.50),
        0.05,
        Surface::Upholstery,
    ));
    parts.push(geometry.block(
        (0.0, seat * 0.46),
        (0.06, 0.04),
        (0.45, 0.64),
        Surface::Metal,
    ));
    parts.push(geometry.soft(
        (0.0, seat * 0.48),
        (seat * 0.9, 0.07),
        (0.60, geometry.element.height),
        0.03,
        Surface::Upholstery,
    ));
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (side * seat * 0.52, 0.0),
            (0.035, 0.035),
            (0.48, 0.66),
            Surface::Metal,
        ));
        parts.push(geometry.soft(
            (side * seat * 0.52, 0.0),
            (0.06, 0.26),
            (0.66, 0.69),
            0.02,
            Surface::OpeningFrame,
        ));
    }
    parts
}

/// Builds a round stool with a central column, segmented footrest, and padded seat.
fn bar_stool(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (diameter, height) = (
        geometry.width().min(geometry.depth()),
        geometry.element.height,
    );
    let mut parts = vec![
        geometry.round((0.0, 0.0), diameter * 0.9, (0.0, 0.02), Surface::Metal),
        geometry.round((0.0, 0.0), 0.05, (0.02, height - 0.06), Surface::Metal),
    ];
    let ring = diameter * 0.36;
    for index in 0..12 {
        let first = index as f64 * 2.0 * PI / 12.0;
        let next = (index + 1) as f64 * 2.0 * PI / 12.0;
        parts.push(geometry.bar(
            (first.cos() * ring, first.sin() * ring),
            (next.cos() * ring, next.sin() * ring),
            0.016,
            (height * 0.38, height * 0.38 + 0.016),
            Surface::Metal,
        ));
    }
    for angle in [0.0, PI / 2.0, PI, 3.0 * PI / 2.0] {
        parts.push(geometry.bar(
            (0.0, 0.0),
            (angle.cos() * ring, angle.sin() * ring),
            0.012,
            (height * 0.38, height * 0.38 + 0.012),
            Surface::Metal,
        ));
    }
    parts.push(geometry.round(
        (0.0, 0.0),
        diameter,
        (height - 0.06, height),
        Surface::Upholstery,
    ));
    parts
}
