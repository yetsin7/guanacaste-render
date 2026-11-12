//! Routes supported supply, sanitary, and storm systems through their connected fixtures.

use super::plumbing_paths::{drain, storm, supply};
use super::storm_runoff::downspout_flows;
use super::plumbing_specifications::{FixtureSpec, fixture_spec};
use crate::document::component_shape as catalog;
use crate::document::{Document, Id, PlumbingSystem, StructuralElement, element_kind, plumbing_point_kind, plumbing_system_kind as kind};
use crate::plan::Point2;
use std::collections::{HashMap, HashSet};

/// One fixture or source in the plumbing tree, with elevations relative to the project origin.
#[derive(Clone, Copy)]
pub(super) struct PlumbingNode {
    pub point: Point2,
    pub center: Point2,
    pub level: f64,
    pub units: f64,
    pub spec: Option<FixtureSpec>,
    pub inlet_z: f64,
    pub outlet_z: f64,
}

/// A closed tube from one 3D endpoint to another, with its outside diameter in millimetres.
pub(super) struct PipeRun {
    pub from: Point2,
    pub from_z: f64,
    pub to: Point2,
    pub to_z: f64,
    pub outside: f64,
}

/// A vent stack serving one or more sanitary fixtures.
pub(super) struct VentRun {
    pub point: Point2,
    pub bottom: f64,
    pub top: f64,
    pub outside: f64,
}

/// Geometry produced for one supported system, in the source renderer's order.
pub(super) struct SystemLayout {
    pub kind: i32,
    pub runs: Vec<PipeRun>,
    pub vents: Vec<VentRun>,
}

/// Routes one system or returns none when its source or system kind still needs the application.
pub(super) fn route_system(document: &Document, system: &PlumbingSystem, elevations: &HashMap<Id, f64>) -> Option<SystemLayout> {
    let components: HashMap<Id, &StructuralElement> = document
        .structural_elements
        .iter()
        .filter(|element| element.kind == element_kind::COMPONENT)
        .map(|element| (element.id, element))
        .collect();
    let root = match system.kind {
        kind::COLD_WATER | kind::SANITARY | kind::STORM_WATER => {
            let point = document.plumbing_points.iter().find(|point| point.id == system.source_id)?;
            let supported = match system.kind {
                kind::COLD_WATER => matches!(point.kind, plumbing_point_kind::WATER_METER | plumbing_point_kind::CISTERN | plumbing_point_kind::ELEVATED_TANK),
                kind::SANITARY => matches!(point.kind, plumbing_point_kind::SEWER_OUTLET | plumbing_point_kind::SEPTIC_TANK),
                _ => point.kind == plumbing_point_kind::STORM_OUTLET,
            };
            if !supported {
                return None;
            }
            let level = *elevations.get(&point.level_id)?;
            PlumbingNode {
                point: point.position,
                center: point.position,
                level,
                units: 0.0,
                spec: None,
                inlet_z: if point.kind == plumbing_point_kind::ELEVATED_TANK { level + point.tank_height } else { level },
                outlet_z: level - point.invert_depth,
            }
        }
        kind::HOT_WATER => {
            let heater = components.get(&system.source_id)?;
            if heater.component_shape != catalog::WATER_HEATER {
                return None;
            }
            let level = *elevations.get(&heater.level_id)?;
            PlumbingNode {
                point: connection(heater),
                center: heater.start,
                level,
                units: 0.0,
                spec: None,
                inlet_z: level + heater.offset + heater.height,
                outlet_z: 0.0,
            }
        }
        _ => return None,
    };
    if system.kind == kind::STORM_WATER {
        let flows = downspout_flows(document);
        let spouts: HashMap<Id, &StructuralElement> = document.structural_elements.iter()
            .filter(|element| element.kind == element_kind::DOWNSPOUT).map(|element| (element.id, element)).collect();
        let mut nodes = vec![root];
        let mut seen = HashSet::new();
        for id in &system.fixtures {
            if !seen.insert(*id) { continue; }
            let Some(spout) = spouts.get(id) else { continue };
            let Some(&level) = elevations.get(&spout.level_id) else { continue };
            nodes.push(PlumbingNode { point: spout.start, center: spout.start, level, units: flows.get(id).copied().unwrap_or(0.0),
                spec: None, inlet_z: level, outlet_z: level + spout.offset });
        }
        return Some(storm(&nodes));
    }
    let heater_units: HashMap<Id, f64> = document
        .plumbing_systems
        .iter()
        .filter(|candidate| candidate.kind == kind::HOT_WATER)
        .fold(HashMap::new(), |mut totals, candidate| {
            let units: f64 = candidate
                .fixtures
                .iter()
                .filter_map(|id| components.get(id).and_then(|element| fixture_spec(element)))
                .map(|spec| spec.hot)
                .sum();
            *totals.entry(candidate.source_id).or_insert(0.0) += units;
            totals
        });
    let mut nodes = vec![root];
    let mut seen = HashSet::new();
    for id in &system.fixtures {
        if !seen.insert(*id) {
            continue;
        }
        let Some(element) = components.get(id) else { continue };
        let Some(spec) = fixture_spec(element) else { continue };
        let Some(&level) = elevations.get(&element.level_id) else {
            continue;
        };
        let units = match system.kind {
            kind::COLD_WATER if element.component_shape == catalog::WATER_HEATER => {
                heater_units.get(id).copied().filter(|units| *units > 0.0).unwrap_or(1.4)
            }
            kind::COLD_WATER => spec.cold,
            kind::HOT_WATER => spec.hot,
            _ => spec.drain,
        };
        if units <= 0.0 {
            continue;
        }
        nodes.push(PlumbingNode {
            point: connection(element),
            center: element.start,
            level,
            units,
            spec: Some(spec),
            inlet_z: level + element.offset + spec.inlet_height,
            outlet_z: level + element.offset.max(0.0) + spec.trap_height.unwrap_or(0.0),
        });
    }
    if system.kind == kind::SANITARY {
        let wall_top = document
            .walls
            .iter()
            .filter_map(|wall| elevations.get(&wall.level_id).map(|level| level + wall.height))
            .fold(f64::NEG_INFINITY, f64::max);
        let roof_level = document
            .levels
            .iter()
            .filter(|level| level.role == 2)
            .map(|level| level.elevation)
            .fold(f64::NEG_INFINITY, f64::max);
        let highest = wall_top.max(roof_level);
        let roof_top = if highest.is_finite() {
            highest
        } else {
            document.levels.iter().map(|level| level.elevation).reduce(f64::max)? + 3.0
        } + 0.30;
        Some(drain(&nodes, &document.plumbing_points, roof_top))
    } else {
        Some(supply(&nodes, system.kind))
    }
}

/// Finds the back-wall connection of a rotated fixture.
fn connection(element: &StructuralElement) -> Point2 {
    let depth = if element.depth > 0.0 { element.depth } else { element.width };
    let back = (depth / 2.0 - 0.06).max(0.0);
    Point2::new(
        element.start.x - element.rotation.sin() * back,
        element.start.y + element.rotation.cos() * back,
    )
}
