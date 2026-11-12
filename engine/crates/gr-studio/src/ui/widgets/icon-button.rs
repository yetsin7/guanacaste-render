//! Precision workstation icon button matching the CAD tool ribbon specification.

use crate::theme;
use eframe::egui::{self, CornerRadius, Rect, Response, Sense, Vec2};

/// Renders a tool icon button with tooltip support and smooth hover feedback.
#[allow(dead_code)]
pub fn icon_button(ui: &mut egui::Ui, icon: &str, tooltip: &str) -> Response {
    icon_button_active(ui, icon, tooltip, false)
}

/// Renders an icon button with active/selected telemetry highlight.
pub fn icon_button_active(ui: &mut egui::Ui, icon: &str, tooltip: &str, is_active: bool) -> Response {
    let size = Vec2::new(32.0, 32.0);
    let (rect, response) = ui.allocate_exact_size(size, Sense::click());

    if ui.is_rect_visible(rect) {
        let painter = ui.painter();
        let bg_fill = if is_active {
            theme::ACCENT_PRESS.gamma_multiply(0.20)
        } else if response.hovered() {
            theme::CARD_HIGH
        } else {
            egui::Color32::TRANSPARENT
        };

        painter.rect_filled(rect, CornerRadius::same(2), bg_fill);

        if is_active {
            let left_border = Rect::from_min_size(rect.min, Vec2::new(2.5, rect.height()));
            painter.rect_filled(left_border, CornerRadius::same(1), theme::ACCENT);
        } else if response.hovered() {
            painter.rect_stroke(rect, CornerRadius::same(2), theme::hairline(), egui::StrokeKind::Inside);
        }

        let text_color = if is_active {
            theme::ACCENT
        } else if response.hovered() {
            theme::TEXT
        } else {
            theme::MUTED
        };

        painter.text(rect.center(), egui::Align2::CENTER_CENTER, icon, theme::icon_font(), text_color);
    }

    if !tooltip.is_empty() {
        response.on_hover_text(tooltip)
    } else {
        response
    }
}
