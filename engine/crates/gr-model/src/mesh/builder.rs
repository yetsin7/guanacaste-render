//! Turns a project into triangles: walls split around their openings, the openings themselves, flat slabs,
//! columns, procedural vegetation and the ground.
//!
//! Ported from `BuildingMesher.Build` in `geometry/building-mesher.cs`, element kind by element kind and in the
//! same order. What is not ported yet is counted in [`Coverage`], never silently left out.

use super::openings::{add_opening_assembly, add_security_bars};
use super::plants::add_plant;
use super::prism::{PrismShape, WallFrame, add_prism, to_f32};
use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::{Document, FloorSlab, Id, Wall, WallOpening, floor, wall};
use crate::plan::{Point2, WallJoins, column_outline, counter_clockwise, wall_solids};
use glam::{Vec2, Vec3};
use std::collections::HashMap;

/// Elements of the project that the application meshes and this mesher does not yet. When any count is not zero
/// the mesh is a partial building, and a complete one still needs the application.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Coverage {
    pub toposolids: usize,
    pub pitched_roofs: usize,
    /// Structural elements with a solid (beams, stairs, furniture, family meshes…) or that cut a slab (openings,
    /// shafts). Procedural plants on flat ground are meshed and not counted.
    pub structural_elements: usize,
    pub electrical_devices: usize,
    pub plumbing_points: usize,
    pub in_place_families: usize,
}

impl Coverage {
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
    for slab in &document.floors {
        if slab.is_toposolid() {
            coverage.toposolids += 1;
            continue;
        }
        if slab.is_pitched() {
            coverage.pitched_roofs += 1;
            continue;
        }
        let first = mesh.triangle_count();
        let top = elevation_of(slab.level_id) + slab.elevation_offset;
        let outline = to_f32(&with_overhang(slab));
        add_prism(&mut mesh, &outline, (top - slab.thickness) as f32, top as f32, slab_surface(slab), &PrismShape::default());
        mesh.mark_element(slab.id, first);
    }

    for column in &document.columns {
        let first = mesh.triangle_count();
        let base = elevation_of(column.level_id);
        let outline = to_f32(&column_outline(column));
        add_prism(&mut mesh, &outline, base as f32, (base + column.height) as f32, SurfaceKind::Wall, &PrismShape::default());
        mesh.mark_element(column.id, first);
    }

    for element in &document.structural_elements {
        // A plant stands on the terrain under it; until toposolids are meshed, only plants on flat ground are grown.
        if element.is_procedural_plant() && coverage.toposolids == 0 {
            let first = mesh.triangle_count();
            // A small embed hides the gap between a trunk and uneven ground.
            add_plant(&mut mesh, element, elevation_of(element.level_id) - 0.08);
            mesh.mark_element(element.id, first);
        } else if element.affects_geometry() {
            coverage.structural_elements += 1;
        }
    }
    coverage.electrical_devices = document.electrical_devices.len();
    coverage.plumbing_points = document.plumbing_points.len();
    coverage.in_place_families = document.in_place_families.len();

    if include_ground {
        let (min, max) = mesh.bounds();
        let center = (min + max) / 2.0;
        // Large enough that the ground meets the sky at the horizon from typical orbit distances.
        let extent = ((max - min).length() * 20.0).max(500.0);
        let lowest = mesh.positions.iter().map(|position| position.z).fold(f32::MAX, f32::min);
        let z = (if lowest == f32::MAX { 0.0 } else { lowest }).min(0.0) - 0.01;
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

/// Whether a plan point of one level lies inside the building: enclosed by walls, meaning a ray from it in each
/// of eight directions meets a wall of the level. A terrace open on one side is outside; doors and windows do not
/// let the rays out, because walls are tested along their whole length.
struct InteriorTest {
    walls: Vec<(Point2, Point2)>,
}

impl InteriorTest {
    fn new(document: &Document, level: Id) -> InteriorTest {
        let walls: Vec<_> = document.walls.iter().filter(|wall| wall.level_id == level).map(|wall| (wall.start, wall.end)).collect();
        // Fewer than three walls cannot enclose anything.
        InteriorTest { walls: if walls.len() < 3 { Vec::new() } else { walls } }
    }

    fn contains(&self, point: Point2) -> bool {
        if self.walls.is_empty() {
            return false;
        }
        (0..8).all(|index| {
            let angle = index as f64 * std::f64::consts::PI / 4.0 + 0.1;
            let direction = (angle.cos(), angle.sin());
            self.walls.iter().any(|&(a, b)| ray_hits(point, direction, a, b))
        })
    }
}

fn ray_hits(origin: Point2, direction: (f64, f64), a: Point2, b: Point2) -> bool {
    let (ex, ey) = (b.x - a.x, b.y - a.y);
    let denominator = direction.0 * ey - direction.1 * ex;
    if denominator.abs() < 1e-12 {
        return false;
    }
    let (ox, oy) = (a.x - origin.x, a.y - origin.y);
    let t = (ox * ey - oy * ex) / denominator;
    let u = (ox * direction.1 - oy * direction.0) / denominator;
    t > 0.0 && t < 60.0 && (-1e-6..=1.0 + 1e-6).contains(&u)
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
fn with_overhang(slab: &FloorSlab) -> Vec<Point2> {
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
mod tests {
    use super::*;

    fn document(json: &str) -> Document {
        serde_json::from_str(json).expect("valid document")
    }

    const LEVEL: &str = "11111111-1111-1111-1111-111111111111";

    #[test]
    fn an_empty_project_gives_only_the_ground() {
        let (mesh, coverage) = build_mesh(&document(r#"{"SchemaVersion":15}"#), true);
        assert_eq!(mesh.triangle_count(), 2);
        assert!(coverage.is_complete() && mesh.element_ranges.is_empty());
        assert!(mesh.kinds.iter().all(|&kind| kind == SurfaceKind::Ground));
    }

    #[test]
    fn what_is_not_meshed_is_counted() {
        let json = format!(
            r#"{{"SchemaVersion":15,"Levels":[{{"Id":"{LEVEL}","Elevation":0}}],
                "Floors":[{{"Id":"22222222-2222-2222-2222-222222222222","LevelId":"{LEVEL}","Kind":4,"Thickness":1,
                            "Boundary":[{{"X":0,"Y":0}},{{"X":9,"Y":0}},{{"X":9,"Y":9}}]}}],
                "StructuralElements":[{{"Kind":12}},{{"Kind":7}},{{"Kind":8}}],
                "ElectricalDevices":[{{}},{{}}]}}"#
        );
        let (mesh, coverage) = build_mesh(&document(&json), false);
        assert_eq!(mesh.triangle_count(), 0);
        // The stair (12) and the slab opening (8) count; the model line (7) is drafting only.
        assert_eq!(coverage, Coverage { toposolids: 1, structural_elements: 2, electrical_devices: 2, ..Default::default() });
        assert_eq!(coverage.missing(), 5);
    }

    #[test]
    fn a_roof_overhang_grows_the_slab_evenly() {
        let json = format!(
            r#"{{"SchemaVersion":15,"Levels":[{{"Id":"{LEVEL}","Elevation":3}}],
                "Floors":[{{"Id":"22222222-2222-2222-2222-222222222222","LevelId":"{LEVEL}","Kind":2,"Thickness":0.2,
                            "Overhang":0.5,"Boundary":[{{"X":0,"Y":0}},{{"X":4,"Y":0}},{{"X":4,"Y":6}},{{"X":0,"Y":6}}]}}]}}"#
        );
        let (mesh, coverage) = build_mesh(&document(&json), false);
        assert!(coverage.is_complete());
        let (min, max) = mesh.bounds();
        assert!((min - Vec3::new(-0.5, -0.5, 2.8)).length() < 1e-5 && (max - Vec3::new(4.5, 6.5, 3.0)).length() < 1e-5);
    }
}
