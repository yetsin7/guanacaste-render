//! Look of the studio: a dark, low-contrast frame so the picture is the brightest thing on screen.

use eframe::egui::{self, Color32};

pub const BACKGROUND: Color32 = Color32::from_rgb(18, 19, 22);
pub const PANEL: Color32 = Color32::from_rgb(27, 29, 33);
pub const RAISED: Color32 = Color32::from_rgb(40, 43, 49);
pub const ACCENT: Color32 = Color32::from_rgb(64, 156, 255);
pub const TEXT: Color32 = Color32::from_rgb(222, 225, 230);
pub const MUTED: Color32 = Color32::from_rgb(140, 146, 156);
pub const WARNING: Color32 = Color32::from_rgb(240, 170, 70);

pub fn apply(context: &egui::Context) {
    context.set_theme(egui::Theme::Dark);
    let mut visuals = egui::Visuals::dark();
    visuals.panel_fill = PANEL;
    visuals.window_fill = PANEL;
    visuals.extreme_bg_color = BACKGROUND;
    visuals.faint_bg_color = RAISED;
    visuals.override_text_color = Some(TEXT);
    visuals.selection.bg_fill = ACCENT.gamma_multiply(0.45);
    visuals.widgets.noninteractive.bg_fill = PANEL;
    visuals.widgets.inactive.bg_fill = RAISED;
    visuals.widgets.inactive.weak_bg_fill = RAISED;
    visuals.widgets.hovered.bg_fill = Color32::from_rgb(54, 58, 66);
    visuals.widgets.hovered.weak_bg_fill = Color32::from_rgb(54, 58, 66);
    visuals.widgets.active.bg_fill = ACCENT.gamma_multiply(0.7);
    visuals.widgets.active.weak_bg_fill = ACCENT.gamma_multiply(0.7);
    visuals.slider_trailing_fill = true;
    context.set_visuals(visuals);

    context.all_styles_mut(|style| {
        style.spacing.item_spacing = egui::vec2(8.0, 7.0);
        style.spacing.button_padding = egui::vec2(10.0, 5.0);
        style.spacing.slider_width = 118.0;
    });
}
