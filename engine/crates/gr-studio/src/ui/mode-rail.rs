//! Obsidian CAD 48px vertical tool rail.

use crate::localization::text;
use crate::ui::top_bar::StudioMode;
use crate::ui::widgets::icon_button::icon_button_active;
use eframe::egui::{Align, Layout, Ui};

/// State required by the vertical tool rail.
pub struct ModeRailState {
    pub active: StudioMode,
    #[allow(dead_code)]
    pub project_loaded: bool,
}

/// Actions dispatched from rail buttons.
pub enum ModeRailAction {
    None,
    SwitchMode(StudioMode),
}

/// Renders the vertical tool rail with technical icon buttons.
pub fn show(ui: &mut Ui, state: &mut ModeRailState) -> ModeRailAction {
    let mut action = ModeRailAction::None;

    ui.vertical_centered(|ui| {
        ui.add_space(4.0);

        // Open Files action
        if icon_button_active(ui, "\u{e8b7}", text("projects_mode"), state.active == StudioMode::Projects).clicked() {
            action = ModeRailAction::SwitchMode(StudioMode::Projects);
        }

        ui.add_space(6.0);
        ui.separator();
        ui.add_space(4.0);

        // Main Modes
        if icon_button_active(ui, "\u{e80f}", text("build_mode"), state.active == StudioMode::Build).clicked() {
            action = ModeRailAction::SwitchMode(StudioMode::Build);
        }

        ui.add_space(2.0);
        if icon_button_active(ui, "\u{e753}", text("weather_mode"), state.active == StudioMode::Weather).clicked() {
            action = ModeRailAction::SwitchMode(StudioMode::Weather);
        }

        ui.add_space(2.0);
        if icon_button_active(ui, "\u{e790}", text("material_mode"), state.active == StudioMode::Material).clicked() {
            action = ModeRailAction::SwitchMode(StudioMode::Material);
        }

        ui.add_space(2.0);
        if icon_button_active(ui, "\u{e722}", text("render_mode"), state.active == StudioMode::Render).clicked() {
            action = ModeRailAction::SwitchMode(StudioMode::Render);
        }

        ui.add_space(2.0);
        if icon_button_active(ui, "\u{e714}", text("movie_mode"), state.active == StudioMode::Movie).clicked() {
            action = ModeRailAction::SwitchMode(StudioMode::Movie);
        }

        // Bottom section: Help & Settings
        ui.with_layout(Layout::bottom_up(Align::Center), |ui| {
            ui.add_space(4.0);
            if icon_button_active(ui, "\u{e713}", text("settings_mode"), state.active == StudioMode::Settings).clicked() {
                action = ModeRailAction::SwitchMode(StudioMode::Settings);
            }

            ui.add_space(2.0);
        });
    });

    action
}
