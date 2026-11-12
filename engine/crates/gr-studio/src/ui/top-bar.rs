//! Obsidian CAD 28px workstation top bar.

use crate::localization::text;
use crate::theme;
use eframe::egui::{self, Align, Layout, RichText, Ui};

/// Major workstation operational modes.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum StudioMode {
    Projects,
    Build,
    Weather,
    Material,
    Render,
    Movie,
    Settings,
}

impl StudioMode {
    /// Provides the localized name used by navigation controls.
    pub fn label(&self) -> &'static str {
        match self {
            Self::Projects => text("projects_mode"),
            Self::Build => text("build_mode"),
            Self::Weather => text("weather_mode"),
            Self::Material => text("material_mode"),
            Self::Render => text("render_mode"),
            Self::Movie => text("movie_mode"),
            Self::Settings => text("settings_mode"),
        }
    }
}

/// Telemetry and document state consumed by the top bar.
pub struct TopBarState {
    pub project_name: Option<String>,
    pub is_loading: bool,
    pub has_scene: bool,
    pub loading_name: Option<String>,
    pub samples: u32,
    pub target_samples: u32,
    pub width: u32,
    pub height: u32,
    pub samples_per_second: f32,
    pub active_mode: StudioMode,
}

/// Interactive user actions dispatched from the top bar.
pub enum TopBarAction {
    None,
    OpenFile,
    SavePng,
    SwitchMode(StudioMode),
}

/// Renders the fixed top bar with brand badge, layer indicator, mode pills, and project actions.
pub fn show(ui: &mut Ui, state: &mut TopBarState) -> TopBarAction {
    let mut action = TopBarAction::None;

    ui.horizontal(|ui| {
        // Brand Symbol: G Badge
        let brand_badge = RichText::new(" G ")
            .strong()
            .color(theme::DEEPEST)
            .background_color(theme::ACCENT)
            .font(theme::mono_font());
        ui.label(brand_badge);

        ui.label(
            RichText::new("GUANACASTE RENDER")
                .strong()
                .font(theme::label_font())
                .color(theme::TEXT),
        );

        ui.label(RichText::new("│").color(theme::BORDER));

        ui.add_space(8.0);

        // Center: Mode navigation tabs
        const MODES: [StudioMode; 6] = [
            StudioMode::Build,
            StudioMode::Weather,
            StudioMode::Material,
            StudioMode::Render,
            StudioMode::Movie,
            StudioMode::Settings,
        ];

        for mode in MODES {
            let is_active = state.active_mode == mode;
            let (bg, fg) = if is_active {
                (theme::ACCENT_PRESS, theme::TEXT)
            } else {
                (theme::RAISED, theme::MUTED)
            };

            let text = RichText::new(mode.label()).font(theme::label_font()).color(fg);

            let btn = egui::Button::new(text)
                .fill(bg)
                .stroke(if is_active {
                    egui::Stroke::new(1.0, theme::ACCENT)
                } else {
                    theme::hairline()
                })
                .corner_radius(egui::CornerRadius::same(2));

            if ui.add(btn).on_hover_text(mode.label()).clicked() {
                action = TopBarAction::SwitchMode(mode);
            }
        }

        // Right side: Project status & Action buttons
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            // Open button (Primary accent)
            let open_btn = egui::Button::new(
                RichText::new(text("open_project"))
                    .font(theme::label_font())
                    .color(theme::DEEPEST)
                    .strong(),
            )
            .fill(theme::ACCENT)
            .corner_radius(egui::CornerRadius::same(2));

            if ui.add(open_btn).clicked() {
                action = TopBarAction::OpenFile;
            }

            // Save PNG (Secondary)
            let save_btn = egui::Button::new(RichText::new(text("save_image")).font(theme::label_font()).color(theme::TEXT))
                .fill(theme::RAISED)
                .stroke(theme::hairline())
                .corner_radius(egui::CornerRadius::same(2));

            if ui.add_enabled(state.has_scene && !state.is_loading, save_btn).clicked() {
                action = TopBarAction::SavePng;
            }

            ui.separator();

            // Status message / telemetry summary
            let status = if state.is_loading {
                let name = state.loading_name.as_deref().unwrap_or("…");
                text("loading").replace("{name}", name)
            } else if state.target_samples > 0 {
                if state.samples_per_second > 0.0 {
                    format!(
                        "{}/{} spp · {}x{} · {:.0} spp/s",
                        state.samples.min(state.target_samples),
                        state.target_samples,
                        state.width,
                        state.height,
                        state.samples_per_second
                    )
                } else {
                    format!("{}/{} spp · {}x{}", state.samples, state.target_samples, state.width, state.height)
                }
            } else {
                let proj = state.project_name.as_deref().unwrap_or(text("idle"));
                proj.to_string()
            };

            ui.label(RichText::new(status).font(theme::label_font()).color(theme::MUTED));
        });
    });

    action
}
