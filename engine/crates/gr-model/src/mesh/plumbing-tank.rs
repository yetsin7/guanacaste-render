//! Explicit-capacity cistern and elevated-tank geometry.

use super::plumbing_geometry::add_tube_sides;
use super::plumbing_solids::{BlockFrame, add_block};
use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::{PlumbingPoint, plumbing_point_kind};
use glam::Vec3;

/// Builds a tank using its stored capacity or the standard capacity for the household's daily reserve.
pub(super) fn add_tank(
    mesh: &mut SceneMesh,
    point: &PlumbingPoint,
    level: f64,
    occupants: i32,
) -> bool {
    let capacity = if point.capacity_liters > 0.0 {
        point.capacity_liters
    } else {
        [450.0, 750.0, 1100.0, 2500.0, 5000.0, 10000.0, 20000.0]
            .into_iter()
            .find(|&size| size >= occupants as f64 * 200.0)
            .unwrap_or(20000.0)
    };
    let volume = capacity / 1000.0;
    let diameter = (4.0 * volume / std::f64::consts::PI).cbrt();
    let height = volume / (std::f64::consts::PI * diameter * diameter / 4.0);
    let radius = diameter as f32 / 2.0;
    let at = Vec3::new(
        point.position.x as f32,
        point.position.y as f32,
        level as f32,
    );
    let frame = BlockFrame {
        origin: at,
        direction: Vec3::X,
        normal: Vec3::Y,
    };
    let mut floor = at.z;
    if point.kind == plumbing_point_kind::ELEVATED_TANK {
        floor = at.z + point.tank_height as f32;
        let half = radius * 0.8 + 0.1;
        for (sx, sy) in [(-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0)] {
            add_tube_sides(
                mesh,
                at + Vec3::new(sx * half, sy * half, 0.0),
                at + Vec3::new(sx * half, sy * half, floor - at.z),
                0.05,
                SurfaceKind::Metal,
                4,
            );
        }
        let mut z = 1.5_f32;
        while z < floor - at.z - 0.3 {
            for ((ax, ay), (bx, by)) in [
                ((-1.0, -1.0), (1.0, -1.0)),
                ((1.0, -1.0), (1.0, 1.0)),
                ((1.0, 1.0), (-1.0, 1.0)),
                ((-1.0, 1.0), (-1.0, -1.0)),
            ] {
                add_tube_sides(
                    mesh,
                    at + Vec3::new(ax * half, ay * half, z),
                    at + Vec3::new(bx * half, by * half, z + 0.9),
                    0.025,
                    SurfaceKind::Metal,
                    4,
                );
            }
            z += 1.8;
        }
        add_block(
            mesh,
            frame,
            (-half - 0.1, half + 0.1),
            (-half - 0.1, half + 0.1),
            (floor - at.z - 0.08, floor - at.z),
            SurfaceKind::Metal,
        );
        add_tube_sides(
            mesh,
            at + Vec3::new(0.0, 0.0, floor - at.z),
            at,
            0.025,
            SurfaceKind::PipeCold,
            8,
        );
    } else {
        add_block(
            mesh,
            frame,
            (-radius - 0.15, radius + 0.15),
            (-radius - 0.15, radius + 0.15),
            (0.0, 0.10),
            SurfaceKind::Structure,
        );
    }
    let bottom = if point.kind == plumbing_point_kind::ELEVATED_TANK {
        floor
    } else {
        floor + 0.10
    };
    let position = |z| Vec3::new(at.x, at.y, z);
    add_tube_sides(
        mesh,
        position(bottom),
        position(bottom + height as f32),
        radius,
        SurfaceKind::OpeningFrame,
        20,
    );
    add_tube_sides(
        mesh,
        position(bottom + height as f32),
        position(bottom + height as f32 + 0.08),
        radius * 0.75,
        SurfaceKind::OpeningFrame,
        20,
    );
    add_tube_sides(
        mesh,
        position(bottom + height as f32 + 0.08),
        position(bottom + height as f32 + 0.14),
        0.25,
        SurfaceKind::OpeningFrame,
        12,
    );
    true
}
