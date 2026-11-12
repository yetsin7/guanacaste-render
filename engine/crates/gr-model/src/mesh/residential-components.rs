//! Shared geometry for the residential catalog, matching the original component insertion and rotation rules.

use super::element_part::ElementPart;
use super::residential_furniture;
use super::scene_mesh::SurfaceKind;
use super::{
    residential_appliances, residential_bedding, residential_decor, residential_fixtures, residential_seating, residential_vehicles,
};
use crate::document::StructuralElement;
use crate::plan::Point2;
use std::f64::consts::PI;

/// Builds only recipes ported from the application; unknown catalog entries remain explicitly unsupported.
pub(super) fn residential_parts(element: &StructuralElement) -> Option<Vec<ElementPart>> {
    let geometry = ComponentGeometry { element };
    let shape = element.component_shape;
    let mut parts = residential_furniture::parts(&geometry, shape)
        .or_else(|| residential_appliances::parts(&geometry, shape))
        .or_else(|| residential_bedding::parts(&geometry, shape))
        .or_else(|| residential_decor::parts(&geometry, shape))
        .or_else(|| residential_fixtures::parts(&geometry, shape))
        .or_else(|| residential_seating::parts(&geometry, shape))
        .or_else(|| residential_vehicles::parts(&geometry, shape))?;
    if element.rotation.abs() >= 1e-12 {
        let (sin, cos) = element.rotation.sin_cos();
        let origin = element.start;
        for part in &mut parts {
            for point in &mut part.footprint {
                let (x, y) = (point.x - origin.x, point.y - origin.y);
                *point = Point2::new(origin.x + x * cos - y * sin, origin.y + x * sin + y * cos);
            }
        }
    }
    Some(parts)
}

/// Local dimensions and insertion point shared by component recipes.
pub(super) struct ComponentGeometry<'a> {
    pub element: &'a StructuralElement,
}

impl ComponentGeometry<'_> {
    /// Bounds dimensions away from zero exactly as the source application does.
    pub fn width(&self) -> f64 {
        self.element.width.max(0.02)
    }

    /// Uses the width for legacy components without a separate depth.
    pub fn depth(&self) -> f64 {
        (if self.element.depth > 0.0 {
            self.element.depth
        } else {
            self.element.width
        })
        .max(0.02)
    }

    /// Places a rectangular prism in component coordinates, before the instance is rotated.
    pub fn block(&self, center: (f64, f64), size: (f64, f64), levels: (f64, f64), kind: SurfaceKind) -> ElementPart {
        let (x, y) = (self.element.start.x + center.0, self.element.start.y + center.1);
        let (half_width, half_depth) = (size.0 / 2.0, size.1 / 2.0);
        ElementPart::block(
            vec![
                Point2::new(x - half_width, y - half_depth),
                Point2::new(x + half_width, y - half_depth),
                Point2::new(x + half_width, y + half_depth),
                Point2::new(x - half_width, y + half_depth),
            ],
            self.element.offset + levels.0,
            self.element.offset + levels.1,
            kind,
        )
    }

    /// Places a twelve-sided prism for round pots, stems, and fittings.
    pub fn round(&self, center: (f64, f64), diameter: f64, levels: (f64, f64), kind: SurfaceKind) -> ElementPart {
        let outline = (0..12)
            .map(|index| {
                let angle = 2.0 * PI * index as f64 / 12.0;
                Point2::new(
                    self.element.start.x + center.0 + angle.cos() * diameter / 2.0,
                    self.element.start.y + center.1 + angle.sin() * diameter / 2.0,
                )
            })
            .collect();
        ElementPart::block(outline, self.element.offset + levels.0, self.element.offset + levels.1, kind)
    }

    /// Gives upholstered pieces the same twelve rounded plan corners as the source geometry.
    pub fn soft(&self, center: (f64, f64), size: (f64, f64), levels: (f64, f64), radius: f64, kind: SurfaceKind) -> ElementPart {
        let radius = radius.min(size.0.min(size.1) * 0.3);
        let (x, y) = (self.element.start.x + center.0, self.element.start.y + center.1);
        let (width, depth) = size;
        let corners = [
            (x + width / 2.0 - radius, y - depth / 2.0 + radius, -PI / 2.0),
            (x + width / 2.0 - radius, y + depth / 2.0 - radius, 0.0),
            (x - width / 2.0 + radius, y + depth / 2.0 - radius, PI / 2.0),
            (x - width / 2.0 + radius, y - depth / 2.0 + radius, PI),
        ];
        let mut outline = Vec::with_capacity(12);
        for (corner_x, corner_y, start) in corners {
            for step in 0..=2 {
                let angle = start + step as f64 * PI / 4.0;
                outline.push(Point2::new(corner_x + radius * angle.cos(), corner_y + radius * angle.sin()));
            }
        }
        ElementPart::block(outline, self.element.offset + levels.0, self.element.offset + levels.1, kind)
    }

    /// Returns the four inset corners in the order used for legs and supports.
    pub fn corners(&self, inset: f64) -> [(f64, f64); 4] {
        let (x, y) = (self.width() / 2.0 - inset, self.depth() / 2.0 - inset);
        [(-x, -y), (-x, y), (x, -y), (x, y)]
    }

    /// Surrounds a rectangular opening with four separately closed strips.
    pub fn frame(
        &self,
        center: (f64, f64),
        outer: (f64, f64),
        inner: (f64, f64),
        levels: (f64, f64),
        kind: SurfaceKind,
    ) -> Vec<ElementPart> {
        let side = (outer.0 - inner.0) / 2.0;
        let end = (outer.1 - inner.1) / 2.0;
        vec![
            self.block((center.0, center.1 - inner.1 / 2.0 - end / 2.0), (outer.0, end), levels, kind),
            self.block((center.0, center.1 + inner.1 / 2.0 + end / 2.0), (outer.0, end), levels, kind),
            self.block((center.0 - inner.0 / 2.0 - side / 2.0, center.1), (side, inner.1), levels, kind),
            self.block((center.0 + inner.0 / 2.0 + side / 2.0, center.1), (side, inner.1), levels, kind),
        ]
    }

    /// Builds the escutcheon, riser, spout, and handle of a deck-mounted mixer.
    pub fn faucet(&self, x: f64, y: f64, deck: f64, reach: f64, rise: f64) -> Vec<ElementPart> {
        vec![
            self.round((x, y), 0.05, (deck, deck + 0.025), SurfaceKind::Metal),
            self.round((x, y), 0.028, (deck, deck + rise), SurfaceKind::Metal),
            self.block(
                (x, y - reach / 2.0),
                (0.024, reach),
                (deck + rise - 0.024, deck + rise),
                SurfaceKind::Metal,
            ),
            self.round(
                (x, y - reach + 0.012),
                0.03,
                (deck + rise - 0.06, deck + rise - 0.02),
                SurfaceKind::Metal,
            ),
            self.block(
                (x, y + 0.03),
                (0.015, 0.06),
                (deck + rise * 0.55, deck + rise * 0.55 + 0.015),
                SurfaceKind::Metal,
            ),
        ]
    }

    /// Places a narrow member between two plan points relative to the component insertion point.
    pub fn bar(&self, from: (f64, f64), to: (f64, f64), thickness: f64, levels: (f64, f64), kind: SurfaceKind) -> ElementPart {
        let (dx, dy) = (to.0 - from.0, to.1 - from.1);
        let length = (dx * dx + dy * dy).sqrt().max(1e-6);
        let (normal_x, normal_y) = (-dy / length * thickness / 2.0, dx / length * thickness / 2.0);
        let (x, y) = (self.element.start.x, self.element.start.y);
        ElementPart::block(
            vec![
                Point2::new(x + from.0 - normal_x, y + from.1 - normal_y),
                Point2::new(x + to.0 - normal_x, y + to.1 - normal_y),
                Point2::new(x + to.0 + normal_x, y + to.1 + normal_y),
                Point2::new(x + from.0 + normal_x, y + from.1 + normal_y),
            ],
            self.element.offset + levels.0,
            self.element.offset + levels.1,
            kind,
        )
    }
}
