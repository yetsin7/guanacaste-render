//! Oriented rectangular solids used by water-storage and septic geometry.

use super::scene_mesh::{SceneMesh, SurfaceKind};
use glam::Vec3;

/// Plan coordinates and orthogonal axes for an oriented plumbing solid.
#[derive(Clone, Copy)]
pub(super) struct BlockFrame {
    pub origin: Vec3,
    pub direction: Vec3,
    pub normal: Vec3,
}

/// Adds a box in a local plan frame, keeping the source renderer's side-then-cap triangle order.
pub(super) fn add_block(
    mesh: &mut SceneMesh,
    frame: BlockFrame,
    along: (f32, f32),
    across: (f32, f32),
    elevation: (f32, f32),
    surface: SurfaceKind,
) {
    let point = |a, b, z| {
        Vec3::new(frame.origin.x, frame.origin.y, 0.0)
            + frame.direction * a
            + frame.normal * b
            + Vec3::new(0.0, 0.0, z)
    };
    let corners = [
        (along.0, across.0),
        (along.1, across.0),
        (along.1, across.1),
        (along.0, across.1),
    ];
    for index in 0..4 {
        let (aa, ab) = corners[index];
        let (ba, bb) = corners[(index + 1) % 4];
        mesh.add_quad(
            point(aa, ab, elevation.0),
            point(ba, bb, elevation.0),
            point(ba, bb, elevation.1),
            point(aa, ab, elevation.1),
            surface,
        );
    }
    mesh.add_quad(
        point(along.0, across.0, elevation.1),
        point(along.1, across.0, elevation.1),
        point(along.1, across.1, elevation.1),
        point(along.0, across.1, elevation.1),
        surface,
    );
    mesh.add_quad(
        point(along.0, across.1, elevation.0),
        point(along.1, across.1, elevation.0),
        point(along.1, across.0, elevation.0),
        point(along.0, across.0, elevation.0),
        surface,
    );
}
