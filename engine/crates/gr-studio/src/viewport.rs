//! The 3D view: owns the GPU path tracer, turns pointer and keyboard input into camera moves, paces the sampling
//! and shows the picture without it ever leaving the graphics card.

use crate::orbit_camera::OrbitCamera;
use eframe::egui::{self, PointerButton, Sense};
use eframe::egui_wgpu::RenderState;
use glam::Vec3;
use gr_core::hardware::TierBudget;
use gr_core::scene_file::Scene;
use gr_gpu::{CameraFrame, GpuOptions, GpuTracer, Mode, wgpu};
use std::path::Path;
use std::time::Instant;

/// What the side panel edits. Changing daylight or bounces restarts the image; exposure does not.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ViewSettings {
    pub exposure: f32,
    /// Compass direction the sunlight comes from, in degrees: 0 = +X, 90 = +Y.
    pub sun_azimuth_degrees: f32,
    pub sun_elevation_degrees: f32,
    pub sun_intensity: f32,
    pub sky_intensity: f32,
    pub max_bounces: u32,
    /// The image stops refining, and the application stops using the GPU, at this many samples per pixel.
    pub target_samples: u32,
}

/// Facts about the open scene, for the panels.
#[derive(Clone, Debug, Default)]
pub struct SceneFacts {
    pub triangles: usize,
    pub materials: usize,
    pub scene_megabytes: f64,
    pub adapter: String,
}

pub struct Viewport {
    tracer: Option<GpuTracer>,
    texture: Option<egui::TextureId>,
    camera: OrbitCamera,
    home: OrbitCamera,
    pub settings: ViewSettings,
    applied: ViewSettings,
    pub facts: SceneFacts,
    samples_per_second: f32,
    rate_mark: (Instant, u32),
}

impl Viewport {
    pub fn new() -> Viewport {
        let camera = OrbitCamera { pivot: Vec3::ZERO, yaw: 0.8, pitch: -0.4, distance: 20.0, vertical_fov_degrees: 50.0 };
        let settings = ViewSettings {
            exposure: 0.4,
            sun_azimuth_degrees: 230.0,
            sun_elevation_degrees: 45.0,
            sun_intensity: 2.0,
            sky_intensity: 1.2,
            max_bounces: 3,
            target_samples: 512,
        };
        Viewport {
            tracer: None,
            texture: None,
            camera,
            home: camera,
            settings,
            applied: settings,
            facts: SceneFacts::default(),
            samples_per_second: 0.0,
            rate_mark: (Instant::now(), 0),
        }
    }

    pub fn has_scene(&self) -> bool {
        self.tracer.is_some()
    }

    pub fn samples(&self) -> u32 {
        self.tracer.as_ref().map_or(0, GpuTracer::samples)
    }

    pub fn resolution(&self) -> (u32, u32) {
        self.tracer.as_ref().map_or((0, 0), |tracer| (tracer.width(), tracer.height()))
    }

    pub fn samples_per_second(&self) -> f32 {
        self.samples_per_second
    }

    pub fn image_megabytes(&self) -> f64 {
        self.tracer.as_ref().map_or(0.0, |tracer| tracer.image_bytes() as f64 / 1048576.0)
    }

    pub fn field_of_view(&mut self) -> &mut f32 {
        &mut self.camera.vertical_fov_degrees
    }

    /// Returns to the camera the scene was saved with.
    pub fn go_home(&mut self) {
        self.camera = self.home;
    }

    /// Uploads a scene to the graphics card and starts rendering it. On failure the previous scene stays.
    pub fn set_scene(&mut self, state: &RenderState, scene: &Scene, max_bounces: u32) -> Result<(), String> {
        let (width, height) = match self.resolution() {
            (0, _) | (_, 0) => (960, 540),
            size => size,
        };
        let adapter = state.adapter.get_info();
        let options = GpuOptions { max_bounces, ..Default::default() };
        let tracer = GpuTracer::with_device(state.device.clone(), state.queue.clone(), &adapter, scene, width, height, &options)
            .map_err(|error| match error {
                gr_gpu::GpuError::TooLarge { needed, limit } => format!(
                    "La escena no cabe en esta tarjeta gráfica: necesita un bloque de {} MB y el máximo es {} MB.",
                    needed >> 20,
                    limit >> 20
                ),
                other => format!("La tarjeta gráfica no pudo cargar la escena: {other}"),
            })?;

        let focus = bounds_centre(scene).unwrap_or(scene.camera.target);
        self.camera = OrbitCamera::from_scene(&scene.camera, focus);
        self.home = self.camera;
        let sun = scene.environment.sun_direction.normalize_or(Vec3::Z);
        self.settings.sun_azimuth_degrees = sun.y.atan2(sun.x).to_degrees().rem_euclid(360.0);
        self.settings.sun_elevation_degrees = sun.z.clamp(-1.0, 1.0).asin().to_degrees().clamp(1.0, 90.0);
        self.settings.sun_intensity = scene.environment.sun_intensity;
        self.settings.sky_intensity = scene.environment.sky_intensity;
        self.settings.exposure = scene.environment.exposure;
        self.settings.max_bounces = max_bounces;
        self.applied = self.settings;
        self.facts = SceneFacts {
            triangles: scene.triangle_count(),
            materials: scene.materials.len(),
            scene_megabytes: tracer.scene_bytes() as f64 / 1048576.0,
            adapter: format!("{} · {}", tracer.adapter_name(), tracer.backend()),
        };
        self.release_texture(state);
        self.tracer = Some(tracer);
        self.rate_mark = (Instant::now(), 0);
        Ok(())
    }

    /// Draws the view into the remaining space of `ui` and advances the render.
    pub fn show(&mut self, ui: &mut egui::Ui, state: &RenderState, budget: &TierBudget) {
        let (rect, response) = ui.allocate_exact_size(ui.available_size(), Sense::click_and_drag());
        let Some(tracer) = self.tracer.as_mut() else { return };

        // Input first, so the frame drawn below already shows the move.
        let before = self.camera;
        if response.dragged_by(PointerButton::Primary) {
            self.camera.orbit(response.drag_delta().x, response.drag_delta().y);
        }
        if response.dragged_by(PointerButton::Secondary) || response.dragged_by(PointerButton::Middle) {
            self.camera.pan(response.drag_delta().x, response.drag_delta().y, rect.height());
        }
        if response.hovered() {
            let (scroll, step, keys) = ui.input(|input| {
                let step = self.camera.distance.max(1.0) * 0.6 * input.stable_dt.min(0.1);
                let axis = |positive, negative| {
                    (input.key_down(positive) as i32 - input.key_down(negative) as i32) as f32
                };
                use egui::Key;
                (input.smooth_scroll_delta.y, step, [axis(Key::W, Key::S), axis(Key::D, Key::A), axis(Key::E, Key::Q)])
            });
            self.camera.zoom(scroll);
            self.camera.fly(keys[0] * step, keys[1] * step, keys[2] * step);
        }

        let pixels = ui.ctx().pixels_per_point() * budget.viewport_scale;
        let width = ((rect.width() * pixels).round() as u32).clamp(16, 4096);
        let height = ((rect.height() * pixels).round() as u32).clamp(16, 4096);
        let resized = (width, height) != (tracer.width(), tracer.height());
        if resized {
            tracer.resize(width, height);
            if let Some(texture) = self.texture.take() {
                state.renderer.write().free_texture(&texture);
            }
        }
        if resized || self.camera != before {
            tracer.set_camera(&CameraFrame::new(&self.camera.to_scene(), width, height));
        }
        if self.settings.max_bounces != self.applied.max_bounces {
            tracer.set_max_bounces(self.settings.max_bounces);
        }
        if daylight_changed(&self.settings, &self.applied) {
            let (azimuth, elevation) =
                (self.settings.sun_azimuth_degrees.to_radians(), self.settings.sun_elevation_degrees.to_radians());
            let sun = Vec3::new(elevation.cos() * azimuth.cos(), elevation.cos() * azimuth.sin(), elevation.sin());
            tracer.set_daylight(sun, self.settings.sun_intensity, self.settings.sky_intensity);
        }
        self.applied = self.settings;

        // Only queue more samples when the card has finished the previous ones; otherwise camera moves would lag
        // behind a growing queue.
        let refining = tracer.samples() < self.settings.target_samples;
        if refining && tracer.pending_passes() < 2 {
            let passes = budget.samples_while_moving.min(self.settings.target_samples - tracer.samples());
            tracer.render(passes);
        }
        tracer.resolve(self.settings.exposure);

        let texture = *self.texture.get_or_insert_with(|| {
            state.renderer.write().register_native_texture(&state.device, tracer.display_view(), wgpu::FilterMode::Linear)
        });
        let whole = egui::Rect::from_min_max(egui::pos2(0.0, 0.0), egui::pos2(1.0, 1.0));
        ui.painter().image(texture, rect, whole, egui::Color32::WHITE);

        let (since, samples_then) = self.rate_mark;
        let elapsed = since.elapsed().as_secs_f32();
        if tracer.samples() < samples_then {
            self.rate_mark = (Instant::now(), tracer.samples());
        } else if elapsed >= 0.5 {
            self.samples_per_second = (tracer.samples() - samples_then) as f32 / elapsed;
            self.rate_mark = (Instant::now(), tracer.samples());
        }
        if refining || tracer.pending_passes() > 0 {
            ui.ctx().request_repaint();
        } else {
            self.samples_per_second = 0.0;
        }
    }

    /// Saves the picture as shown, as an 8-bit sRGB PNG.
    pub fn save_picture(&self, path: &Path) -> Result<(), String> {
        let tracer = self.tracer.as_ref().ok_or("No hay ninguna escena abierta.")?;
        let pixels = tracer.read_display().map_err(|error| format!("No se pudo leer la imagen: {error}"))?;
        let file = std::fs::File::create(path).map_err(|error| format!("No se pudo crear el archivo: {error}"))?;
        let mut encoder = png::Encoder::new(std::io::BufWriter::new(file), tracer.width(), tracer.height());
        encoder.set_color(png::ColorType::Rgba);
        encoder.set_depth(png::BitDepth::Eight);
        encoder.set_source_srgb(png::SrgbRenderingIntent::Perceptual);
        encoder
            .write_header()
            .and_then(|mut writer| writer.write_image_data(&pixels))
            .map_err(|error| format!("No se pudo escribir la imagen: {error}"))
    }

    /// Restarts the image, for example after the quality tier changed the number of bounces.
    pub fn restart(&mut self) {
        if let Some(tracer) = self.tracer.as_mut() {
            tracer.reset(Mode::PathTrace);
        }
    }

    fn release_texture(&mut self, state: &RenderState) {
        if let Some(texture) = self.texture.take() {
            state.renderer.write().free_texture(&texture);
        }
    }
}

fn daylight_changed(now: &ViewSettings, before: &ViewSettings) -> bool {
    now.sun_azimuth_degrees != before.sun_azimuth_degrees
        || now.sun_elevation_degrees != before.sun_elevation_degrees
        || now.sun_intensity != before.sun_intensity
        || now.sky_intensity != before.sky_intensity
}

/// Centre of the building, ignoring the far corners of an endless ground plane: the median vertex is used instead
/// of the middle of the bounding box.
fn bounds_centre(scene: &Scene) -> Option<Vec3> {
    if scene.positions.is_empty() {
        return None;
    }
    let step = (scene.positions.len() / 4096).max(1);
    let mut sample: Vec<Vec3> = scene.positions.iter().step_by(step).copied().collect();
    let middle = sample.len() / 2;
    let mut median = |axis: usize| {
        sample.select_nth_unstable_by(middle, |a, b| a[axis].total_cmp(&b[axis]));
        sample[middle][axis]
    };
    Some(Vec3::new(median(0), median(1), median(2)))
}
