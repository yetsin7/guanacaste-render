//! Status and telemetry capsule badges (pill styling).

use crate::theme;
use eframe::egui::{self, CornerRadius, RichText, Ui};

/// Stylistic variants for badges indicating engine state and parameters.
#[allow(dead_code)]
#[derive(Clone, Copy, PartialEq, Eq)]
pub enum BadgeVariant {
    Primary,
    Secondary,
    Tertiary,
    Error,
    Neutral,
}

/// Renders a capsule pill badge with technical uppercase typography.
pub fn mode_badge(ui: &mut Ui, label: &str, variant: BadgeVariant) {
    let (bg_color, text_color, border_stroke) = match variant {
        BadgeVariant::Primary => (theme::RAISED, theme::ACCENT, egui::Stroke::new(1.0, theme::ACCENT)),
        BadgeVariant::Secondary => (theme::ACCENT_PRESS, theme::TEXT, egui::Stroke::NONE),
        BadgeVariant::Tertiary => (theme::DEEPEST, theme::TEAL, egui::Stroke::new(1.0, theme::TEAL.gamma_multiply(0.5))),
        BadgeVariant::Error => (theme::RAISED, theme::ERROR, egui::Stroke::new(1.0, theme::ERROR)),
        BadgeVariant::Neutral => (theme::DEEPEST, theme::MUTED, theme::hairline()),
    };

    let text = RichText::new(label.to_uppercase()).font(theme::label_font()).color(text_color);

    let frame = egui::Frame::new()
        .fill(bg_color)
        .stroke(border_stroke)
        .corner_radius(CornerRadius::same(255))
        .inner_margin(egui::Margin::symmetric(6, 2));

    frame.show(ui, |ui| {
        ui.label(text);
    });
}
