//! Stairs: how many risers a stair has, and the solids of stairs with a landing (L and U) and of spiral stairs.
//!
//! Ported from `architecture/stair-geometry.cs`.

use super::element_part::ElementPart;
use super::scene_mesh::SurfaceKind;
use crate::document::{StructuralElement, element_kind, element_style};
use crate::plan::{Point2, lerp, point_footprint, strip};
use std::f64::consts::PI;

/// Tallest riser used when a stair does not ask for one.
const MAXIMUM_RISER: f64 = 0.175;
/// Going of one step, used as the least run of a first flight.
const TREAD: f64 = 0.28;
const TREAD_THICKNESS: f64 = 0.05;
/// Clear gap between the two flights of a U-shaped stair.
const SWITCHBACK_GAP: f64 = 0.10;
const COLUMN_RADIUS: f64 = 0.10;

/// One straight run of steps.
#[derive(Clone, Copy)]
struct Flight {
    start: Point2,
    end: Point2,
    bottom: f64,
    rise: f64,
    risers: i32,
}

/// Risers of a stair: its height divided by the requested riser, or by the tallest comfortable one; at least two.
pub fn riser_count(stair: &StructuralElement) -> i32 {
    let riser = if stair.riser_height > 0.0 {
        stair.riser_height
    } else {
        MAXIMUM_RISER
    };
    ((stair.height / riser - 1e-9).ceil() as i32).max(2)
}

/// Whether the stair is more than one straight run: an L, a U or a spiral.
pub fn is_composite(stair: &StructuralElement) -> bool {
    stair.kind == element_kind::STAIR
        && matches!(
            stair.style,
            element_style::L_SHAPED | element_style::U_SHAPED | element_style::SPIRAL
        )
}

/// Moves a plan point along a direction by a signed distance.
fn offset(point: Point2, direction: Point2, distance: f64) -> Point2 {
    Point2::new(point.x + direction.x * distance, point.y + direction.y * distance)
}

/// Unit vector along the stair and the one to its left.
fn axes(stair: &StructuralElement) -> (Point2, Point2) {
    let length = stair.length().max(1e-9);
    let direction = Point2::new((stair.end.x - stair.start.x) / length, (stair.end.y - stair.start.y) / length);
    (direction, Point2::new(-direction.y, direction.x))
}

/// The flights of an L or U stair: a first run to the landing, then a second one to the left or back.
fn flights(stair: &StructuralElement) -> Vec<Flight> {
    let risers = riser_count(stair);
    let riser = stair.height / risers as f64;
    let (direction, left) = axes(stair);
    let width = stair.width;
    let first_run = TREAD.max(stair.length() - width);
    let first_risers = risers / 2;
    let second_risers = risers - first_risers;
    let going = first_run / first_risers as f64;
    let landing_top = stair.offset + first_risers as f64 * riser;
    let first = Flight {
        start: stair.start,
        end: offset(stair.start, direction, first_run),
        bottom: stair.offset,
        rise: first_risers as f64 * riser,
        risers: first_risers,
    };
    let second_rise = second_risers as f64 * riser;
    let second = if stair.style == element_style::L_SHAPED {
        let landing_center = offset(stair.start, direction, first_run + width / 2.0);
        let start = offset(landing_center, left, width / 2.0);
        Flight {
            start,
            end: offset(start, left, second_risers as f64 * going),
            bottom: landing_top,
            rise: second_rise,
            risers: second_risers,
        }
    } else {
        let start = offset(offset(stair.start, direction, first_run), left, width + SWITCHBACK_GAP);
        Flight {
            start,
            end: offset(start, direction, -(second_risers as f64) * going),
            bottom: landing_top,
            rise: second_rise,
            risers: second_risers,
        }
    };
    vec![first, second]
}

/// The solids of an L, U or spiral stair.
pub fn composite_parts(stair: &StructuralElement) -> Vec<ElementPart> {
    if stair.style == element_style::SPIRAL {
        return spiral_parts(stair);
    }
    if stair.length() < 0.05 {
        return Vec::new();
    }
    let flights = flights(stair);
    let mut parts = Vec::new();
    for flight in &flights {
        let riser = flight.rise / flight.risers as f64;
        for index in 0..flight.risers {
            let from = lerp(flight.start, flight.end, index as f64 / flight.risers as f64);
            let to = lerp(flight.start, flight.end, (index + 1) as f64 / flight.risers as f64);
            let top = flight.bottom + riser * (index + 1) as f64;
            parts.push(ElementPart::block(
                strip(from, to, 0.0, stair.width),
                flight.bottom,
                top,
                SurfaceKind::Structure,
            ));
        }
    }
    parts.push(landing(stair, &flights[0]));
    parts
}

/// The landing between the two flights: as wide as the stair, reaching sideways to the second flight.
fn landing(stair: &StructuralElement, first: &Flight) -> ElementPart {
    let (direction, left) = axes(stair);
    let top = first.bottom + first.rise;
    let near = first.end;
    let far = offset(near, direction, stair.width);
    let reach = if stair.style == element_style::U_SHAPED {
        stair.width * 1.5 + SWITCHBACK_GAP
    } else {
        stair.width / 2.0
    };
    let outline = vec![
        offset(near, left, -stair.width / 2.0),
        offset(far, left, -stair.width / 2.0),
        offset(far, left, reach),
        offset(near, left, reach),
    ];
    ElementPart::block(outline, stair.offset.max(top - 0.20), top, SurfaceKind::Structure)
}

/// Wedge-shaped treads winding around a central column.
fn spiral_parts(stair: &StructuralElement) -> Vec<ElementPart> {
    let risers = riser_count(stair);
    let center = stair.start;
    let radius = (stair.width / 2.0).max(0.6);
    let start_angle = if stair.length() > 1e-6 {
        (stair.end.y - stair.start.y).atan2(stair.end.x - stair.start.x)
    } else {
        0.0
    };
    // About 22 cm of going (or the requested tread) on the walking line at 60 % of the radius, between 15 and 40
    // degrees per tread.
    let going = if stair.tread_depth > 0.0 { stair.tread_depth } else { 0.22 };
    let step = (going / (radius * 0.6)).clamp(15.0 * PI / 180.0, 40.0 * PI / 180.0);
    let riser = stair.height / risers as f64;
    const ARC_STEPS: i32 = 4;
    let on_circle = |r: f64, angle: f64| Point2::new(center.x + r * angle.cos(), center.y + r * angle.sin());
    let mut parts = Vec::new();
    for index in 0..risers {
        let a = start_angle + step * index as f64;
        let mut outline: Vec<Point2> = (0..=ARC_STEPS)
            .map(|k| on_circle(radius, a + step * k as f64 / ARC_STEPS as f64))
            .collect();
        outline.push(on_circle(COLUMN_RADIUS, a + step));
        outline.push(on_circle(COLUMN_RADIUS, a));
        let top = stair.offset + riser * (index + 1) as f64;
        parts.push(ElementPart::block(outline, top - TREAD_THICKNESS, top, SurfaceKind::Structure));
    }
    parts.push(ElementPart::block(
        point_footprint(center, COLUMN_RADIUS * 2.0, true),
        stair.offset,
        stair.offset + stair.height + 0.9,
        SurfaceKind::OpeningFrame,
    ));
    parts
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Constructs a stair with fixed dimensions for shape checks.
    fn stair(style: i32, length: f64) -> StructuralElement {
        StructuralElement {
            kind: element_kind::STAIR,
            start: Point2::new(0.0, 0.0),
            end: Point2::new(length, 0.0),
            width: 1.0,
            height: 3.0,
            style,
            ..Default::default()
        }
    }

    #[test]
    /// Checks default and explicit riser heights, including the minimum count.
    fn a_three_metre_stair_has_eighteen_risers_unless_asked_otherwise() {
        assert_eq!(riser_count(&stair(0, 4.0)), 18);
        assert_eq!(
            riser_count(&StructuralElement {
                riser_height: 0.15,
                ..stair(0, 4.0)
            }),
            20
        );
        assert_eq!(
            riser_count(&StructuralElement {
                height: 0.1,
                ..stair(0, 4.0)
            }),
            2
        );
    }

    #[test]
    /// Checks the landing elevation and direction of the second flight.
    fn an_l_stair_turns_left_at_a_landing_and_reaches_its_height() {
        let parts = composite_parts(&stair(element_style::L_SHAPED, 4.0));
        // Eighteen steps and the landing.
        assert_eq!(parts.len(), 19);
        let highest = parts.iter().map(|part| part.top).fold(0.0, f64::max);
        assert!((highest - 3.0).abs() < 1e-12);
        // The second flight runs north (to the left of an east-bound stair).
        assert!(parts[17].footprint.iter().all(|point| point.y > 0.4));
        assert!((parts[18].top - 1.5).abs() < 1e-12);
    }

    #[test]
    /// Checks that the return flight runs beside the first one.
    fn a_u_stair_comes_back_beside_its_first_flight() {
        let parts = composite_parts(&stair(element_style::U_SHAPED, 4.0));
        let last_step = &parts[17];
        assert!(last_step.footprint.iter().all(|point| point.y > 0.5 && point.x < 3.1));
    }

    #[test]
    /// Checks the tread count and central support of a spiral stair.
    fn a_spiral_stair_has_a_tread_per_riser_and_a_column() {
        let parts = composite_parts(&stair(element_style::SPIRAL, 0.0));
        assert_eq!(parts.len(), 19);
        assert_eq!(parts[0].footprint.len(), 7);
        assert_eq!(parts[18].kind, SurfaceKind::OpeningFrame);
    }
}
