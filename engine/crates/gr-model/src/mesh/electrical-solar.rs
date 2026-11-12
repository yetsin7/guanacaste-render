//! Tilted photovoltaic modules and their support rails.

use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::ElectricalDevice;
use glam::Vec3;

/// Three unit axes describing the orientation of a solar module.
#[derive(Clone, Copy)]
struct SolarFrame {
    across: Vec3,
    up_slope: Vec3,
    normal: Vec3,
}

/// Builds the same rows, module count, inclination, and support rails as the source renderer.
pub(super) fn add_solar_array(mesh: &mut SceneMesh, device: &ElectricalDevice, datum: f64) {
    let facing = device.facing as f32;
    let facing = Vec3::new(facing.cos(), facing.sin(), 0.0);
    let tilt = (device.tilt * std::f64::consts::PI / 180.0) as f32;
    let frame = SolarFrame {
        across: Vec3::new(-facing.y, facing.x, 0.0),
        up_slope: (-facing * tilt.cos() + Vec3::Z * tilt.sin()).normalize(),
        normal: (facing * tilt.sin() + Vec3::Z * tilt.cos()).normalize(),
    };
    let center = Vec3::new(
        device.position.x as f32,
        device.position.y as f32,
        (datum + device.elevation) as f32,
    );
    let rows = (((device.panel_count as f64 / 2.5).sqrt().round() as i32).max(1)) as usize;
    let columns = (device.panel_count as f64 / rows as f64).ceil() as usize;
    let (length, width, gap) = (2.28_f32, 1.13_f32, 0.02_f32);
    let mut placed = 0;
    for row in 0..rows {
        for column in 0..columns {
            if placed >= device.panel_count {
                break;
            }
            let across = (column as f32 - (columns as f32 - 1.0) / 2.0) * (width + gap);
            let up = (row as f32 - (rows as f32 - 1.0) / 2.0) * (length + gap);
            let middle = center + frame.across * across + frame.up_slope * up;
            add_oriented_box(
                mesh,
                middle + frame.normal * 0.02,
                frame,
                Vec3::new(width / 2.0 - 0.02, length / 2.0 - 0.02, 0.004),
                SurfaceKind::SolarCell,
            );
            add_oriented_box(
                mesh,
                middle,
                frame,
                Vec3::new(width / 2.0, length / 2.0, 0.018),
                SurfaceKind::Metal,
            );
            placed += 1;
        }
    }
    for row in 0..rows {
        for at in [-0.25, 0.25] {
            let middle = center
                + frame.up_slope
                    * ((row as f32 - (rows as f32 - 1.0) / 2.0) * (length + gap) + at * length)
                - frame.normal * 0.04;
            add_oriented_box(
                mesh,
                middle,
                frame,
                Vec3::new(columns as f32 * (width + gap) / 2.0, 0.02, 0.02),
                SurfaceKind::Metal,
            );
        }
    }
}

/// Emits a box centered on three arbitrary axes with the original renderer's face order.
fn add_oriented_box(
    mesh: &mut SceneMesh,
    center: Vec3,
    frame: SolarFrame,
    half: Vec3,
    surface: SurfaceKind,
) {
    let point = |u: f32, v: f32, n: f32| {
        center
            + frame.across * (u * half.x)
            + frame.up_slope * (v * half.y)
            + frame.normal * (n * half.z)
    };
    for corners in [
        [(-1., -1., 1.), (1., -1., 1.), (1., 1., 1.), (-1., 1., 1.)],
        [
            (-1., 1., -1.),
            (1., 1., -1.),
            (1., -1., -1.),
            (-1., -1., -1.),
        ],
        [
            (-1., -1., -1.),
            (1., -1., -1.),
            (1., -1., 1.),
            (-1., -1., 1.),
        ],
        [(1., 1., -1.), (-1., 1., -1.), (-1., 1., 1.), (1., 1., 1.)],
        [(1., -1., -1.), (1., 1., -1.), (1., 1., 1.), (1., -1., 1.)],
        [
            (-1., 1., -1.),
            (-1., -1., -1.),
            (-1., -1., 1.),
            (-1., 1., 1.),
        ],
    ] {
        let [a, b, c, d] = corners.map(|(u, v, n)| point(u, v, n));
        mesh.add_quad(a, b, c, d, surface);
    }
}
