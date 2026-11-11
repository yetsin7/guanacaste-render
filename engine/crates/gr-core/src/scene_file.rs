//! Reader of the `.grscene` exchange file written by `SceneExchange` in the C# application
//! (`src/GuanacasteRender.Core/rendering/scene-exchange.cs`). Both sides must change together.
//!
//! Layout, little endian:
//!
//! ```text
//! "GRSCENE1"                       8 bytes
//! version u32 (= 1), flags u32     bit 0: vertex normals, bit 1: per-corner texture coordinates
//! vertices, triangles, materials, lights            4 x u32
//! camera position, target (2 x vec3), vertical field of view in degrees (f32)
//! sun direction (vec3), sun intensity, sky intensity, exposure (3 x f32)
//! positions            vertices x vec3
//! indices              triangles x 3 x u32
//! triangle material    triangles x u32
//! normals              vertices x vec3          (flag bit 0)
//! corner uvs           triangles x 3 x vec2     (flag bit 1)
//! materials            64 bytes each, see [`Material`]
//! lights               28 bytes each: position, intensity (2 x vec3), radius
//! ```
//!
//! The world is Z-up and measured in metres. Every count and index is validated, so a truncated or hostile file is
//! rejected with an error instead of producing out-of-range geometry.

use glam::{Vec2, Vec3};
use std::fmt;

pub const VERSION: u32 = 1;
const HAS_NORMALS: u32 = 1;
const HAS_UVS: u32 = 2;
/// Refuse files that claim more elements than any real project needs; guards the allocations below.
const MAX_ELEMENTS: u64 = 400_000_000;

#[derive(Debug)]
pub enum SceneError {
    Io(std::io::Error),
    Invalid(&'static str),
}

impl fmt::Display for SceneError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            SceneError::Io(error) => write!(f, "cannot read the scene file: {error}"),
            SceneError::Invalid(reason) => write!(f, "invalid scene file: {reason}"),
        }
    }
}

impl std::error::Error for SceneError {}

impl From<std::io::Error> for SceneError {
    fn from(error: std::io::Error) -> Self {
        SceneError::Io(error)
    }
}

#[derive(Clone, Copy, Debug)]
pub struct Camera {
    pub position: Vec3,
    pub target: Vec3,
    pub vertical_fov_degrees: f32,
}

#[derive(Clone, Copy, Debug)]
pub struct Environment {
    /// Unit vector from the scene towards the sun.
    pub sun_direction: Vec3,
    pub sun_intensity: f32,
    pub sky_intensity: f32,
    pub exposure: f32,
}

#[derive(Clone, Copy, Debug)]
pub struct Material {
    pub base_color: Vec3,
    pub roughness: f32,
    pub metallic: f32,
    pub ior: f32,
    pub emission: Vec3,
    pub transmission: f32,
    pub transmission_tint: Vec3,
    pub translucency: f32,
    pub alpha_cutoff: f32,
    /// Identifier of the procedural surface pattern in the C# engine (0 = none).
    pub pattern: u32,
}

#[derive(Clone, Copy, Debug)]
pub struct Light {
    pub position: Vec3,
    pub intensity: Vec3,
    pub radius: f32,
}

/// A triangulated scene: indexed triangles with one material each.
#[derive(Debug)]
pub struct Scene {
    pub positions: Vec<Vec3>,
    pub indices: Vec<[u32; 3]>,
    pub triangle_material: Vec<u32>,
    pub normals: Option<Vec<Vec3>>,
    pub corner_uvs: Option<Vec<[Vec2; 3]>>,
    pub materials: Vec<Material>,
    pub lights: Vec<Light>,
    pub camera: Camera,
    pub environment: Environment,
}

impl Scene {
    pub fn triangle_count(&self) -> usize {
        self.indices.len()
    }

    pub fn load(path: impl AsRef<std::path::Path>) -> Result<Scene, SceneError> {
        Scene::parse(&std::fs::read(path)?)
    }

    pub fn parse(bytes: &[u8]) -> Result<Scene, SceneError> {
        let mut reader = Reader { bytes, at: 0 };
        if reader.take(8)? != b"GRSCENE1" {
            return Err(SceneError::Invalid("missing GRSCENE1 signature"));
        }
        if reader.u32()? != VERSION {
            return Err(SceneError::Invalid("unsupported version"));
        }
        let flags = reader.u32()?;
        let vertices = reader.count()?;
        let triangles = reader.count()?;
        let material_count = reader.count()?;
        let light_count = reader.count()?;

        let camera = Camera { position: reader.vec3()?, target: reader.vec3()?, vertical_fov_degrees: reader.f32()? };
        let environment = Environment {
            sun_direction: reader.vec3()?,
            sun_intensity: reader.f32()?,
            sky_intensity: reader.f32()?,
            exposure: reader.f32()?,
        };

        let positions = reader.vec3s(vertices)?;
        let raw_indices = reader.u32s(triangles * 3)?;
        let triangle_material = reader.u32s(triangles)?;
        let normals = if flags & HAS_NORMALS != 0 { Some(reader.vec3s(vertices)?) } else { None };
        let corner_uvs = if flags & HAS_UVS != 0 {
            let flat = reader.f32s(triangles * 6)?;
            Some(
                flat.chunks_exact(6)
                    .map(|c| [Vec2::new(c[0], c[1]), Vec2::new(c[2], c[3]), Vec2::new(c[4], c[5])])
                    .collect(),
            )
        } else {
            None
        };

        let mut materials = Vec::with_capacity(material_count);
        for _ in 0..material_count {
            materials.push(Material {
                base_color: reader.vec3()?,
                roughness: reader.f32()?,
                metallic: reader.f32()?,
                ior: reader.f32()?,
                emission: reader.vec3()?,
                transmission: reader.f32()?,
                transmission_tint: reader.vec3()?,
                translucency: reader.f32()?,
                alpha_cutoff: reader.f32()?,
                pattern: reader.u32()?,
            });
        }
        let mut lights = Vec::with_capacity(light_count);
        for _ in 0..light_count {
            lights.push(Light { position: reader.vec3()?, intensity: reader.vec3()?, radius: reader.f32()? });
        }
        if reader.at != bytes.len() {
            return Err(SceneError::Invalid("unexpected trailing bytes"));
        }

        if positions.iter().any(|p| !p.is_finite()) {
            return Err(SceneError::Invalid("non-finite vertex position"));
        }
        if raw_indices.iter().any(|&index| index as usize >= vertices) {
            return Err(SceneError::Invalid("vertex index out of range"));
        }
        if triangle_material.iter().any(|&material| material as usize >= material_count) {
            return Err(SceneError::Invalid("material index out of range"));
        }
        let indices = raw_indices.chunks_exact(3).map(|c| [c[0], c[1], c[2]]).collect();
        Ok(Scene { positions, indices, triangle_material, normals, corner_uvs, materials, lights, camera, environment })
    }
}

/// Rays written by `SceneExchange.WriteRays`: `"GRRAYS01"`, a `u32` count, then origin and direction per ray.
pub fn load_rays(path: impl AsRef<std::path::Path>) -> Result<Vec<(Vec3, Vec3)>, SceneError> {
    let bytes = std::fs::read(path)?;
    let mut reader = Reader { bytes: &bytes, at: 0 };
    if reader.take(8)? != b"GRRAYS01" {
        return Err(SceneError::Invalid("missing GRRAYS01 signature"));
    }
    let count = reader.count()?;
    let flat = reader.f32s(count * 6)?;
    Ok(flat.chunks_exact(6).map(|c| (Vec3::new(c[0], c[1], c[2]), Vec3::new(c[3], c[4], c[5]))).collect())
}

struct Reader<'a> {
    bytes: &'a [u8],
    at: usize,
}

impl<'a> Reader<'a> {
    fn take(&mut self, length: usize) -> Result<&'a [u8], SceneError> {
        let end = self.at.checked_add(length).ok_or(SceneError::Invalid("size overflow"))?;
        let slice = self.bytes.get(self.at..end).ok_or(SceneError::Invalid("file is truncated"))?;
        self.at = end;
        Ok(slice)
    }

    fn u32(&mut self) -> Result<u32, SceneError> {
        let b = self.take(4)?;
        Ok(u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
    }

    fn f32(&mut self) -> Result<f32, SceneError> {
        Ok(f32::from_bits(self.u32()?))
    }

    fn count(&mut self) -> Result<usize, SceneError> {
        let value = self.u32()? as u64;
        if value > MAX_ELEMENTS {
            return Err(SceneError::Invalid("element count is implausibly large"));
        }
        Ok(value as usize)
    }

    fn vec3(&mut self) -> Result<Vec3, SceneError> {
        Ok(Vec3::new(self.f32()?, self.f32()?, self.f32()?))
    }

    fn u32s(&mut self, count: usize) -> Result<Vec<u32>, SceneError> {
        let bytes = self.take(count.checked_mul(4).ok_or(SceneError::Invalid("size overflow"))?)?;
        Ok(bytes.chunks_exact(4).map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]])).collect())
    }

    fn f32s(&mut self, count: usize) -> Result<Vec<f32>, SceneError> {
        let bytes = self.take(count.checked_mul(4).ok_or(SceneError::Invalid("size overflow"))?)?;
        Ok(bytes.chunks_exact(4).map(|b| f32::from_le_bytes([b[0], b[1], b[2], b[3]])).collect())
    }

    fn vec3s(&mut self, count: usize) -> Result<Vec<Vec3>, SceneError> {
        let bytes = self.take(count.checked_mul(12).ok_or(SceneError::Invalid("size overflow"))?)?;
        Ok(bytes
            .chunks_exact(12)
            .map(|b| {
                Vec3::new(
                    f32::from_le_bytes([b[0], b[1], b[2], b[3]]),
                    f32::from_le_bytes([b[4], b[5], b[6], b[7]]),
                    f32::from_le_bytes([b[8], b[9], b[10], b[11]]),
                )
            })
            .collect())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_truncated_and_foreign_files() {
        assert!(matches!(Scene::parse(b"nope"), Err(SceneError::Invalid(_))));
        assert!(matches!(Scene::parse(b"GRSCENE1\x01\x00\x00\x00"), Err(SceneError::Invalid(_))));
    }

    #[test]
    fn rejects_out_of_range_indices() {
        let mut file = Vec::new();
        file.extend_from_slice(b"GRSCENE1");
        for value in [VERSION, 0, 3, 1, 1, 0] {
            file.extend_from_slice(&value.to_le_bytes());
        }
        file.extend_from_slice(&[0u8; 13 * 4]); // camera and environment
        for value in [0f32, 0., 0., 1., 0., 0., 0., 1., 0.] {
            file.extend_from_slice(&value.to_le_bytes());
        }
        for index in [0u32, 1, 7] {
            file.extend_from_slice(&index.to_le_bytes());
        }
        file.extend_from_slice(&0u32.to_le_bytes()); // triangle material
        file.extend_from_slice(&[0u8; 64]); // one material
        assert!(matches!(Scene::parse(&file), Err(SceneError::Invalid("vertex index out of range"))));
    }
}
