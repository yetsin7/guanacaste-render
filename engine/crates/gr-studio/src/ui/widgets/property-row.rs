//! CAD property rows with aligned labels and precision parameter controls.

use crate::theme;
use eframe::egui::{Align, Layout, RichText, Slider, Ui};
use std::ops::RangeInclusive;

/// Renders a key-value fact row with a technical label and right-aligned value.
pub fn property_row(ui: &mut Ui, label: &str, value: &str) {
    ui.horizontal(|ui| {
        ui.label(RichText::new(label).font(theme::label_font()).color(theme::MUTED));
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            ui.label(RichText::new(value).font(theme::mono_font()).color(theme::TEXT));
        });
    });
}

/// Renders a labeled precision float slider with unit suffix.
pub fn property_slider(ui: &mut Ui, label: &str, value: &mut f32, range: RangeInclusive<f32>, suffix: &str) -> bool {
    let mut changed = false;
    ui.horizontal(|ui| {
        ui.label(RichText::new(label).font(theme::label_font()).color(theme::MUTED));
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            let mut slider = Slider::new(value, range).show_value(true).fixed_decimals(1);
            if !suffix.is_empty() {
                slider = slider.suffix(suffix);
            }
            if ui.add(slider).changed() {
                changed = true;
            }
        });
    });
    changed
}

/// Renders an integer parameter slider.
pub fn property_int_slider(ui: &mut Ui, label: &str, value: &mut u32, range: RangeInclusive<u32>, suffix: &str) -> bool {
    let mut changed = false;
    ui.horizontal(|ui| {
        ui.label(RichText::new(label).font(theme::label_font()).color(theme::MUTED));
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            let mut slider = Slider::new(value, range).show_value(true);
            if !suffix.is_empty() {
                slider = slider.suffix(suffix);
            }
            if ui.add(slider).changed() {
                changed = true;
            }
        });
    });
    changed
}
