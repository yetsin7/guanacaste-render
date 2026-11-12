//! Build: the scene at full size with the placement workflow over it. Vegetation from the library is placed with a
//! click; the view navigates without ray tracing until the preview or the render is asked for.

use super::common::{self, compact, floating};
use crate::app::{Page, Studio, Tool};
use crate::studio::vegetation::CATALOG;
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use crate::viewport::ViewMode;
use eframe::egui::{self, Align, Align2, Layout, Rect, Ui, pos2, vec2};
use eframe::egui_wgpu::RenderState;

/// The four steps of the placement workflow the step card walks through.
const STEPS: [(&str, &str); 4] = [
    (
        "Elegir especie",
        "Elige en la biblioteca o con «Siguiente especie» la planta que vas a colocar. Su tamaño se ajusta en el panel de propiedades.",
    ),
    (
        "Poblar el terreno",
        "Con la herramienta Colocar activa, haz clic sobre el terreno o una superficie del proyecto para plantar un ejemplar en ese punto.",
    ),
    (
        "Ajustar la luz",
        "Orienta el sol en la pestaña Clima. La vista de navegación muestra la luz directa al instante, sin trazado de rayos.",
    ),
    (
        "Revisar el resultado",
        "Pulsa Vista previa para una pasada ligera de trazado de rayos, y Render cuando el encuadre esté listo.",
    ),
];

pub struct BuildUi {
    pub step: usize,
    pub weather: bool,
    pub show_marker: bool,
    pub hide_panels: bool,
}

impl Default for BuildUi {
    fn default() -> BuildUi {
        BuildUi {
            step: 1,
            weather: false,
            show_marker: true,
            hide_panels: false,
        }
    }
}

pub fn show(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>, local: &mut BuildUi) {
    let rect = ui.max_rect();
    let Some(state) = state.filter(|_| studio.viewport.has_scene()) else {
        common::empty_state(ui, studio, rect);
        return;
    };
    studio.library.ensure_thumbnails(state);
    let response = studio.viewport.show(ui, rect, state);
    common::view_shortcuts(ui, studio);
    handle_tools(ui, studio, state, rect, &response);
    draw_marker(ui, studio, rect, &response, local);

    if local.hide_panels {
        floating(
            ui,
            "build-restore",
            pos2(rect.right() - 16.0, rect.top() + 16.0),
            Align2::RIGHT_TOP,
            40.0,
            theme::hud(),
            |ui| {
                if widgets::icon_button(ui, icons::FULLSCREEN, 28.0, true, "Mostrar paneles")
                    .clicked()
                {
                    local.hide_panels = false;
                }
            },
        );
        return;
    }
    title_pill(ui, studio, rect);
    view_toolbar(ui, studio, rect, local);
    step_card(ui, studio, rect, local);
    if local.weather {
        weather_panel(ui, studio, rect);
    } else {
        properties_panel(ui, studio, rect, local);
    }
    bottom_bar(ui, studio, rect, local);
}

/// Clicks in the view, by tool: place a plant, select or delete a placed one.
fn handle_tools(
    ui: &Ui,
    studio: &mut Studio,
    state: &RenderState,
    rect: Rect,
    response: &egui::Response,
) {
    if !ui.ctx().egui_wants_keyboard_input() {
        ui.input(|input| {
            if input.key_pressed(egui::Key::P) {
                studio.build.tool = Tool::Place;
            }
            if input.key_pressed(egui::Key::Escape) {
                studio.build.tool = Tool::Select;
            }
        });
        if ui.input(|input| input.key_pressed(egui::Key::Delete))
            && let Some(selected) = studio.build.selected
        {
            studio.delete_placed(state, selected);
        }
    }
    if !response.clicked() || studio.viewport.is_updating() {
        return;
    }
    let Some(position) = response.interact_pointer_pos() else {
        return;
    };
    let Some(point) = studio.viewport.pick(rect, position) else {
        studio.message = Some((
            "Haz clic sobre el terreno o una superficie del proyecto, no sobre el cielo.".into(),
            true,
        ));
        return;
    };
    studio.build.cursor = Some(point);
    match studio.build.tool {
        Tool::Place => studio.place_plant(state, point),
        Tool::Select => studio.build.selected = studio.placed_near(point),
        Tool::Delete => match studio.placed_near(point) {
            Some(index) => studio.delete_placed(state, index),
            None => {
                studio.message = Some((
                    "Ahí no hay ninguna planta colocada en esta sesión.".into(),
                    true,
                ))
            }
        },
    }
}

/// The placement reticle under the pointer, with the species and the snap it will use.
fn draw_marker(ui: &Ui, studio: &Studio, rect: Rect, response: &egui::Response, local: &BuildUi) {
    if !local.show_marker || studio.build.tool != Tool::Place {
        return;
    }
    let Some(pointer) = response.hover_pos() else {
        return;
    };
    let painter = ui.painter_at(rect);
    let box_rect = Rect::from_center_size(pointer, vec2(48.0, 48.0));
    painter.rect_stroke(
        box_rect,
        8.0,
        egui::Stroke::new(1.0, egui::Color32::WHITE.gamma_multiply(0.35)),
        egui::StrokeKind::Inside,
    );
    painter.rect_stroke(
        Rect::from_center_size(pointer, vec2(32.0, 32.0)),
        6.0,
        egui::Stroke::new(1.5, egui::Color32::WHITE),
        egui::StrokeKind::Inside,
    );
    painter.text(
        pointer,
        Align2::CENTER_CENTER,
        icons::ADD,
        theme::icon(18.0),
        egui::Color32::WHITE,
    );
    for (from, to) in [
        ((0.0, -36.0), (0.0, -30.0)),
        ((0.0, 30.0), (0.0, 36.0)),
        ((-36.0, 0.0), (-30.0, 0.0)),
        ((30.0, 0.0), (36.0, 0.0)),
    ] {
        painter.line_segment(
            [pointer + vec2(from.0, from.1), pointer + vec2(to.0, to.1)],
            egui::Stroke::new(1.5, egui::Color32::WHITE),
        );
    }
    let label = format!(
        "{}  |  Ajuste: superficie",
        CATALOG[studio.build.species].name
    );
    let galley = painter.layout_no_wrap(label.clone(), theme::mono(12.0), theme::TEXT);
    let tag = Rect::from_min_size(
        pointer + vec2(-galley.size().x / 2.0 - 14.0, 44.0),
        vec2(galley.size().x + 28.0, 22.0),
    );
    painter.rect_filled(tag, 2.0, theme::PANEL.gamma_multiply(0.9));
    painter.circle_filled(
        pos2(tag.left() + 8.0, tag.center().y),
        3.0,
        theme::TELEMETRY,
    );
    painter.text(
        pos2(tag.left() + 16.0, tag.center().y),
        Align2::LEFT_CENTER,
        label,
        theme::mono(12.0),
        theme::TEXT,
    );
}

fn title_pill(ui: &Ui, studio: &mut Studio, rect: Rect) {
    floating(
        ui,
        "build-title",
        pos2(rect.left() + 16.0, rect.top() + 16.0),
        Align2::LEFT_TOP,
        0.0,
        theme::hud().inner_margin(egui::Margin::symmetric(12, 8)),
        |ui| {
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 10.0;
                if widgets::icon_button(ui, icons::CLOSE, 24.0, false, "Volver a Proyectos")
                    .clicked()
                {
                    studio.page = Page::Projects;
                }
                widgets::icon(ui, icons::BUILD, 18.0, theme::TEXT);
                text(ui, "Construcción", theme::heading(19.0), theme::TEXT);
                text(
                    ui,
                    studio.project_name().unwrap_or_default(),
                    theme::mono(13.0),
                    theme::TEXT_VARIANT,
                );
                let pending = studio.unsaved_changes();
                let savable = studio.can_save_project();
                let label = if pending > 0 { format!("Guardar ({pending})") } else { "Guardado".to_owned() };
                let style = if pending > 0 { Style::Primary } else { Style::Ghost };
                let response = ui
                    .add_enabled_ui(savable && pending > 0, |ui| {
                        widgets::button(ui, style, Some(icons::SAVE), &label, theme::mono_medium(13.0), 28.0)
                    })
                    .inner;
                let response = if savable {
                    response.on_hover_text("Escribe las plantas en el proyecto (Ctrl+S); la versión anterior queda como .bak")
                } else {
                    response.on_disabled_hover_text("Las escenas .grscene son de solo lectura: abre un proyecto .gr o .g3d para guardar la vegetación")
                };
                if response.clicked() {
                    studio.save_project();
                }
                if studio.viewport.is_updating() {
                    ui.add(egui::Spinner::new().size(14.0).color(theme::PRIMARY));
                    text(
                        ui,
                        "Actualizando escena…",
                        theme::mono(11.0),
                        theme::PRIMARY,
                    );
                }
            });
        },
    );
}

/// Top right: the preview and render buttons, saving the picture and hiding the panels.
fn view_toolbar(ui: &Ui, studio: &mut Studio, rect: Rect, local: &mut BuildUi) {
    floating(
        ui,
        "build-toolbar",
        pos2(rect.right() - 16.0, rect.top() + 16.0),
        Align2::RIGHT_TOP,
        0.0,
        theme::hud().inner_margin(egui::Margin::symmetric(8, 6)),
        |ui| {
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 6.0;
                widgets::status(
                    ui,
                    &common::mode_label(studio),
                    common::mode_color(studio),
                    theme::mono(11.0),
                );
                ui.add_space(4.0);
                common::trace_buttons(ui, studio);
                if widgets::icon_button(
                    ui,
                    icons::IMAGE,
                    30.0,
                    false,
                    "Guardar imagen (Ctrl+Mayús+S)",
                )
                .clicked()
                {
                    studio.save_image_dialog();
                }
                if widgets::icon_button(
                    ui,
                    icons::TARGET,
                    30.0,
                    false,
                    "Volver a la vista inicial (H)",
                )
                .clicked()
                {
                    studio.viewport.go_home();
                }
                if widgets::icon_button(ui, icons::FULLSCREEN, 30.0, false, "Ocultar paneles")
                    .clicked()
                {
                    local.hide_panels = true;
                }
            });
        },
    );
}

fn step_card(ui: &Ui, studio: &mut Studio, rect: Rect, local: &mut BuildUi) {
    let species = &CATALOG[studio.build.species];
    let fps = studio.telemetry.fps_label();
    floating(
        ui,
        "build-steps",
        pos2(rect.left() + 16.0, rect.top() + 72.0),
        Align2::LEFT_TOP,
        452.0,
        theme::floating(),
        |ui| {
            ui.horizontal(|ui| {
                widgets::tag(
                    ui,
                    &format!("Paso {} de 4", local.step + 1),
                    theme::HIGHEST,
                    theme::PRIMARY,
                );
                text(
                    ui,
                    "Modo: colocación",
                    theme::mono(12.0),
                    theme::TEXT_VARIANT,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    egui::Frame::new()
                        .fill(theme::RECESSED)
                        .corner_radius(2)
                        .inner_margin(egui::Margin::symmetric(8, 3))
                        .show(ui, |ui| {
                            widgets::status(ui, &fps, theme::TELEMETRY, theme::mono(12.0));
                        });
                });
            });
            ui.add_space(6.0);
            let (title, description) = STEPS[local.step];
            text(ui, title, theme::heading(19.0), theme::TEXT);
            ui.add(
                egui::Label::new(
                    egui::RichText::new(description)
                        .font(theme::body(14.0))
                        .color(theme::TEXT_VARIANT),
                )
                .wrap(),
            );
            ui.add_space(8.0);
            egui::Frame::new()
                .fill(theme::CONTAINER)
                .corner_radius(2)
                .inner_margin(egui::Margin::same(10))
                .show(ui, |ui| {
                    ui.set_width(ui.available_width());
                    ui.horizontal(|ui| {
                        match studio.library.thumbnails.get(&studio.build.species) {
                            Some(thumbnail) => {
                                ui.add(
                                    egui::Image::new(thumbnail)
                                        .fit_to_exact_size(vec2(80.0, 80.0))
                                        .corner_radius(2),
                                );
                            }
                            None => {
                                let (thumb, _) =
                                    ui.allocate_exact_size(vec2(80.0, 80.0), egui::Sense::hover());
                                ui.painter().rect_filled(thumb, 2.0, theme::RECESSED);
                                ui.painter().text(
                                    thumb.center(),
                                    Align2::CENTER_CENTER,
                                    icons::NATURE,
                                    theme::icon(28.0),
                                    theme::OUTLINE,
                                );
                            }
                        }
                        ui.vertical(|ui| {
                            ui.horizontal(|ui| {
                                text(ui, species.name, theme::mono_medium(14.0), theme::TEXT);
                                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                    let tool = if studio.build.tool == Tool::Place {
                                        "Activo"
                                    } else {
                                        "En pausa"
                                    };
                                    text(ui, tool, theme::mono(12.0), theme::PRIMARY);
                                });
                            });
                            let triangles = studio
                                .library
                                .facts
                                .get(&studio.build.species)
                                .map(|facts| compact(facts.triangles));
                            egui::Grid::new("specimen-facts")
                                .num_columns(2)
                                .spacing(vec2(18.0, 2.0))
                                .show(ui, |ui| {
                                    text(
                                        ui,
                                        format!("• {}", species.taxon.label()),
                                        theme::mono(12.0),
                                        theme::TEXT_VARIANT,
                                    );
                                    text(
                                        ui,
                                        format!("• Altura: {:.1} m", studio.build.height),
                                        theme::mono(12.0),
                                        theme::TEXT_VARIANT,
                                    );
                                    ui.end_row();
                                    let placed = studio
                                        .build
                                        .placed
                                        .iter()
                                        .filter(|plant| plant.species == studio.build.species)
                                        .count();
                                    text(
                                        ui,
                                        format!("• Colocados: {placed}"),
                                        theme::mono(12.0),
                                        theme::TEXT_VARIANT,
                                    );
                                    text(
                                        ui,
                                        format!(
                                            "• Triángulos: {}",
                                            triangles.as_deref().unwrap_or("…")
                                        ),
                                        theme::mono(12.0),
                                        theme::TEXT_VARIANT,
                                    );
                                    ui.end_row();
                                });
                        });
                    });
                });
            ui.add_space(8.0);
            ui.horizontal(|ui| {
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    if widgets::button(
                        ui,
                        Style::Primary,
                        Some(icons::ARROW_FORWARD),
                        "Siguiente especie",
                        theme::mono_medium(13.0),
                        30.0,
                    )
                    .clicked()
                    {
                        studio.build.species = (studio.build.species + 1) % CATALOG.len();
                        studio.build.height = CATALOG[studio.build.species].height as f32;
                        studio.build.spread = CATALOG[studio.build.species].spread as f32;
                    }
                    if widgets::button(
                        ui,
                        Style::Quiet,
                        None,
                        "Siguiente paso",
                        theme::mono(13.0),
                        30.0,
                    )
                    .clicked()
                    {
                        local.step = (local.step + 1) % STEPS.len();
                    }
                });
            });
        },
    );
}

fn properties_panel(ui: &Ui, studio: &mut Studio, rect: Rect, local: &mut BuildUi) {
    floating(
        ui,
        "build-properties",
        pos2(rect.right() - 16.0, rect.top() + 72.0),
        Align2::RIGHT_TOP,
        320.0,
        theme::floating(),
        |ui| {
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::TUNE, 20.0, theme::PRIMARY);
                text(
                    ui,
                    "Propiedades del objeto",
                    theme::heading(19.0),
                    theme::TEXT,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    if widgets::icon_button(
                        ui,
                        icons::TARGET,
                        26.0,
                        false,
                        "Volver a la vista inicial",
                    )
                    .clicked()
                    {
                        studio.viewport.go_home();
                    }
                });
            });
            ui.add_space(8.0);
            let species = studio.build.species;
            let (strip, _) =
                ui.allocate_exact_size(vec2(ui.available_width(), 132.0), egui::Sense::hover());
            let painter = ui.painter();
            match studio.library.thumbnails.get(&species) {
                Some(thumbnail) => {
                    // A band across the middle of the specimen picture, where the canopy is.
                    painter.image(
                        thumbnail.id(),
                        strip,
                        Rect::from_min_max(pos2(0.0, 0.18), pos2(1.0, 0.52)),
                        egui::Color32::WHITE,
                    );
                }
                None => {
                    painter.rect_filled(strip, 2.0, theme::RECESSED);
                }
            }
            painter.rect_filled(
                Rect::from_min_size(strip.min, vec2(strip.width(), 30.0)),
                0.0,
                theme::PANEL.gamma_multiply(0.75),
            );
            painter.text(
                pos2(strip.left() + 10.0, strip.top() + 15.0),
                Align2::LEFT_CENTER,
                "Follaje · hojas reales",
                theme::mono_medium(13.0),
                theme::TEXT,
            );
            let leaves = studio
                .library
                .facts
                .get(&species)
                .map_or("…".into(), |facts| compact(facts.triangles));
            painter.text(
                pos2(strip.right() - 10.0, strip.top() + 15.0),
                Align2::RIGHT_CENTER,
                format!("Tri: {leaves}"),
                theme::mono(12.0),
                theme::TELEMETRY,
            );
            painter.rect_filled(
                Rect::from_min_max(pos2(strip.left(), strip.bottom() - 30.0), strip.max),
                0.0,
                theme::PANEL.gamma_multiply(0.75),
            );
            painter.text(
                pos2(strip.left() + 10.0, strip.bottom() - 15.0),
                Align2::LEFT_CENTER,
                format!("{}  {}", icons::SUN, "Translucidez"),
                theme::mono(13.0),
                theme::TEXT,
            );
            let translucency =
                crate::studio::vegetation::stock(gr_model::mesh::SurfaceKind::Leaf).translucency;
            painter.text(
                pos2(strip.right() - 10.0, strip.bottom() - 15.0),
                Align2::RIGHT_CENTER,
                format!("{:.0}%", translucency * 100.0),
                theme::mono_medium(13.0),
                theme::PRIMARY,
            );
            ui.add_space(10.0);

            egui::Frame::new()
                .fill(theme::CONTAINER)
                .corner_radius(2)
                .inner_margin(egui::Margin::symmetric(10, 8))
                .show(ui, |ui| {
                    ui.set_width(ui.available_width());
                    let mut enabled = studio.build.tool == Tool::Place;
                    row(ui, "Colocar con clic", |ui| {
                        if widgets::toggle(ui, &mut enabled).changed() {
                            studio.build.tool = if enabled { Tool::Place } else { Tool::Select };
                        }
                    });
                    row(ui, "Mostrar marcador", |ui| {
                        widgets::toggle(ui, &mut local.show_marker);
                    });
                });
            ui.add_space(8.0);
            let width = ui.available_width();
            let height_range =
                (CATALOG[species].height * 0.4) as f32..=(CATALOG[species].height * 2.0) as f32;
            slider_row(
                ui,
                "Altura de copa",
                &format!("{:.2} m", studio.build.height),
                &mut studio.build.height,
                height_range,
                width,
            );
            let spread_range =
                (CATALOG[species].spread * 0.4) as f32..=(CATALOG[species].spread * 2.0) as f32;
            slider_row(
                ui,
                "Extensión de copa",
                &format!("{:.2} m", studio.build.spread),
                &mut studio.build.spread,
                spread_range,
                width,
            );
            ui.add_space(4.0);
            text(
                ui,
                "VARIACIÓN DE ESCALA",
                theme::mono_medium(13.0),
                theme::TEXT_VARIANT,
            );
            widgets::segmented(
                ui,
                &["No", "Sí", "Aleatoria"],
                &mut studio.build.variation,
                width,
            );
            ui.add_space(6.0);
            text(
                ui,
                "CALIDAD DE VISTA",
                theme::mono_medium(13.0),
                theme::TEXT_VARIANT,
            );
            let mut mode = match studio.viewport.mode() {
                ViewMode::Navigate => 0,
                ViewMode::Preview => 1,
                ViewMode::Render => 2,
            };
            if widgets::segmented(
                ui,
                &["Navegación", "Vista previa", "Render"],
                &mut mode,
                width,
            ) {
                studio
                    .viewport
                    .set_mode([ViewMode::Navigate, ViewMode::Preview, ViewMode::Render][mode]);
            }
            ui.add_space(8.0);
            egui::Frame::new()
                .fill(theme::CONTAINER)
                .corner_radius(2)
                .inner_margin(egui::Margin::symmetric(10, 8))
                .show(ui, |ui| {
                    ui.set_width(ui.available_width());
                    ui.horizontal(|ui| {
                        text(ui, "Punto activo", theme::mono(12.0), theme::TEXT_VARIANT);
                        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                            text(ui, "Mundo", theme::mono(12.0), theme::TELEMETRY)
                        });
                    });
                    ui.add_space(4.0);
                    ui.columns(3, |columns| {
                        for (index, (axis, column)) in ["X", "Y", "Z"]
                            .into_iter()
                            .zip(columns.iter_mut())
                            .enumerate()
                        {
                            let value = studio
                                .build
                                .cursor
                                .map_or("—".into(), |point| format!("{:.2}", point[index]));
                            egui::Frame::new()
                                .fill(theme::RECESSED)
                                .corner_radius(2)
                                .inner_margin(egui::Margin::symmetric(6, 4))
                                .show(column, |ui| {
                                    ui.horizontal(|ui| {
                                        text(
                                            ui,
                                            format!("{axis}:"),
                                            theme::mono(12.0),
                                            theme::PRIMARY,
                                        );
                                        text(ui, value, theme::mono(12.0), theme::TEXT);
                                    });
                                });
                        }
                    });
                });
        },
    );
}

/// The sun, in place of the object properties while the Clima tab is open.
fn weather_panel(ui: &Ui, studio: &mut Studio, rect: Rect) {
    floating(
        ui,
        "build-weather",
        pos2(rect.right() - 16.0, rect.top() + 72.0),
        Align2::RIGHT_TOP,
        320.0,
        theme::floating(),
        |ui| {
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::SUN, 20.0, theme::PRIMARY);
                text(ui, "Sol y cielo", theme::heading(19.0), theme::TEXT);
            });
            ui.add_space(10.0);
            let width = ui.available_width();
            let settings = &mut studio.viewport.settings;
            slider_row(
                ui,
                "Orientación del sol",
                &format!("{:.0}°", settings.sun_azimuth_degrees),
                &mut settings.sun_azimuth_degrees,
                0.0..=360.0,
                width,
            );
            slider_row(
                ui,
                "Altura del sol",
                &format!("{:.1}°", settings.sun_elevation_degrees),
                &mut settings.sun_elevation_degrees,
                1.0..=90.0,
                width,
            );
            slider_row(
                ui,
                "Intensidad del sol",
                &format!("{:.2}", settings.sun_intensity),
                &mut settings.sun_intensity,
                0.0..=6.0,
                width,
            );
            slider_row(
                ui,
                "Intensidad del cielo",
                &format!("{:.2}", settings.sky_intensity),
                &mut settings.sky_intensity,
                0.0..=4.0,
                width,
            );
            slider_row(
                ui,
                "Exposición",
                &format!("{:.2}", settings.exposure),
                &mut settings.exposure,
                0.05..=2.0,
                width,
            );
            ui.add_space(4.0);
            ui.add(egui::Label::new(egui::RichText::new("La navegación muestra el sol sin sombras; la vista previa y el render las calculan.").font(theme::body(12.0)).color(theme::OUTLINE)).wrap());
        },
    );
}

fn bottom_bar(ui: &Ui, studio: &mut Studio, rect: Rect, local: &mut BuildUi) {
    floating(
        ui,
        "build-tabs",
        pos2(rect.left() + 16.0, rect.bottom() - 156.0),
        Align2::LEFT_BOTTOM,
        0.0,
        theme::hud().inner_margin(egui::Margin::same(4)),
        |ui| {
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 4.0;
                let tab = |ui: &mut Ui, glyph, label: &str, active: bool, enabled: bool| {
                    ui.add_enabled_ui(enabled, |ui| {
                        widgets::button(
                            ui,
                            if active { Style::Primary } else { Style::Quiet },
                            Some(glyph),
                            label,
                            theme::body(16.0),
                            42.0,
                        )
                    })
                    .inner
                    .clicked()
                };
                if tab(ui, icons::NATURE, "Objetos", !local.weather, true) {
                    local.weather = false;
                }
                if tab(ui, icons::TEXTURE, "Materiales", false, true) {
                    studio.page = Page::Materials;
                }
                let _ = tab(ui, icons::LANDSCAPE, "Paisaje", false, false);
                if tab(ui, icons::CLOUD, "Clima", local.weather, true) {
                    local.weather = !local.weather;
                }
            });
        },
    );
    floating(
        ui,
        "build-categories",
        pos2(rect.left() + 16.0, rect.bottom() - 98.0),
        Align2::LEFT_BOTTOM,
        0.0,
        theme::hud().inner_margin(egui::Margin::same(6)),
        |ui| {
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 4.0;
                for (glyph, label, available) in [
                    (icons::UPLOAD, "Modelos importados", false),
                    (icons::NATURE, "Naturaleza", true),
                    (icons::PEOPLE, "Personas", false),
                    (icons::CAR, "Vehículos", false),
                    (icons::SEAT, "Interior", false),
                    (icons::HOME_OUTDOOR, "Exterior", false),
                    (icons::LIGHT, "Luces", false),
                    (icons::EFFECTS, "Efectos", false),
                ] {
                    let response = ui
                        .add_enabled_ui(available, |ui| {
                            widgets::chip(ui, Some(glyph), label, None, available)
                        })
                        .inner;
                    if response.clicked() {
                        studio.page = Page::Library;
                    }
                    if !available {
                        response.on_disabled_hover_text(
                            "Esta categoría aún no tiene recursos en el motor",
                        );
                    }
                }
            });
        },
    );
    floating(
        ui,
        "build-tools",
        pos2(rect.left() + 16.0, rect.bottom() - 16.0),
        Align2::LEFT_BOTTOM,
        0.0,
        theme::hud().inner_margin(egui::Margin::same(6)),
        |ui| {
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 6.0;
                for (glyph, label, tool) in [
                    (icons::TOUCH, "Colocar", Some(Tool::Place)),
                    (icons::SELECT, "Seleccionar", Some(Tool::Select)),
                    (icons::DELETE, "Eliminar", Some(Tool::Delete)),
                ] {
                    let active = tool == Some(studio.build.tool);
                    if widgets::button(
                        ui,
                        if active { Style::Pale } else { Style::Quiet },
                        Some(glyph),
                        label,
                        theme::mono_medium(14.0),
                        36.0,
                    )
                    .clicked()
                        && let Some(tool) = tool
                    {
                        studio.build.tool = tool;
                    }
                }
                for (glyph, label) in [
                    (icons::MOVE, "Mover"),
                    (icons::ROTATE, "Rotar"),
                    (icons::SCALE, "Escalar"),
                ] {
                    ui.add_enabled_ui(false, |ui| {
                        widgets::button(
                            ui,
                            Style::Quiet,
                            Some(glyph),
                            label,
                            theme::mono_medium(14.0),
                            36.0,
                        )
                    })
                    .inner
                    .on_disabled_hover_text(
                        "Aún no disponible: por ahora coloca de nuevo el ejemplar",
                    );
                }
                ui.add_space(12.0);
                egui::Frame::new()
                    .fill(theme::RECESSED)
                    .corner_radius(2)
                    .inner_margin(egui::Margin::symmetric(10, 4))
                    .show(ui, |ui| {
                        ui.horizontal(|ui| {
                            text(ui, "CURSOR\nXYZ:", theme::mono(12.0), theme::TEXT_VARIANT);
                            for (index, axis) in ["X", "Y", "Z"].into_iter().enumerate() {
                                ui.vertical(|ui| {
                                    ui.spacing_mut().item_spacing.y = 0.0;
                                    text(ui, format!("{axis}:"), theme::mono(12.0), theme::PRIMARY);
                                    let value = studio.build.cursor.map_or("—".into(), |point| {
                                        format!("{:.2} m", point[index])
                                    });
                                    text(ui, value, theme::mono(14.0), theme::PRIMARY);
                                });
                            }
                            if widgets::icon_button(
                                ui,
                                icons::REFRESH,
                                24.0,
                                false,
                                "Borrar el punto",
                            )
                            .clicked()
                            {
                                studio.build.cursor = None;
                            }
                        });
                    });
            });
        },
    );
    floating(
        ui,
        "build-keys",
        pos2(rect.right() - 16.0, rect.bottom() - 16.0),
        Align2::RIGHT_BOTTOM,
        0.0,
        theme::hud().inner_margin(egui::Margin::same(6)),
        |ui| {
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 6.0;
                for (key, hint) in [
                    ("SHIFT", "Rápido ×4"),
                    ("WASD", "Caminar"),
                    ("QE", "Bajar / subir"),
                    ("CLIC", "Orbitar"),
                    ("CLIC DER", "Desplazar"),
                    ("RUEDA", "Acercar"),
                ] {
                    widgets::key(
                        ui,
                        key,
                        if key.starts_with("CLIC") || key == "RUEDA" {
                            theme::TELEMETRY
                        } else {
                            theme::TEXT
                        },
                    )
                    .on_hover_text(hint);
                }
            });
        },
    );
}

fn row(ui: &mut Ui, label: &str, add: impl FnOnce(&mut Ui)) {
    ui.horizontal(|ui| {
        text(ui, label, theme::body(14.0), theme::TEXT);
        ui.with_layout(Layout::right_to_left(Align::Center), add);
    });
}

fn slider_row(
    ui: &mut Ui,
    label: &str,
    value_text: &str,
    value: &mut f32,
    range: std::ops::RangeInclusive<f32>,
    width: f32,
) -> bool {
    ui.horizontal(|ui| {
        text(
            ui,
            label.to_uppercase(),
            theme::mono_medium(13.0),
            theme::TEXT_VARIANT,
        );
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            text(ui, value_text, theme::mono_medium(13.0), theme::TEXT)
        });
    });
    let changed = widgets::bar_slider(ui, value, range, width);
    ui.add_space(6.0);
    changed
}
