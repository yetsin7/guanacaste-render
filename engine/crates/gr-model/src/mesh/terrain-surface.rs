//! Thin-plate interpolation of surveyed terrain, using the source application's numeric precision.

use crate::document::{FloorSlab, TopographyPoint};
use crate::plan::Point2;
use glam::{Vec2, Vec3};

/// A surface through survey points and boundary anchors, relative to its level.
pub(super) struct TerrainSurface {
    constant: f64,
    weights: Option<Vec<f64>>,
    affine: [f64; 3],
    centers: Vec<Vec2>,
    center: (f64, f64),
    scale: f64,
    pub minimum: f64,
}

impl TerrainSurface {
    /// Solves the thin-plate spline; a singular or level survey falls back to a constant elevation.
    pub fn new(slab: &FloorSlab) -> Self {
        let points = slab.topo_points.as_deref().unwrap_or_default();
        let mut anchors = Vec::new();
        for corner in &slab.boundary {
            if !points
                .iter()
                .any(|point| Point2::new(point.x, point.y).distance_to(*corner) <= 0.05)
            {
                anchors.push(TopographyPoint {
                    x: corner.x,
                    y: corner.y,
                    elevation: slab.elevation_offset,
                });
            }
        }
        for &point in points {
            anchors.retain(|existing| Point2::new(existing.x, existing.y).distance_to(Point2::new(point.x, point.y)) >= 0.01);
            anchors.push(point);
        }
        let mut surface = Self {
            constant: slab.elevation_offset,
            weights: None,
            affine: [0.0; 3],
            centers: Vec::new(),
            center: (0.0, 0.0),
            scale: 1.0,
            minimum: slab.elevation_offset,
        };
        if anchors.len() < 3 || anchors.iter().all(|anchor| (anchor.elevation - anchors[0].elevation).abs() < 1e-9) {
            surface.constant = anchors.first().map_or(surface.constant, |anchor| anchor.elevation);
            surface.minimum = surface.constant;
            return surface;
        }
        let count = anchors.len();
        surface.center = (
            anchors.iter().map(|anchor| anchor.x).sum::<f64>() / count as f64,
            anchors.iter().map(|anchor| anchor.y).sum::<f64>() / count as f64,
        );
        surface.scale = anchors
            .iter()
            .map(|anchor| (anchor.x - surface.center.0).abs().max((anchor.y - surface.center.1).abs()))
            .fold(0.0, f64::max)
            .max(1e-6);
        surface.centers = anchors
            .iter()
            .map(|anchor| {
                Vec2::new(
                    ((anchor.x - surface.center.0) / surface.scale) as f32,
                    ((anchor.y - surface.center.1) / surface.scale) as f32,
                )
            })
            .collect();
        let size = count + 3;
        let mut matrix = vec![0.0; size * size];
        let mut values = vec![0.0; size];
        for row in 0..count {
            for column in 0..count {
                matrix[row * size + column] = kernel(surface.centers[row].distance(surface.centers[column]));
            }
            matrix[row * size + row] += 1e-9;
            matrix[row * size + count] = 1.0;
            matrix[count * size + row] = 1.0;
            matrix[row * size + count + 1] = surface.centers[row].x as f64;
            matrix[(count + 1) * size + row] = surface.centers[row].x as f64;
            matrix[row * size + count + 2] = surface.centers[row].y as f64;
            matrix[(count + 2) * size + row] = surface.centers[row].y as f64;
            values[row] = anchors[row].elevation;
        }
        let Some(solution) = solve(matrix, values) else {
            surface.constant = anchors.iter().map(|anchor| anchor.elevation).sum::<f64>() / count as f64;
            return surface;
        };
        surface.weights = Some(solution[..count].to_vec());
        surface.affine.copy_from_slice(&solution[count..]);
        surface.minimum = anchors.iter().map(|anchor| anchor.elevation).fold(f64::MAX, f64::min);
        surface
    }

    /// Evaluates the terrain elevation at a world-plan coordinate.
    pub fn elevation_at(&self, point: Point2) -> f64 {
        let Some(weights) = &self.weights else { return self.constant };
        let u = ((point.x - self.center.0) / self.scale) as f32;
        let v = ((point.y - self.center.1) / self.scale) as f32;
        let mut sum = self.affine[0] + self.affine[1] * u as f64 + self.affine[2] * v as f64;
        let position = Vec2::new(u, v);
        for (weight, center) in weights.iter().zip(&self.centers) {
            sum += weight * kernel(position.distance(*center));
        }
        sum
    }

    /// Evaluates the analytic upward normal for smooth shading between survey points.
    pub fn normal_at(&self, point: Point2) -> Vec3 {
        let Some(weights) = &self.weights else { return Vec3::Z };
        let u = (point.x - self.center.0) / self.scale;
        let v = (point.y - self.center.1) / self.scale;
        let (mut gradient_x, mut gradient_y) = (self.affine[1], self.affine[2]);
        for (weight, center) in weights.iter().zip(&self.centers) {
            let (dx, dy) = (u - center.x as f64, v - center.y as f64);
            let squared = dx * dx + dy * dy;
            if squared < 1e-18 {
                continue;
            }
            let factor = weight * (squared.ln() + 1.0);
            gradient_x += factor * dx;
            gradient_y += factor * dy;
        }
        Vec3::new((-gradient_x / self.scale) as f32, (-gradient_y / self.scale) as f32, 1.0).normalize()
    }
}

/// Thin-plate kernel; the squared distance is rounded as a float before the logarithm is applied.
fn kernel(distance: f32) -> f64 {
    if distance < 1e-9 {
        0.0
    } else {
        (distance * distance) as f64 * (distance as f64).ln()
    }
}

/// Solves the dense interpolation system with partial pivoting, returning none for a singular system.
fn solve(mut matrix: Vec<f64>, mut values: Vec<f64>) -> Option<Vec<f64>> {
    let size = values.len();
    for pivot in 0..size {
        let mut best = pivot;
        for row in pivot + 1..size {
            if matrix[row * size + pivot].abs() > matrix[best * size + pivot].abs() {
                best = row;
            }
        }
        if matrix[best * size + pivot].abs() < 1e-12 {
            return None;
        }
        if best != pivot {
            for column in 0..size {
                matrix.swap(pivot * size + column, best * size + column);
            }
            values.swap(pivot, best);
        }
        for row in pivot + 1..size {
            let factor = matrix[row * size + pivot] / matrix[pivot * size + pivot];
            if factor == 0.0 {
                continue;
            }
            for column in pivot..size {
                matrix[row * size + column] -= factor * matrix[pivot * size + column];
            }
            values[row] -= factor * values[pivot];
        }
    }
    let mut result = vec![0.0; size];
    for row in (0..size).rev() {
        let mut sum = values[row];
        for column in row + 1..size {
            sum -= matrix[row * size + column] * result[column];
        }
        result[row] = sum / matrix[row * size + row];
    }
    result.iter().all(|value| value.is_finite()).then_some(result)
}
