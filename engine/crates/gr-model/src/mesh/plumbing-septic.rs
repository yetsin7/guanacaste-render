//! Septic-tank and drain-field geometry derived from the original site design.

use super::household_size::household_size;
use super::plumbing_geometry::add_tube_sides;
use super::plumbing_solids::{BlockFrame, add_block};
use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::{Document, PlumbingPoint};
use glam::Vec3;

/// Builds the tank, outlet, distribution box, and infiltration trenches from household and soil data.
pub(super) fn add_septic(
    mesh: &mut SceneMesh,
    document: &Document,
    point: &PlumbingPoint,
    level: f64,
) {
    let people = household_size(document, point) as f64;
    let daily = people * 150.0;
    let detention = if daily <= 1500.0 {
        1.0
    } else if daily <= 3000.0 {
        0.92
    } else if daily <= 4500.0 {
        0.83
    } else {
        0.75
    };
    let volume = 1000.0 + people * (150.0 * detention + 57.0);
    let width = (volume / 1000.0 / (2.0 * 1.2)).sqrt();
    let field_length = daily / point.percolation.max(1.0) / 0.6;
    let trenches = ((field_length / 20.0).ceil() as i32).max(1);
    let walls: Vec<_> = document
        .walls
        .iter()
        .filter(|wall| wall.level_id == point.level_id)
        .collect();
    let (cx, cy) = if walls.is_empty() {
        (point.position.x - 1.0, point.position.y)
    } else {
        (
            walls
                .iter()
                .map(|wall| (wall.start.x + wall.end.x) / 2.0)
                .sum::<f64>()
                / walls.len() as f64,
            walls
                .iter()
                .map(|wall| (wall.start.y + wall.end.y) / 2.0)
                .sum::<f64>()
                / walls.len() as f64,
        )
    };
    let (dx, dy) = (point.position.x - cx, point.position.y - cy);
    let length = (dx * dx + dy * dy).sqrt();
    let (dx, dy) = if length > 1e-6 {
        (dx / length, dy / length)
    } else {
        (1.0, 0.0)
    };
    let direction = Vec3::new(dx as f32, dy as f32, 0.0);
    let normal = Vec3::new(-direction.y, direction.x, 0.0);
    let center = Vec3::new(point.position.x as f32, point.position.y as f32, 0.0);
    let frame = BlockFrame {
        origin: center,
        direction,
        normal,
    };
    let half_length = (2.0 * width) as f32 / 2.0 + 0.10;
    let half_width = width as f32 / 2.0 + 0.10;
    let top = level as f32 - 0.25;
    let bottom = top - 1.2 - 0.40;
    let block = |mesh: &mut SceneMesh, along, across, elevation, surface| {
        add_block(mesh, frame, along, across, elevation, surface);
    };
    block(
        mesh,
        (-half_length, half_length),
        (-half_width, half_width),
        (bottom, bottom + 0.10),
        SurfaceKind::Structure,
    );
    block(
        mesh,
        (-half_length, half_length),
        (-half_width, half_width),
        (top - 0.10, top),
        SurfaceKind::Structure,
    );
    block(
        mesh,
        (-half_length, -half_length + 0.10),
        (-half_width, half_width),
        (bottom, top),
        SurfaceKind::Structure,
    );
    block(
        mesh,
        (half_length - 0.10, half_length),
        (-half_width, half_width),
        (bottom, top),
        SurfaceKind::Structure,
    );
    block(
        mesh,
        (-half_length, half_length),
        (-half_width, -half_width + 0.10),
        (bottom, top),
        SurfaceKind::Structure,
    );
    block(
        mesh,
        (-half_length, half_length),
        (half_width - 0.10, half_width),
        (bottom, top),
        SurfaceKind::Structure,
    );
    let baffle = -half_length + 2.0 * half_length * 2.0 / 3.0;
    block(
        mesh,
        (baffle - 0.04, baffle + 0.04),
        (-half_width + 0.10, half_width - 0.10),
        (bottom + 0.3, top - 0.15),
        SurfaceKind::Structure,
    );
    for at in [-half_length * 0.55, half_length * 0.65] {
        add_tube_sides(
            mesh,
            center + direction * at + Vec3::new(0.0, 0.0, top),
            center + direction * at + Vec3::new(0.0, 0.0, level as f32 + 0.03),
            0.30,
            SurfaceKind::Metal,
            8,
        );
    }
    let pipe_z = top - 0.30;
    let distribution = center + direction * (half_length + 1.5);
    add_tube_sides(
        mesh,
        center + direction * half_length + Vec3::new(0.0, 0.0, pipe_z),
        distribution + Vec3::new(0.0, 0.0, pipe_z - 0.03),
        0.055,
        SurfaceKind::PipeDrain,
        8,
    );
    add_block(
        mesh,
        BlockFrame {
            origin: distribution,
            ..frame
        },
        (-0.3, 0.3),
        (-0.3, 0.3),
        (pipe_z - 0.35, level as f32 - 0.05),
        SurfaceKind::Structure,
    );
    let trench_length = (field_length / trenches as f64) as f32;
    for trench in 0..trenches {
        let offset = (trench as f32 - (trenches - 1) as f32 / 2.0) * 2.0;
        let start = distribution + direction * 1.0 + normal * offset;
        let gravel_top = pipe_z - 0.02;
        add_block(
            mesh,
            BlockFrame {
                origin: start,
                ..frame
            },
            (0.0, trench_length),
            (-0.6 / 2.0, 0.6 / 2.0),
            (gravel_top - 0.35, gravel_top),
            SurfaceKind::Earth,
        );
        add_tube_sides(
            mesh,
            start + Vec3::new(0.0, 0.0, gravel_top - 0.10),
            start
                + direction * trench_length
                + Vec3::new(0.0, 0.0, gravel_top - 0.10 - 0.005 * trench_length),
            0.05,
            SurfaceKind::PipeDrain,
            8,
        );
        if offset.abs() > 0.01 {
            add_tube_sides(
                mesh,
                distribution + Vec3::new(0.0, 0.0, gravel_top - 0.10),
                start + Vec3::new(0.0, 0.0, gravel_top - 0.10),
                0.05,
                SurfaceKind::PipeDrain,
                8,
            );
        }
    }
}
