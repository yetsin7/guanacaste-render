//! Appliance and kitchen component solids matching the original application.

use super::element_part::ElementPart;
use super::residential_components::ComponentGeometry;
use super::scene_mesh::SurfaceKind as Surface;
use crate::document::component_shape as catalog;
use std::f64::consts::PI;

/// Selects the appliance recipes represented by catalog numbers in project files.
pub(super) fn parts(
    geometry: &ComponentGeometry<'_>,
    component_shape: i32,
) -> Option<Vec<ElementPart>> {
    match component_shape {
        catalog::REFRIGERATOR => Some(refrigerator(geometry)),
        catalog::RANGE => Some(range(geometry)),
        catalog::DISHWASHER
        | catalog::WASHER_DRYER
        | catalog::CLOTHES_DRYER
        | catalog::MICROWAVE => Some(appliance(
            geometry,
            component_shape == catalog::WASHER_DRYER || component_shape == catalog::CLOTHES_DRYER,
        )),
        catalog::KITCHEN_SINK => Some(kitchen_sink(geometry)),
        catalog::RANGE_HOOD => Some(range_hood(geometry)),
        catalog::WATER_HEATER => Some(water_heater(geometry)),
        _ => None,
    }
}

/// Builds a wall-mounted steel canopy, chimney, and filter face.
fn range_hood(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    vec![
        geometry.block((0.0, 0.0), (width, depth), (0.0, 0.06), Surface::Metal),
        geometry.block(
            (0.0, depth * 0.12),
            (width * 0.72, depth * 0.76),
            (0.06, 0.20),
            Surface::Metal,
        ),
        geometry.block(
            (0.0, depth / 2.0 - 0.14),
            (0.30_f64.min(width * 0.4), 0.26),
            (0.20, height),
            Surface::Metal,
        ),
        geometry.block(
            (0.0, 0.0),
            (width * 0.9, depth * 0.8),
            (-0.004, 0.0),
            Surface::OpeningFrame,
        ),
    ]
}

/// Builds a refrigerator body, split doors, and projecting handles.
fn refrigerator(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = vec![geometry.block(
        (0.0, 0.0),
        (width, depth),
        (0.0, height),
        Surface::OpeningFrame,
    )];
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (side * width * 0.25, -depth / 2.0 - 0.012),
            (width * 0.48, 0.024),
            (0.05, height - 0.05),
            Surface::Structure,
        ));
        parts.push(geometry.block(
            (side * width * 0.09, -depth / 2.0 - 0.035),
            (0.025, 0.025),
            (height * 0.42, height * 0.78),
            Surface::OpeningFrame,
        ));
    }
    parts
}

/// Builds a range body, cooktop, control panel, and four round burners.
fn range(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = vec![
        geometry.block(
            (0.0, 0.0),
            (width, depth),
            (0.0, height * 0.82),
            Surface::OpeningFrame,
        ),
        geometry.block(
            (0.0, 0.0),
            (width, depth),
            (height * 0.82, height * 0.87),
            Surface::Structure,
        ),
        geometry.block(
            (0.0, depth * 0.42),
            (width, depth * 0.14),
            (height * 0.87, height),
            Surface::OpeningFrame,
        ),
    ];
    for x in [-width * 0.24, width * 0.24] {
        for y in [-depth * 0.24, depth * 0.24] {
            parts.push(geometry.round(
                (x, y),
                width.min(depth) * 0.22,
                (height * 0.87, height * 0.89),
                Surface::Structure,
            ));
        }
    }
    parts
}

/// Builds a freestanding appliance and, for laundry machines, its round loading door.
fn appliance(geometry: &ComponentGeometry<'_>, laundry: bool) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = vec![
        geometry.block(
            (0.0, 0.0),
            (width, depth),
            (0.0, height),
            Surface::OpeningFrame,
        ),
        geometry.block(
            (0.0, -depth / 2.0 - 0.012),
            (width * 0.86, 0.024),
            (height * 0.12, height * 0.82),
            Surface::Structure,
        ),
    ];
    if laundry {
        parts.push(geometry.round(
            (0.0, -depth / 2.0 - 0.032),
            width * 0.52,
            (height * 0.22, height * 0.68),
            Surface::Glass,
        ));
    }
    parts
}

/// Builds the sink cabinet, open counter and steel bowl, and deck-mounted mixer.
fn kitchen_sink(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let toe = 0.10_f64.min(height / 6.0);
    let mut parts = vec![
        geometry.block(
            (0.0, 0.0),
            (width, depth),
            (toe, height - 0.24),
            Surface::DoorLeaf,
        ),
        geometry.block(
            (0.0, depth * 0.08),
            (width * 0.88, depth * 0.82),
            (0.0, toe),
            Surface::Structure,
        ),
    ];
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (side * width / 2.0 - side * 0.01, 0.0),
            (0.02, depth),
            (height - 0.24, height - 0.04),
            Surface::DoorLeaf,
        ));
        parts.push(geometry.block(
            (side * width * 0.25, -depth / 2.0 - 0.006),
            (width * 0.47, 0.012),
            (toe + 0.02, height - 0.08),
            Surface::DoorLeaf,
        ));
    }
    let bowl_width = (width - 0.20).min(0.80);
    let bowl_depth = (depth - 0.18).min(0.42);
    let bowl_y = -0.02;
    parts.extend(geometry.frame(
        (0.0, bowl_y),
        (width + 0.04, depth + 0.04),
        (bowl_width, bowl_depth),
        (height - 0.04, height),
        Surface::Floor,
    ));
    parts.extend(geometry.frame(
        (0.0, bowl_y),
        (bowl_width, bowl_depth),
        (bowl_width - 0.02, bowl_depth - 0.02),
        (height - 0.22, height - 0.04),
        Surface::Metal,
    ));
    parts.push(geometry.block(
        (0.0, bowl_y),
        (bowl_width, bowl_depth),
        (height - 0.23, height - 0.22),
        Surface::Metal,
    ));
    let faucet_y = bowl_y + bowl_depth / 2.0 + (depth / 2.0 - bowl_y - bowl_depth / 2.0) / 2.0;
    parts.extend(geometry.faucet(0.0, faucet_y, height, 0.22, 0.34));
    parts
}

/// Builds the heater's legs, tank, cap, controls, and pipe connections.
fn water_heater(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (diameter, height) = (
        geometry.width().min(geometry.depth()),
        geometry.element.height,
    );
    let mut parts = Vec::new();
    for angle in [PI / 2.0, PI * 7.0 / 6.0, PI * 11.0 / 6.0] {
        parts.push(geometry.block(
            (angle.cos() * diameter * 0.32, angle.sin() * diameter * 0.32),
            (0.03, 0.03),
            (0.0, 0.06),
            Surface::Metal,
        ));
    }
    parts.push(geometry.round(
        (0.0, 0.0),
        diameter,
        (0.06, height - 0.06),
        Surface::Ceramic,
    ));
    parts.push(geometry.round(
        (0.0, 0.0),
        diameter * 0.92,
        (height - 0.06, height),
        Surface::Metal,
    ));
    parts.push(geometry.block(
        (0.0, -diameter / 2.0 + 0.01),
        (0.12, 0.05),
        (0.22, 0.38),
        Surface::OpeningFrame,
    ));
    for side in [-1.0, 1.0] {
        parts.push(geometry.round(
            (side * diameter * 0.2, 0.0),
            0.025,
            (height, height + 0.30),
            Surface::Metal,
        ));
    }
    parts
}
