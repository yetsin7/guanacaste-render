//! Render quality presets, bounce limits, and hardware telemetry panel.

use crate::localization::text;
use crate::ui::widgets::property_row::property_int_slider;
use crate::ui::widgets::property_row::property_slider;
use crate::ui::widgets::section_header::{section_divider, section_header};
use crate::viewport::Viewport;
use eframe::egui::Ui;
use gr_core::hardware::QualityTier;

/// Quality tiers and the localization keys for their labels and descriptions.
pub const TIERS: [(QualityTier, &str, &str); 4] = [
    (QualityTier::Economico, "tier_low", "tier_low_hint"),
    (QualityTier::Equilibrado, "tier_balanced", "tier_balanced_hint"),
    (QualityTier::Alto, "tier_high", "tier_high_hint"),
    (QualityTier::Ultra, "tier_ultra", "tier_ultra_hint"),
];

/// Renders the render quality configuration panel.
pub fn show(ui: &mut Ui, current_tier: &mut QualityTier, suggested: QualityTier, viewport: &mut Viewport) {
    let mut field_of_view = viewport.field_of_view();
    if property_slider(ui, text("camera_fov"), &mut field_of_view, 20.0..=100.0, "°") {
        viewport.set_field_of_view(field_of_view);
    }
    if ui.button(text("camera_home")).clicked() {
        viewport.go_home();
    }
    ui.add_space(8.0);
    let mut open_quality = true;
    section_header(ui, "quality", text("quality_title"), &mut open_quality);
    if open_quality {
        ui.add_space(4.0);
        for (tier, name, hint) in TIERS {
            let is_suggested = tier == suggested;
            let label = if is_suggested {
                text("suggested").replace("{name}", text(name))
            } else {
                text(name).to_string()
            };

            if ui.radio(*current_tier == tier, label).on_hover_text(text(hint)).clicked() && *current_tier != tier {
                *current_tier = tier;
                viewport.settings.max_bounces = tier.budget().max_bounces;
                viewport.restart();
            }
        }

        ui.add_space(8.0);
        section_divider(ui);
        ui.add_space(6.0);

        if property_int_slider(ui, text("bounces"), &mut viewport.settings.max_bounces, 0..=8, "") {
            viewport.restart();
        }

        property_int_slider(ui, text("samples"), &mut viewport.settings.target_samples, 16..=4096, "");
    }
}
