//! The movie timeline: camera and sun keyframes, smooth playback through them and export as a numbered PNG
//! sequence rendered by the path tracer.

use crate::orbit_camera::OrbitCamera;
use std::path::PathBuf;
use std::time::Instant;

/// The camera and the sun at one moment of the clip.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Keyframe {
    /// Seconds from the start of the clip.
    pub time: f32,
    pub camera: OrbitCamera,
    pub sun_azimuth: f32,
    pub sun_elevation: f32,
}

/// What the timeline shows at one moment: interpolated between the keyframes around it.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Pose {
    pub camera: OrbitCamera,
    pub sun_azimuth: f32,
    pub sun_elevation: f32,
}

/// An export in progress: one frame is rendered per interface frame, so the window stays responsive.
pub struct Export {
    pub folder: PathBuf,
    pub frame: u32,
    pub total: u32,
    pub width: u32,
    pub height: u32,
    pub samples: u32,
    pub started: Instant,
}

pub struct Timeline {
    pub keys: Vec<Keyframe>,
    pub duration: f32,
    pub playhead: f32,
    pub playing: bool,
    pub looping: bool,
    pub selected: Option<usize>,
    pub fps: u32,
    pub samples_per_frame: u32,
    pub export: Option<Export>,
    last_tick: Option<Instant>,
}

impl Default for Timeline {
    fn default() -> Timeline {
        Timeline {
            keys: Vec::new(),
            duration: 14.0,
            playhead: 0.0,
            playing: false,
            looping: true,
            selected: None,
            fps: 30,
            samples_per_frame: 64,
            export: None,
            last_tick: None,
        }
    }
}

impl Timeline {
    /// Records the current camera and sun at the playhead, replacing a keyframe already there.
    pub fn add_key(&mut self, pose: Pose) {
        let time = self.playhead;
        let key = Keyframe {
            time,
            camera: pose.camera,
            sun_azimuth: pose.sun_azimuth,
            sun_elevation: pose.sun_elevation,
        };
        match self
            .keys
            .iter()
            .position(|existing| (existing.time - time).abs() < 0.05)
        {
            Some(index) => {
                self.keys[index] = key;
                self.selected = Some(index);
            }
            None => {
                self.keys.push(key);
                self.keys.sort_by(|a, b| a.time.total_cmp(&b.time));
                self.selected = self.keys.iter().position(|existing| existing.time == time);
            }
        }
    }

    /// Overwrites the selected keyframe with the current camera and sun, keeping its time.
    pub fn update_selected(&mut self, pose: Pose) {
        if let Some(key) = self.selected.and_then(|index| self.keys.get_mut(index)) {
            key.camera = pose.camera;
            key.sun_azimuth = pose.sun_azimuth;
            key.sun_elevation = pose.sun_elevation;
        }
    }

    /// Copies the selected keyframe one second later.
    pub fn duplicate_selected(&mut self) {
        let Some(key) = self
            .selected
            .and_then(|index| self.keys.get(index))
            .copied()
        else {
            return;
        };
        self.playhead = (key.time + 1.0).min(self.duration);
        self.add_key(Pose {
            camera: key.camera,
            sun_azimuth: key.sun_azimuth,
            sun_elevation: key.sun_elevation,
        });
    }

    pub fn delete_selected(&mut self) {
        if let Some(index) = self
            .selected
            .take()
            .filter(|&index| index < self.keys.len())
        {
            self.keys.remove(index);
        }
    }

    /// Advances the playhead while playing; returns true while it is playing.
    pub fn tick(&mut self) -> bool {
        let now = Instant::now();
        let elapsed = self
            .last_tick
            .map_or(0.0, |last| now.duration_since(last).as_secs_f32().min(0.1));
        self.last_tick = Some(now);
        if !self.playing {
            return false;
        }
        self.playhead += elapsed;
        if self.playhead >= self.duration {
            if self.looping {
                self.playhead %= self.duration.max(0.01);
            } else {
                self.playhead = self.duration;
                self.playing = false;
            }
        }
        self.playing
    }

    /// The pose at `time`: a smooth curve through the keyframes (Catmull-Rom), held before the first and after the
    /// last. `None` without keyframes.
    pub fn pose_at(&self, time: f32) -> Option<Pose> {
        let keys = &self.keys;
        let first = keys.first()?;
        let pose = |key: &Keyframe| Pose {
            camera: key.camera,
            sun_azimuth: key.sun_azimuth,
            sun_elevation: key.sun_elevation,
        };
        if keys.len() == 1 || time <= first.time {
            return Some(pose(first));
        }
        let last = keys.last()?;
        if time >= last.time {
            return Some(pose(last));
        }
        let next = keys.iter().position(|key| key.time > time)?;
        let (i1, i2) = (next - 1, next);
        let (i0, i3) = (i1.saturating_sub(1), (i2 + 1).min(keys.len() - 1));
        let t = (time - keys[i1].time) / (keys[i2].time - keys[i1].time).max(1e-4);
        let blend = |f: &dyn Fn(&Keyframe) -> f32| {
            catmull_rom(f(&keys[i0]), f(&keys[i1]), f(&keys[i2]), f(&keys[i3]), t)
        };
        // Headings are unwrapped against the segment start, so a turn through north does not spin the long way.
        let yaw_near = |key: &Keyframe| unwrap(key.camera.yaw, keys[i1].camera.yaw);
        let azimuth_near = |key: &Keyframe| {
            unwrap(
                key.sun_azimuth.to_radians(),
                keys[i1].sun_azimuth.to_radians(),
            )
        };
        let pivot = |axis: usize| blend(&|key: &Keyframe| key.camera.pivot[axis]);
        Some(Pose {
            camera: OrbitCamera {
                pivot: glam::Vec3::new(pivot(0), pivot(1), pivot(2)),
                yaw: blend(&yaw_near),
                pitch: blend(&|key: &Keyframe| key.camera.pitch),
                distance: blend(&|key: &Keyframe| key.camera.distance).max(0.05),
                vertical_fov_degrees: blend(&|key: &Keyframe| key.camera.vertical_fov_degrees),
            },
            sun_azimuth: blend(&azimuth_near).to_degrees().rem_euclid(360.0),
            sun_elevation: blend(&|key: &Keyframe| key.sun_elevation),
        })
    }

    /// Frames in the clip at its frame rate.
    pub fn frame_count(&self) -> u32 {
        (self.duration * self.fps as f32).round().max(1.0) as u32
    }
}

fn catmull_rom(p0: f32, p1: f32, p2: f32, p3: f32, t: f32) -> f32 {
    let (t2, t3) = (t * t, t * t * t);
    0.5 * (2.0 * p1
        + (p2 - p0) * t
        + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
        + (3.0 * p1 - p0 - 3.0 * p2 + p3) * t3)
}

/// The angle equal to `angle` (radians) that lies within half a turn of `reference`.
fn unwrap(angle: f32, reference: f32) -> f32 {
    let tau = std::f32::consts::TAU;
    angle - ((angle - reference + std::f32::consts::PI).div_euclid(tau)) * tau
}

/// Seconds as `MM:SS`, the clock of the timeline.
pub fn clock(seconds: f32) -> String {
    let whole = seconds.max(0.0) as u32;
    format!("{:02}:{:02}", whole / 60, whole % 60)
}

#[cfg(test)]
mod tests {
    use super::*;
    use glam::Vec3;

    fn camera(x: f32, yaw: f32) -> OrbitCamera {
        OrbitCamera {
            pivot: Vec3::new(x, 0.0, 0.0),
            yaw,
            pitch: -0.3,
            distance: 10.0,
            vertical_fov_degrees: 50.0,
        }
    }

    fn pose(x: f32, yaw: f32) -> Pose {
        Pose {
            camera: camera(x, yaw),
            sun_azimuth: 200.0,
            sun_elevation: 30.0,
        }
    }

    #[test]
    fn playback_passes_through_every_keyframe() {
        let mut timeline = Timeline::default();
        for (time, x) in [(0.0, 0.0), (4.0, 10.0), (8.0, 4.0)] {
            timeline.playhead = time;
            timeline.add_key(pose(x, 0.0));
        }
        for key in timeline.keys.clone() {
            let at = timeline.pose_at(key.time).expect("keyframes exist");
            assert!((at.camera.pivot.x - key.camera.pivot.x).abs() < 1e-4);
        }
        let middle = timeline
            .pose_at(2.0)
            .expect("keyframes exist")
            .camera
            .pivot
            .x;
        assert!(middle > 0.0 && middle < 10.0);
        assert_eq!(timeline.pose_at(20.0).expect("held").camera.pivot.x, 4.0);
    }

    #[test]
    fn a_turn_through_north_takes_the_short_way() {
        let mut timeline = Timeline::default();
        timeline.add_key(pose(0.0, 3.0));
        timeline.playhead = 2.0;
        timeline.add_key(pose(0.0, -3.0));
        let yaw = timeline.pose_at(1.0).expect("keyframes exist").camera.yaw;
        // From 3.0 rad to -3.0 rad the short way passes pi, not zero.
        assert!(yaw.abs() > 3.0, "yaw {yaw}");
    }

    #[test]
    fn a_keyframe_at_the_same_time_replaces_the_old_one() {
        let mut timeline = Timeline::default();
        timeline.add_key(pose(0.0, 0.0));
        timeline.add_key(pose(5.0, 0.0));
        assert_eq!(timeline.keys.len(), 1);
        assert_eq!(timeline.keys[0].camera.pivot.x, 5.0);
        assert_eq!(clock(74.6), "01:14");
    }
}
