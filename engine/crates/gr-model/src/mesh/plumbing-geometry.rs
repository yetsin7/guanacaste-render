//! Service-point geometry that does not require the plumbing-system analysis.

use super::household_size::household_size;
use super::plumbing_routing::SystemLayout;
use super::plumbing_tank::add_tank;
use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::{Document, PlumbingPoint, plumbing_point_kind as kind, plumbing_system_kind};
use glam::Vec3;

/// Adds a meter or outlet; vents are emitted by system analysis, and unported point kinds return false.
pub(super) fn add_service_point(
    mesh: &mut SceneMesh,
    document: &Document,
    point: &PlumbingPoint,
    level: f64,
) -> bool {
    let at = Vec3::new(
        point.position.x as f32,
        point.position.y as f32,
        level as f32,
    );
    match point.kind {
        kind::WATER_METER => {
            add_tube(
                mesh,
                at - Vec3::Z * 0.25,
                at + Vec3::Z * 0.015,
                0.17,
                SurfaceKind::Structure,
            );
            add_tube(
                mesh,
                at + Vec3::Z * 0.015,
                at + Vec3::Z * 0.025,
                0.15,
                SurfaceKind::Metal,
            );
        }
        kind::SEWER_OUTLET | kind::STORM_OUTLET => {
            let surface = if point.kind == kind::STORM_OUTLET {
                SurfaceKind::PipeStorm
            } else {
                SurfaceKind::PipeDrain
            };
            add_tube(
                mesh,
                at - Vec3::Z * 0.02,
                at + Vec3::Z * 0.012,
                0.07,
                surface,
            );
        }
        kind::VENT_STACK | kind::SEPTIC_TANK => {}
        kind::CISTERN | kind::ELEVATED_TANK => {
            return add_tank(mesh, point, level, household_size(document, point));
        }
        _ => return false,
    }
    true
}

/// Draws a system's routed pipes, vent stacks, and supply-riser valves in the original order.
pub(super) fn add_system(mesh: &mut SceneMesh, layout: &SystemLayout) {
    let surface = match layout.kind {
        plumbing_system_kind::COLD_WATER => SurfaceKind::PipeCold,
        plumbing_system_kind::HOT_WATER => SurfaceKind::PipeHot,
        plumbing_system_kind::STORM_WATER => SurfaceKind::PipeStorm,
        _ => SurfaceKind::PipeDrain,
    };
    for run in &layout.runs {
        add_tube(
            mesh,
            Vec3::new(run.from.x as f32, run.from.y as f32, run.from_z as f32),
            Vec3::new(run.to.x as f32, run.to.y as f32, run.to_z as f32),
            (run.outside / 2000.0) as f32,
            surface,
        );
    }
    for vent in &layout.vents {
        let foot = Vec3::new(vent.point.x as f32, vent.point.y as f32, vent.bottom as f32);
        let top = Vec3::new(foot.x, foot.y, vent.top as f32);
        let radius = (vent.outside / 2000.0) as f32;
        add_tube(mesh, foot, top, radius, SurfaceKind::PipeDrain);
        add_tube(
            mesh,
            top - Vec3::Z * 0.06,
            top + Vec3::Z * 0.04,
            radius + 0.015,
            SurfaceKind::PipeDrain,
        );
    }
    if layout.kind != plumbing_system_kind::SANITARY {
        for run in &layout.runs {
            if run.from.distance_to(run.to) < 1e-9 && run.to_z > run.from_z {
                let top = Vec3::new(run.to.x as f32, run.to.y as f32, run.to_z as f32);
                add_tube(
                    mesh,
                    top - Vec3::Z * 0.02,
                    top + Vec3::Z * 0.03,
                    0.016,
                    SurfaceKind::Metal,
                );
            }
        }
    }
}

/// Extrudes an eight-sided tube with closed ends in the same winding order as the source geometry.
fn add_tube(mesh: &mut SceneMesh, a: Vec3, b: Vec3, radius: f32, surface: SurfaceKind) {
    add_tube_sides(mesh, a, b, radius, surface, 8);
}

/// Extrudes a closed polygonal tube with the requested number of sides.
pub(super) fn add_tube_sides(
    mesh: &mut SceneMesh,
    a: Vec3,
    b: Vec3,
    radius: f32,
    surface: SurfaceKind,
    sides: usize,
) {
    let axis = b - a;
    let length = axis.length();
    if length < 1e-5 || radius <= 0.0 {
        return;
    }
    let direction = axis / length;
    let reference_axis = if direction.z.abs() < 0.9 {
        Vec3::Z
    } else {
        Vec3::X
    };
    let u = reference_axis.cross(direction).normalize();
    let v = direction.cross(u);
    let mut ring_a = vec![Vec3::ZERO; sides];
    let mut ring_b = vec![Vec3::ZERO; sides];
    for index in 0..sides {
        let angle = index as f32 * std::f32::consts::TAU / sides as f32;
        let offset = (u * angle.cos() + v * angle.sin()) * radius;
        ring_a[index] = a + offset;
        ring_b[index] = b + offset;
    }
    for index in 0..sides {
        let next = (index + 1) % sides;
        mesh.add_quad(
            ring_a[index],
            ring_a[next],
            ring_b[next],
            ring_b[index],
            surface,
        );
    }
    for index in 1..sides - 1 {
        mesh.add_triangle(ring_b[0], ring_b[index], ring_b[index + 1], surface);
        mesh.add_triangle(ring_a[0], ring_a[index + 1], ring_a[index], surface);
    }
}
