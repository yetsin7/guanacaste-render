//! Focused checks for the structural element recipes.

use super::*;
use crate::mesh::SceneMesh;
use crate::mesh::element_part::add_part;
use glam::Vec3;

/// Constructs a simple line element for the geometry checks.
fn element(kind: i32, style: i32, length: f64) -> StructuralElement {
    StructuralElement {
        kind,
        style,
        start: Point2::new(0.0, 0.0),
        end: Point2::new(length, 0.0),
        width: 0.1,
        height: 1.1,
        ..Default::default()
    }
}

/// Requires the test element to have a supported geometry recipe.
fn parts(element: &StructuralElement) -> Vec<ElementPart> {
    element_parts(element).expect("a supported element")
}

#[test]
/// Checks the spacing and elevations of the default railing members.
fn a_default_railing_has_posts_every_one_point_two_metres_and_two_rails() {
    let railing = parts(&element(kind::RAILING, style::DEFAULT, 3.6));
    // Four posts and two rails.
    assert_eq!(railing.len(), 6);
    assert!((railing[4].top - 0.55).abs() < 1e-12 && (railing[5].top - 1.1).abs() < 1e-12);
}

#[test]
/// Checks that railing styles create the expected infill and material.
fn each_railing_style_adds_its_own_infill() {
    let count = |style| parts(&element(kind::RAILING, style, 3.6)).len();
    // Three posts and a top rail, then: one glass pane; five cables on four posts; a bottom rail and pickets.
    assert_eq!(count(style::GLASS_PANEL), 3 + 1 + 1);
    assert_eq!(count(style::CABLE), 4 + 1 + 5);
    assert_eq!(count(style::SOLID_PARAPET), 2);
    assert_eq!(count(style::PICKETS), 3 + 1 + 1 + 31);
    let glass = parts(&element(kind::RAILING, style::GLASS_PANEL, 3.6));
    assert_eq!(glass.last().map(|part| part.kind), Some(SurfaceKind::Glass));
}

#[test]
/// Checks that a sloped guard follows the full requested rise.
fn a_sloped_railing_carries_its_rise_to_the_far_end() {
    let railing = parts(&StructuralElement {
        rise: 2.0,
        ..element(kind::RAILING, style::DEFAULT, 3.6)
    });
    let top_rail = &railing[3];
    assert_eq!(top_rail.end_top, Some(3.1));
    assert!((top_rail.top - 1.1).abs() < 1e-12);
}

#[test]
/// Checks the glass and breeze-block grid recipes.
fn a_curtain_wall_is_glass_behind_mullions_and_transoms() {
    let wall = parts(&StructuralElement {
        height: 3.0,
        ..element(kind::CURTAIN_WALL, style::DEFAULT, 4.5)
    });
    // One pane, four mullions (three bays of 1.5 m) and four transoms (three rows of 1.2 m, rounded up).
    assert_eq!(wall.len(), 1 + 4 + 4);
    assert_eq!(wall[0].kind, SurfaceKind::Glass);
    let screen = parts(&StructuralElement {
        height: 1.0,
        ..element(kind::CURTAIN_WALL, style::BREEZE_BLOCK, 2.0)
    });
    assert_eq!(screen.len(), 11 + 6);
    assert!(screen.iter().all(|part| part.kind == SurfaceKind::Wall));
}

#[test]
/// Distinguishes unsupported elements from annotations with no solid.
fn what_is_not_ported_is_reported_and_drafting_has_no_solid() {
    assert!(
        element_parts(&StructuralElement {
            component_shape: 999,
            ..element(kind::COMPONENT, 0, 0.0)
        })
        .is_none()
    );
    assert!(element_parts(&element(kind::ELEVATOR, 0, 2.0)).is_none());
    assert_eq!(
        element_parts(&element(kind::OPENING, 0, 2.0)),
        Some(Vec::new())
    );
}

#[test]
/// Checks the volume and elevation of a sloped ramp solid.
fn a_ramp_is_a_closed_wedge() {
    let ramp = parts(&StructuralElement {
        width: 1.5,
        height: 0.6,
        ..element(kind::RAMP, 0, 6.0)
    });
    let mut mesh = SceneMesh::default();
    add_part(&mut mesh, &ramp[0], 10.0);
    assert_eq!(mesh.triangle_count(), 12);
    let volume: f32 = (0..12)
        .map(|t| mesh.triangle(t))
        .map(|[a, b, c]| (a - Vec3::Z * 10.0).dot((b - Vec3::Z * 10.0).cross(c - Vec3::Z * 10.0)))
        .sum::<f32>()
        / 6.0;
    // A toe of 8 cm rising to 60 cm over 6 m, 1.5 m wide: the mean height is 0.34 m.
    assert!((volume - 6.0 * 1.5 * 0.34).abs() < 1e-3, "volume {volume}");
    let (min, max) = mesh.bounds();
    assert!((min.z - 10.0).abs() < 1e-6 && (max.z - 10.6).abs() < 1e-6);
}
