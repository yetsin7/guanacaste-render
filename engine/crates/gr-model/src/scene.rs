//! From a meshed project to the scene the render engine consumes: one stock material per surface kind, a camera
//! that frames the building and default daylight.

use crate::mesh::{SceneMesh, SurfaceKind};
use glam::Vec3;
use gr_core::scene_file::{Camera, Environment, Material, Scene};
use std::collections::HashMap;

/// The stock material of every surface kind, exported from `MaterialLibrary.ForKind` of the C# application by
/// `RenderBench --mesh-reference`. One row per kind, in the order of [`SurfaceKind`].
const STOCK_MATERIALS: &str = include_str!("../data/stock-materials.tsv");

/// The material of each surface kind, indexed by its number.
pub fn stock_materials() -> Vec<Material> {
    let materials: Vec<Material> = STOCK_MATERIALS
        .lines()
        .skip(1)
        .filter(|line| !line.trim().is_empty())
        .map(|line| {
            let fields: Vec<&str> = line.split('\t').collect();
            let number = |index: usize| fields.get(index).and_then(|field| field.parse::<f32>().ok()).unwrap_or(0.0);
            let colour = |index: usize| Vec3::new(number(index), number(index + 1), number(index + 2));
            Material {
                base_color: colour(2),
                roughness: number(5),
                metallic: number(6),
                ior: number(7),
                emission: colour(8),
                transmission: number(11),
                transmission_tint: colour(12),
                translucency: number(15),
                alpha_cutoff: number(16),
                pattern: number(17) as u32,
            }
        })
        .collect();
    debug_assert_eq!(materials.len(), SurfaceKind::COUNT);
    materials
}

/// Frames bounds from a south-east, elevated three-quarter viewpoint, as `RenderCamera.FrameBounds` does.
pub fn framing_camera(min: Vec3, max: Vec3) -> Camera {
    let center = (min + max) / 2.0;
    let radius = ((max - min).length() / 2.0).max(3.0);
    let (azimuth, elevation) = ((-50.0f32).to_radians(), 28.0f32.to_radians());
    let direction = Vec3::new(elevation.cos() * azimuth.cos(), elevation.cos() * azimuth.sin(), elevation.sin());
    const FIELD_OF_VIEW: f32 = 45.0;
    // A bounding-sphere fit is conservative for box-like buildings; 0.8 frames them more tightly.
    let distance = radius / (FIELD_OF_VIEW.to_radians() / 2.0).sin() * 0.8;
    Camera {
        position: center + direction * distance,
        target: center,
        vertical_fov_degrees: FIELD_OF_VIEW,
    }
}

/// The scene of a mesh. `building_bounds` are the bounds without the ground plane, which the camera frames.
pub fn to_scene(mesh: SceneMesh, building_bounds: (Vec3, Vec3)) -> Scene {
    let triangles = mesh.triangle_count();
    let mut materials = stock_materials();
    let mut dimmed = HashMap::new();
    let triangle_material = mesh
        .kinds
        .iter()
        .enumerate()
        .map(|(index, &kind)| {
            let original = kind as usize;
            let scale = mesh.emission_scale.as_ref().map_or(1.0, |values| values[index]);
            if scale >= 1.0 || materials[original].emission == Vec3::ZERO {
                return original as u32;
            }
            let step = (scale * 64.0).round_ties_even() as u32;
            *dimmed.entry((original, step)).or_insert_with(|| {
                let mut material = materials[original];
                material.emission *= step as f32 / 64.0;
                let index = materials.len() as u32;
                materials.push(material);
                index
            })
        })
        .collect();
    Scene {
        indices: (0..triangles as u32)
            .map(|triangle| [triangle * 3, triangle * 3 + 1, triangle * 3 + 2])
            .collect(),
        triangle_material,
        normals: mesh.normals,
        positions: mesh.positions,
        corner_uvs: None,
        materials,
        lights: Vec::new(),
        camera: framing_camera(building_bounds.0, building_bounds.1),
        // The defaults of `RenderSettings` in the application.
        environment: Environment {
            sun_direction: Vec3::new(-0.45, -0.55, 0.70),
            sun_intensity: 2.0,
            sky_intensity: 1.2,
            exposure: 0.4,
        },
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    /// Keeps the stock material table aligned with the serialized surface categories.
    fn there_is_one_stock_material_per_surface_kind() {
        let materials = stock_materials();
        assert_eq!(materials.len(), SurfaceKind::COUNT);
        let glass = &materials[SurfaceKind::Glass as usize];
        assert!(glass.transmission == 1.0 && glass.ior == 1.5);
        let ground = &materials[SurfaceKind::Ground as usize];
        assert!((ground.base_color - Vec3::new(0.22, 0.28, 0.13)).length() < 1e-6);
        assert!(
            materials
                .iter()
                .all(|m| m.base_color.is_finite() && (0.0..=1.0).contains(&m.roughness))
        );
    }

    #[test]
    /// Verifies that the initial camera faces the scene center from above.
    fn the_framing_camera_looks_at_the_centre_from_above() {
        let camera = framing_camera(Vec3::new(0.0, 0.0, 0.0), Vec3::new(12.0, 8.0, 3.0));
        assert!((camera.target - Vec3::new(6.0, 4.0, 1.5)).length() < 1e-6);
        assert!(camera.position.z > camera.target.z && camera.position.y < camera.target.y);
    }
}
