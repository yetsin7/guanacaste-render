//! Bathroom fixture solids ported from the application's residential catalog.

use super::element_part::ElementPart;
use super::residential_components::ComponentGeometry;
use super::residential_furniture::cabinet;
use super::scene_mesh::SurfaceKind as Surface;
use crate::document::component_shape as catalog;
use crate::plan::Point2;
use std::f64::consts::TAU;

/// Selects the supported bathroom fixture recipes by their project catalog identifiers.
pub(super) fn parts(
    geometry: &ComponentGeometry<'_>,
    component_shape: i32,
) -> Option<Vec<ElementPart>> {
    match component_shape {
        catalog::TOILET => Some(toilet(geometry)),
        catalog::VANITY => Some(vanity(geometry)),
        catalog::BATHTUB => Some(bathtub(geometry)),
        catalog::SHOWER => Some(shower(geometry)),
        catalog::PEDESTAL_SINK => Some(pedestal_sink(geometry)),
        catalog::BIDET => Some(bidet(geometry)),
        catalog::URINAL => Some(urinal(geometry)),
        catalog::MIRROR => Some(mirror(geometry)),
        _ => None,
    }
}

/// Forms the four walls and bottom of a bathtub while preserving its open interior.
fn bathtub(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let wall = 0.08;
    let rim = 0.12;
    vec![
        geometry.block(
            (0.0, -depth / 2.0 + wall / 2.0),
            (width, wall),
            (0.0, height),
            Surface::Ceramic,
        ),
        geometry.block(
            (0.0, depth / 2.0 - wall / 2.0),
            (width, wall),
            (0.0, height),
            Surface::Ceramic,
        ),
        geometry.block(
            (-width / 2.0 + wall / 2.0, 0.0),
            (wall, depth - 2.0 * wall),
            (0.0, height),
            Surface::Ceramic,
        ),
        geometry.block(
            (width / 2.0 - wall / 2.0, 0.0),
            (wall, depth - 2.0 * wall),
            (0.0, height),
            Surface::Ceramic,
        ),
        geometry.block(
            (0.0, 0.0),
            (width - 2.0 * rim, depth - 2.0 * rim),
            (0.0, 0.05),
            Surface::Ceramic,
        ),
    ]
}

/// Builds a toilet pedestal, open bowl, seat, cistern, and exposed fittings.
fn toilet(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let seat = (height * 0.57).min(0.43);
    let mut parts = vec![
        geometry.soft(
            (0.0, depth * 0.03),
            (width * 0.58, depth * 0.40),
            (0.0, seat * 0.72),
            0.07,
            Surface::Ceramic,
        ),
        geometry.soft(
            (0.0, -depth * 0.08),
            (width * 0.86, depth * 0.60),
            (seat * 0.65, seat * 0.78),
            0.08,
            Surface::Ceramic,
        ),
    ];
    parts.extend(toilet_rim(
        geometry,
        (width * 0.49, depth * 0.31),
        (width * 0.34, depth * 0.21),
        -depth * 0.08,
        (seat * 0.76, seat),
        Surface::Ceramic,
    ));
    parts.extend(toilet_rim(
        geometry,
        (width * 0.45, depth * 0.28),
        (width * 0.32, depth * 0.19),
        -depth * 0.08,
        (seat, seat + 0.025),
        Surface::OpeningFrame,
    ));
    let tank_top = (seat + 0.06).max(height - 0.025);
    parts.push(geometry.soft(
        (0.0, depth * 0.34),
        (width * 0.93, depth * 0.24),
        (seat * 0.78, tank_top),
        0.035,
        Surface::Ceramic,
    ));
    parts.push(geometry.soft(
        (0.0, depth * 0.34),
        (width * 0.97, depth * 0.25),
        (tank_top, height),
        0.03,
        Surface::Ceramic,
    ));
    parts.push(geometry.round(
        (0.0, depth * 0.34),
        0.045,
        (height, height + 0.008),
        Surface::Metal,
    ));
    parts.push(geometry.round(
        (0.0, depth / 2.0 - 0.06),
        0.035,
        (0.18, 0.23),
        Surface::Metal,
    ));
    parts
}

/// Forms the open elliptical rim from 24 closed sectors, in the application's winding order.
fn toilet_rim(
    geometry: &ComponentGeometry<'_>,
    outer: (f64, f64),
    inner: (f64, f64),
    center_y: f64,
    levels: (f64, f64),
    surface: Surface,
) -> Vec<ElementPart> {
    let mut parts = Vec::with_capacity(24);
    for index in 0..24 {
        let first = index as f64 * TAU / 24.0;
        let next = (index + 1) as f64 * TAU / 24.0;
        let point = |radii: (f64, f64), angle: f64| {
            Point2::new(
                geometry.element.start.x + radii.0 * angle.cos(),
                geometry.element.start.y + center_y + radii.1 * angle.sin(),
            )
        };
        parts.push(ElementPart::block(
            vec![
                point(outer, first),
                point(outer, next),
                point(inner, next),
                point(inner, first),
            ],
            geometry.element.offset + levels.0,
            geometry.element.offset + levels.1,
            surface,
        ));
    }
    parts
}

/// Adds one or two vessel basins and their mixers to the standard vanity cabinet.
fn vanity(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = cabinet(geometry, false);
    let basins = if width >= 1.3 {
        vec![-width / 4.0, width / 4.0]
    } else {
        vec![0.0]
    };
    let basin_width = (width / basins.len() as f64 * 0.62).min(0.50);
    for x in basins {
        parts.push(geometry.soft(
            (x, -depth * 0.06),
            (basin_width, depth * 0.62),
            (height, height + 0.02),
            0.08,
            Surface::Ceramic,
        ));
        parts.extend(geometry.frame(
            (x, -depth * 0.06),
            (basin_width, depth * 0.62),
            (basin_width - 0.03, depth * 0.62 - 0.03),
            (height + 0.02, height + 0.14),
            Surface::Ceramic,
        ));
        parts.extend(geometry.faucet(x, depth * 0.36, height, 0.14, 0.24));
    }
    parts
}

/// Builds a shower tray and two transparent enclosure panels.
fn shower(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    vec![
        geometry.block((0.0, 0.0), (width, depth), (0.0, 0.08), Surface::Ceramic),
        geometry.block(
            (0.0, depth / 2.0 - 0.01),
            (width, 0.02),
            (0.08, height),
            Surface::Glass,
        ),
        geometry.block(
            (width / 2.0 - 0.01, 0.0),
            (0.02, depth),
            (0.08, height),
            Surface::Glass,
        ),
    ]
}

/// Builds a pedestal, an open ceramic basin, and the mixer on its rear deck.
fn pedestal_sink(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let basin = height - 0.16;
    let mut parts = vec![
        geometry.soft(
            (0.0, depth * 0.08),
            (width * 0.32, depth * 0.40),
            (0.0, basin),
            0.06,
            Surface::Ceramic,
        ),
        geometry.soft(
            (0.0, 0.0),
            (width, depth),
            (basin, basin + 0.05),
            0.08,
            Surface::Ceramic,
        ),
    ];
    parts.extend(geometry.frame(
        (0.0, -0.01),
        (width, depth - 0.02),
        (width * 0.66, depth * 0.52),
        (basin + 0.05, height),
        Surface::Ceramic,
    ));
    parts.extend(geometry.faucet(0.0, depth / 2.0 - 0.06, height, 0.10, 0.14));
    parts
}

/// Builds a bidet pedestal, open bowl, and mixer.
fn bidet(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = vec![
        geometry.soft(
            (0.0, depth * 0.06),
            (width * 0.6, depth * 0.55),
            (0.0, height - 0.10),
            0.06,
            Surface::Ceramic,
        ),
        geometry.soft(
            (0.0, 0.0),
            (width, depth),
            (height - 0.10, height - 0.07),
            0.12,
            Surface::Ceramic,
        ),
    ];
    parts.extend(geometry.frame(
        (0.0, -0.02),
        (width, depth - 0.04),
        (width * 0.7, depth * 0.62),
        (height - 0.07, height),
        Surface::Ceramic,
    ));
    parts.extend(geometry.faucet(0.0, depth / 2.0 - 0.05, height, 0.07, 0.08));
    parts
}

/// Builds a wall-hung urinal and its visible flush valve.
fn urinal(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = vec![
        geometry.block(
            (0.0, depth / 2.0 - 0.04),
            (width, 0.08),
            (0.0, height),
            Surface::Ceramic,
        ),
        geometry.soft(
            (0.0, 0.0),
            (width, depth),
            (0.0, 0.06),
            0.10,
            Surface::Ceramic,
        ),
    ];
    for side in [-1.0, 1.0] {
        parts.push(geometry.soft(
            (side * (width / 2.0 - 0.03), 0.0),
            (0.06, depth - 0.02),
            (0.06, height * 0.85),
            0.03,
            Surface::Ceramic,
        ));
    }
    parts.push(geometry.block(
        (0.0, -depth / 2.0 + 0.03),
        (width - 0.08, 0.06),
        (0.06, 0.24),
        Surface::Ceramic,
    ));
    parts.push(geometry.round(
        (0.0, depth / 2.0 - 0.03),
        0.03,
        (height, height + 0.38),
        Surface::Metal,
    ));
    parts.push(geometry.round(
        (0.0, depth / 2.0 - 0.06),
        0.07,
        (height + 0.20, height + 0.30),
        Surface::Metal,
    ));
    parts
}

/// Builds a thin metal frame and the silvered face of a wall mirror.
fn mirror(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    vec![
        geometry.block(
            (0.0, 0.004),
            (width, depth - 0.008),
            (0.0, height),
            Surface::Metal,
        ),
        geometry.block(
            (0.0, -depth / 2.0 + 0.003),
            (width - 0.03, 0.006),
            (0.015, height - 0.015),
            Surface::Mirror,
        ),
    ]
}
