//! Rainfall runoff and storm-pipe sizing used by the original plumbing analysis.

use super::builder::with_overhang;
use crate::document::{Document, Id, element_kind, floor, plumbing_point_kind};
use crate::plan::PlanRectangle;
use std::collections::HashMap;

/// Inside and outside diameters in millimetres for the available storm-drain sizes.
pub(super) const STORM_SIZES: [(f64, f64); 5] = [
    (72.0, 89.0),
    (104.0, 114.0),
    (152.0, 168.0),
    (200.0, 219.0),
    (250.0, 273.0),
];

/// Distributes each roof's design runoff evenly among the downspouts near its extended boundary.
pub(super) fn downspout_flows(document: &Document) -> HashMap<Id, f64> {
    let spouts: Vec<_> = document
        .structural_elements
        .iter()
        .filter(|element| element.kind == element_kind::DOWNSPOUT)
        .collect();
    let mut flows: HashMap<Id, f64> = spouts.iter().map(|spout| (spout.id, 0.0)).collect();
    let rainfall = document
        .plumbing_points
        .iter()
        .find(|point| point.kind == plumbing_point_kind::STORM_OUTLET)
        .map_or(150.0, |point| point.rainfall_mm_per_hour);
    for roof in document
        .floors
        .iter()
        .filter(|slab| slab.kind == floor::KIND_ROOF)
    {
        let outline = with_overhang(roof);
        let Some(extent) = PlanRectangle::of(&outline) else {
            continue;
        };
        let draining: Vec<_> = spouts
            .iter()
            .filter(|spout| {
                spout.start.x >= extent.min_x - 0.5
                    && spout.start.x <= extent.max_x + 0.5
                    && spout.start.y >= extent.min_y - 0.5
                    && spout.start.y <= extent.max_y + 0.5
            })
            .collect();
        if draining.is_empty() {
            continue;
        }
        let mut doubled_area = 0.0;
        for index in 0..outline.len() {
            let a = outline[index];
            let b = outline[(index + 1) % outline.len()];
            doubled_area += a.x * b.y - b.x * a.y;
        }
        let runoff = 0.95 * rainfall * (doubled_area / 2.0).abs() / 3600.0;
        for spout in &draining {
            *flows.entry(spout.id).or_default() += runoff / draining.len() as f64;
        }
    }
    flows
}

/// Chooses the smallest pipe that carries the design flow at the source model's one-percent slope.
pub(super) fn storm_size(flow: f64) -> usize {
    STORM_SIZES
        .iter()
        .position(|&(inside, _)| {
            let diameter = inside / 1000.0;
            let area = std::f64::consts::PI * diameter * diameter / 4.0;
            let capacity =
                area / 0.010 * (diameter / 4.0).powf(2.0 / 3.0) * 0.01_f64.sqrt() * 1000.0;
            capacity >= flow
        })
        .unwrap_or(STORM_SIZES.len() - 1)
}
