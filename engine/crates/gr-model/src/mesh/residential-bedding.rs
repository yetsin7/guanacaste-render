//! Children's bed solids from the residential component catalog.

use super::element_part::ElementPart;
use super::residential_components::ComponentGeometry;
use super::scene_mesh::SurfaceKind as Surface;
use crate::document::component_shape as catalog;

/// Selects the bunk-bed and crib recipes by catalog identifier.
pub(super) fn parts(geometry: &ComponentGeometry<'_>, component_shape: i32) -> Option<Vec<ElementPart>> {
    match component_shape {
        catalog::BUNK_BED => Some(bunk_bed(geometry)),
        catalog::CRIB => Some(crib(geometry)),
        _ => None,
    }
}

/// Builds both bunk decks, bedding, guard rail, and ladder.
fn bunk_bed(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    const POST: f64 = 0.07;
    let mut parts = Vec::new();
    for center in geometry.corners(POST / 2.0) {
        parts.push(geometry.block(center, (POST, POST), (0.0, height), Surface::DoorLeaf));
    }
    let upper = 1.0_f64.max(height - 0.55);
    for deck in [0.20, upper] {
        for side in [-1.0, 1.0] {
            parts.push(geometry.block(
                (side * (width / 2.0 - 0.025), 0.0),
                (0.05, depth - 2.0 * POST),
                (deck, deck + 0.15),
                Surface::DoorLeaf,
            ));
            parts.push(geometry.block(
                (0.0, side * (depth / 2.0 - 0.025)),
                (width - 2.0 * POST, 0.05),
                (deck, deck + 0.30),
                Surface::DoorLeaf,
            ));
        }
        parts.push(geometry.block(
            (0.0, 0.0),
            (width - 0.1, depth - 0.1),
            (deck + 0.04, deck + 0.06),
            Surface::DoorLeaf,
        ));
        parts.push(geometry.soft(
            (0.0, 0.0),
            (width - 0.1, depth - 0.12),
            (deck + 0.06, deck + 0.22),
            0.05,
            Surface::Bedding,
        ));
        parts.push(geometry.soft(
            (0.0, -depth * 0.12),
            (width - 0.08, depth * 0.62),
            (deck + 0.10, deck + 0.25),
            0.05,
            Surface::Upholstery,
        ));
        parts.push(geometry.soft(
            (0.0, depth / 2.0 - 0.25),
            (width * 0.55, 0.32),
            (deck + 0.22, deck + 0.33),
            0.06,
            Surface::Bedding,
        ));
    }
    parts.push(geometry.block(
        (-width / 2.0 + 0.025, depth * 0.12),
        (0.04, depth * 0.6),
        (upper + 0.38, upper + 0.44),
        Surface::DoorLeaf,
    ));
    let ladder_y = -depth / 2.0 + 0.35;
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (-width / 2.0 - 0.02, ladder_y + side * 0.2),
            (0.04, 0.04),
            (0.0, upper + 0.45),
            Surface::DoorLeaf,
        ));
    }
    let mut z = 0.30;
    while z < upper + 0.3 {
        parts.push(geometry.block((-width / 2.0 - 0.02, ladder_y), (0.03, 0.40), (z, z + 0.03), Surface::DoorLeaf));
        z += 0.28;
    }
    parts
}

/// Builds a crib with corner posts, rails, regularly spaced slats, and a raised mattress.
fn crib(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    const POST: f64 = 0.05;
    let mut parts = Vec::new();
    for center in geometry.corners(POST / 2.0) {
        parts.push(geometry.block(center, (POST, POST), (0.0, height), Surface::DoorLeaf));
    }
    for levels in [(0.22, 0.27), (height - 0.05, height)] {
        for side in [-1.0, 1.0] {
            parts.push(geometry.block(
                (side * (width / 2.0 - 0.02), 0.0),
                (0.04, depth - 2.0 * POST),
                levels,
                Surface::DoorLeaf,
            ));
            parts.push(geometry.block(
                (0.0, side * (depth / 2.0 - 0.02)),
                (width - 2.0 * POST, 0.04),
                levels,
                Surface::DoorLeaf,
            ));
        }
    }
    for side in [-1.0, 1.0] {
        let mut y = -depth / 2.0 + POST + 0.06;
        while y < depth / 2.0 - POST - 0.03 {
            parts.push(geometry.block(
                (side * (width / 2.0 - 0.02), y),
                (0.02, 0.02),
                (0.27, height - 0.05),
                Surface::DoorLeaf,
            ));
            y += 0.07;
        }
        let mut x = -width / 2.0 + POST + 0.06;
        while x < width / 2.0 - POST - 0.03 {
            parts.push(geometry.block(
                (x, side * (depth / 2.0 - 0.02)),
                (0.02, 0.02),
                (0.27, height - 0.05),
                Surface::DoorLeaf,
            ));
            x += 0.07;
        }
    }
    parts.push(geometry.soft((0.0, 0.0), (width - 0.08, depth - 0.08), (0.30, 0.42), 0.04, Surface::Bedding));
    parts
}
