//! The studio window: its state, the title bar and tool rail around every page, and the work that crosses pages
//! (opening projects, placing vegetation, quality profiles).

use crate::project::{self, Loaded};
use crate::recent_projects::{self, RecentProject};
use crate::studio::preferences::Preferences;
use crate::studio::telemetry::Telemetry;
use crate::studio::thumbnails::Thumbnails;
use crate::studio::timeline::{Pose, Timeline};
use crate::studio::vegetation::{self, CATALOG, Library, Taxon};
use crate::theme;
use crate::ui::{pages, shell};
use crate::viewport::{ViewMode, Viewport};
use eframe::egui;
use eframe::egui_wgpu::RenderState;
use glam::Vec3;
use gr_core::hardware::{AdapterKind, Capabilities, QualityTier, suggest_tier};
use gr_core::scene_file::{Material, Scene};
use gr_gpu::wgpu;
use std::path::PathBuf;
use std::sync::Arc;
use std::sync::mpsc::Receiver;
use std::time::Instant;

/// The pages of the studio, one per screen of the prototype.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Page {
    Projects,
    Build,
    Library,
    Inspector,
    Materials,
    Movie,
    Render,
    Settings,
    Help,
}

/// What the Build tools do on a click in the 3D view.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Tool {
    Place,
    Select,
    Delete,
}

/// A plant placed in this session.
#[derive(Clone, Copy, Debug)]
pub struct Placed {
    pub species: usize,
    pub at: Vec3,
    pub height: f64,
    pub spread: f64,
    pub instance: u128,
}

pub struct BuildState {
    pub species: usize,
    pub tool: Tool,
    pub placed: Vec<Placed>,
    pub selected: Option<usize>,
    /// The last point picked in the 3D view.
    pub cursor: Option<Vec3>,
    pub height: f32,
    pub spread: f32,
    /// 0 off, 1 on (±10 %), 2 random (±25 %).
    pub variation: usize,
    next_instance: u128,
}

pub struct MaterialState {
    pub selected: usize,
    pub names: Vec<String>,
    /// The materials as the project had them, for "Restablecer".
    pub original: Vec<Material>,
}

pub struct LibraryState {
    pub filter: Option<Taxon>,
    pub search: String,
    /// The species the inspector shows.
    pub inspected: usize,
}

pub struct Studio {
    pub page: Page,
    pub viewport: Viewport,
    pub library: Library,
    pub timeline: Timeline,
    pub telemetry: Telemetry,
    pub preferences: Preferences,
    pub thumbnails: Thumbnails,
    pub tier: QualityTier,
    pub suggested: QualityTier,
    pub project: Option<PathBuf>,
    pub recent: Vec<RecentProject>,
    pub loading: Option<(Receiver<Loaded>, String)>,
    pub message: Option<(String, bool)>,
    /// What the open project's view leaves out, when it is partial.
    pub notice: Option<String>,
    pub build: BuildState,
    pub materials: MaterialState,
    pub library_state: LibraryState,
    pub help_step: usize,
    pub logo: Option<egui::TextureHandle>,
    /// Interface-only state of the pages.
    pub local: pages::Local,
    /// The settings as last saved, to save again only when they change.
    saved_settings: Option<crate::viewport::ViewSettings>,
    /// The scene as opened, before any vegetation was placed; edits are rebuilt from it.
    base_scene: Option<Arc<Scene>>,
    capture_thumbnail: bool,
    opened_at: Option<std::time::SystemTime>,
    last_frame: Instant,
}

impl Studio {
    pub fn new(creation: &eframe::CreationContext<'_>, open: Option<PathBuf>) -> Studio {
        theme::apply(&creation.egui_ctx);
        let adapter = creation
            .wgpu_render_state
            .as_ref()
            .map(|state| state.adapter.get_info().device_type);
        let suggested = suggest_tier(&Capabilities {
            adapter: match adapter {
                Some(wgpu::DeviceType::DiscreteGpu) => AdapterKind::Discrete,
                Some(wgpu::DeviceType::IntegratedGpu) => AdapterKind::Integrated,
                _ => AdapterKind::Cpu,
            },
            video_memory_bytes: None,
            system_memory_bytes: None,
            measured_mega_rays: None,
            on_battery: false,
        });
        let preferences = Preferences::load();
        let tier = preferences.tier.map_or(suggested, tier_from);
        let mut viewport = Viewport::new();
        viewport.settings.denoise = preferences.denoise;
        viewport.settings.preview_scale = preferences.preview_scale;
        viewport.settings.preview_samples = preferences.preview_samples;
        viewport.settings.render_samples = preferences.render_samples;
        viewport.settings.max_bounces = preferences.bounces;
        let logo = crate::app_icon::load().map(|icon| {
            let image = egui::ColorImage::from_rgba_unmultiplied(
                [icon.width as usize, icon.height as usize],
                &icon.rgba,
            );
            creation
                .egui_ctx
                .load_texture("studio-logo", image, egui::TextureOptions::LINEAR)
        });
        let mut studio = Studio {
            page: Page::Projects,
            viewport,
            library: Library::default(),
            timeline: Timeline::default(),
            telemetry: Telemetry::default(),
            preferences,
            thumbnails: Thumbnails::default(),
            tier,
            suggested,
            project: None,
            recent: recent_projects::load(),
            loading: None,
            message: None,
            notice: None,
            build: BuildState {
                species: 0,
                tool: Tool::Place,
                placed: Vec::new(),
                selected: None,
                cursor: None,
                height: CATALOG[0].height as f32,
                spread: CATALOG[0].spread as f32,
                variation: 2,
                next_instance: 1,
            },
            materials: MaterialState {
                selected: 0,
                names: Vec::new(),
                original: Vec::new(),
            },
            library_state: LibraryState {
                filter: None,
                search: String::new(),
                inspected: 0,
            },
            help_step: 0,
            logo,
            local: pages::Local::default(),
            saved_settings: None,
            base_scene: None,
            capture_thumbnail: false,
            opened_at: None,
            last_frame: Instant::now(),
        };
        if let Some(path) = open {
            studio.open(path, &creation.egui_ctx);
        }
        studio
    }

    pub fn project_name(&self) -> Option<String> {
        self.project
            .as_ref()
            .and_then(|path| path.file_stem())
            .map(|name| name.to_string_lossy().into_owned())
    }

    /// Minutes since the open project was last written, for "Modificado hace…".
    pub fn modified_ago(&self) -> Option<std::time::Duration> {
        self.opened_at.and_then(|time| time.elapsed().ok())
    }

    /// Loads a project on another thread; the window keeps responding.
    pub fn open(&mut self, path: PathBuf, context: &egui::Context) {
        let name = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default();
        let wake = context.clone();
        self.loading = Some((
            project::load_in_background(path, move || wake.request_repaint()),
            name,
        ));
        self.message = None;
    }

    pub fn open_dialog(&mut self, context: &egui::Context) {
        if let Some(path) = rfd::FileDialog::new()
            .add_filter("Proyectos de Guanacaste", &project::PROJECT_EXTENSIONS)
            .pick_file()
        {
            self.open(path, context);
        }
    }

    /// Saves the picture currently shown, after asking where.
    pub fn save_image_dialog(&mut self) {
        let name = format!(
            "{}.png",
            self.project_name().unwrap_or_else(|| "render".into())
        );
        if let Some(path) = rfd::FileDialog::new()
            .add_filter("Imagen PNG", &["png"])
            .set_file_name(name)
            .save_file()
        {
            self.message = Some(match self.viewport.save_picture(&path) {
                Ok(()) => (format!("Imagen guardada en {}", path.display()), false),
                Err(reason) => (reason, true),
            });
        }
    }

    fn receive_loaded(&mut self, state: Option<&RenderState>) {
        let Some((receiver, _)) = &self.loading else {
            return;
        };
        let Ok(result) = receiver.try_recv() else {
            return;
        };
        self.loading = None;
        let Some(state) = state else {
            self.message = Some((
                "Esta ventana no tiene acceso a la tarjeta gráfica.".into(),
                true,
            ));
            return;
        };
        let opened = match result {
            Ok(opened) => opened,
            Err(reason) => {
                self.message = Some((reason, true));
                return;
            }
        };
        let base = Arc::new(opened.scene.clone());
        match self.viewport.set_scene(state, opened.scene) {
            Ok(()) => {
                recent_projects::remember(&mut self.recent, &opened.path);
                self.opened_at = std::fs::metadata(&opened.path)
                    .and_then(|meta| meta.modified())
                    .ok();
                self.capture_thumbnail = !self.thumbnails.has(&opened.path);
                self.project = Some(opened.path);
                self.notice = opened.notice;
                self.base_scene = Some(base);
                self.build.placed.clear();
                self.build.selected = None;
                self.timeline = Timeline::default();
                self.refresh_material_names();
                self.materials.original = self.viewport.materials().to_vec();
                self.materials.selected = 0;
                self.apply_tier(self.tier);
                self.page = Page::Build;
            }
            Err(reason) => self.message = Some((reason, true)),
        }
    }

    /// Names each material after the stock material it matches ("Pared", "Vidrio"…), or by number.
    fn refresh_material_names(&mut self) {
        let stock = gr_model::scene::stock_materials();
        let names = gr_model::scene::stock_material_names();
        let same = |a: &Material, b: &Material| {
            (a.base_color - b.base_color).length() < 1e-4
                && (a.roughness - b.roughness).abs() < 1e-4
                && (a.metallic - b.metallic).abs() < 1e-4
                && (a.transmission - b.transmission).abs() < 1e-4
        };
        self.materials.names = self
            .viewport
            .materials()
            .iter()
            .enumerate()
            .map(|(index, material)| {
                stock
                    .iter()
                    .position(|candidate| same(candidate, material))
                    .and_then(|kind| names.get(kind))
                    .map_or_else(
                        || format!("Material {}", index + 1),
                        |name| (*name).to_owned(),
                    )
            })
            .collect();
    }

    /// Applies a quality profile: preview resolution and light bounces.
    pub fn apply_tier(&mut self, tier: QualityTier) {
        self.tier = tier;
        let budget = tier.budget();
        self.viewport.settings.preview_scale = budget.viewport_scale.max(0.5);
        self.viewport.settings.max_bounces = budget.max_bounces;
        self.viewport.restart();
        self.preferences.tier = Some(tier_index(tier));
        self.save_preferences();
    }

    pub fn save_preferences(&mut self) {
        let settings = &self.viewport.settings;
        self.preferences.denoise = settings.denoise;
        self.preferences.preview_scale = settings.preview_scale;
        self.preferences.preview_samples = settings.preview_samples;
        self.preferences.render_samples = settings.render_samples;
        self.preferences.bounces = settings.max_bounces;
        self.preferences.save();
    }

    /// Saves the preferences when a page changed a view setting since the last save.
    pub fn save_preferences_if_changed(&mut self) {
        if self.saved_settings != Some(self.viewport.settings) {
            if self.saved_settings.is_some() {
                self.save_preferences();
            }
            self.saved_settings = Some(self.viewport.settings);
        }
    }

    /// The current camera and sun, as the timeline records them.
    pub fn pose(&self) -> Pose {
        Pose {
            camera: self.viewport.camera(),
            sun_azimuth: self.viewport.settings.sun_azimuth_degrees,
            sun_elevation: self.viewport.settings.sun_elevation_degrees,
        }
    }

    pub fn apply_pose(&mut self, pose: Pose) {
        self.viewport.set_camera(pose.camera);
        self.viewport.settings.sun_azimuth_degrees = pose.sun_azimuth;
        self.viewport.settings.sun_elevation_degrees = pose.sun_elevation;
    }

    /// Places the selected species at a point of the scene, with the size and variation the panel sets.
    pub fn place_plant(&mut self, state: &RenderState, at: Vec3) {
        let instance = self.build.next_instance;
        self.build.next_instance += 1;
        let factor = match self.build.variation {
            0 => 1.0,
            1 => 0.9 + 0.2 * unit_noise(instance),
            _ => 0.75 + 0.5 * unit_noise(instance),
        };
        self.build.placed.push(Placed {
            species: self.build.species,
            at,
            height: (self.build.height * factor) as f64,
            spread: (self.build.spread * factor) as f64,
            instance,
        });
        self.build.selected = Some(self.build.placed.len() - 1);
        self.rebuild_scene(state);
    }

    /// The placed plant nearest a point, within its canopy.
    pub fn placed_near(&self, point: Vec3) -> Option<usize> {
        self.build
            .placed
            .iter()
            .enumerate()
            .map(|(index, plant)| {
                (
                    index,
                    (plant.at.truncate() - point.truncate()).length(),
                    plant.spread as f32 / 2.0,
                )
            })
            .filter(|(_, distance, reach)| distance <= reach)
            .min_by(|a, b| a.1.total_cmp(&b.1))
            .map(|(index, _, _)| index)
    }

    pub fn delete_placed(&mut self, state: &RenderState, index: usize) {
        if index < self.build.placed.len() {
            self.build.placed.remove(index);
            self.build.selected = None;
            self.rebuild_scene(state);
        }
    }

    /// The opened scene plus every placed plant, uploaded on another thread.
    fn rebuild_scene(&mut self, state: &RenderState) {
        let Some(base) = &self.base_scene else { return };
        let mut scene = Scene::clone(base);
        scene.materials = self
            .viewport
            .materials()
            .iter()
            .take(base.materials.len())
            .copied()
            .collect();
        for plant in &self.build.placed {
            let mesh =
                CATALOG[plant.species].mesh(plant.at, plant.height, plant.spread, plant.instance);
            vegetation::append_mesh(&mut scene, &mesh);
        }
        self.viewport.replace_scene(state, scene);
    }

    /// Keeps a picture of the project for the Projects page, once its navigation view has been drawn.
    fn capture_thumbnail(&mut self, context: &egui::Context) {
        if !self.capture_thumbnail
            || self.viewport.mode() != ViewMode::Navigate
            || self.viewport.resolution().0 == 0
        {
            return;
        }
        let Some(project) = self.project.clone() else {
            return;
        };
        let path = std::env::temp_dir().join("guanacaste-studio-thumbnail.png");
        if self.viewport.save_picture(&path).is_ok()
            && let Some((pixels, width, height)) = read_rgba(&path)
        {
            self.thumbnails
                .store(context, &project, &pixels, width, height);
        }
        let _ = std::fs::remove_file(path);
        self.capture_thumbnail = false;
    }

    /// Advances the timeline and renders the next frame of a movie export.
    fn advance_timeline(&mut self, context: &egui::Context) {
        if self.timeline.tick()
            && let Some(pose) = self.timeline.pose_at(self.timeline.playhead)
        {
            self.apply_pose(pose);
            context.request_repaint();
        }
        let Some(export) = &mut self.timeline.export else {
            return;
        };
        let total = export.total;
        let frame = export.frame;
        let time = frame as f32 / self.timeline.fps as f32;
        let (width, height, samples, folder) = (
            export.width,
            export.height,
            export.samples,
            export.folder.clone(),
        );
        let camera = self
            .timeline
            .pose_at(time)
            .map_or_else(|| self.viewport.camera(), |pose| pose.camera);
        match self.viewport.render_frame(camera, width, height, samples) {
            Ok(pixels) => {
                let path = folder.join(format!("fotograma_{:05}.png", frame + 1));
                if let Err(reason) = crate::viewport::save_png(&path, &pixels, width, height) {
                    self.message = Some((reason, true));
                    self.timeline.export = None;
                    return;
                }
            }
            Err(reason) => {
                self.message = Some((reason, true));
                self.timeline.export = None;
                return;
            }
        }
        if frame + 1 >= total {
            let seconds = self
                .timeline
                .export
                .as_ref()
                .map_or(0.0, |export| export.started.elapsed().as_secs_f32());
            self.message = Some((
                format!(
                    "Película exportada: {total} fotogramas en {} ({seconds:.0} s)",
                    folder.display()
                ),
                false,
            ));
            self.timeline.export = None;
            self.viewport.restart();
        } else if let Some(export) = &mut self.timeline.export {
            export.frame += 1;
        }
        context.request_repaint();
    }

    fn show_toast(&mut self, context: &egui::Context) {
        let Some((text, is_error)) = self.message.clone() else {
            return;
        };
        egui::Area::new(egui::Id::new("studio-toast"))
            .anchor(egui::Align2::CENTER_BOTTOM, egui::vec2(0.0, -28.0))
            .order(egui::Order::Tooltip)
            .show(context, |ui| {
                theme::floating()
                    .inner_margin(egui::Margin::symmetric(14, 8))
                    .show(ui, |ui| {
                        ui.horizontal(|ui| {
                            let color = if is_error {
                                theme::WARNING
                            } else {
                                theme::TELEMETRY
                            };
                            crate::ui::widgets::text(ui, text, theme::body(13.0), color);
                            if crate::ui::widgets::icon_button(
                                ui,
                                crate::ui::icons::CLOSE,
                                22.0,
                                false,
                                "",
                            )
                            .clicked()
                            {
                                self.message = None;
                            }
                        });
                    });
            });
    }
}

impl eframe::App for Studio {
    fn ui(&mut self, ui: &mut egui::Ui, frame: &mut eframe::Frame) {
        let now = Instant::now();
        self.telemetry
            .record(now.duration_since(self.last_frame).as_secs_f32() * 1000.0);
        self.last_frame = now;
        if self.telemetry.is_reading_hardware() {
            // The readouts that show the adapter's memory appear once Windows has answered.
            ui.ctx()
                .request_repaint_after(std::time::Duration::from_millis(250));
        }
        if !self.telemetry.is_idle() {
            // One more frame once the interface settles, so the readouts can say it is idle.
            ui.ctx()
                .request_repaint_after(std::time::Duration::from_millis(600));
        }
        let state = frame.wgpu_render_state().cloned();
        self.receive_loaded(state.as_ref());
        if self.library.poll(ui.ctx()) {
            ui.ctx()
                .request_repaint_after(std::time::Duration::from_millis(100));
        }
        self.advance_timeline(ui.ctx());

        egui::Panel::top("title-bar")
            .frame(
                egui::Frame::NONE
                    .fill(theme::PANEL)
                    .stroke(theme::hairline()),
            )
            .exact_size(28.0)
            .show(ui, |ui| shell::title_bar(ui, self));
        egui::Panel::left("tool-rail")
            .frame(
                egui::Frame::NONE
                    .fill(theme::PANEL)
                    .stroke(theme::hairline()),
            )
            .exact_size(48.0)
            .resizable(false)
            .show(ui, |ui| shell::rail(ui, self));
        egui::CentralPanel::default()
            .frame(egui::Frame::NONE.fill(theme::CANVAS))
            .show(ui, |ui| {
                pages::show(ui, self, state.as_ref());
            });
        self.capture_thumbnail(ui.ctx());
        shell::resize_edges(ui.ctx());
        self.show_toast(ui.ctx());
    }
}

/// A stable pseudo-random number in [0, 1) for a plant instance.
fn unit_noise(seed: u128) -> f32 {
    let mut x = (seed as u64).wrapping_mul(0x9e37_79b9_7f4a_7c15);
    x ^= x >> 29;
    x = x.wrapping_mul(0xbf58_476d_1ce4_e5b9);
    x ^= x >> 32;
    (x >> 40) as f32 / (1u64 << 24) as f32
}

pub fn tier_index(tier: QualityTier) -> u8 {
    match tier {
        QualityTier::Economico => 0,
        QualityTier::Equilibrado => 1,
        QualityTier::Alto => 2,
        QualityTier::Ultra => 3,
    }
}

pub fn tier_from(index: u8) -> QualityTier {
    match index {
        0 => QualityTier::Economico,
        1 => QualityTier::Equilibrado,
        2 => QualityTier::Alto,
        _ => QualityTier::Ultra,
    }
}

fn read_rgba(path: &std::path::Path) -> Option<(Vec<u8>, u32, u32)> {
    let decoder = png::Decoder::new(std::io::BufReader::new(std::fs::File::open(path).ok()?));
    let mut reader = decoder.read_info().ok()?;
    let mut buffer = vec![0; reader.output_buffer_size()?];
    let info = reader.next_frame(&mut buffer).ok()?;
    (info.color_type == png::ColorType::Rgba).then(|| {
        (
            buffer[..info.buffer_size()].to_vec(),
            info.width,
            info.height,
        )
    })
}
