//! Application orchestrator for Guanacaste Render Studio.
//! Manages the viewport, mode transitions, asynchronous loading, and docked decks.

use crate::localization::text;
use crate::project::{self, Loaded};
use crate::theme;
use crate::ui::environment_panel;
use crate::ui::hud::{self, HudState};
use crate::ui::material_panel;
use crate::ui::mode_rail::{self, ModeRailAction, ModeRailState};
use crate::ui::render_panel;
use crate::ui::scene_panel;
use crate::ui::settings_page;
use crate::ui::start_screen::{self, ProjectPreview, RecentProject, StartScreenAction, StartScreenState};
use crate::ui::top_bar::{self, StudioMode, TopBarAction, TopBarState};
use crate::viewport::Viewport;
use eframe::egui::{self, RichText};
use gr_core::hardware::{AdapterKind, Capabilities, QualityTier, suggest_tier};
use gr_gpu::wgpu;
use std::path::PathBuf;
use std::sync::mpsc::Receiver;

/// Root application struct owning workstation state and viewports.
pub struct StudioApp {
    viewport: Viewport,
    tier: QualityTier,
    suggested: QualityTier,
    mode: StudioMode,
    loading: Option<(Receiver<Loaded>, String)>,
    project: Option<PathBuf>,
    recent: Vec<RecentProject>,
    selected_material: usize,
    message: Option<(String, bool)>,
    logo: Option<egui::TextureHandle>,
}

impl StudioApp {
    /// Initializes workstation state, detects graphics capabilities, and sets themes.
    pub fn new(creation: &eframe::CreationContext<'_>, open: Option<PathBuf>) -> StudioApp {
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

        let mut app = StudioApp {
            viewport: Viewport::new(),
            tier: suggested,
            suggested,
            mode: StudioMode::Projects,
            loading: None,
            project: None,
            recent: crate::recent_projects::load(),
            selected_material: 0,
            message: None,
            logo: crate::app_icon::load().map(|icon| {
                let image = egui::ColorImage::from_rgba_unmultiplied(
                    [icon.width as usize, icon.height as usize],
                    &icon.rgba,
                );
                creation.egui_ctx.load_texture("studio-logo", image, egui::TextureOptions::LINEAR)
            }),
        };

        if let Some(path) = open {
            app.open(path, &creation.egui_ctx);
        }
        app
    }

    /// Loads a project asynchronously and wakes the interface when the result is ready.
    fn open(&mut self, path: PathBuf, context: &egui::Context) {
        let name = path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
        let wake = context.clone();
        self.loading = Some((project::load_in_background(path, move || wake.request_repaint()), name));
        self.message = None;
    }

    /// Opens the platform file picker for supported project and scene formats.
    fn open_dialog(&mut self, context: &egui::Context) {
        if let Some(path) = rfd::FileDialog::new()
            .add_filter(text("project_filter"), &project::PROJECT_EXTENSIONS)
            .pick_file()
        {
            self.open(path, context);
        }
    }

    /// Installs a completed scene without discarding the previous view on failure.
    fn receive_loaded(&mut self, frame: &eframe::Frame) {
        let Some((receiver, _)) = &self.loading else {
            return;
        };
        let Ok(result) = receiver.try_recv() else {
            return;
        };
        self.loading = None;
        let Some(state) = frame.wgpu_render_state() else {
            self.message = Some((text("gpu_unavailable").into(), true));
            return;
        };
        match result.and_then(|opened| {
            self.viewport
                .set_scene(state, &opened.scene, self.tier.budget().max_bounces)
                .map(|()| (opened.path, opened.notice))
        }) {
            Ok((path, notice)) => {
                crate::recent_projects::remember(&mut self.recent, &path);
                self.project = Some(path);
                self.selected_material = 0;
                self.message = notice.map(|text| (text, true));
                self.mode = StudioMode::Render;
            }
            Err(reason) => self.message = Some((reason, true)),
        }
    }

    /// Collects current loading, scene, and renderer state for navigation.
    fn top_bar_state(&self) -> TopBarState {
        let (samples, target_samples, width, height, sps) = if self.viewport.has_scene() {
            let (w, h) = self.viewport.resolution();
            (
                self.viewport.samples(),
                self.viewport.settings.target_samples,
                w,
                h,
                self.viewport.samples_per_second(),
            )
        } else {
            (0, 0, 0, 0, 0.0)
        };
        TopBarState {
            project_name: self
                .project
                .as_ref()
                .and_then(|p| p.file_name())
                .map(|n| n.to_string_lossy().into_owned()),
            is_loading: self.loading.is_some(),
            has_scene: self.viewport.has_scene(),
            loading_name: self.loading.as_ref().map(|(_, name)| name.clone()),
            samples,
            target_samples,
            width,
            height,
            samples_per_second: sps,
            active_mode: self.mode,
            logo: self.logo.as_ref().map(egui::TextureHandle::id),
        }
    }

    /// Routes the selected workspace to its supported inspector controls.
    fn side_panel(&mut self, ui: &mut egui::Ui) {
        match self.mode {
            StudioMode::Projects => {}
            StudioMode::Weather => {
                if environment_panel::show(ui, &mut self.viewport.settings) {
                    self.viewport.restart();
                }
            }
            StudioMode::Render => {
                render_panel::show(ui, &mut self.tier, self.suggested, &mut self.viewport);
            }
            StudioMode::Material => material_panel::show(ui, self.viewport.materials(), &mut self.selected_material),
            StudioMode::Build | StudioMode::Movie | StudioMode::Settings => {
                ui.centered_and_justified(|ui| {
                    ui.label(RichText::new(text("coming_soon")).color(theme::MUTED).size(11.0));
                });
            }
        }
    }

    /// Keeps loading and export feedback visible above the active workspace.
    fn show_toast(&self, ctx: &egui::Context) {
        let Some((text, is_error)) = &self.message else {
            return;
        };
        egui::Area::new(egui::Id::new("studio-toast"))
            .anchor(egui::Align2::CENTER_TOP, egui::vec2(0.0, 36.0))
            .order(egui::Order::Foreground)
            .show(ctx, |ui| {
                egui::Frame::new()
                    .fill(theme::PANEL)
                    .stroke(theme::hairline())
                    .corner_radius(egui::CornerRadius::same(4))
                    .inner_margin(egui::Margin::symmetric(12, 6))
                    .show(ui, |ui| {
                        let color = if *is_error { theme::ERROR } else { theme::TEAL };
                        ui.label(RichText::new(text).color(color));
                    });
            });
    }
}

impl eframe::App for StudioApp {
    /// Coordinates navigation, loading, rendering, and floating scene information.
    fn ui(&mut self, ui: &mut egui::Ui, frame: &mut eframe::Frame) {
        self.receive_loaded(frame);

        // 1. Fixed Top Menu Bar (28px)
        let top_frame = egui::Frame::NONE
            .fill(theme::PANEL)
            .stroke(theme::hairline())
            .inner_margin(egui::Margin::symmetric(10, 0));

        egui::Panel::top("top-bar").frame(top_frame).exact_size(28.0).show(ui, |ui| {
            let mut state = self.top_bar_state();
            match top_bar::show(ui, &mut state) {
                TopBarAction::OpenFile => self.open_dialog(ui.ctx()),
                TopBarAction::SavePng => {
                    if let Some(path) = rfd::FileDialog::new()
                        .add_filter(text("png_filter"), &["png"])
                        .set_file_name("render.png")
                        .save_file()
                    {
                        self.message = Some(match self.viewport.save_picture(&path) {
                            Ok(()) => (text("image_saved").replace("{path}", &path.display().to_string()), false),
                            Err(reason) => (reason, true),
                        });
                    }
                }
                TopBarAction::SwitchMode(m) => self.mode = m,
                TopBarAction::None => {}
            }
        });

        // 2. Left Tool Rail (48px)
        let rail_frame = egui::Frame::NONE
            .fill(theme::DEEPEST)
            .stroke(theme::hairline())
            .inner_margin(egui::Margin::symmetric(3, 5));

        egui::Panel::left("mode-rail")
            .frame(rail_frame)
            .exact_size(48.0)
            .resizable(false)
            .show(ui, |ui| {
                let mut state = ModeRailState {
                    active: self.mode,
                    project_loaded: self.viewport.has_scene(),
                };
                match mode_rail::show(ui, &mut state) {
                    ModeRailAction::SwitchMode(m) => self.mode = m,
                    ModeRailAction::None => {}
                }
            });

        // 3. Right Property & Tool Inspector
        let side_frame = egui::Frame::NONE
            .fill(theme::PANEL)
            .stroke(theme::hairline())
            .inner_margin(egui::Margin::same(8));

        if !matches!(self.mode, StudioMode::Projects | StudioMode::Settings) && self.viewport.has_scene() {
            egui::Panel::right("side-panel")
                .frame(side_frame)
                .default_size(320.0)
                .size_range(240.0..=420.0)
                .show(ui, |ui| self.side_panel(ui));
        }

        // 4. Central 3D Canvas / Start Screen
        egui::CentralPanel::default()
            .frame(egui::Frame::NONE.fill(theme::BACKGROUND))
            .show(ui, |ui| {
                match (
                    self.viewport.has_scene() && self.mode != StudioMode::Projects,
                    frame.wgpu_render_state(),
                ) {
                    (true, Some(state)) if self.mode == StudioMode::Settings => {
                        let name = self
                            .project
                            .as_ref()
                            .and_then(|path| path.file_name())
                            .map(|name| name.to_string_lossy().into_owned());
                        settings_page::show(ui, &mut self.viewport, state, &mut self.tier, self.suggested, name.as_deref());
                    }
                    (true, Some(state)) => {
                        self.viewport.show(ui, state, &self.tier.budget());
                    }
                    _ if self.loading.is_some() => {
                        ui.centered_and_justified(|ui| {
                            let name = self.loading.as_ref().map(|(_, n)| n.as_str()).unwrap_or("…");
                            ui.label(
                                RichText::new(text("loading").replace("{name}", name))
                                    .color(theme::MUTED)
                                    .size(14.0),
                            );
                        });
                    }
                    _ => {
                        let ss_state = StartScreenState {
                            recent: self.recent.clone(),
                            active_name: self
                                .project
                                .as_ref()
                                .and_then(|path| path.file_name())
                                .map(|name| name.to_string_lossy().into_owned()),
                        };
                        let budget = self.tier.budget();
                        let preview = frame
                            .wgpu_render_state()
                            .filter(|_| self.viewport.has_scene())
                            .map(|render_state| ProjectPreview {
                                viewport: &mut self.viewport,
                                render_state,
                                budget: &budget,
                            });
                        match start_screen::show(ui, &ss_state, preview) {
                            StartScreenAction::Open(path) => self.open(path, ui.ctx()),
                            StartScreenAction::OpenDialog => self.open_dialog(ui.ctx()),
                            StartScreenAction::OpenViewport => self.mode = StudioMode::Render,
                            StartScreenAction::None => {}
                        }
                    }
                }
            });

        // 5. HUD & Overlays
        if self.viewport.has_scene() && !matches!(self.mode, StudioMode::Projects | StudioMode::Settings) {
            let project_name = self
                .project
                .as_ref()
                .and_then(|p| p.file_name())
                .map(|n| n.to_string_lossy().into_owned());

            egui::Area::new(egui::Id::new("scene-info-card"))
                .order(egui::Order::Foreground)
                .fixed_pos(egui::pos2(58.0, 36.0))
                .show(ui.ctx(), |ui| {
                    scene_panel::show_scene_card(ui, &self.viewport.facts, project_name.as_deref());
                });

            let (w, h) = self.viewport.resolution();
            let hud = HudState {
                samples: self.viewport.samples(),
                target_samples: self.viewport.settings.target_samples,
                width: w,
                height: h,
                samples_per_second: self.viewport.samples_per_second(),
                is_rendering: self.viewport.samples_per_second() > 0.0,
            };
            hud::show(ui.ctx(), &hud);
        }

        self.show_toast(ui.ctx());
    }
}
