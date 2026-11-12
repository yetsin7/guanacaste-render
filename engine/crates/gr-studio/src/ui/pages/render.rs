//! Render studio: the final picture. The view traces at the chosen output size until it reaches its samples, shows
//! its progress and saves it as a PNG.

use super::common::{self, floating};
use crate::app::{Page, Studio};
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use crate::viewport::ViewMode;
use eframe::egui::{self, Align, Align2, Layout, Rect, Ui, pos2, vec2};
use eframe::egui_wgpu::RenderState;

/// Output sizes offered; `None` follows the size of the view.
const SIZES: [(&str, Option<(u32, u32)>); 5] = [
    ("Vista", None),
    ("HD 1280×720", Some((1280, 720))),
    ("Full HD 1920×1080", Some((1920, 1080))),
    ("2K 2560×1440", Some((2560, 1440))),
    ("4K 3840×2160", Some((3840, 2160))),
];

pub fn show(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>) {
    let full = ui.max_rect();
    let header = Rect::from_min_size(full.min, vec2(full.width(), 46.0));
    ui.painter().rect_filled(header, 0.0, theme::PANEL);
    ui.painter()
        .hline(full.x_range(), header.bottom(), theme::hairline());
    let mut header_ui = ui.new_child(
        egui::UiBuilder::new()
            .max_rect(header.shrink2(vec2(16.0, 0.0)))
            .layout(Layout::left_to_right(Align::Center)),
    );
    header_bar(&mut header_ui, studio);

    let body = Rect::from_min_max(pos2(full.left(), header.bottom() + 1.0), full.max);
    let Some(state) = state.filter(|_| studio.viewport.has_scene()) else {
        let mut empty = ui.new_child(egui::UiBuilder::new().max_rect(body));
        common::empty_state(&mut empty, studio, body);
        return;
    };
    let panel_width = 400.0f32.min(body.width() * 0.3);
    let view = Rect::from_min_max(body.min, pos2(body.right() - panel_width, body.bottom()));
    let panel = Rect::from_min_max(pos2(view.right(), body.top()), body.max);
    let mut view_ui = ui.new_child(egui::UiBuilder::new().max_rect(view));
    view_ui.set_clip_rect(view);
    studio.viewport.show(&mut view_ui, view, state);
    common::view_shortcuts(ui, studio);
    floating(
        ui,
        "render-status",
        view.min + vec2(10.0, 10.0),
        Align2::LEFT_TOP,
        0.0,
        theme::hud(),
        |ui| {
            widgets::status(
                ui,
                &common::mode_label(studio),
                common::mode_color(studio),
                theme::mono_medium(13.0),
            );
        },
    );
    if studio.viewport.mode() != ViewMode::Render {
        floating(
            ui,
            "render-hint",
            pos2(view.center().x, view.bottom() - 16.0),
            Align2::CENTER_BOTTOM,
            0.0,
            theme::hud(),
            |ui| {
                text(
                    ui,
                    "Encuadra con la navegación y pulsa «Iniciar render». Mover la cámara detiene el render.",
                    theme::mono(12.0),
                    theme::TEXT_VARIANT,
                );
            },
        );
    }

    ui.painter().rect_filled(panel, 0.0, theme::PANEL);
    ui.painter()
        .vline(panel.left(), panel.y_range(), theme::hairline());
    let mut panel_ui = ui.new_child(egui::UiBuilder::new().max_rect(panel.shrink(14.0)));
    egui::ScrollArea::vertical()
        .auto_shrink([false, false])
        .show(&mut panel_ui, |ui| controls(ui, studio));
}

fn header_bar(ui: &mut Ui, studio: &mut Studio) {
    ui.spacing_mut().item_spacing.x = 10.0;
    if widgets::icon_button(ui, icons::CLOSE, 28.0, false, "Volver a Construcción").clicked() {
        studio.page = Page::Build;
    }
    widgets::icon(ui, icons::CAMERA, 20.0, theme::PRIMARY);
    text(
        ui,
        "ESTUDIO DE RENDER",
        theme::mono_medium(16.0),
        theme::PRIMARY,
    );
    text(
        ui,
        format!(
            "ESCENA: {}",
            studio.project_name().unwrap_or_else(|| "ninguna".into())
        ),
        theme::mono(14.0),
        theme::TEXT_VARIANT,
    );
    text(
        ui,
        studio.viewport.facts.adapter.clone(),
        theme::mono(14.0),
        theme::TELEMETRY,
    );
}

fn controls(ui: &mut Ui, studio: &mut Studio) {
    let width = ui.available_width();
    widgets::section_title(ui, Some(icons::IMAGE), "SALIDA", None);
    ui.add_space(6.0);
    let current = SIZES
        .iter()
        .position(|(_, size)| *size == studio.viewport.render_resolution)
        .unwrap_or(0);
    for (index, (label, size)) in SIZES.iter().enumerate() {
        let selected = index == current;
        if widgets::chip(ui, selected.then_some(icons::CHECK), label, None, selected).clicked()
            && !selected
        {
            studio.viewport.render_resolution = *size;
            studio.viewport.restart();
        }
    }
    ui.add_space(10.0);
    widgets::section_title(ui, Some(icons::TUNE), "CALIDAD", None);
    ui.add_space(6.0);
    let settings = &mut studio.viewport.settings;
    let mut samples = settings.render_samples as f32;
    value_row(
        ui,
        "Muestras por píxel",
        &format!("{}", settings.render_samples),
    );
    if widgets::bar_slider(ui, &mut samples, 16.0..=4096.0, width) {
        settings.render_samples = samples.round() as u32;
    }
    let mut bounces = settings.max_bounces as f32;
    value_row(ui, "Rebotes de luz", &settings.max_bounces.to_string());
    if widgets::bar_slider(ui, &mut bounces, 1.0..=12.0, width) {
        settings.max_bounces = bounces.round() as u32;
    }
    ui.horizontal(|ui| {
        text(
            ui,
            "Reducir ruido al inicio",
            theme::body(15.0),
            theme::TEXT,
        );
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            widgets::toggle(ui, &mut settings.denoise);
        });
    });
    text(
        ui,
        "El filtro À-trous suaviza las primeras 48 muestras; después la imagen es el trazado puro.",
        theme::body(12.0),
        theme::OUTLINE,
    );
    ui.add_space(10.0);
    widgets::section_title(ui, Some(icons::SUN), "LUZ", None);
    ui.add_space(6.0);
    value_row(ui, "Exposición", &format!("{:.2}", settings.exposure));
    widgets::bar_slider(ui, &mut settings.exposure, 0.05..=2.0, width);
    value_row(
        ui,
        "Altura del sol",
        &format!("{:.0}°", settings.sun_elevation_degrees),
    );
    widgets::bar_slider(ui, &mut settings.sun_elevation_degrees, 1.0..=90.0, width);
    value_row(
        ui,
        "Orientación del sol",
        &format!("{:.0}°", settings.sun_azimuth_degrees),
    );
    widgets::bar_slider(ui, &mut settings.sun_azimuth_degrees, 0.0..=360.0, width);
    ui.add_space(14.0);

    let rendering = studio.viewport.mode() == ViewMode::Render;
    let samples = studio
        .viewport
        .samples()
        .min(studio.viewport.target_samples());
    let target = studio.viewport.target_samples();
    if rendering {
        let fraction = samples as f32 / target.max(1) as f32;
        let (bar, _) = ui.allocate_exact_size(vec2(width, 6.0), egui::Sense::hover());
        ui.painter().rect_filled(bar, 2.0, theme::HIGHEST);
        ui.painter().rect_filled(
            Rect::from_min_size(bar.min, vec2(width * fraction, 6.0)),
            2.0,
            if studio.viewport.is_finished() {
                theme::TELEMETRY
            } else {
                theme::PRIMARY_CONTAINER
            },
        );
        ui.add_space(4.0);
        let rate = studio.viewport.samples_per_second();
        let remaining = if rate > 0.0 {
            (target - samples) as f32 / rate
        } else {
            0.0
        };
        ui.horizontal(|ui| {
            text(
                ui,
                format!("{samples} / {target} muestras"),
                theme::mono(13.0),
                theme::TEXT,
            );
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                let note = if studio.viewport.is_finished() {
                    "Terminado".to_owned()
                } else {
                    format!("{rate:.1} m/s · quedan {remaining:.0} s")
                };
                text(ui, note, theme::mono(12.0), theme::TELEMETRY);
            });
        });
        ui.add_space(8.0);
    }
    let label = if rendering {
        "Detener render"
    } else {
        "Iniciar render"
    };
    let style = if rendering { Style::Ghost } else { Style::Pale };
    if widgets::wide_button(
        ui,
        style,
        Some(icons::CAMERA),
        label,
        theme::mono_medium(15.0),
        40.0,
    )
    .clicked()
    {
        studio.viewport.set_mode(if rendering {
            ViewMode::Navigate
        } else {
            ViewMode::Render
        });
    }
    ui.add_space(6.0);
    ui.add_enabled_ui(rendering && samples > 0, |ui| {
        if widgets::wide_button(
            ui,
            Style::Ghost,
            Some(icons::SAVE),
            "Guardar imagen PNG",
            theme::mono_medium(14.0),
            36.0,
        )
        .clicked()
        {
            studio.save_image_dialog();
        }
    });
    ui.add_space(6.0);
    if widgets::button(
        ui,
        Style::Quiet,
        Some(icons::VISIBILITY),
        "Vista previa rápida",
        theme::mono(13.0),
        30.0,
    )
    .clicked()
    {
        studio.viewport.set_mode(ViewMode::Preview);
    }
    studio.save_preferences_if_changed();
}

fn value_row(ui: &mut Ui, label: &str, value: &str) {
    ui.horizontal(|ui| {
        text(
            ui,
            label.to_uppercase(),
            theme::mono_medium(12.0),
            theme::TEXT_VARIANT,
        );
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            text(ui, value, theme::mono_medium(13.0), theme::TEXT)
        });
    });
}
