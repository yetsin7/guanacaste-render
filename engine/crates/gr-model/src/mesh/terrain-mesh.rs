//! Smooth terrain surface, side walls, and underside in the original mesher's triangle order.

use super::scene_mesh::{SceneMesh, SurfaceKind};
use super::terrain_surface::TerrainSurface;
use crate::document::FloorSlab;
use crate::plan::{PlanRectangle, Point2, counter_clockwise, signed_double_area, triangulate};
use glam::Vec3;

/// Appends the sampled terrain solid and returns the lowest elevation at a boundary corner.
pub(super) fn add_terrain(
    mesh: &mut SceneMesh,
    slab: &FloorSlab,
    surface: &TerrainSurface,
    level: f64,
) -> Option<f64> {
    let boundary = counter_clockwise(&slab.boundary);
    let extent = PlanRectangle::of(&boundary)?;
    let spacing =
        ((extent.max_x - extent.min_x).max(extent.max_y - extent.min_y) / 90.0).clamp(0.2, 5.0);
    let datum = level as f32;
    let lift = |point: Point2| {
        Vec3::new(
            point.x as f32,
            point.y as f32,
            datum + surface.elevation_at(point) as f32,
        )
    };
    let mut lowest = f64::MAX;
    let columns = (((extent.max_x - extent.min_x) / spacing).ceil() as i32).max(1);
    let rows = (((extent.max_y - extent.min_y) / spacing).ceil() as i32).max(1);
    for row in 0..rows {
        for column in 0..columns {
            let x = extent.min_x + column as f64 * spacing;
            let y = extent.min_y + row as f64 * spacing;
            let cell = [
                Point2::new(x, y),
                Point2::new(x + spacing, y),
                Point2::new(x + spacing, y + spacing),
                Point2::new(x, y + spacing),
            ];
            let mut piece = clip_to_cell(&boundary, &cell);
            if piece.len() < 3 || signed_double_area(&piece).abs() < 1e-10 {
                continue;
            }
            let triangles = match triangulate(&piece) {
                Ok(triangles) => triangles,
                Err(_) => {
                    piece = cell.to_vec();
                    [[0, 1, 2], [0, 2, 3]]
                        .into_iter()
                        .filter(|triangle| {
                            let [a, b, c] = *triangle;
                            contains(
                                &boundary,
                                Point2::new(
                                    (cell[a].x + cell[b].x + cell[c].x) / 3.0,
                                    (cell[a].y + cell[b].y + cell[c].y) / 3.0,
                                ),
                            )
                        })
                        .collect()
                }
            };
            let lifted: Vec<Vec3> = piece.iter().copied().map(lift).collect();
            let normals: Vec<Vec3> = piece
                .iter()
                .copied()
                .map(|point| surface.normal_at(point))
                .collect();
            for point in &lifted {
                lowest = lowest.min((point.z - datum) as f64);
            }
            for [a, b, c] in triangles {
                mesh.add_smooth_triangle(
                    [lifted[a], lifted[b], lifted[c]],
                    [normals[a], normals[b], normals[c]],
                    SurfaceKind::Ground,
                );
            }
        }
    }
    let mut edge_points = Vec::new();
    for index in 0..boundary.len() {
        let from = boundary[index];
        let to = boundary[(index + 1) % boundary.len()];
        let steps = ((from.distance_to(to) / spacing).ceil() as i32).max(1);
        for step in 0..steps {
            edge_points.push(Point2::new(
                from.x + (to.x - from.x) * step as f64 / steps as f64,
                from.y + (to.y - from.y) * step as f64 / steps as f64,
            ));
        }
    }
    for &point in &edge_points {
        lowest = lowest.min(surface.elevation_at(point));
    }
    if lowest == f64::MAX {
        lowest = surface.minimum;
    }
    let bottom = datum + (lowest - slab.thickness.max(0.03)) as f32;
    for index in 0..edge_points.len() {
        let top_a = lift(edge_points[index]);
        let top_b = lift(edge_points[(index + 1) % edge_points.len()]);
        mesh.add_quad(
            Vec3::new(top_a.x, top_a.y, bottom),
            Vec3::new(top_b.x, top_b.y, bottom),
            top_b,
            top_a,
            SurfaceKind::Earth,
        );
    }
    if let Ok(triangles) = triangulate(&boundary) {
        for [a, b, c] in triangles {
            mesh.add_triangle(
                Vec3::new(boundary[a].x as f32, boundary[a].y as f32, bottom),
                Vec3::new(boundary[c].x as f32, boundary[c].y as f32, bottom),
                Vec3::new(boundary[b].x as f32, boundary[b].y as f32, bottom),
                SurfaceKind::Earth,
            );
        }
    }
    boundary
        .iter()
        .map(|&point| level + surface.elevation_at(point))
        .reduce(f64::min)
}

/// Tests the even-odd interior rule used when positioning plants on a site.
pub(super) fn contains(polygon: &[Point2], point: Point2) -> bool {
    if polygon.is_empty() {
        return false;
    }
    let mut inside = false;
    let mut previous = polygon.len() - 1;
    for index in 0..polygon.len() {
        let a = polygon[index];
        let b = polygon[previous];
        if (a.y > point.y) != (b.y > point.y)
            && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x
        {
            inside = !inside;
        }
        previous = index;
    }
    inside
}

/// Clips the site polygon against one counter-clockwise grid cell.
fn clip_to_cell(subject: &[Point2], cell: &[Point2]) -> Vec<Point2> {
    let mut output = subject.to_vec();
    for edge in 0..cell.len() {
        if output.is_empty() {
            break;
        }
        let a = cell[edge];
        let b = cell[(edge + 1) % cell.len()];
        let side = |point: Point2| (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
        let input = std::mem::take(&mut output);
        for index in 0..input.len() {
            let current = input[index];
            let previous = input[(index + input.len() - 1) % input.len()];
            let current_side = side(current);
            let previous_side = side(previous);
            let intersection = || {
                let fraction = previous_side / (previous_side - current_side);
                Point2::new(
                    previous.x + (current.x - previous.x) * fraction,
                    previous.y + (current.y - previous.y) * fraction,
                )
            };
            if current_side >= 0.0 {
                if previous_side < 0.0 {
                    output.push(intersection());
                }
                output.push(current);
            } else if previous_side >= 0.0 {
                output.push(intersection());
            }
        }
    }
    let mut cleaned: Vec<Point2> = Vec::new();
    for point in output {
        if cleaned
            .last()
            .is_none_or(|last| last.distance_to(point) > 1e-9)
        {
            cleaned.push(point);
        }
    }
    if cleaned.len() > 1 && cleaned[0].distance_to(*cleaned.last().expect("nonempty")) <= 1e-9 {
        cleaned.pop();
    }
    cleaned
}
