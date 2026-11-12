//! Environment, sky, daylight, and camera exposure inspector panel.

use crate::localization::text;
use crate::ui::widgets::property_row::property_slider;
use crate::ui::widgets::section_header::{section_divider, section_header};
use crate::viewport::ViewSettings;
use eframe::egui::Ui;

/// Renders the daylight and environment configuration decks.
/// Returns true if parameters impacting sunlight or bounces were modified.
pub fn show(ui: &mut Ui, settings: &mut ViewSettings) -> bool {
    let mut daylight_changed = false;

    ui.vertical(|ui| {
        ui.add_space(6.0);

        // Section 1: Daylight & Atmospheric Solar position
        let mut open_sun = true;
        section_header(ui, "sun_section", text("daylight"), &mut open_sun);
        if open_sun {
            ui.add_space(4.0);
            if property_slider(ui, text("azimuth"), &mut settings.sun_azimuth_degrees, 0.0..=360.0, "°") {
                daylight_changed = true;
            }

            if property_slider(ui, text("elevation"), &mut settings.sun_elevation_degrees, -90.0..=90.0, "°") {
                daylight_changed = true;
            }

            if property_slider(ui, text("sun_intensity"), &mut settings.sun_intensity, 0.0..=8.0, "x") {
                daylight_changed = true;
            }

            if property_slider(ui, text("sky_intensity"), &mut settings.sky_intensity, 0.0..=5.0, "x") {
                daylight_changed = true;
            }
        }

        ui.add_space(10.0);
        section_divider(ui);
        ui.add_space(6.0);

        // Section 2: Sensor Exposure & Optical Calibration
        let mut open_camera = true;
        section_header(ui, "cam_section", text("camera_exposure"), &mut open_camera);
        if open_camera {
            ui.add_space(4.0);
            property_slider(ui, text("exposure"), &mut settings.exposure, 0.05..=5.0, "");
        }
    });

    daylight_changed
}
