//! The 3D view. Three ways of drawing the same camera:
//!
//! - **Navigation**: the scene rasterised, no ray tracing. It is drawn only when something changes, so a still
//!   view leaves the graphics card idle.
//! - **Preview**: a light path trace, at reduced resolution and with the denoiser, that stops after a few dozen
//!   samples. It follows the camera live, so the light can be judged while framing.
//! - **Render**: the full-quality path trace at the output resolution, until its sample target. Moving the camera
//!   cancels it and returns to navigation.

use crate::orbit_camera::OrbitCamera;
use eframe::egui::{self, PointerButton, Rect, Response, Sense};
use eframe::egui_wgpu::RenderState;
use glam::Vec3;
use gr_core::scene_file::{Material, Scene};
use gr_gpu::{CameraFrame, GpuOptions, GpuTracer, Mode, wgpu};
use std::path::Path;
use std::sync::Arc;
use std::sync::mpsc::{Receiver, channel};
use std::time::Instant;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ViewMode {
    Navigate,
    Preview,
    Render,
}

/// What the panels edit.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ViewSettings {
    pub exposure: f32,
    /// Compass direction the sunlight comes from, in degrees: 0 = +X, 90 = +Y.
    pub sun_azimuth_degrees: f32,
    pub sun_elevation_degrees: f32,
    pub sun_intensity: f32,
    pub sky_intensity: f32,
    pub max_bounces: u32,
    /// Samples per pixel of the preview; it stops there.
    pub preview_samples: u32,
    /// Fraction of the view's resolution the preview traces at.
    pub preview_scale: f32,
    /// Samples per pixel of the final render.
    pub render_samples: u32,
    /// Whether the preview and the first samples of a render are denoised.
    pub denoise: bool,
}

impl Default for ViewSettings {
    fn default() -> ViewSettings {
        ViewSettings {
            exposure: 0.4,
            sun_azimuth_degrees: 230.0,
            sun_elevation_degrees: 45.0,
            sun_intensity: 2.0,
            sky_intensity: 1.2,
            max_bounces: 3,
            preview_samples: 64,
            preview_scale: 0.6,
            render_samples: 512,
            denoise: true,
        }
    }
}

/// Facts about the open scene, for the panels.
#[derive(Clone, Debug, Default)]
pub struct SceneFacts {
    pub triangles: usize,
    pub scene_megabytes: f64,
    pub adapter: String,
    pub backend: String,
}

/// A scene being uploaded on another thread after an edit.
type Pending = Receiver<Result<(GpuTracer, Arc<Scene>), String>>;

pub struct Viewport {
    tracer: Option<GpuTracer>,
    scene: Option<Arc<Scene>>,
    pending: Option<Pending>,
    /// The path-traced picture registered with the window, and the navigation picture.
    traced_texture: Option<egui::TextureId>,
    raster_texture: Option<egui::TextureId>,
    camera: OrbitCamera,
    home: OrbitCamera,
    trace_camera_dirty: bool,
    raster_dirty: bool,
    mode: ViewMode,
    raster_supported: bool,
    pub settings: ViewSettings,
    applied: ViewSettings,
    pub facts: SceneFacts,
    materials: Vec<Material>,
    /// Output size of a final render; `None` renders at the size of the view.
    pub render_resolution: Option<(u32, u32)>,
    samples_per_second: f32,
    rate_mark: (Instant, u32),
    /// Milliseconds the last navigation frame took to submit, for the status readouts.
    pub last_draw_ms: f32,
    view_size: (u32, u32),
    /// A CPU hierarchy of the shown scene for picking surfaces, built the first time one is picked.
    picker: Option<(usize, gr_core::bvh::Bvh)>,
}

impl Viewport {
    pub fn new() -> Viewport {
        let camera = OrbitCamera {
            pivot: Vec3::ZERO,
            yaw: 0.8,
            pitch: -0.4,
            distance: 20.0,
            vertical_fov_degrees: 50.0,
        };
        Viewport {
            tracer: None,
            scene: None,
            pending: None,
            traced_texture: None,
            raster_texture: None,
            camera,
            home: camera,
            trace_camera_dirty: true,
            raster_dirty: true,
            mode: ViewMode::Navigate,
            raster_supported: true,
            settings: ViewSettings::default(),
            applied: ViewSettings::default(),
            facts: SceneFacts::default(),
            materials: Vec::new(),
            render_resolution: None,
            samples_per_second: 0.0,
            rate_mark: (Instant::now(), 0),
            last_draw_ms: 0.0,
            view_size: (0, 0),
            picker: None,
        }
    }

    pub fn has_scene(&self) -> bool {
        self.tracer.is_some()
    }

    /// True while an edited scene is being uploaded.
    pub fn is_updating(&self) -> bool {
        self.pending.is_some()
    }

    pub fn mode(&self) -> ViewMode {
        self.mode
    }

    /// Switches the drawing mode. Entering preview or render starts its image again.
    pub fn set_mode(&mut self, mode: ViewMode) {
        if mode != self.mode {
            self.mode = mode;
            self.trace_camera_dirty = true;
            self.raster_dirty = true;
            self.rate_mark = (Instant::now(), 0);
        }
    }

    pub fn samples(&self) -> u32 {
        self.tracer.as_ref().map_or(0, GpuTracer::samples)
    }

    /// Samples the current mode stops at; zero in navigation.
    pub fn target_samples(&self) -> u32 {
        match self.mode {
            ViewMode::Navigate => 0,
            ViewMode::Preview => self.settings.preview_samples,
            ViewMode::Render => self.settings.render_samples,
        }
    }

    /// Whether the current preview or render has reached its sample target.
    pub fn is_finished(&self) -> bool {
        self.mode != ViewMode::Navigate && self.samples() >= self.target_samples()
    }

    /// Size of the picture the current mode draws.
    pub fn resolution(&self) -> (u32, u32) {
        match self.mode {
            ViewMode::Navigate => self.view_size,
            _ => self
                .tracer
                .as_ref()
                .map_or((0, 0), |tracer| (tracer.width(), tracer.height())),
        }
    }

    pub fn samples_per_second(&self) -> f32 {
        self.samples_per_second
    }

    pub fn materials(&self) -> &[Material] {
        &self.materials
    }

    pub fn camera(&self) -> OrbitCamera {
        self.camera
    }

    /// Moves the camera, as the timeline does when it plays.
    pub fn set_camera(&mut self, camera: OrbitCamera) {
        if camera != self.camera {
            self.camera = camera;
            self.trace_camera_dirty = true;
            self.raster_dirty = true;
        }
    }

    pub fn field_of_view(&self) -> f32 {
        self.camera.vertical_fov_degrees
    }

    pub fn set_field_of_view(&mut self, degrees: f32) {
        self.camera.vertical_fov_degrees = degrees.clamp(10.0, 120.0);
        self.trace_camera_dirty = true;
        self.raster_dirty = true;
    }

    /// Returns to the camera the scene was saved with.
    pub fn go_home(&mut self) {
        self.set_camera(self.home);
    }

    /// Bytes of video memory the scene, the images and the navigation view hold.
    pub fn video_megabytes(&self) -> f64 {
        self.tracer.as_ref().map_or(0.0, |tracer| {
            (tracer.scene_bytes() + tracer.image_bytes()) as f64 / 1048576.0
        })
    }

    /// Uploads a scene and shows it in navigation. On failure the previous scene stays.
    pub fn set_scene(&mut self, state: &RenderState, scene: Scene) -> Result<(), String> {
        let tracer = build_tracer(state, &scene, self.settings.max_bounces)?;
        let focus = bounds_centre(&scene).unwrap_or(scene.camera.target);
        self.camera = OrbitCamera::from_scene(&scene.camera, focus);
        self.home = self.camera;
        let sun = scene.environment.sun_direction.normalize_or(Vec3::Z);
        self.settings.sun_azimuth_degrees = sun.y.atan2(sun.x).to_degrees().rem_euclid(360.0);
        self.settings.sun_elevation_degrees = sun.z.clamp(-1.0, 1.0).asin().to_degrees();
        self.settings.sun_intensity = scene.environment.sun_intensity;
        self.settings.sky_intensity = scene.environment.sky_intensity;
        self.settings.exposure = scene.environment.exposure;
        self.applied = self.settings;
        self.raster_supported = gr_gpu::raster_supported(&state.adapter);
        self.install(state, tracer, Arc::new(scene));
        self.mode = ViewMode::Navigate;
        Ok(())
    }

    fn install(&mut self, state: &RenderState, tracer: GpuTracer, scene: Arc<Scene>) {
        self.facts = SceneFacts {
            triangles: scene.triangle_count(),
            scene_megabytes: tracer.scene_bytes() as f64 / 1048576.0,
            adapter: tracer.adapter_name().to_owned(),
            backend: tracer.backend().to_owned(),
        };
        self.materials = scene.materials.clone();
        self.release_textures(state);
        self.tracer = Some(tracer);
        self.scene = Some(scene);
        self.trace_camera_dirty = true;
        self.raster_dirty = true;
        self.applied.max_bounces = self.settings.max_bounces;
        self.rate_mark = (Instant::now(), 0);
    }

    /// Replaces the scene after an edit (placed vegetation) without leaving the window waiting: the new scene is
    /// uploaded on another thread and the current one stays on screen until it is ready. The camera is kept.
    pub fn replace_scene(&mut self, state: &RenderState, scene: Scene) {
        let (sender, receiver) = channel();
        let state = state.clone();
        let bounces = self.settings.max_bounces;
        std::thread::spawn(move || {
            let result =
                build_tracer(&state, &scene, bounces).map(|tracer| (tracer, Arc::new(scene)));
            let _ = sender.send(result);
        });
        self.pending = Some(receiver);
    }

    /// Replaces one material, in both the navigation view and the path tracer.
    pub fn set_material(&mut self, index: usize, material: Material) {
        let Some(slot) = self.materials.get_mut(index) else {
            return;
        };
        *slot = material;
        if let Some(tracer) = self.tracer.as_mut() {
            tracer.set_material(index, &material);
        }
        if let Some(scene) = self.scene.as_mut() {
            Arc::make_mut(scene).materials[index] = material;
        }
        self.raster_dirty = true;
    }

    /// The point of the scene under a position of the view, or `None` over the sky. It traces one primary ray per
    /// pixel of the current path-traced image size, so it is meant for clicks, not for every frame.
    pub fn pick(&mut self, rect: Rect, position: egui::Pos2) -> Option<Vec3> {
        let tracer = self.tracer.as_mut()?;
        let (width, height) = (tracer.width(), tracer.height());
        let frame = CameraFrame::new(&self.camera.to_scene(), width, height);
        tracer.set_camera(&frame);
        tracer.reset(Mode::PrimaryDistance);
        tracer.render(1);
        let distances = tracer.read_accumulation().ok();
        tracer.reset(Mode::PathTrace);
        self.trace_camera_dirty = true;
        let x = (((position.x - rect.left()) / rect.width()) * width as f32)
            .clamp(0.0, width as f32 - 1.0) as u32;
        let y = (((position.y - rect.top()) / rect.height()) * height as f32)
            .clamp(0.0, height as f32 - 1.0) as u32;
        let distance = distances?.get((y * width + x) as usize)?[0];
        (distance > 0.0)
            .then(|| frame.position + frame.pixel_direction(x, y, width, height) * distance)
    }

    /// The material of the surface under a position of the view, or `None` over the sky.
    pub fn pick_material(&mut self, rect: Rect, position: egui::Pos2) -> Option<usize> {
        let scene = self.scene.clone()?;
        let key = Arc::as_ptr(&scene) as usize;
        if self.picker.as_ref().is_none_or(|(shown, _)| *shown != key) {
            self.picker = Some((
                key,
                gr_core::bvh::Bvh::build(&scene.positions, &scene.indices),
            ));
        }
        let (_, bvh) = self.picker.as_ref()?;
        let (width, height) = (rect.width().max(1.0) as u32, rect.height().max(1.0) as u32);
        let frame = CameraFrame::new(&self.camera.to_scene(), width, height);
        let x = ((position.x - rect.left()).max(0.0) as u32).min(width - 1);
        let y = ((position.y - rect.top()).max(0.0) as u32).min(height - 1);
        let hit = bvh.intersect(
            frame.position,
            frame.pixel_direction(x, y, width, height),
            1e-4,
            f32::INFINITY,
        )?;
        scene
            .triangle_material
            .get(hit.triangle as usize)
            .map(|&material| material as usize)
    }

    /// Draws the view into `rect` and handles the pointer and keys over it. The response lets a page react to
    /// clicks (placement, selection).
    pub fn show(&mut self, ui: &mut egui::Ui, rect: Rect, state: &RenderState) -> Response {
        let response = ui.interact(rect, ui.id().with("3d-view"), Sense::click_and_drag());
        self.receive_pending(state);
        let pixels_per_point = ui.ctx().pixels_per_point();
        let view_size = (
            ((rect.width() * pixels_per_point).round() as u32).clamp(16, 8192),
            ((rect.height() * pixels_per_point).round() as u32).clamp(16, 8192),
        );
        if view_size != self.view_size {
            self.view_size = view_size;
            self.raster_dirty = true;
            self.trace_camera_dirty = true;
        }
        if self.tracer.is_none() {
            ui.painter().rect_filled(rect, 0.0, crate::theme::CANVAS);
            return response;
        }

        let moved = self.handle_input(ui, &response, rect);
        if moved && self.mode == ViewMode::Render {
            // A render is for one framing; moving means the user is navigating again.
            self.mode = ViewMode::Navigate;
        }
        self.apply_settings();

        let mode = if self.mode == ViewMode::Navigate && !self.raster_supported {
            ViewMode::Preview
        } else {
            self.mode
        };
        match mode {
            ViewMode::Navigate => self.draw_navigation(ui, rect, state),
            ViewMode::Preview | ViewMode::Render => self.draw_traced(ui, rect, state, mode),
        }
        if ui.input(|input| fly_keys(input) != [0.0; 3]) && response.hovered() {
            ui.ctx().request_repaint();
        }
        response
    }

    /// Turns pointer and keys into camera moves; returns whether the camera moved.
    fn handle_input(&mut self, ui: &egui::Ui, response: &Response, rect: Rect) -> bool {
        let before = self.camera;
        if response.dragged_by(PointerButton::Primary) {
            self.camera
                .orbit(response.drag_delta().x, response.drag_delta().y);
        }
        if response.dragged_by(PointerButton::Secondary)
            || response.dragged_by(PointerButton::Middle)
        {
            self.camera.pan(
                response.drag_delta().x,
                response.drag_delta().y,
                rect.height(),
            );
        }
        if response.hovered() {
            let (scroll, dt, keys, sprint) = ui.input(|input| {
                (
                    input.smooth_scroll_delta.y,
                    input.stable_dt.min(0.1),
                    fly_keys(input),
                    input.modifiers.shift,
                )
            });
            let step = self.camera.distance.max(1.0) * 0.6 * dt * if sprint { 4.0 } else { 1.0 };
            self.camera.zoom(scroll);
            self.camera
                .fly(keys[0] * step, keys[1] * step, keys[2] * step);
        }
        let moved = self.camera != before;
        if moved {
            self.trace_camera_dirty = true;
            self.raster_dirty = true;
        }
        moved
    }

    /// Passes changed light and bounces to the renderer.
    fn apply_settings(&mut self) {
        let Some(tracer) = self.tracer.as_mut() else {
            return;
        };
        if self.settings.max_bounces != self.applied.max_bounces {
            tracer.set_max_bounces(self.settings.max_bounces);
        }
        let s = &self.settings;
        let a = &self.applied;
        if s.sun_azimuth_degrees != a.sun_azimuth_degrees
            || s.sun_elevation_degrees != a.sun_elevation_degrees
            || s.sun_intensity != a.sun_intensity
            || s.sky_intensity != a.sky_intensity
        {
            let (azimuth, elevation) = (
                s.sun_azimuth_degrees.to_radians(),
                s.sun_elevation_degrees.to_radians(),
            );
            let sun = Vec3::new(
                elevation.cos() * azimuth.cos(),
                elevation.cos() * azimuth.sin(),
                elevation.sin(),
            );
            tracer.set_daylight(sun, s.sun_intensity, s.sky_intensity);
            self.raster_dirty = true;
        }
        if s.exposure != a.exposure {
            self.raster_dirty = true;
        }
        if s.preview_scale != a.preview_scale
            || s.render_samples != a.render_samples
            || s.preview_samples != a.preview_samples
        {
            self.trace_camera_dirty = true;
        }
        self.applied = self.settings;
    }

    fn draw_navigation(&mut self, ui: &mut egui::Ui, rect: Rect, state: &RenderState) {
        let Some(tracer) = self.tracer.as_mut() else {
            return;
        };
        let (width, height) = self.view_size;
        if self.raster_dirty || self.raster_texture.is_none() {
            let started = Instant::now();
            let frame = CameraFrame::new(&self.camera.to_scene(), width, height);
            let drawn = tracer.rasterize(&frame, width, height, self.settings.exposure);
            if drawn.replaced || self.raster_texture.is_none() {
                let mut renderer = state.renderer.write();
                if let Some(old) = self.raster_texture.take() {
                    renderer.free_texture(&old);
                }
                self.raster_texture = Some(renderer.register_native_texture(
                    &state.device,
                    drawn.view,
                    wgpu::FilterMode::Linear,
                ));
            }
            self.last_draw_ms = started.elapsed().as_secs_f32() * 1000.0;
            self.raster_dirty = false;
        }
        if let Some(texture) = self.raster_texture {
            ui.painter()
                .image(texture, rect, whole(), egui::Color32::WHITE);
        }
        self.samples_per_second = 0.0;
    }

    fn draw_traced(&mut self, ui: &mut egui::Ui, rect: Rect, state: &RenderState, mode: ViewMode) {
        let Some(tracer) = self.tracer.as_mut() else {
            return;
        };
        let (view_width, view_height) = self.view_size;
        let (width, height) = match mode {
            ViewMode::Render => self.render_resolution.unwrap_or((view_width, view_height)),
            _ => {
                let scale = self.settings.preview_scale.clamp(0.25, 1.0);
                (
                    ((view_width as f32 * scale) as u32).max(16),
                    ((view_height as f32 * scale) as u32).max(16),
                )
            }
        };
        let target = if mode == ViewMode::Render {
            self.settings.render_samples
        } else {
            self.settings.preview_samples
        };
        if (width, height) != (tracer.width(), tracer.height()) {
            tracer.resize(width, height);
            if let Some(old) = self.traced_texture.take() {
                state.renderer.write().free_texture(&old);
            }
            self.trace_camera_dirty = true;
        }
        if self.trace_camera_dirty {
            tracer.reset(Mode::PathTrace);
            tracer.set_camera(&CameraFrame::new(&self.camera.to_scene(), width, height));
            self.trace_camera_dirty = false;
        }
        let refining = tracer.samples() < target;
        if refining && tracer.pending_passes() < 2 {
            tracer.render(
                if mode == ViewMode::Render { 2 } else { 1 }.min(target - tracer.samples()),
            );
        }
        // Denoise the preview always, a render only while it is still grainy.
        tracer.set_denoise(
            self.settings.denoise && (mode == ViewMode::Preview || tracer.samples() < 48),
        );
        tracer.resolve(self.settings.exposure);
        let texture = *self.traced_texture.get_or_insert_with(|| {
            state.renderer.write().register_native_texture(
                &state.device,
                tracer.display_view(),
                wgpu::FilterMode::Linear,
            )
        });
        // A render at its own resolution is fitted into the view without stretching.
        let shown = if mode == ViewMode::Render {
            let aspect = width as f32 / height as f32;
            let fitted = if rect.width() / rect.height() > aspect {
                egui::vec2(rect.height() * aspect, rect.height())
            } else {
                egui::vec2(rect.width(), rect.width() / aspect)
            };
            ui.painter().rect_filled(rect, 0.0, crate::theme::CANVAS);
            Rect::from_center_size(rect.center(), fitted)
        } else {
            rect
        };
        ui.painter()
            .image(texture, shown, whole(), egui::Color32::WHITE);

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

    fn receive_pending(&mut self, state: &RenderState) {
        let Some(receiver) = &self.pending else {
            return;
        };
        let Ok(result) = receiver.try_recv() else {
            return;
        };
        self.pending = None;
        if let Ok((tracer, scene)) = result {
            self.install(state, tracer, scene);
        }
    }

    /// Saves the picture as shown: the render or preview when there is one, otherwise the navigation view.
    pub fn save_picture(&self, path: &Path) -> Result<(), String> {
        let tracer = self
            .tracer
            .as_ref()
            .ok_or("No hay ninguna escena abierta.")?;
        let (pixels, width, height) = if self.mode == ViewMode::Navigate {
            let pixels = tracer
                .read_raster()
                .map_err(|e| e.to_string())?
                .ok_or("La vista aún no se ha dibujado.")?;
            (pixels, self.view_size.0, self.view_size.1)
        } else {
            (
                tracer.read_display().map_err(|e| e.to_string())?,
                tracer.width(),
                tracer.height(),
            )
        };
        save_png(path, &pixels, width, height)
    }

    /// Restarts the path-traced image, for example after the quality profile changed.
    pub fn restart(&mut self) {
        self.trace_camera_dirty = true;
    }

    /// Renders one finished frame of `camera` at `width` x `height` with `samples` samples, blocking until it is
    /// done, and returns its RGBA bytes. Used by the movie export.
    pub fn render_frame(
        &mut self,
        camera: OrbitCamera,
        width: u32,
        height: u32,
        samples: u32,
    ) -> Result<Vec<u8>, String> {
        let tracer = self
            .tracer
            .as_mut()
            .ok_or("No hay ninguna escena abierta.")?;
        if (width, height) != (tracer.width(), tracer.height()) {
            tracer.resize(width, height);
        }
        tracer.reset(Mode::PathTrace);
        tracer.set_camera(&CameraFrame::new(&camera.to_scene(), width, height));
        let mut done = 0;
        while done < samples {
            let batch = (samples - done).min(16);
            tracer.render(batch);
            tracer.wait().map_err(|e| e.to_string())?;
            done += batch;
        }
        tracer.set_denoise(self.settings.denoise && samples < 48);
        tracer.resolve(self.settings.exposure);
        let pixels = tracer.read_display().map_err(|e| e.to_string())?;
        self.trace_camera_dirty = true;
        Ok(pixels)
    }

    fn release_textures(&mut self, state: &RenderState) {
        let mut renderer = state.renderer.write();
        for texture in [self.traced_texture.take(), self.raster_texture.take()]
            .into_iter()
            .flatten()
        {
            renderer.free_texture(&texture);
        }
    }
}

/// Uploads a scene to the window's graphics device.
fn build_tracer(state: &RenderState, scene: &Scene, max_bounces: u32) -> Result<GpuTracer, String> {
    let adapter = state.adapter.get_info();
    let options = GpuOptions {
        max_bounces,
        ..Default::default()
    };
    GpuTracer::with_device(state.device.clone(), state.queue.clone(), &adapter, scene, 960, 540, &options).map_err(|error| match error {
        gr_gpu::GpuError::TooLarge { needed, limit } => format!(
            "La escena no cabe en la tarjeta gráfica: necesita un bloque de {} MB y el máximo es {} MB.",
            needed >> 20,
            limit >> 20
        ),
        other => format!("La tarjeta gráfica no pudo cargar la escena: {other}"),
    })
}

/// Forward, right and up from W/S, D/A and E/Q (Space also rises).
fn fly_keys(input: &egui::InputState) -> [f32; 3] {
    use egui::Key;
    let axis = |positive: bool, negative: bool| (positive as i32 - negative as i32) as f32;
    [
        axis(input.key_down(Key::W), input.key_down(Key::S)),
        axis(input.key_down(Key::D), input.key_down(Key::A)),
        axis(
            input.key_down(Key::E) || input.key_down(Key::Space),
            input.key_down(Key::Q),
        ),
    ]
}

fn whole() -> Rect {
    Rect::from_min_max(egui::pos2(0.0, 0.0), egui::pos2(1.0, 1.0))
}

/// Writes RGBA bytes as an 8-bit sRGB PNG.
pub fn save_png(path: &Path, pixels: &[u8], width: u32, height: u32) -> Result<(), String> {
    let file = std::fs::File::create(path)
        .map_err(|error| format!("No se pudo crear el archivo: {error}"))?;
    let mut encoder = png::Encoder::new(std::io::BufWriter::new(file), width, height);
    encoder.set_color(png::ColorType::Rgba);
    encoder.set_depth(png::BitDepth::Eight);
    encoder.set_source_srgb(png::SrgbRenderingIntent::Perceptual);
    encoder
        .write_header()
        .and_then(|mut writer| writer.write_image_data(pixels))
        .map_err(|error| format!("No se pudo escribir la imagen: {error}"))
}

/// Centre of the building, ignoring the far corners of an endless ground plane: the median vertex.
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
