//! Camera that orbits a pivot: the navigation of the 3D view.

use glam::Vec3;
use gr_core::scene_file::Camera;

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct OrbitCamera {
    /// Point the camera looks at and turns around.
    pub pivot: Vec3,
    /// Heading in radians, 0 looking along +X, increasing towards +Y.
    pub yaw: f32,
    /// Elevation of the view direction in radians; negative looks down.
    pub pitch: f32,
    pub distance: f32,
    pub vertical_fov_degrees: f32,
}

const PITCH_LIMIT: f32 = 1.553; // 89 degrees
const MIN_DISTANCE: f32 = 0.05;

impl OrbitCamera {
    /// Starts from a saved camera, pivoting on the point of its view line nearest to `focus`.
    pub fn from_scene(camera: &Camera, focus: Vec3) -> OrbitCamera {
        let forward = (camera.target - camera.position).normalize_or(Vec3::Y);
        let distance = (focus - camera.position).dot(forward).max(1.0);
        OrbitCamera {
            pivot: camera.position + forward * distance,
            yaw: forward.y.atan2(forward.x),
            pitch: forward
                .z
                .clamp(-1.0, 1.0)
                .asin()
                .clamp(-PITCH_LIMIT, PITCH_LIMIT),
            distance,
            vertical_fov_degrees: camera.vertical_fov_degrees.clamp(10.0, 120.0),
        }
    }

    pub fn forward(&self) -> Vec3 {
        Vec3::new(
            self.pitch.cos() * self.yaw.cos(),
            self.pitch.cos() * self.yaw.sin(),
            self.pitch.sin(),
        )
    }

    pub fn position(&self) -> Vec3 {
        self.pivot - self.forward() * self.distance
    }

    pub fn to_scene(self) -> Camera {
        Camera {
            position: self.position(),
            target: self.pivot,
            vertical_fov_degrees: self.vertical_fov_degrees,
        }
    }

    /// Turns around the pivot. Dragging right moves the camera to the right, so the scene turns with the hand.
    pub fn orbit(&mut self, delta_x: f32, delta_y: f32) {
        self.yaw -= delta_x * 0.006;
        self.pitch = (self.pitch - delta_y * 0.006).clamp(-PITCH_LIMIT, PITCH_LIMIT);
    }

    /// Slides the pivot in the view plane; `viewport_height` makes the scene follow the pointer exactly.
    pub fn pan(&mut self, delta_x: f32, delta_y: f32, viewport_height: f32) {
        let forward = self.forward();
        let right = forward.cross(Vec3::Z).normalize_or(Vec3::X);
        let up = right.cross(forward);
        let metres_per_pixel =
            2.0 * self.distance * (self.vertical_fov_degrees.to_radians() * 0.5).tan()
                / viewport_height.max(1.0);
        self.pivot += (up * delta_y - right * delta_x) * metres_per_pixel;
    }

    /// Moves towards (positive) or away from the pivot by a proportion of the distance.
    pub fn zoom(&mut self, scroll: f32) {
        self.distance = (self.distance * (-scroll * 0.0015).exp()).max(MIN_DISTANCE);
    }

    /// Walks the pivot and the camera together: `forward`, `right` and `up` in metres.
    pub fn fly(&mut self, forward: f32, right: f32, up: f32) {
        let direction = self.forward();
        let side = direction.cross(Vec3::Z).normalize_or(Vec3::X);
        self.pivot += direction * forward + side * right + Vec3::Z * up;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_saved_camera_survives_the_round_trip() {
        let saved = Camera {
            position: Vec3::new(4.0, -9.0, 3.0),
            target: Vec3::new(1.0, 2.0, 1.0),
            vertical_fov_degrees: 55.0,
        };
        let orbit = OrbitCamera::from_scene(&saved, Vec3::new(1.0, 2.0, 1.0));
        assert!((orbit.position() - saved.position).length() < 1e-4);
        assert!((orbit.forward() - (saved.target - saved.position).normalize()).length() < 1e-5);
        assert!((orbit.pivot - saved.target).length() < 1e-3);
    }

    #[test]
    fn orbiting_keeps_the_distance_and_never_flips_over_the_top() {
        let mut orbit = OrbitCamera {
            pivot: Vec3::ZERO,
            yaw: 0.3,
            pitch: -0.2,
            distance: 12.0,
            vertical_fov_degrees: 50.0,
        };
        orbit.orbit(250.0, -9000.0);
        assert!((orbit.position().length() - 12.0).abs() < 1e-4);
        assert!(orbit.pitch <= PITCH_LIMIT && orbit.forward().cross(Vec3::Z).length() > 1e-3);
    }

    #[test]
    fn panning_moves_the_scene_by_the_pointer_distance() {
        let mut orbit = OrbitCamera {
            pivot: Vec3::ZERO,
            yaw: 0.0,
            pitch: 0.0,
            distance: 10.0,
            vertical_fov_degrees: 90.0,
        };
        // At 90 degrees and 10 m, the view is 20 m tall: 100 px of a 1000 px view are 2 m.
        orbit.pan(0.0, 100.0, 1000.0);
        assert!((orbit.pivot - Vec3::new(0.0, 0.0, 2.0)).length() < 1e-4);
    }
}
