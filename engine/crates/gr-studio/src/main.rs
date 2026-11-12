//! Guanacaste Render Studio. `GuanacasteStudio.exe [project.gr | scene.grscene]`.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod app;
mod localization;
mod orbit_camera;
mod project;
mod theme;
mod ui;
mod viewport;

use eframe::egui;
use eframe::egui_wgpu::WgpuSetup;
use gr_gpu::wgpu;
use std::sync::Arc;

/// Starts the native workstation with the most capable available graphics adapter.
fn main() -> eframe::Result {
    let open = std::env::args_os().nth(1).map(std::path::PathBuf::from);
    let mut options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title("Guanacaste Render Studio")
            .with_inner_size([1280.0, 720.0])
            .with_min_inner_size([900.0, 560.0])
            .with_maximized(true),
        ..Default::default()
    };
    if let WgpuSetup::CreateNew(setup) = &mut options.wgpu_options.wgpu_setup {
        // The dedicated card when there is one, and every limit it offers: large scenes need large buffers.
        setup.power_preference = wgpu::PowerPreference::HighPerformance;
        setup.device_descriptor = Arc::new(|adapter| wgpu::DeviceDescriptor {
            label: Some("guanacaste studio"),
            required_limits: adapter.limits(),
            ..Default::default()
        });
    }
    eframe::run_native(
        "Guanacaste Render Studio",
        options,
        Box::new(move |creation| Ok(Box::new(app::StudioApp::new(creation, open)))),
    )
}
