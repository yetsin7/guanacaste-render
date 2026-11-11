//! Camera frame shared by the shader and by CPU code that must shoot the same rays.

use glam::Vec3;
use gr_core::scene_file::Camera;

/// Camera frame shared by the shader and by CPU code that must shoot the same rays.
#[derive(Clone, Copy, Debug)]
pub struct CameraFrame {
    pub position: Vec3,
    pub forward: Vec3,
    /// Scaled by `tan(fov / 2) * aspect`.
    pub right: Vec3,
    /// Scaled by `tan(fov / 2)`.
    pub up: Vec3,
}

impl CameraFrame {
    pub fn new(camera: &Camera, width: u32, height: u32) -> CameraFrame {
        let forward = (camera.target - camera.position).normalize_or(Vec3::Y);
        let side = forward.cross(Vec3::Z).normalize_or(Vec3::X);
        let half = (camera.vertical_fov_degrees.to_radians() * 0.5).tan();
        CameraFrame {
            position: camera.position,
            forward,
            right: side * half * (width as f32 / height as f32),
            up: side.cross(forward) * half,
        }
    }

    /// Direction through the centre of a pixel; the same formula as `primary_ray` in the shader.
    pub fn pixel_direction(&self, x: u32, y: u32, width: u32, height: u32) -> Vec3 {
        let px = (x as f32 + 0.5) / width as f32 * 2.0 - 1.0;
        let py = 1.0 - (y as f32 + 0.5) / height as f32 * 2.0;
        (self.forward + self.right * px + self.up * py).normalize()
    }
}
