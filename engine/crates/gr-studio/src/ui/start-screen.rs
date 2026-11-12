//! Project workspace showing the current render and recently opened files.

use crate::localization::text;
use crate::theme;
use crate::ui::scene_panel;
use crate::ui::widgets::mode_badge::{BadgeVariant, mode_badge};
use crate::viewport::Viewport;
use eframe::egui::{self, RichText, Ui, Vec2};
use eframe::egui_wgpu::RenderState;
use gr_core::hardware::TierBudget;
use std::path::PathBuf;

/// Metadata for a project opened during the current application session.
#[derive(Clone, Debug)]
pub struct RecentProject {
    pub name: String,
    pub path: PathBuf,
    pub file_size_mb: Option<f64>,
}

/// Projects and the active filename presented in the workspace.
pub struct StartScreenState {
    pub recent: Vec<RecentProject>,
    pub active_name: Option<String>,
}

/// The actual renderer and quality budget used by the active project preview.
pub struct ProjectPreview<'a> {
    pub viewport: &'a mut Viewport,
    pub render_state: &'a RenderState,
    pub budget: &'a TierBudget,
}

/// Navigation requested by a project workspace action.
pub enum StartScreenAction {
    None,
    Open(PathBuf),
    OpenDialog,
    OpenViewport,
}

/// Draws the project dashboard using the real scene when one is open.
pub fn show(ui: &mut Ui, state: &StartScreenState, preview: Option<ProjectPreview<'_>>) -> StartScreenAction {
    let mut action = StartScreenAction::None;
    let width = ui.available_width();
    ui.add_space(12.0);
    ui.horizontal(|ui| {
        ui.add_space(18.0);
        ui.label(RichText::new(text("start_title")).size(24.0).strong().color(theme::TEXT));
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
            ui.add_space(18.0);
            if ui
                .add(egui::Button::new(RichText::new(text("open_another")).color(theme::DEEPEST)).fill(theme::ACCENT))
                .clicked()
            {
                action = StartScreenAction::OpenDialog;
            }
        });
    });
    ui.add_space(12.0);
    ui.separator();
    ui.add_space(12.0);
    ui.horizontal(|ui| {
        ui.add_space(18.0);
        ui.label(
            RichText::new(if preview.is_some() {
                text("active_project")
            } else {
                text("recent_projects")
            })
            .font(theme::label_font())
            .color(theme::ACCENT)
            .strong(),
        );
    });
    ui.add_space(8.0);

    if let Some(preview) = preview {
        ui.horizontal_top(|ui| {
            ui.add_space(18.0);
            let preview_width = (width * 0.66).max(250.0);
            let preview_height = (ui.available_height() * 0.62).clamp(220.0, 510.0);
            egui::Frame::new().fill(theme::DEEPEST).stroke(theme::hairline()).show(ui, |ui| {
                ui.allocate_ui(Vec2::new(preview_width, preview_height), |ui| {
                    preview.viewport.show(ui, preview.render_state, preview.budget);
                });
            });
            ui.add_space(8.0);
            ui.vertical(|ui| {
                mode_badge(ui, text("project_status"), BadgeVariant::Tertiary);
                ui.add_space(8.0);
                scene_panel::show_scene_card(ui, &preview.viewport.facts, state.active_name.as_deref());
                ui.add_space(14.0);
                if ui
                    .add(
                        egui::Button::new(RichText::new(text("open_viewport")).color(theme::DEEPEST))
                            .fill(theme::ACCENT)
                            .min_size(Vec2::new(220.0, 32.0)),
                    )
                    .clicked()
                {
                    action = StartScreenAction::OpenViewport;
                }
            });
        });
    } else {
        ui.add_space(30.0);
        ui.vertical_centered(|ui| {
            ui.label(RichText::new(text("start_subtitle")).size(22.0).strong().color(theme::TEXT));
            ui.add_space(8.0);
            ui.label(RichText::new(text("start_description")).color(theme::MUTED));
            ui.add_space(18.0);
            if ui
                .add(
                    egui::Button::new(RichText::new(text("open_project")).color(theme::DEEPEST).strong())
                        .fill(theme::ACCENT)
                        .min_size(Vec2::new(180.0, 34.0)),
                )
                .clicked()
            {
                action = StartScreenAction::OpenDialog;
            }
            ui.add_space(10.0);
            ui.label(RichText::new(text("formats")).font(theme::label_font()).color(theme::MUTED));
        });
    }

    ui.add_space(20.0);
    ui.horizontal(|ui| {
        ui.add_space(18.0);
        ui.label(RichText::new(text("recent_session")).font(theme::label_font()).color(theme::MUTED));
    });
    ui.add_space(8.0);
    ui.horizontal_wrapped(|ui| {
        ui.add_space(18.0);
        if state.recent.is_empty() {
            ui.label(RichText::new(text("recent_empty")).color(theme::MUTED));
        }
        for project in &state.recent {
            let label = match project.file_size_mb {
                Some(size) if size < 0.1 => format!("{}  ·  {:.0} KB", project.name, size * 1024.0),
                Some(size) => format!("{}  ·  {:.1} MB", project.name, size),
                None => project.name.clone(),
            };
            if ui
                .add(egui::Button::new(label).fill(theme::RAISED).min_size(Vec2::new(190.0, 36.0)))
                .on_hover_text(project.path.to_string_lossy())
                .clicked()
            {
                action = StartScreenAction::Open(project.path.clone());
            }
        }
    });
    action
}
