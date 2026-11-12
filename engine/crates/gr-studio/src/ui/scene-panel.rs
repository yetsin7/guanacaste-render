//! Floating scene telemetry card overlaid directly on the viewport.

use crate::localization::text;
use crate::theme;
use crate::ui::widgets::property_row::property_row;
use crate::ui::widgets::section_header::section_divider;
use crate::viewport::SceneFacts;
use eframe::egui::{self, CornerRadius, RichText, Ui};

/// Renders the floating scene card with geometry and hardware telemetry.
pub fn show_scene_card(ui: &mut Ui, facts: &SceneFacts, project_name: Option<&str>) {
    let frame = egui::Frame::new()
        .fill(theme::PANEL.gamma_multiply(0.92))
        .stroke(theme::hairline())
        .corner_radius(CornerRadius::same(4))
        .inner_margin(egui::Margin::same(10));

    frame.show(ui, |ui| {
        ui.set_width(230.0);

        ui.horizontal(|ui| {
            ui.label(RichText::new(text("scene")).font(theme::label_font()).color(theme::ACCENT).strong());
        });

        section_divider(ui);
        ui.add_space(2.0);

        let name = project_name.unwrap_or(text("no_project"));
        ui.label(RichText::new(name).font(theme::body_font()).strong().color(theme::TEXT));

        ui.add_space(4.0);
        property_row(ui, text("triangles"), &group_thousands(facts.triangles));
        property_row(ui, text("materials"), &facts.materials.to_string());
        property_row(ui, text("estimated_memory"), &format!("{:.1} MB", facts.scene_megabytes));

        ui.add_space(4.0);
        section_divider(ui);
        ui.add_space(2.0);

        ui.label(RichText::new(&facts.adapter).font(theme::label_font()).color(theme::MUTED));
    });
}

/// Groups large mesh counts for compact, readable telemetry.
fn group_thousands(value: usize) -> String {
    let digits = value.to_string();
    let mut grouped = String::with_capacity(digits.len() + digits.len() / 3);
    for (index, digit) in digits.chars().enumerate() {
        if index > 0 && (digits.len() - index).is_multiple_of(3) {
            grouped.push(' ');
        }
        grouped.push(digit);
    }
    grouped
}
