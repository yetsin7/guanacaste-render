//! Lighting and outdoor component solids from the residential catalog.

use super::element_part::ElementPart;
use super::residential_components::ComponentGeometry;
use super::scene_mesh::SurfaceKind as Surface;
use crate::document::component_shape as catalog;

/// Selects ported lighting and outdoor catalog recipes.
pub(super) fn parts(
    geometry: &ComponentGeometry<'_>,
    component_shape: i32,
) -> Option<Vec<ElementPart>> {
    match component_shape {
        catalog::FLOOR_LAMP => Some(floor_lamp(geometry)),
        catalog::CEILING_LIGHT => Some(ceiling_light(geometry)),
        catalog::BARBECUE => Some(barbecue(geometry)),
        catalog::HAMMOCK => Some(hammock(geometry)),
        catalog::SUN_LOUNGER => Some(sun_lounger(geometry)),
        catalog::PARASOL => Some(parasol(geometry)),
        _ => None,
    }
}

/// Builds a floor lamp with a round base, stem, and luminous shade.
fn floor_lamp(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (diameter, height) = (
        geometry.width().min(geometry.depth()),
        geometry.element.height,
    );
    vec![
        geometry.round(
            (0.0, 0.0),
            diameter * 0.65,
            (0.0, 0.04),
            Surface::OpeningFrame,
        ),
        geometry.round(
            (0.0, 0.0),
            0.025,
            (0.04, height * 0.78),
            Surface::OpeningFrame,
        ),
        geometry.round(
            (0.0, 0.0),
            diameter,
            (height * 0.72, height),
            Surface::LightEmitter,
        ),
    ]
}

/// Builds the luminous diffuser and metal canopy of a ceiling light.
fn ceiling_light(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (diameter, height) = (
        geometry.width().min(geometry.depth()),
        geometry.element.height,
    );
    vec![
        geometry.round(
            (0.0, 0.0),
            diameter * 0.82,
            (0.0, height * 0.6),
            Surface::LightEmitter,
        ),
        geometry.round((0.0, 0.0), diameter, (height * 0.6, height), Surface::Metal),
    ]
}

/// Builds a barbecue cabinet, raised hood, side shelves, and legs.
fn barbecue(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = vec![
        geometry.block(
            (0.0, 0.0),
            (width * 0.62, depth),
            (0.42, height * 0.76),
            Surface::OpeningFrame,
        ),
        geometry.block(
            (0.0, depth * 0.10),
            (width * 0.62, depth * 0.70),
            (height * 0.76, height),
            Surface::Structure,
        ),
    ];
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (side * width * 0.41, 0.0),
            (width * 0.18, depth * 0.72),
            (height * 0.62, height * 0.68),
            Surface::OpeningFrame,
        ));
        parts.push(geometry.block(
            (side * width * 0.23, 0.0),
            (0.05, 0.05),
            (0.0, 0.42),
            Surface::OpeningFrame,
        ));
    }
    parts
}

/// Builds a timber hammock stand, segmented hanging fabric, and ropes.
fn hammock(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let post_x = width / 2.0 - 0.06;
    let mut parts = vec![geometry.block(
        (0.0, 0.0),
        (width - 0.1, 0.08),
        (0.0, 0.08),
        Surface::DoorLeaf,
    )];
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (side * post_x, 0.0),
            (0.10, depth),
            (0.0, 0.07),
            Surface::DoorLeaf,
        ));
        parts.push(geometry.block(
            (side * post_x, 0.0),
            (0.08, 0.08),
            (0.07, height),
            Surface::DoorLeaf,
        ));
    }
    let end = width * 0.30;
    let high = (height - 0.35).min(0.85);
    for index in 0..16 {
        let x0 = -end + 2.0 * end * index as f64 / 16.0;
        let x1 = -end + 2.0 * end * (index + 1) as f64 / 16.0;
        let middle = (x0 + x1) / 2.0 / end;
        let z = 0.45 + (high - 0.45) * middle * middle;
        let spread = depth * 0.78 * (1.0 - 0.55 * middle * middle);
        parts.push(geometry.block(
            ((x0 + x1) / 2.0, 0.0),
            (x1 - x0 + 0.01, spread),
            (z, z + 0.015),
            Surface::Upholstery,
        ));
    }
    for side in [-1.0, 1.0] {
        for step in 0..8 {
            let t = (step as f64 + 0.5) / 8.0;
            let x = side * (post_x - (post_x - end) * t);
            let z = height - 0.08 - (height - 0.08 - high) * t;
            parts.push(geometry.block(
                (x, 0.0),
                ((post_x - end) / 8.0 + 0.01, 0.012),
                (z - 0.012, z),
                Surface::Bark,
            ));
        }
    }
    parts
}

/// Builds the legs, horizontal bed, cushion, and stepped raised back of a sun lounger.
fn sun_lounger(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = Vec::new();
    for center in geometry.corners(0.06) {
        parts.push(geometry.block(center, (0.04, 0.04), (0.0, 0.30), Surface::DoorLeaf));
    }
    let hinge = depth * 0.18;
    let center = (-depth / 2.0 + hinge) / 2.0;
    parts.push(geometry.block(
        (0.0, center),
        (width, hinge + depth / 2.0),
        (0.30, 0.34),
        Surface::DoorLeaf,
    ));
    parts.push(geometry.soft(
        (0.0, center),
        (width - 0.06, hinge + depth / 2.0 - 0.04),
        (0.34, 0.40),
        0.04,
        Surface::Upholstery,
    ));
    let back = depth / 2.0 - hinge;
    for index in 0..7 {
        let y = hinge + back * (index as f64 + 0.5) / 7.0;
        let z = 0.40 + (height - 0.40) * (index + 1) as f64 / 7.0;
        parts.push(geometry.soft(
            (0.0, y),
            (width - 0.06, back / 7.0 + 0.01),
            (z - 0.10, z),
            0.02,
            Surface::Upholstery,
        ));
    }
    parts
}

/// Builds a weighted base, pole, stepped canopy, and finial for a patio umbrella.
fn parasol(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (diameter, height) = (
        geometry.width().min(geometry.depth()),
        geometry.element.height,
    );
    let mut parts = vec![
        geometry.round((0.0, 0.0), 0.45, (0.0, 0.08), Surface::Structure),
        geometry.round((0.0, 0.0), 0.04, (0.08, height), Surface::DoorLeaf),
    ];
    let bottom = height - 0.48;
    for tier in 0..6 {
        parts.push(geometry.round(
            (0.0, 0.0),
            diameter * (1.0 - tier as f64 * 0.16),
            (
                bottom + tier as f64 * 0.07,
                bottom + tier as f64 * 0.07 + 0.07,
            ),
            Surface::Upholstery,
        ));
    }
    parts.push(geometry.round((0.0, 0.0), 0.05, (height, height + 0.08), Surface::DoorLeaf));
    parts
}
