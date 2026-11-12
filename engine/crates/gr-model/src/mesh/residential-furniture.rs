//! Residential furniture solids, ported from the application's parametric catalog.

use super::element_part::ElementPart;
use super::residential_components::ComponentGeometry;
use super::scene_mesh::SurfaceKind as Surface;
use crate::document::component_shape as catalog;

/// Dispatches the furniture recipes whose numeric identifiers are stored in project files.
pub(super) fn parts(
    geometry: &ComponentGeometry<'_>,
    component_shape: i32,
) -> Option<Vec<ElementPart>> {
    match component_shape {
        catalog::TABLE => Some(table(geometry)),
        catalog::CHAIR => Some(chair(geometry)),
        catalog::SOFA => Some(sofa(geometry)),
        catalog::BED => Some(bed(geometry)),
        catalog::PLANT => Some(plant(geometry)),
        catalog::CABINET | catalog::WALL_CABINET => {
            Some(cabinet(geometry, component_shape == catalog::WALL_CABINET))
        }
        catalog::DESK => Some(desk(geometry)),
        catalog::DRESSER => Some(dresser(geometry)),
        catalog::BOOKSHELF => Some(bookshelf(geometry)),
        catalog::TELEVISION => Some(television(geometry)),
        catalog::RUG => Some(vec![geometry.block(
            (0.0, 0.0),
            (geometry.width(), geometry.depth()),
            (0.0, geometry.element.height),
            Surface::Upholstery,
        )]),
        catalog::ROUND_TABLE => Some(round_table(geometry)),
        _ => None,
    }
}

/// Builds a television screen with its central pedestal and base.
fn television(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    vec![
        geometry.block(
            (0.0, 0.0),
            (width, depth),
            (0.0, height),
            Surface::OpeningFrame,
        ),
        geometry.block(
            (0.0, -depth / 2.0 - 0.006),
            (width * 0.94, 0.012),
            (height * 0.06, height * 0.94),
            Surface::Glass,
        ),
        geometry.block(
            (0.0, 0.0),
            (0.05, depth),
            (-0.18, 0.0),
            Surface::OpeningFrame,
        ),
        geometry.block(
            (0.0, 0.0),
            (width * 0.30, depth * 1.8),
            (-0.20, -0.18),
            Surface::OpeningFrame,
        ),
    ]
}

/// Builds a round tabletop, narrow column, and weighted round foot.
fn round_table(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (diameter, height) = (
        geometry.width().min(geometry.depth()),
        geometry.element.height,
    );
    vec![
        geometry.round(
            (0.0, 0.0),
            diameter,
            (height - 0.035, height),
            Surface::DoorLeaf,
        ),
        geometry.round(
            (0.0, 0.0),
            diameter * 0.12,
            (height - 0.06, height - 0.035),
            Surface::DoorLeaf,
        ),
        geometry.round((0.0, 0.0), 0.08, (0.04, height - 0.06), Surface::Metal),
        geometry.round((0.0, 0.0), diameter * 0.52, (0.0, 0.04), Surface::Metal),
    ]
}

/// Builds a tabletop and four inset legs with proportions scaled to its dimensions.
fn table(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let top = (height * 0.08).clamp(0.035, 0.08);
    let leg = (width.min(depth) * 0.08).clamp(0.035, 0.09);
    let mut parts = vec![geometry.block(
        (0.0, 0.0),
        (width, depth),
        (height - top, height),
        Surface::DoorLeaf,
    )];
    for center in geometry.corners(leg * 1.7) {
        parts.push(geometry.block(center, (leg, leg), (0.0, height - top), Surface::DoorLeaf));
    }
    parts
}

/// Builds a chair seat, four legs, and a back panel.
fn chair(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let seat = 0.48_f64.min(height * 0.55);
    let member = (width.min(depth) * 0.09).clamp(0.025, 0.05);
    let mut parts = vec![geometry.block(
        (0.0, 0.0),
        (width, depth),
        (seat - 0.07, seat),
        Surface::DoorLeaf,
    )];
    for center in geometry.corners(member * 1.5) {
        parts.push(geometry.block(
            center,
            (member, member),
            (0.0, seat - 0.07),
            Surface::DoorLeaf,
        ));
    }
    parts.push(geometry.block(
        (0.0, depth / 2.0 - member),
        (width, member * 1.4),
        (seat, height),
        Surface::DoorLeaf,
    ));
    parts
}

/// Builds an upholstered sofa, including its cushions and short legs.
fn sofa(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let arm = (width * 0.08).clamp(0.09, 0.18);
    let mut parts = vec![
        geometry.soft(
            (0.0, 0.0),
            (width, depth),
            (0.12, 0.38),
            0.04,
            Surface::Upholstery,
        ),
        geometry.soft(
            (0.0, depth * 0.34),
            (width, depth * 0.28),
            (0.32, height),
            0.06,
            Surface::Upholstery,
        ),
    ];
    for side in [-1.0, 1.0] {
        parts.push(geometry.soft(
            (side * (width / 2.0 - arm / 2.0), 0.0),
            (arm, depth),
            (0.20, height * 0.72),
            0.04,
            Surface::Upholstery,
        ));
    }
    for center in geometry.corners(0.08) {
        parts.push(geometry.block(center, (0.04, 0.04), (0.0, 0.12), Surface::DoorLeaf));
    }
    let count = ((width / 0.70).round_ties_even() as i32).clamp(1, 3);
    let cushion_width = (width - 2.0 * arm - 0.04 * (count - 1) as f64) / count as f64;
    for index in 0..count {
        let x = -width / 2.0 + arm + cushion_width / 2.0 + index as f64 * (cushion_width + 0.04);
        parts.push(geometry.soft(
            (x, -depth * 0.08),
            (cushion_width, depth * 0.58),
            (0.38, 0.52),
            0.05,
            Surface::Upholstery,
        ));
        parts.push(geometry.soft(
            (x, depth * 0.17),
            (cushion_width, 0.16),
            (0.50, height - 0.04),
            0.05,
            Surface::Upholstery,
        ));
    }
    parts
}

/// Builds the frame, mattress, duvet, headboard, and pillows of a bed.
fn bed(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = vec![
        geometry.block((0.0, 0.0), (width, depth), (0.08, 0.25), Surface::DoorLeaf),
        geometry.soft(
            (0.0, 0.0),
            (width * 0.96, depth * 0.91),
            (0.25, 0.48),
            0.06,
            Surface::Bedding,
        ),
        geometry.soft(
            (0.0, -depth * 0.12),
            (width, depth * 0.66),
            (0.30, 0.52),
            0.05,
            Surface::Upholstery,
        ),
        geometry.block(
            (0.0, depth / 2.0 - 0.055),
            (width, 0.11),
            (0.08, height),
            Surface::DoorLeaf,
        ),
    ];
    let count = if width >= 1.30 { 2 } else { 1 };
    for index in 0..count {
        let x = if count == 1 {
            0.0
        } else {
            (if index == 0 { -1.0 } else { 1.0 }) * width * 0.24
        };
        let pillow_width = width * if count == 1 { 0.55 } else { 0.38 };
        parts.push(geometry.soft(
            (x, depth * 0.30),
            (pillow_width, depth * 0.20),
            (0.48, 0.61),
            0.06,
            Surface::Bedding,
        ));
    }
    parts
}

/// Builds the pot, trunk, and three foliage masses of a small ornamental plant.
fn plant(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (diameter, height) = (
        geometry.width().min(geometry.depth()),
        geometry.element.height,
    );
    let pot_top = (height * 0.28).min(diameter * 0.75);
    vec![
        geometry.round(
            (0.0, 0.0),
            diameter * 0.58,
            (0.0, pot_top),
            Surface::DoorLeaf,
        ),
        geometry.round(
            (0.0, 0.0),
            0.025_f64.max(diameter * 0.10),
            (pot_top * 0.7, height * 0.78),
            Surface::DoorLeaf,
        ),
        geometry.round(
            (0.0, 0.0),
            diameter,
            (height * 0.48, height),
            Surface::Structure,
        ),
        geometry.round(
            (-diameter * 0.20, diameter * 0.12),
            diameter * 0.62,
            (height * 0.38, height * 0.78),
            Surface::Structure,
        ),
        geometry.round(
            (diameter * 0.22, -diameter * 0.10),
            diameter * 0.58,
            (height * 0.42, height * 0.82),
            Surface::Structure,
        ),
    ]
}

/// Builds a cabinet carcass, optional toe kick, top, and paired door fronts.
pub(super) fn cabinet(geometry: &ComponentGeometry<'_>, wall_mounted: bool) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let toe = if wall_mounted {
        0.0
    } else {
        0.10_f64.min(height / 6.0)
    };
    let mut parts = vec![geometry.block(
        (0.0, 0.0),
        (width, depth),
        (toe, height - 0.04),
        Surface::DoorLeaf,
    )];
    if toe > 0.0 {
        parts.push(geometry.block(
            (0.0, depth * 0.08),
            (width * 0.88, depth * 0.82),
            (0.0, toe),
            Surface::Structure,
        ));
    }
    parts.push(geometry.block(
        (0.0, 0.0),
        (width + 0.04, depth + 0.04),
        (height - 0.04, height),
        Surface::Floor,
    ));
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (side * width * 0.25, -depth / 2.0 - 0.006),
            (width * 0.47, 0.012),
            (toe + 0.02, height - 0.08),
            Surface::DoorLeaf,
        ));
    }
    parts
}

/// Builds a desktop, two supports, and a shallow drawer bank.
fn desk(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = vec![geometry.block(
        (0.0, 0.0),
        (width, depth),
        (height - 0.06, height),
        Surface::DoorLeaf,
    )];
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (side * width * 0.43, 0.0),
            (0.08, depth * 0.85),
            (0.0, height - 0.06),
            Surface::DoorLeaf,
        ));
    }
    parts.push(geometry.block(
        (width * 0.24, -depth * 0.08),
        (width * 0.34, depth * 0.72),
        (height * 0.46, height - 0.08),
        Surface::DoorLeaf,
    ));
    parts
}

/// Builds a dresser body with a top cap and three drawer fronts.
fn dresser(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let mut parts = vec![
        geometry.block(
            (0.0, 0.0),
            (width, depth),
            (0.06, height - 0.04),
            Surface::DoorLeaf,
        ),
        geometry.block(
            (0.0, 0.0),
            (width + 0.03, depth + 0.03),
            (height - 0.04, height),
            Surface::DoorLeaf,
        ),
    ];
    for index in 0..3 {
        let bottom = 0.10 + index as f64 * (height - 0.16) / 3.0;
        let top = 0.10 + (index + 1) as f64 * (height - 0.16) / 3.0 - 0.015;
        parts.push(geometry.block(
            (0.0, -depth / 2.0 - 0.008),
            (width * 0.90, 0.016),
            (bottom, top),
            Surface::Structure,
        ));
    }
    parts
}

/// Builds the side panels, back, and evenly spaced shelves of a bookcase.
fn bookshelf(geometry: &ComponentGeometry<'_>) -> Vec<ElementPart> {
    let (width, depth, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    const PANEL: f64 = 0.035;
    let mut parts = Vec::new();
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (side * (width / 2.0 - PANEL / 2.0), 0.0),
            (PANEL, depth),
            (0.0, height),
            Surface::DoorLeaf,
        ));
    }
    parts.push(geometry.block(
        (0.0, depth / 2.0 - PANEL / 2.0),
        (width, PANEL),
        (0.0, height),
        Surface::DoorLeaf,
    ));
    let count = ((height / 0.38).round_ties_even() as i32).max(3);
    for index in 0..=count {
        let level = height * index as f64 / count as f64;
        parts.push(geometry.block(
            (0.0, 0.0),
            (width, depth),
            (0.0_f64.max(level - PANEL), level),
            Surface::DoorLeaf,
        ));
    }
    parts
}
