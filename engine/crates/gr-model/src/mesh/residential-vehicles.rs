//! Procedural residential vehicle solids ported from the original catalog.

use super::element_part::ElementPart;
use super::residential_components::ComponentGeometry;
use super::scene_mesh::SurfaceKind as Surface;
use crate::document::component_shape as catalog;

/// Selects the sedan geometry and paint associated with its catalog identifier.
pub(super) fn parts(
    geometry: &ComponentGeometry<'_>,
    component_shape: i32,
) -> Option<Vec<ElementPart>> {
    let paint = match component_shape {
        catalog::CAR => Surface::CarPaint,
        catalog::SILVER_CAR => Surface::CarPaintSilver,
        _ => return None,
    };
    Some(sedan(geometry, paint))
}

/// Builds the tires, wheel arches, body, stepped glass cabin, roof, and lights of a parked sedan.
fn sedan(geometry: &ComponentGeometry<'_>, paint: Surface) -> Vec<ElementPart> {
    let (width, length, height) = (geometry.width(), geometry.depth(), geometry.element.height);
    let axle = length / 2.0 - length * 0.19;
    const TYRE: f64 = 0.66;
    const TREAD: f64 = 0.22;
    let mut parts = Vec::new();
    for y in [-axle, axle] {
        for side in [-1.0, 1.0] {
            let x = side * (width / 2.0 - TREAD / 2.0 - 0.01);
            for slice in 0..6 {
                let middle = (slice as f64 + 0.5) / 6.0 * TYRE - TYRE / 2.0;
                let chord = 2.0 * (TYRE * TYRE / 4.0 - middle * middle).sqrt();
                parts.push(geometry.block(
                    (x, y),
                    (TREAD, chord),
                    (slice as f64 * TYRE / 6.0, (slice + 1) as f64 * TYRE / 6.0),
                    Surface::Rubber,
                ));
            }
            parts.push(geometry.block(
                (x + side * 0.005, y),
                (TREAD, 0.36),
                (TYRE / 2.0 - 0.12, TYRE / 2.0 + 0.12),
                Surface::Metal,
            ));
        }
    }
    const SILL: f64 = 0.20;
    const ARCH: f64 = 0.48;
    let arch_half = TYRE / 2.0 + 0.07;
    let front = -length / 2.0;
    for (from, to) in [
        (front + 0.04, -axle - arch_half),
        (-axle + arch_half, axle - arch_half),
        (axle + arch_half, length / 2.0 - 0.04),
    ] {
        parts.push(geometry.block(
            (0.0, (from + to) / 2.0),
            (width - 0.04, to - from),
            (SILL, ARCH),
            paint,
        ));
    }
    parts.push(geometry.block(
        (0.0, 0.0),
        (width - 0.02, length - 0.06),
        (ARCH, height * 0.53),
        paint,
    ));
    parts.push(geometry.block(
        (0.0, 0.05),
        (width - 0.10, length - 0.30),
        (height * 0.53, height * 0.57),
        paint,
    ));
    let glass_bottom = height * 0.57;
    let roof = height - 0.05;
    for band in 0..5 {
        let t = (band as f64 + 0.5) / 5.0;
        let start = front + length * 0.33 + length * 0.13 * t;
        let end = length / 2.0 - length * 0.22 - length * 0.08 * t;
        let bottom = glass_bottom + (roof - glass_bottom) * band as f64 / 5.0;
        let top = glass_bottom + (roof - glass_bottom) * (band + 1) as f64 / 5.0;
        parts.push(geometry.block(
            (0.0, (start + end) / 2.0),
            (width - 0.14 - 0.16 * t, end - start),
            (bottom, top),
            Surface::Glass,
        ));
    }
    let cabin_start = front + length * 0.36;
    let cabin_end = length / 2.0 - length * 0.26;
    parts.push(geometry.block(
        (0.0, (cabin_start + cabin_end) / 2.0),
        (width - 0.40, cabin_end - cabin_start),
        (glass_bottom, roof - 0.08),
        Surface::Rubber,
    ));
    let roof_start = front + length * 0.46;
    let roof_end = length / 2.0 - length * 0.30;
    parts.push(geometry.block(
        (0.0, (roof_start + roof_end) / 2.0),
        (width - 0.32, roof_end - roof_start),
        (roof, height),
        paint,
    ));
    for side in [-1.0, 1.0] {
        parts.push(geometry.block(
            (side * (width / 2.0 - 0.22), front + 0.03),
            (0.30, 0.04),
            (ARCH + 0.06, ARCH + 0.16),
            Surface::Mirror,
        ));
        parts.push(geometry.block(
            (side * (width / 2.0 - 0.20), length / 2.0 - 0.03),
            (0.34, 0.04),
            (ARCH + 0.08, ARCH + 0.18),
            Surface::FireRed,
        ));
    }
    parts
}
