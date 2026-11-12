//! Viewport telemetry HUD overlay with real-time rendering statistics.

use crate::localization::text;
use crate::theme;
use eframe::egui::{self, CornerRadius, Rect, RichText, Vec2};

/// Snapshot of live rendering performance indicators.
#[derive(Clone, Debug)]
pub struct HudState {
    pub samples: u32,
    pub target_samples: u32,
    pub width: u32,
    pub height: u32,
    pub samples_per_second: f32,
    pub is_rendering: bool,
}

/// Renders the performance telemetry capsule floating over the 3D viewport.
pub fn show(ctx: &egui::Context, state: &HudState) {
    egui::Area::new(egui::Id::new("viewport_hud_telemetry"))
        .anchor(egui::Align2::LEFT_BOTTOM, Vec2::new(58.0, -14.0))
        .order(egui::Order::Foreground)
        .show(ctx, |ui| {
            let frame = egui::Frame::new()
                .fill(theme::PANEL.gamma_multiply(0.85))
                .stroke(theme::hairline())
                .corner_radius(CornerRadius::same(4))
                .inner_margin(egui::Margin::symmetric(10, 6));

            frame.show(ui, |ui| {
                ui.horizontal(|ui| {
                    // Resolution badge
                    ui.label(
                        RichText::new(format!("{}×{}", state.width, state.height))
                            .font(theme::mono_font())
                            .color(theme::MUTED),
                    );

                    ui.label(RichText::new("│").color(theme::BORDER));

                    // Samples progress
                    let spp_text = format!("SPP: {}/{}", state.samples.min(state.target_samples), state.target_samples);
                    let spp_color = if state.is_rendering { theme::TEAL } else { theme::TEXT };

                    ui.label(RichText::new(spp_text).font(theme::mono_font()).color(spp_color));

                    // Rate
                    if state.samples_per_second > 0.0 {
                        ui.label(RichText::new("│").color(theme::BORDER));
                        ui.label(
                            RichText::new(format!("{:.0} spp/s", state.samples_per_second))
                                .font(theme::mono_font())
                                .color(theme::TEAL),
                        );
                    } else if state.samples >= state.target_samples && state.target_samples > 0 {
                        ui.label(RichText::new("│").color(theme::BORDER));
                        ui.label(RichText::new(text("complete")).font(theme::label_font()).color(theme::TEAL));
                    }

                    // Progress micro-bar
                    if state.target_samples > 0 {
                        ui.add_space(4.0);
                        let progress = (state.samples as f32 / state.target_samples as f32).clamp(0.0, 1.0);
                        let bar_size = Vec2::new(60.0, 4.0);
                        let (rect, _) = ui.allocate_exact_size(bar_size, egui::Sense::hover());
                        if ui.is_rect_visible(rect) {
                            let painter = ui.painter();
                            painter.rect_filled(rect, CornerRadius::same(1), theme::DEEPEST);
                            let fill_rect = Rect::from_min_size(rect.min, Vec2::new(rect.width() * progress, rect.height()));
                            painter.rect_filled(fill_rect, CornerRadius::same(1), theme::ACCENT_PRESS);
                        }
                    }
                });
            });
        });
}
