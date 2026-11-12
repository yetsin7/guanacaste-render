//! Settings workspace with real rendering controls and a live scene preview.

use crate::localization::text;
use crate::theme;
use crate::ui::{environment_panel, render_panel, scene_panel};
use crate::viewport::Viewport;
use eframe::egui::{self, RichText, Ui, Vec2};
use eframe::egui_wgpu::RenderState;
use gr_core::hardware::QualityTier;

/// Displays the current settings alongside the same live renderer used by the viewport.
pub fn show(
    ui: &mut Ui,
    viewport: &mut Viewport,
    render_state: &RenderState,
    tier: &mut QualityTier,
    suggested: QualityTier,
    name: Option<&str>,
) {
    ui.add_space(10.0);
    ui.label(RichText::new(text("settings_title")).size(20.0).strong().color(theme::TEXT));
    ui.add_space(8.0);
    ui.separator();
    ui.add_space(10.0);
    ui.columns(2, |columns| {
        egui::ScrollArea::vertical()
            .id_salt("settings-controls")
            .show(&mut columns[0], |ui| {
                ui.set_min_width(ui.available_width());
                render_panel::show(ui, tier, suggested, viewport);
                ui.add_space(12.0);
                if environment_panel::show(ui, &mut viewport.settings) {
                    viewport.restart();
                }
            });
        let ui = &mut columns[1];
        ui.label(
            RichText::new(text("settings_preview"))
                .font(theme::label_font())
                .color(theme::ACCENT),
        );
        ui.add_space(6.0);
        let preview_size = Vec2::new(ui.available_width(), (ui.available_height() * 0.52).clamp(180.0, 380.0));
        egui::Frame::new().fill(theme::DEEPEST).stroke(theme::hairline()).show(ui, |ui| {
            ui.allocate_ui(preview_size, |ui| viewport.show(ui, render_state, &tier.budget()));
        });
        ui.add_space(12.0);
        ui.label(
            RichText::new(text("settings_hardware"))
                .font(theme::label_font())
                .color(theme::ACCENT),
        );
        ui.add_space(6.0);
        scene_panel::show_scene_card(ui, &viewport.facts, name);
    });
}
