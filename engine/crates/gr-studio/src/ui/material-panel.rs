//! Read-only inspection of the scene's actual surface materials.

use crate::localization::text;
use crate::theme;
use crate::ui::widgets::property_row::property_row;
use eframe::egui::{self, Color32, RichText, Ui, Vec2};
use gr_core::scene_file::Material;

/// Shows the properties carried by the selected scene material without implying edits can be saved.
pub fn show(ui: &mut Ui, materials: &[Material], selected: &mut usize) {
    ui.add_space(6.0);
    ui.label(
        RichText::new(text("material_title"))
            .font(theme::label_font())
            .color(theme::ACCENT)
            .strong(),
    );
    ui.add_space(8.0);
    if materials.is_empty() {
        ui.label(text("material_empty"));
        return;
    }
    *selected = (*selected).min(materials.len() - 1);
    let material_label = |index: usize| text("material_name").replace("{number}", &(index + 1).to_string());
    egui::ComboBox::from_id_salt("material-selection")
        .selected_text(material_label(*selected))
        .width(ui.available_width())
        .show_ui(ui, |ui| {
            for index in 0..materials.len() {
                ui.selectable_value(selected, index, material_label(index));
            }
        });
    ui.add_space(12.0);
    let material = &materials[*selected];
    let color = material.base_color.clamp(glam::Vec3::ZERO, glam::Vec3::ONE);
    let swatch = Color32::from_rgb((color.x * 255.0) as u8, (color.y * 255.0) as u8, (color.z * 255.0) as u8);
    let (rect, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), 64.0), egui::Sense::hover());
    ui.painter().rect_filled(rect, 2.0, swatch);
    ui.add_space(8.0);
    property_row(
        ui,
        text("material_color"),
        &format!("{:.2} / {:.2} / {:.2}", color.x, color.y, color.z),
    );
    property_row(ui, text("material_roughness"), &format!("{:.2}", material.roughness));
    property_row(ui, text("material_metallic"), &format!("{:.2}", material.metallic));
    property_row(ui, text("material_transmission"), &format!("{:.2}", material.transmission));
    property_row(ui, text("material_ior"), &format!("{:.2}", material.ior));
    property_row(
        ui,
        text("material_emission"),
        &format!(
            "{:.2} / {:.2} / {:.2}",
            material.emission.x, material.emission.y, material.emission.z
        ),
    );
    ui.add_space(12.0);
    ui.label(
        RichText::new(text("material_read_only"))
            .font(theme::body_font())
            .color(theme::MUTED),
    );
}
