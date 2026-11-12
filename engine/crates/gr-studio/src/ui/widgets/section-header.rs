//! Collapsible technical section header and hairline dividers.

use crate::theme;
use eframe::egui::{self, CornerRadius, Pos2, Rect, Sense, Vec2};

/// Renders a precision CAD section header with an accent indicator and toggle chevron.
pub fn section_header(ui: &mut egui::Ui, id_salt: &str, label: &str, is_open: &mut bool) -> bool {
    let id = ui.make_persistent_id(id_salt);
    *is_open = ui.ctx().data_mut(|memory| memory.get_temp(id).unwrap_or(*is_open));
    let desired_size = Vec2::new(ui.available_width(), 22.0);
    let (rect, response) = ui.allocate_exact_size(desired_size, Sense::click());

    if response.clicked() {
        *is_open = !*is_open;
        ui.ctx().data_mut(|memory| memory.insert_temp(id, *is_open));
    }

    if ui.is_rect_visible(rect) {
        let painter = ui.painter();
        let bg_color = if response.hovered() { theme::CARD_HIGH } else { theme::DEEPEST };

        painter.rect(rect, CornerRadius::same(2), bg_color, theme::hairline(), egui::StrokeKind::Inside);

        if *is_open {
            let strip = Rect::from_min_size(rect.min, Vec2::new(3.0, rect.height()));
            painter.rect_filled(strip, CornerRadius::same(1), theme::ACCENT);
        }

        let chevron = if *is_open { "▼" } else { "▶" };
        let text_pos = Pos2::new(rect.min.x + 10.0, rect.min.y + 3.0);
        painter.text(
            text_pos,
            egui::Align2::LEFT_TOP,
            format!("{} {}", chevron, label.to_uppercase()),
            theme::label_font(),
            if *is_open { theme::TEXT } else { theme::MUTED },
        );
    }

    *is_open
}

/// Draws an ultra-thin 1px horizontal containment hairline.
pub fn section_divider(ui: &mut egui::Ui) {
    let width = ui.available_width();
    let (rect, _) = ui.allocate_exact_size(Vec2::new(width, 1.0), Sense::hover());
    if ui.is_rect_visible(rect) {
        ui.painter()
            .line_segment([rect.left_center(), rect.right_center()], theme::hairline());
    }
}
