//! The two solids nearly everything is built from: a vertical prism over a plan outline, and a box laid out in
//! the coordinates of a wall.
//!
//! Ported from `AddPrism` and `AddWallBlock` in `geometry/building-mesher.cs`. Arithmetic is done in the same
//! precision at the same steps as the original (plan geometry in `f64`, then `f32` corners), because the result is
//! compared with it triangle by triangle.

use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::Wall;
use crate::plan::{Point2, signed_double_area, triangulate};
use glam::{Vec2, Vec3};

/// How the sides and the top of a prism depart from a plain box.
#[derive(Default)]
pub struct PrismShape<'a> {
    /// Surface of one side, from the middle of its edge and its outward direction; `None` uses the prism's kind.
    pub side_kind: Option<&'a dyn Fn(Vec2, Vec2) -> SurfaceKind>,
    /// Height of the top above one corner; `None` gives a flat top.
    pub top_at: Option<&'a dyn Fn(Vec2) -> f32>,
}

fn lift(point: Vec2, z: f32) -> Vec3 {
    Vec3::new(point.x, point.y, z)
}

pub fn to_f32(outline: &[Point2]) -> Vec<Vec2> {
    outline
        .iter()
        .map(|point| Vec2::new(point.x as f32, point.y as f32))
        .collect()
}

/// Extrudes a simple polygon vertically into a closed, outward-facing prism.
pub fn add_prism(
    mesh: &mut SceneMesh,
    outline: &[Vec2],
    bottom: f32,
    top: f32,
    kind: SurfaceKind,
    shape: &PrismShape<'_>,
) {
    let top_of = |corner: Vec2| shape.top_at.map_or(top, |top_at| top_at(corner));
    let as_plan = |points: &[Vec2]| -> Vec<Point2> {
        points
            .iter()
            .map(|p| Point2::new(p.x as f64, p.y as f64))
            .collect()
    };
    let counter_clockwise = signed_double_area(&as_plan(outline)) > 0.0;
    let ordered: Vec<Vec2> = if counter_clockwise {
        outline.to_vec()
    } else {
        outline.iter().rev().copied().collect()
    };
    // Far from the origin an `f32` can no longer tell the corners of a thin outline apart and its caps collapse to
    // no area. The sides still stand: one flattened element must not stop the whole model from meshing.
    let caps = triangulate(&as_plan(&ordered)).unwrap_or_default();
    for [a, b, c] in caps {
        mesh.add_triangle(
            lift(ordered[a], top_of(ordered[a])),
            lift(ordered[b], top_of(ordered[b])),
            lift(ordered[c], top_of(ordered[c])),
            kind,
        );
        mesh.add_triangle(
            lift(ordered[c], bottom),
            lift(ordered[b], bottom),
            lift(ordered[a], bottom),
            kind,
        );
    }
    for index in 0..ordered.len() {
        let current = ordered[index];
        let next = ordered[(index + 1) % ordered.len()];
        let mut side = kind;
        if let Some(side_kind) = shape.side_kind
            && current.distance_squared(next) > 1e-12
        {
            // Counter-clockwise outline: the outward normal of an edge is its direction turned clockwise.
            let direction = (next - current) / (next - current).length();
            side = side_kind((current + next) / 2.0, Vec2::new(direction.y, -direction.x));
        }
        mesh.add_quad(
            lift(current, bottom),
            lift(next, bottom),
            lift(next, top_of(next)),
            lift(current, top_of(current)),
            side,
        );
    }
}

/// The frame a wall gives to boxes laid out along it.
#[derive(Clone, Copy)]
pub struct WallFrame {
    origin: Vec2,
    direction: Vec2,
    normal: Vec2,
}

impl WallFrame {
    pub fn new(wall: &Wall) -> WallFrame {
        let length = wall.length();
        let direction = Vec2::new(
            ((wall.end.x - wall.start.x) / length) as f32,
            ((wall.end.y - wall.start.y) / length) as f32,
        );
        WallFrame {
            origin: Vec2::new(wall.start.x as f32, wall.start.y as f32),
            direction,
            normal: Vec2::new(-direction.y, direction.x),
        }
    }

    /// Adds a box: `from`..`to` run along the wall from its start, `side_a` and `side_b` measure across it from
    /// the centreline (in either order), all in metres.
    #[allow(clippy::too_many_arguments)]
    pub fn block(
        &self,
        mesh: &mut SceneMesh,
        from: f64,
        to: f64,
        side_a: f64,
        side_b: f64,
        bottom: f64,
        top: f64,
        kind: SurfaceKind,
    ) {
        let a = self.origin + self.direction * from as f32;
        let b = self.origin + self.direction * to as f32;
        let near = self.normal * side_a as f32;
        let far = self.normal * side_b as f32;
        add_prism(
            mesh,
            &[a + near, b + near, b + far, a + far],
            bottom as f32,
            top as f32,
            kind,
            &PrismShape::default(),
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Six times the signed volume enclosed by the triangles; positive when they face outwards.
    fn signed_volume(mesh: &SceneMesh) -> f32 {
        (0..mesh.triangle_count())
            .map(|t| mesh.triangle(t))
            .map(|[a, b, c]| a.dot(b.cross(c)))
            .sum::<f32>()
            / 6.0
    }

    #[test]
    fn a_prism_is_closed_and_faces_outwards_whatever_the_winding() {
        let square = [
            Vec2::new(0., 0.),
            Vec2::new(2., 0.),
            Vec2::new(2., 3.),
            Vec2::new(0., 3.),
        ];
        let reversed: Vec<Vec2> = square.iter().rev().copied().collect();
        for outline in [&square[..], &reversed[..]] {
            let mut mesh = SceneMesh::default();
            add_prism(
                &mut mesh,
                outline,
                1.0,
                5.0,
                SurfaceKind::Wall,
                &PrismShape::default(),
            );
            assert_eq!(mesh.triangle_count(), 12);
            assert!((signed_volume(&mesh) - 24.0).abs() < 1e-4);
        }
    }

    #[test]
    fn sides_can_take_their_own_surface_and_the_top_can_slope() {
        let square = [
            Vec2::new(0., 0.),
            Vec2::new(2., 0.),
            Vec2::new(2., 2.),
            Vec2::new(0., 2.),
        ];
        let facing_east = |_: Vec2, outward: Vec2| {
            if outward.x > 0.5 {
                SurfaceKind::WallBrick
            } else {
                SurfaceKind::InteriorWall
            }
        };
        let rising = |corner: Vec2| 1.0 + corner.x;
        let mut mesh = SceneMesh::default();
        add_prism(
            &mut mesh,
            &square,
            0.0,
            3.0,
            SurfaceKind::Wall,
            &PrismShape {
                side_kind: Some(&facing_east),
                top_at: Some(&rising),
            },
        );
        assert_eq!(
            mesh.kinds
                .iter()
                .filter(|&&kind| kind == SurfaceKind::WallBrick)
                .count(),
            2
        );
        assert_eq!(
            mesh.kinds
                .iter()
                .filter(|&&kind| kind == SurfaceKind::InteriorWall)
                .count(),
            6
        );
        // A wedge 1 m tall at x = 0 and 3 m at x = 2 over a 2 x 2 base.
        assert!((signed_volume(&mesh) - 8.0).abs() < 1e-4);
    }

    #[test]
    fn a_block_is_laid_out_along_and_across_its_wall() {
        let wall: Wall = serde_json::from_str(
            r#"{"Id":"7d256c39-bda6-4166-b3c7-5ecde5c223ac","LevelId":"7d256c39-bda6-4166-b3c7-5ecde5c223ac",
                "Start":{"X":1,"Y":1},"End":{"X":1,"Y":5},"Thickness":0.2,"Height":3,
                "WallTypeId":"7d256c39-bda6-4166-b3c7-5ecde5c223ac"}"#,
        )
        .expect("valid wall");
        let mut mesh = SceneMesh::default();
        WallFrame::new(&wall).block(
            &mut mesh,
            1.0,
            2.0,
            0.1,
            -0.1,
            0.5,
            1.5,
            SurfaceKind::OpeningFrame,
        );
        let (min, max) = mesh.bounds();
        // The wall runs north, so "along" is +Y and its left side is -X.
        assert!(
            (min - Vec3::new(0.9, 2.0, 0.5)).length() < 1e-6
                && (max - Vec3::new(1.1, 3.0, 1.5)).length() < 1e-6
        );
        assert!((signed_volume(&mesh) - 0.2).abs() < 1e-5);
    }
}
