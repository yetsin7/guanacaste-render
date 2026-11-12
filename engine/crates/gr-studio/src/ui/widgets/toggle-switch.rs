//! CAD-style compact rectangular toggle switch (24x12px).

use crate::theme;
use eframe::egui::{self, CornerRadius, Pos2, Rect, Response, Sense, StrokeKind, Vec2};

/// Renders a rectangular micro-switch matching industrial control standards.
#[allow(dead_code)]
pub fn toggle_switch(ui: &mut egui::Ui, value: &mut bool) -> Response {
    let desired_size = Vec2::new(24.0, 12.0);
    let (rect, mut response) = ui.allocate_exact_size(desired_size, Sense::click());

    if response.clicked() {
        *value = !*value;
        response.mark_changed();
    }

    if ui.is_rect_visible(rect) {
        let how_on = ui.ctx().animate_bool(response.id, *value);
        let painter = ui.painter();

        let track_fill = if *value { theme::ACCENT_PRESS } else { theme::RAISED };

        painter.rect(rect, CornerRadius::same(2), track_fill, theme::hairline(), StrokeKind::Inside);

        let thumb_size = Vec2::new(10.0, 8.0);
        let thumb_x = egui::lerp((rect.min.x + 2.0)..=(rect.max.x - thumb_size.x - 2.0), how_on);
        let thumb_rect = Rect::from_min_size(Pos2::new(thumb_x, rect.min.y + 2.0), thumb_size);

        let thumb_color = if *value { theme::TEXT } else { theme::MUTED };

        painter.rect_filled(thumb_rect, CornerRadius::same(1), thumb_color);
    }

    response
}
