//! Pitched roof facets and gable walls, matching the original application's triangulation order.

use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::{FloorSlab, floor};
use crate::plan::{PlanRectangle, Point2, counter_clockwise, signed_double_area, triangulate};
use glam::Vec3;

const EPSILON: f64 = 1e-9;

/// Builds a supported pitched roof over its grown boundary, then closes rising edges when requested.
pub(super) fn add_pitched_roof(
    mesh: &mut SceneMesh,
    roof: &FloorSlab,
    boundary: &[Point2],
    level: f64,
) {
    let Some(extent) = PlanRectangle::of(boundary) else {
        return;
    };
    let rise = |point| rise_at(roof, &extent, point);
    let dropped_eave = if boundary != roof.boundary.as_slice() {
        roof.overhang * roof.pitch.to_radians().tan()
    } else {
        0.0
    };
    let eave_top = level + roof.elevation_offset - dropped_eave;
    let ordered = counter_clockwise(boundary);
    let surface = |point: Point2, drop: f64| {
        Vec3::new(
            point.x as f32,
            point.y as f32,
            (eave_top + rise(point) - drop) as f32,
        )
    };
    for facet in facets(roof, &ordered, &extent) {
        let Ok(triangles) = triangulate(&facet) else {
            continue;
        };
        for [a, b, c] in triangles {
            mesh.add_triangle(
                surface(facet[a], 0.0),
                surface(facet[b], 0.0),
                surface(facet[c], 0.0),
                SurfaceKind::Roof,
            );
            mesh.add_triangle(
                surface(facet[c], roof.thickness),
                surface(facet[b], roof.thickness),
                surface(facet[a], roof.thickness),
                SurfaceKind::Floor,
            );
        }
        for index in 0..facet.len() {
            let current = facet[index];
            let next = facet[(index + 1) % facet.len()];
            let midpoint = Point2::new((current.x + next.x) / 2.0, (current.y + next.y) / 2.0);
            if lies_on_boundary(&ordered, midpoint) {
                mesh.add_quad(
                    surface(current, roof.thickness),
                    surface(next, roof.thickness),
                    surface(next, 0.0),
                    surface(current, 0.0),
                    SurfaceKind::Floor,
                );
            }
        }
    }
    if roof.gable_walls {
        add_gable_walls(mesh, roof, level + roof.elevation_offset);
    }
}

/// Returns the roof height above its eave using the shape's bounding-box construction.
fn rise_at(roof: &FloorSlab, extent: &PlanRectangle, point: Point2) -> f64 {
    let along_x = extent.max_x - extent.min_x >= extent.max_y - extent.min_y;
    let (coordinate, minimum, span) = if along_x {
        (point.y, extent.min_y, extent.max_y - extent.min_y)
    } else {
        (point.x, extent.min_x, extent.max_x - extent.min_x)
    };
    let pitch = roof.pitch.to_radians().tan();
    let rise = match roof.shape {
        floor::SHAPE_SHED => pitch * (coordinate - minimum),
        floor::SHAPE_GABLE => pitch * (span / 2.0 - (coordinate - (minimum + span / 2.0)).abs()),
        floor::SHAPE_HIP => {
            pitch
                * (point.x - extent.min_x)
                    .min(extent.max_x - point.x)
                    .min((point.y - extent.min_y).min(extent.max_y - point.y))
        }
        _ => 0.0,
    };
    rise.max(0.0)
}

/// Splits gable and hip roofs into the same planar facets as the source mesher.
fn facets(roof: &FloorSlab, boundary: &[Point2], extent: &PlanRectangle) -> Vec<Vec<Point2>> {
    if roof.shape == floor::SHAPE_SHED {
        return vec![boundary.to_vec()];
    }
    let mut pieces = Vec::new();
    if roof.shape == floor::SHAPE_GABLE {
        if extent.max_x - extent.min_x >= extent.max_y - extent.min_y {
            let middle = (extent.min_y + extent.max_y) / 2.0;
            pieces.push(clip(boundary, 0.0, 1.0, -middle));
            pieces.push(clip(boundary, 0.0, -1.0, middle));
        } else {
            let middle = (extent.min_x + extent.max_x) / 2.0;
            pieces.push(clip(boundary, 1.0, 0.0, -middle));
            pieces.push(clip(boundary, -1.0, 0.0, middle));
        }
    } else {
        let sides = [
            (1.0, 0.0, -extent.min_x),
            (-1.0, 0.0, extent.max_x),
            (0.0, 1.0, -extent.min_y),
            (0.0, -1.0, extent.max_y),
        ];
        for index in 0..sides.len() {
            let mut piece = boundary.to_vec();
            for other in 0..sides.len() {
                if piece.len() < 3 {
                    break;
                }
                if other == index {
                    continue;
                }
                piece = clip(
                    &piece,
                    sides[index].0 - sides[other].0,
                    sides[index].1 - sides[other].1,
                    sides[index].2 - sides[other].2,
                );
            }
            pieces.push(piece);
        }
    }
    pieces
        .into_iter()
        .filter(|piece| piece.len() >= 3 && signed_double_area(piece).abs() > EPSILON)
        .collect()
}

/// Clips a polygon against one half-plane and removes consecutive points on the clipping line.
fn clip(polygon: &[Point2], a: f64, b: f64, c: f64) -> Vec<Point2> {
    let mut result = Vec::new();
    for index in 0..polygon.len() {
        let current = polygon[index];
        let previous = polygon[(index + polygon.len() - 1) % polygon.len()];
        let current_value = a * current.x + b * current.y + c;
        let previous_value = a * previous.x + b * previous.y + c;
        let intersection = || {
            let fraction = previous_value / (previous_value - current_value);
            Point2::new(
                previous.x + (current.x - previous.x) * fraction,
                previous.y + (current.y - previous.y) * fraction,
            )
        };
        if current_value <= EPSILON {
            if previous_value > EPSILON {
                result.push(intersection());
            }
            result.push(current);
        } else if previous_value <= EPSILON {
            result.push(intersection());
        }
    }
    result
        .iter()
        .enumerate()
        .filter_map(|(index, &point)| {
            (point.distance_to(result[(index + 1) % result.len()]) > EPSILON).then_some(point)
        })
        .collect()
}

/// Identifies exterior facet edges so internal ridges do not gain duplicate side faces.
fn lies_on_boundary(boundary: &[Point2], point: Point2) -> bool {
    (0..boundary.len()).any(|index| {
        let a = boundary[index];
        let b = boundary[(index + 1) % boundary.len()];
        let edge = a.distance_to(b);
        if edge < 1e-12 {
            return false;
        }
        let cross = ((b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x)).abs() / edge;
        let along = ((point.x - a.x) * (b.x - a.x) + (point.y - a.y) * (b.y - a.y)) / edge;
        cross < 1e-6 && along >= -1e-6 && along <= edge + 1e-6
    })
}

/// Closes rising edges of the original, unextended footprint with inside and outside wall faces.
fn add_gable_walls(mesh: &mut SceneMesh, roof: &FloorSlab, eave_top: f64) {
    let boundary = counter_clockwise(&roof.boundary);
    let Some(extent) = PlanRectangle::of(&boundary) else {
        return;
    };
    let bottom = (eave_top - roof.thickness) as f32;
    let lift = |point: Point2, z: f32| Vec3::new(point.x as f32, point.y as f32, z);
    for index in 0..boundary.len() {
        let from = boundary[index];
        let to = boundary[(index + 1) % boundary.len()];
        let length = from.distance_to(to);
        if length < 1e-6 {
            continue;
        }
        let profile: Vec<_> = (0..=24)
            .map(|sample| {
                let point = Point2::new(
                    from.x + (to.x - from.x) * sample as f64 / 24.0,
                    from.y + (to.y - from.y) * sample as f64 / 24.0,
                );
                (point, rise_at(roof, &extent, point))
            })
            .collect();
        if profile.iter().map(|(_, rise)| *rise).fold(0.0, f64::max) < 0.05 {
            continue;
        }
        let inward = Point2::new(
            -(to.y - from.y) / length * 0.20,
            (to.x - from.x) / length * 0.20,
        );
        for pair in profile.windows(2) {
            let ((a, rise_a), (b, rise_b)) = (pair[0], pair[1]);
            let top_a = (eave_top + rise_a - roof.thickness) as f32;
            let top_b = (eave_top + rise_b - roof.thickness) as f32;
            if top_a - bottom < 1e-4 && top_b - bottom < 1e-4 {
                continue;
            }
            let inner_a = Point2::new(a.x + inward.x, a.y + inward.y);
            let inner_b = Point2::new(b.x + inward.x, b.y + inward.y);
            mesh.add_quad(
                lift(a, bottom),
                lift(b, bottom),
                lift(b, top_b),
                lift(a, top_a),
                SurfaceKind::Wall,
            );
            mesh.add_quad(
                lift(inner_b, bottom),
                lift(inner_a, bottom),
                lift(inner_a, top_a),
                lift(inner_b, top_b),
                SurfaceKind::InteriorWall,
            );
            mesh.add_quad(
                lift(a, top_a),
                lift(b, top_b),
                lift(inner_b, top_b),
                lift(inner_a, top_a),
                SurfaceKind::Wall,
            );
        }
    }
}
