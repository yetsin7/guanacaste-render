//! Turns a project into triangles: walls split around their openings, the openings themselves, flat slabs,
//! columns, residential components, terrain, plumbing, procedural vegetation, and the ground.
//!
//! Ported from `BuildingMesher.Build` in `geometry/building-mesher.cs`, element kind by element kind and in the
//! same order. What is not ported yet is counted in [`Coverage`], never silently left out.

use super::element_part::add_part;
use super::electrical_geometry::add_electrical_device;
use super::interior_test::InteriorTest;
use super::elements::element_parts;
use super::openings::{add_opening_assembly, add_security_bars};
use super::plants::add_plant;
use super::pitched_roof::add_pitched_roof;
use super::plumbing_geometry::{add_service_point, add_system};
use super::plumbing_routing::route_system;
use super::plumbing_septic::add_septic;
use super::prism::{PrismShape, WallFrame, add_prism, to_f32};
use super::scene_mesh::{SceneMesh, SurfaceKind};
use super::terrain_mesh::{add_terrain, contains as terrain_contains};
use super::terrain_surface::TerrainSurface;
use crate::document::{Document, FloorSlab, Id, Wall, WallOpening, element_kind, floor, wall};
use crate::plan::{PlanRectangle, Point2, WallJoins, column_outline, contains_rectangle, counter_clockwise, slab_pieces, wall_solids};
use glam::{Vec2, Vec3};
use std::collections::{HashMap, HashSet};

/// Elements of the project that the application meshes and this mesher does not yet. When any count is not zero
/// the mesh is a partial building, and a complete one still needs the application.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Coverage {
    pub toposolids: usize,
    pub pitched_roofs: usize,
    /// Structural elements that are not generated yet, such as family meshes, elevators, and members with cuts.
    pub structural_elements: usize,
    pub electrical_devices: usize,
    pub plumbing_points: usize,
    pub plumbing_systems: usize,
    pub in_place_families: usize,
}

impl Coverage {
    /// Reports whether any geometry still requires the original application.
    pub fn is_complete(&self) -> bool {
        *self == Coverage::default()
    }

    /// Total number of elements left out.
    pub fn missing(&self) -> usize {
        self.toposolids
            + self.pitched_roofs
            + self.structural_elements
            + self.electrical_devices
            + self.plumbing_points
            + self.plumbing_systems
            + self.in_place_families
    }
}

/// Builds the mesh of a project. `include_ground` adds the endless ground plane under it.
pub fn build_mesh(document: &Document, include_ground: bool) -> (SceneMesh, Coverage) {
    let elevations: HashMap<Id, f64> = document.levels.iter().map(|level| (level.id, level.elevation)).collect();
    let elevation_of = |level: Id| elevations.get(&level).copied().unwrap_or(0.0);
    let opening_types: HashMap<Id, _> = document.opening_types.iter().map(|kind| (kind.id, kind)).collect();
    let wall_types: HashMap<Id, _> = document.wall_types.iter().map(|kind| (kind.id, kind)).collect();
    let joins = WallJoins::build(&document.walls);
    let mut openings_of: HashMap<Id, Vec<&WallOpening>> = HashMap::new();
    for opening in &document.openings {
        openings_of.entry(opening.wall_id).or_default().push(opening);
    }
    let interiors: HashMap<Id, InteriorTest> =
        document.levels.iter().map(|level| (level.id, InteriorTest::new(document, level.id))).collect();
    let outside_everything = InteriorTest { walls: Vec::new() };

    let mut mesh = SceneMesh::default();
    for wall in &document.walls {
        let z = elevation_of(wall.level_id);
        let openings = openings_of.get(&wall.id).map(Vec::as_slice).unwrap_or_default();
        let interior = interiors.get(&wall.level_id).unwrap_or(&outside_everything);
        let wall_type = wall_types.get(&wall.wall_type_id);
        let construction = wall_type.map_or(wall::PLASTER, |kind| kind.construction);
        let facade = match construction {
            wall::BRICK => SurfaceKind::WallBrick,
            wall::BLOCK => SurfaceKind::WallBlock,
            wall::CONCRETE => SurfaceKind::WallConcrete,
            wall::STONE => SurfaceKind::WallStone,
            wall::WOOD => SurfaceKind::WallWood,
            wall::METAL => SurfaceKind::WallMetal,
            _ => SurfaceKind::Wall,
        };
        // An interior partition of brick, block or concrete is usually left exposed on both faces.
        let exposed_inside = wall_type.is_some_and(|kind| kind.function == wall::FUNCTION_INTERIOR)
            && matches!(construction, wall::BRICK | wall::BLOCK | wall::CONCRETE);
        // A face that looks into the building is plaster; one that looks out is the facade.
        let face = |middle: Vec2, outward: Vec2| {
            let probe = Point2::new(middle.x as f64 + outward.x as f64 * 0.3, middle.y as f64 + outward.y as f64 * 0.3);
            if interior.contains(probe) && !exposed_inside { SurfaceKind::InteriorWall } else { facade }
        };

        let wall_first = mesh.triangle_count();
        for solid in wall_solids(wall, openings) {
            if construction == wall::GLASS {
                add_glass_wall(&mut mesh, wall, solid.from, solid.to, z + solid.bottom, z + solid.top);
                continue;
            }
            let outline = to_f32(&joins.section_outline(wall, solid.from, solid.to));
            let (bottom, top) = ((z + solid.bottom) as f32, (z + solid.top) as f32);
            if !solid.sloped() {
                add_prism(&mut mesh, &outline, bottom, top, facade, &PrismShape { side_kind: Some(&face), top_at: None });
                continue;
            }
            // A wedge: the top at each corner is the height of the slope above that corner's place along the wall.
            let (axis_x, axis_y) = wall.direction();
            let top_at = |corner: Vec2| {
                let along = (corner.x as f64 - wall.start.x) * axis_x + (corner.y as f64 - wall.start.y) * axis_y;
                (z + (solid.bottom + 0.001).max(solid.top_height_at(along))) as f32
            };
            add_prism(&mut mesh, &outline, bottom, top, facade, &PrismShape { side_kind: Some(&face), top_at: Some(&top_at) });
        }
        mesh.mark_element(wall.id, wall_first);

        for &opening in openings {
            let Some(&kind) = opening_types.get(&opening.opening_type_id) else { continue };
            let opening_first = mesh.triangle_count();
            add_opening_assembly(&mut mesh, wall, opening, kind, z);
            if kind.security_bars {
                add_security_bars(&mut mesh, wall, opening, z, &|point| interior.contains(point));
            }
            mesh.mark_element(opening.id, opening_first);
        }
    }

    let mut coverage = Coverage::default();
    let mut terrain_surfaces = Vec::new();
    let mut site_edge: Option<f64> = None;
    for slab in &document.floors {
        if slab.is_toposolid() {
            if slab.boundary.len() < 3 {
                coverage.toposolids += 1;
                continue;
            }
            let first = mesh.triangle_count();
            let surface = TerrainSurface::new(slab);
            if let Some(edge) = add_terrain(&mut mesh, slab, &surface, elevation_of(slab.level_id)) {
                site_edge = Some(site_edge.map_or(edge, |existing| existing.min(edge)));
                mesh.mark_element(slab.id, first);
                terrain_surfaces.push((slab, surface));
            } else {
                coverage.toposolids += 1;
            }
            continue;
        }
        if slab.is_pitched() {
            if !matches!(slab.shape, floor::SHAPE_SHED | floor::SHAPE_GABLE | floor::SHAPE_HIP) {
                coverage.pitched_roofs += 1;
                continue;
            }
            let first = mesh.triangle_count();
            add_pitched_roof(&mut mesh, slab, &with_overhang(slab), elevation_of(slab.level_id));
            mesh.mark_element(slab.id, first);
            continue;
        }
        let first = mesh.triangle_count();
        let top = elevation_of(slab.level_id) + slab.elevation_offset;
        let openings = slab_openings(document, slab, &elevation_of);
        add_slab(&mut mesh, &with_overhang(slab), top, slab.thickness, &openings, slab_surface(slab));
        mesh.mark_element(slab.id, first);
    }

    for column in &document.columns {
        let first = mesh.triangle_count();
        let base = elevation_of(column.level_id);
        let outline = to_f32(&column_outline(column));
        add_prism(&mut mesh, &outline, base as f32, (base + column.height) as f32, SurfaceKind::Wall, &PrismShape::default());
        mesh.mark_element(column.id, first);
    }

    let has_panel = document.electrical_devices.iter().any(|device| device.kind == crate::document::electrical_device_kind::PANEL);
    let controlled: HashSet<Id> = document.electrical_devices.iter().flat_map(|device| device.controls.iter().copied()).collect();
    for device in &document.electrical_devices {
        let first = mesh.triangle_count();
        let brightness = if device.is_luminaire() {
            if device.circuit_id.is_some() || controlled.contains(&device.id) { None } else { Some(!has_panel) }
        } else { Some(false) };
        if !elevations.get(&device.level_id).zip(brightness).is_some_and(|(&level, lit)| add_electrical_device(&mut mesh, device, level, lit)) {
            coverage.electrical_devices += 1;
        }
        mesh.mark_element(device.id, first);
    }

    // Buried service geometry contributes to the framing bounds but must not lower the ground plane.
    let buried_from = mesh.positions.len();
    for kind in 0..=3 {
        for system in document.plumbing_systems.iter().filter(|system| system.kind == kind) {
            if let Some(layout) = route_system(document, system, &elevations) {
                add_system(&mut mesh, &layout);
            } else {
                coverage.plumbing_systems += 1;
            }
        }
    }
    coverage.plumbing_systems += document.plumbing_systems.iter().filter(|system| !(0..=3).contains(&system.kind)).count();
    for point in document.plumbing_points.iter().filter(|point| point.kind == crate::document::plumbing_point_kind::SEPTIC_TANK) {
        if let Some(&level) = elevations.get(&point.level_id) {
            add_septic(&mut mesh, document, point, level);
        }
    }
    for point in &document.plumbing_points {
        if !elevations.get(&point.level_id).is_some_and(|&level| add_service_point(&mut mesh, document, point, level)) {
            coverage.plumbing_points += 1;
        }
    }
    let buried_to = mesh.positions.len();

    for element in &document.structural_elements {
        // Plants use the first site on their own level that contains their insertion point.
        if element.is_procedural_plant() && coverage.toposolids == 0 {
            let first = mesh.triangle_count();
            let ground = terrain_surfaces.iter()
                .find(|(slab, _)| slab.level_id == element.level_id && terrain_contains(&slab.boundary, element.start))
                .map_or(0.0, |(_, surface)| surface.elevation_at(element.start));
            // A small embed hides the gap between a trunk and uneven ground.
            add_plant(&mut mesh, element, elevation_of(element.level_id) + ground - 0.08);
            mesh.mark_element(element.id, first);
            continue;
        }
        // Family meshes, unhandled plant placements, elevators, and members with cuts remain unsupported.
        let parts = if element.family_id.is_some() || element.kind == element_kind::PLANTING { None } else { element_parts(element) };
        match parts {
            Some(parts) => {
                let first = mesh.triangle_count();
                let datum = elevation_of(element.level_id);
                for part in &parts {
                    add_part(&mut mesh, part, datum);
                }
                mesh.mark_element(element.id, first);
            }
            None => coverage.structural_elements += 1,
        }
    }
    coverage.in_place_families = document.in_place_families.len();

    if include_ground {
        let (min, max) = mesh.bounds();
        let center = (min + max) / 2.0;
        // Large enough that the ground meets the sky at the horizon from typical orbit distances.
        let extent = ((max - min).length() * 20.0).max(500.0);
        let lowest = mesh.positions.iter().enumerate()
            .filter(|(index, _)| *index < buried_from || *index >= buried_to)
            .map(|(_, position)| position.z).fold(f32::MAX, f32::min);
        let z = site_edge.map_or_else(
            || (if lowest == f32::MAX { 0.0 } else { lowest }).min(0.0) - 0.01,
            |edge| edge as f32 - 0.02,
        );
        mesh.add_quad(
            Vec3::new(center.x - extent, center.y - extent, z),
            Vec3::new(center.x + extent, center.y - extent, z),
            Vec3::new(center.x + extent, center.y + extent, z),
            Vec3::new(center.x - extent, center.y + extent, z),
            SurfaceKind::Ground,
        );
    }
    (mesh, coverage)
}

/// A glass partition between two stations of its wall: a tempered pane in aluminium head and sill channels, with
/// a mullion at least every 1.2 m.
fn add_glass_wall(mesh: &mut SceneMesh, wall: &Wall, from: f64, to: f64, bottom: f64, top: f64) {
    let frame = WallFrame::new(wall);
    let half = (wall.thickness / 2.0).max(0.025);
    const CHANNEL: f64 = 0.05;
    frame.block(mesh, from, to, -half, half, bottom, bottom + CHANNEL, SurfaceKind::Metal);
    frame.block(mesh, from, to, -half, half, top - CHANNEL, top, SurfaceKind::Metal);
    frame.block(mesh, from, to, -0.006, 0.006, bottom + CHANNEL, top - CHANNEL, SurfaceKind::Glass);
    let bays = (((to - from) / 1.2).ceil() as i32).max(1);
    for index in 0..=bays {
        let at = from + (to - from) * index as f64 / bays as f64;
        frame.block(mesh, from.max(at - 0.025), to.min(at + 0.025), -half, half, bottom + CHANNEL, top - CHANNEL, SurfaceKind::Metal);
    }
}

/// The openings that cut a slab: openings of its own level (floors only), shafts from at or below it, and the
/// dormers and skylights of a roof. An opening that is not wholly inside the slab does not cut it.
fn slab_openings(document: &Document, slab: &FloorSlab, elevation_of: &dyn Fn(Id) -> f64) -> Vec<PlanRectangle> {
    let slab_elevation = elevation_of(slab.level_id);
    document
        .structural_elements
        .iter()
        .filter(|element| match element.kind {
            element_kind::OPENING => slab.kind == floor::KIND_FLOOR && element.level_id == slab.level_id,
            element_kind::SHAFT => elevation_of(element.level_id) <= slab_elevation,
            element_kind::DORMER | element_kind::SKYLIGHT => slab.kind == floor::KIND_ROOF && element.level_id == slab.level_id,
            _ => false,
        })
        .filter_map(|element| PlanRectangle::of(&element.footprint()))
        .filter(|rectangle| contains_rectangle(&slab.boundary, rectangle).unwrap_or(false))
        .collect()
}

/// Adds a slab whose top face is at `elevation`. Openings are cut out: the top and bottom faces are built from
/// convex pieces, and each opening gets its own inner walls.
fn add_slab(mesh: &mut SceneMesh, boundary: &[Point2], elevation: f64, thickness: f64, openings: &[PlanRectangle], surface: SurfaceKind) {
    let (bottom, top) = ((elevation - thickness) as f32, elevation as f32);
    let ordered = counter_clockwise(boundary);
    let pieces = if openings.is_empty() { None } else { slab_pieces(&ordered, openings).ok() };
    let Some(pieces) = pieces else {
        add_prism(mesh, &to_f32(boundary), bottom, top, surface, &PrismShape::default());
        return;
    };
    let lift = |point: Point2, z: f32| Vec3::new(point.x as f32, point.y as f32, z);
    // A cut slab is drawn as flooring whatever its finish, as in the application.
    for piece in &pieces {
        for corner in 1..piece.len() - 1 {
            mesh.add_triangle(lift(piece[0], top), lift(piece[corner], top), lift(piece[corner + 1], top), SurfaceKind::Floor);
            mesh.add_triangle(lift(piece[corner + 1], bottom), lift(piece[corner], bottom), lift(piece[0], bottom), SurfaceKind::Floor);
        }
    }
    let mut side_walls = |outline: &[Point2]| {
        for index in 0..outline.len() {
            let (current, next) = (outline[index], outline[(index + 1) % outline.len()]);
            mesh.add_quad(lift(current, bottom), lift(next, bottom), lift(next, top), lift(current, top), SurfaceKind::Floor);
        }
    };
    side_walls(&ordered);
    for opening in openings {
        // Walking the opening clockwise makes its walls face into the hole, away from the slab.
        let mut corners = opening.corners();
        corners.reverse();
        side_walls(&corners);
    }
}

/// The render surface of a flat slab: water for a pool, otherwise its finish.
fn slab_surface(slab: &FloorSlab) -> SurfaceKind {
    if slab.kind == floor::KIND_WATER {
        return SurfaceKind::Water;
    }
    match slab.finish {
        floor::FINISH_EXPOSED_CONCRETE => SurfaceKind::WallConcrete,
        floor::FINISH_PAINT => SurfaceKind::Wall,
        floor::FINISH_ASPHALT => SurfaceKind::Asphalt,
        floor::FINISH_GRASS => SurfaceKind::Ground,
        _ => SurfaceKind::Floor,
    }
}

/// The boundary of a slab; for a roof with an overhang, grown outwards by it with mitred corners.
pub(super) fn with_overhang(slab: &FloorSlab) -> Vec<Point2> {
    if slab.kind != floor::KIND_ROOF || slab.overhang <= 0.0 {
        return slab.boundary.clone();
    }
    let ordered = counter_clockwise(&slab.boundary);
    let count = ordered.len();
    let outward_normal = |a: Point2, b: Point2| {
        let length = a.distance_to(b).max(1e-12);
        ((b.y - a.y) / length, -(b.x - a.x) / length)
    };
    let mut grown = Vec::with_capacity(count);
    for index in 0..count {
        let previous = ordered[(index + count - 1) % count];
        let current = ordered[index];
        let next = ordered[(index + 1) % count];
        let (n1x, n1y) = outward_normal(previous, current);
        let (n2x, n2y) = outward_normal(current, next);
        let (bisector_x, bisector_y) = (n1x + n2x, n1y + n2y);
        if 1.0 + n1x * n2x + n1y * n2y < 0.1 {
            // A corner too sharp to mitre: the roof keeps its boundary.
            return slab.boundary.clone();
        }
        // A mitred corner moves along the bisector by distance / cos(half angle) = 2 * distance / |n1 + n2|^2.
        let scale = slab.overhang * 2.0 / (bisector_x * bisector_x + bisector_y * bisector_y);
        grown.push(Point2::new(current.x + bisector_x * scale, current.y + bisector_y * scale));
    }
    grown
}

#[cfg(test)]
#[path = "builder-tests.rs"]
mod tests;
