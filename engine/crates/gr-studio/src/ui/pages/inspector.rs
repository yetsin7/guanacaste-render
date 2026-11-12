//! Asset inspector: one species large, path-traced at 96 samples next to the 1.8 m figure, with the figures of its
//! mesh measured from the plant the engine grows.

use super::common::{compact, grouped};
use super::library::select;
use crate::app::{Page, Studio};
use crate::studio::vegetation::{CATALOG, stock};
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use eframe::egui::{self, Align, Align2, Color32, Layout, Rect, Sense, Ui, pos2, vec2};
use eframe::egui_wgpu::RenderState;
use gr_model::mesh::SurfaceKind;

pub fn show(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>) {
    let index = studio.library_state.inspected.min(CATALOG.len() - 1);
    if let Some(state) = state {
        studio.library.ensure_inspector(state, index);
    }
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
    header_bar(&mut header_ui, studio, index);

    let body = Rect::from_min_max(pos2(full.left(), header.bottom() + 1.0), full.max).shrink(10.0);
    let side_width = 490.0f32.min(body.width() * 0.32);
    let left = Rect::from_min_max(
        body.min,
        pos2(body.right() - side_width - 10.0, body.bottom()),
    );
    let right = Rect::from_min_max(pos2(left.right() + 10.0, body.top()), body.max);
    let mut left_ui = ui.new_child(egui::UiBuilder::new().max_rect(left));
    egui::ScrollArea::vertical()
        .id_salt("inspector-left")
        .auto_shrink([false, false])
        .show(&mut left_ui, |ui| main_column(ui, studio, index));
    let mut right_ui = ui.new_child(egui::UiBuilder::new().max_rect(right));
    egui::ScrollArea::vertical()
        .id_salt("inspector-right")
        .auto_shrink([false, false])
        .show(&mut right_ui, |ui| metadata(ui, studio, index));
}

fn header_bar(ui: &mut Ui, studio: &mut Studio, index: usize) {
    ui.spacing_mut().item_spacing.x = 10.0;
    if widgets::icon_button(ui, icons::ARROW_BACK, 28.0, false, "Volver a la biblioteca").clicked()
    {
        studio.page = Page::Library;
    }
    widgets::icon(ui, icons::ZOOM_IN, 20.0, theme::PRIMARY);
    text(
        ui,
        "Inspector de recursos",
        theme::heading(20.0),
        theme::TEXT,
    );
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(6, 2))
        .show(ui, |ui| {
            text(
                ui,
                format!(
                    "ESCENA: {}",
                    studio
                        .project_name()
                        .unwrap_or_else(|| "ninguna".into())
                        .to_uppercase()
                ),
                theme::mono(13.0),
                theme::TEXT_VARIANT,
            );
        });
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(6, 2))
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                widgets::status(
                    ui,
                    &studio.telemetry.fps_label(),
                    theme::TELEMETRY,
                    theme::mono(13.0),
                );
                text(ui, "| TRAZADO GPU", theme::mono(13.0), theme::TEXT_VARIANT);
            });
        });
    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
        ui.spacing_mut().item_spacing.x = 4.0;
        for (step, glyph, tooltip) in [
            (1usize, icons::CHEVRON_RIGHT, "Especie siguiente"),
            (CATALOG.len() - 1, icons::CHEVRON_LEFT, "Especie anterior"),
        ] {
            if widgets::icon_button(ui, glyph, 30.0, false, tooltip).clicked() {
                studio.library_state.inspected = (index + step) % CATALOG.len();
            }
        }
        text(
            ui,
            format!("{} / {}", index + 1, CATALOG.len()),
            theme::mono(13.0),
            theme::TEXT_VARIANT,
        );
    });
}

fn main_column(ui: &mut Ui, studio: &mut Studio, index: usize) {
    let species = &CATALOG[index];
    let width = ui.available_width();
    let (stage, _) = ui.allocate_exact_size(
        vec2(
            width,
            (width * 0.6)
                .min(ui.clip_rect().height() * 0.52)
                .clamp(300.0, 660.0),
        ),
        Sense::hover(),
    );
    let painter = ui.painter_at(stage);
    painter.rect_filled(stage, 2.0, Color32::from_rgb(0x10, 0x11, 0x14));
    // Dotted backdrop of the prototype's specimen stage.
    let mut y = stage.top() + 12.0;
    while y < stage.bottom() {
        let mut x = stage.left() + 12.0;
        while x < stage.right() {
            painter.circle_filled(pos2(x, y), 0.8, theme::HIGHEST);
            x += 24.0;
        }
        y += 24.0;
    }
    match studio
        .library
        .inspector
        .as_ref()
        .filter(|(shown, _)| *shown == index)
    {
        Some((_, texture)) => {
            let picture_height = stage.height() * 0.66;
            let picture = Rect::from_center_size(
                stage.center() - vec2(0.0, 10.0),
                vec2(picture_height * texture.aspect_ratio(), picture_height),
            );
            painter.rect_filled(picture.expand(1.0), 0.0, theme::HAIRLINE);
            painter.image(
                texture.id(),
                picture,
                Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)),
                Color32::WHITE,
            );
        }
        None => {
            painter.text(
                stage.center() - vec2(0.0, 16.0),
                Align2::CENTER_CENTER,
                icons::NATURE,
                theme::icon(54.0),
                theme::HIGHEST,
            );
            painter.text(
                stage.center() + vec2(0.0, 28.0),
                Align2::CENTER_CENTER,
                "Trazando el ejemplar a 96 muestras…",
                theme::mono(13.0),
                theme::OUTLINE,
            );
        }
    }
    let tag = Rect::from_min_size(stage.min + vec2(10.0, 10.0), vec2(380.0, 24.0));
    painter.rect_filled(tag, 2.0, theme::PANEL.gamma_multiply(0.85));
    painter.circle_filled(pos2(tag.left() + 10.0, tag.center().y), 4.0, theme::PRIMARY);
    painter.text(
        pos2(tag.left() + 20.0, tag.center().y),
        Align2::LEFT_CENTER,
        "VISTA FOTOMÉTRICA TRAZADA   96 SPP",
        theme::mono_medium(13.0),
        theme::PRIMARY,
    );

    let scale = Rect::from_min_size(
        pos2(stage.left() + 10.0, stage.bottom() - 96.0),
        vec2(270.0, 86.0),
    );
    painter.rect_filled(scale, 2.0, theme::PANEL.gamma_multiply(0.92));
    painter.text(
        scale.min + vec2(10.0, 12.0),
        Align2::LEFT_TOP,
        "REFERENCIA DE ESCALA",
        theme::mono(12.0),
        theme::TEXT_VARIANT,
    );
    painter.text(
        scale.min + vec2(180.0, 12.0),
        Align2::LEFT_TOP,
        "1:1",
        theme::mono(12.0),
        theme::TELEMETRY,
    );
    let bar = Rect::from_min_size(scale.min + vec2(10.0, 40.0), vec2(80.0, 3.0));
    painter.rect_filled(bar, 1.0, theme::PRIMARY);
    painter.text(
        pos2(bar.right() + 10.0, bar.center().y),
        Align2::LEFT_CENTER,
        "Figura humana 1,80 m",
        theme::mono(13.0),
        theme::TEXT,
    );
    painter.text(
        scale.min + vec2(10.0, 62.0),
        Align2::LEFT_TOP,
        format!(
            "Ejemplar: {:.1} m × {:.1} m",
            species.height, species.spread
        ),
        theme::mono(12.0),
        theme::OUTLINE,
    );

    let sun = Rect::from_min_size(
        pos2(stage.right() - 330.0, stage.bottom() - 36.0),
        vec2(320.0, 26.0),
    );
    painter.rect_filled(sun, 2.0, theme::PANEL.gamma_multiply(0.92));
    painter.text(
        pos2(sun.left() + 10.0, sun.center().y),
        Align2::LEFT_CENTER,
        format!("{}  Sol cenital  |  Cielo: estudio oscuro", icons::SUN),
        theme::mono(13.0),
        theme::TEXT,
    );

    ui.add_space(10.0);
    theme::card().show(ui, |ui| {
        ui.set_width(ui.available_width());
        ui.horizontal(|ui| {
            ui.vertical(|ui| {
                ui.horizontal(|ui| {
                    text(ui, species.name, theme::heading(26.0), theme::TEXT);
                    widgets::tag(
                        ui,
                        &species.taxon.label().to_uppercase(),
                        theme::HIGHEST,
                        theme::PRIMARY,
                    );
                });
                text(
                    ui,
                    format!("{} — {}", species.latin, species.description),
                    egui::FontId::new(15.0, egui::FontFamily::Proportional),
                    theme::TEXT_VARIANT,
                );
            });
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                if widgets::button(
                    ui,
                    Style::Pale,
                    Some(icons::ADD),
                    "Colocar en escena",
                    theme::body(16.0),
                    38.0,
                )
                .clicked()
                {
                    select(studio, index);
                    studio.page = Page::Build;
                }
                ui.add_enabled_ui(false, |ui| {
                    widgets::button(
                        ui,
                        Style::Quiet,
                        Some(icons::EDIT),
                        "Editar geometría",
                        theme::body(15.0),
                        38.0,
                    )
                })
                .inner
                .on_disabled_hover_text(
                    "La geometría de las plantas es procedural: cambia altura y copa al colocarlas",
                );
            });
        });
    });
    ui.add_space(10.0);
    let facts = studio.library.facts.get(&index).copied();
    ui.columns(3, |columns| {
        stat(
            &mut columns[0],
            "ALTURA",
            &format!("{:.1}", species.height),
            "m",
            "Altura por defecto de la copa",
            icons::SWAP_VERTICAL,
            theme::TEXT_VARIANT,
        );
        stat(
            &mut columns[1],
            "EXTENSIÓN DE COPA",
            &format!("{:.1}", species.spread),
            "m",
            "Diámetro de la copa",
            icons::SWAP_HORIZONTAL,
            theme::TEXT_VARIANT,
        );
        let triangles = facts.map_or("…".into(), |facts| compact(facts.triangles));
        stat(
            &mut columns[2],
            "COMPLEJIDAD DE MALLA",
            &triangles,
            "triángulos",
            "Hojas como geometría real",
            icons::HUB,
            theme::TELEMETRY,
        );
    });
    ui.add_space(10.0);
    theme::card().show(ui, |ui| {
        ui.set_width(ui.available_width());
        ui.horizontal(|ui| {
            widgets::icon(ui, icons::SHOW_CHART, 20.0, theme::PRIMARY);
            text(ui, "Perfil de la copa", theme::heading(19.0), theme::TEXT);
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                text(
                    ui,
                    "Triángulos por altura",
                    theme::mono(12.0),
                    theme::TEXT_VARIANT,
                );
                let (swatch, _) = ui.allocate_exact_size(vec2(12.0, 3.0), Sense::hover());
                ui.painter().rect_filled(swatch, 1.0, theme::PRIMARY);
            });
        });
        ui.add_space(6.0);
        let (plot, _) = ui.allocate_exact_size(vec2(ui.available_width(), 120.0), Sense::hover());
        let painter = ui.painter_at(plot);
        painter.rect_filled(plot, 2.0, theme::RECESSED);
        if let Some(facts) = facts {
            let peak = facts.profile.iter().copied().max().unwrap_or(1).max(1) as f32;
            let points: Vec<egui::Pos2> = facts
                .profile
                .iter()
                .enumerate()
                .map(|(band, &count)| {
                    pos2(
                        plot.left() + 10.0 + (plot.width() - 20.0) * band as f32 / 9.0,
                        plot.bottom() - 10.0 - (plot.height() - 24.0) * count as f32 / peak,
                    )
                })
                .collect();
            for pair in points.windows(2) {
                let quad = vec![
                    pair[0],
                    pair[1],
                    pos2(pair[1].x, plot.bottom()),
                    pos2(pair[0].x, plot.bottom()),
                ];
                painter.add(egui::Shape::convex_polygon(
                    quad,
                    theme::PRIMARY.gamma_multiply(0.12),
                    egui::Stroke::NONE,
                ));
            }
            painter.add(egui::Shape::line(
                points,
                egui::Stroke::new(2.0, theme::PRIMARY),
            ));
        }
        ui.horizontal(|ui| {
            text(ui, "Base (0 m)", theme::mono(12.0), theme::TEXT_VARIANT);
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                text(
                    ui,
                    format!("Cima ({:.1} m)", species.height),
                    theme::mono(12.0),
                    theme::TEXT_VARIANT,
                )
            });
        });
    });
}

fn stat(
    ui: &mut Ui,
    label: &str,
    value: &str,
    unit: &str,
    note: &str,
    glyph: char,
    note_color: Color32,
) {
    theme::card().show(ui, |ui| {
        ui.set_width(ui.available_width());
        ui.horizontal(|ui| {
            ui.vertical(|ui| {
                text(ui, label, theme::mono(12.0), theme::TEXT_VARIANT);
                ui.horizontal(|ui| {
                    ui.spacing_mut().item_spacing.x = 4.0;
                    text(ui, value, theme::heading(30.0), theme::TEXT);
                    text(ui, unit, theme::mono(13.0), theme::TELEMETRY);
                });
                text(ui, note, theme::body(14.0), note_color);
            });
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                let (rect, _) = ui.allocate_exact_size(vec2(52.0, 52.0), Sense::hover());
                ui.painter().rect_filled(rect, 2.0, theme::HIGHEST);
                ui.painter().text(
                    rect.center(),
                    Align2::CENTER_CENTER,
                    glyph,
                    theme::icon(22.0),
                    theme::TEXT,
                );
            });
        });
    });
}

fn metadata(ui: &mut Ui, studio: &mut Studio, index: usize) {
    let species = &CATALOG[index];
    let leaf = stock(SurfaceKind::Leaf);
    egui::Frame::new()
        .fill(theme::PANEL)
        .stroke(theme::hairline())
        .corner_radius(4)
        .inner_margin(egui::Margin::same(14))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::TUNE, 20.0, theme::TEXT);
                text(
                    ui,
                    "METADATOS DEL RECURSO",
                    theme::heading(19.0),
                    theme::TEXT,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    widgets::status(ui, "PROCEDURAL", theme::TELEMETRY, theme::mono(12.0))
                });
            });
            ui.add_space(10.0);
            row(
                ui,
                icons::NATURE,
                "Familia",
                species.taxon.label(),
                theme::PRIMARY,
            );
            row(
                ui,
                icons::BOOK,
                "Versión de biblioteca",
                &format!("Motor {}", env!("CARGO_PKG_VERSION")),
                theme::PRIMARY,
            );
            section(ui, |ui| {
                ui.add_enabled_ui(false, |ui| {
                    ui.horizontal(|ui| {
                        widgets::icon(ui, icons::WIND, 18.0, theme::TEXT);
                        text(ui, "Respuesta al viento", theme::body(16.0), theme::TEXT);
                        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                            text(ui, "No disponible", theme::mono(13.0), theme::OUTLINE)
                        });
                    });
                    let mut wind = 0.0;
                    widgets::thin_slider(ui, &mut wind, 0.0..=1.0, ui.available_width());
                })
                .response
                .on_disabled_hover_text("El motor aún no anima la vegetación");
            });
            row(
                ui,
                icons::SUN,
                "Calidad de sombreado",
                &format!("Hojas translúcidas {:.0} %", leaf.translucency * 100.0),
                theme::TELEMETRY,
            );
            section(ui, |ui| {
                ui.horizontal(|ui| {
                    widgets::icon(ui, icons::LAYERS, 18.0, theme::TEXT);
                    text(ui, "Niveles de detalle", theme::body(16.0), theme::TEXT);
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        text(ui, "Una sola malla", theme::mono(13.0), theme::TELEMETRY)
                    });
                });
                ui.add_space(4.0);
                let triangles = studio
                    .library
                    .facts
                    .get(&index)
                    .map(|facts| grouped(facts.triangles));
                egui::Frame::new()
                    .fill(theme::RECESSED)
                    .corner_radius(2)
                    .inner_margin(egui::Margin::symmetric(10, 6))
                    .show(ui, |ui| {
                        ui.set_width(ui.available_width());
                        ui.horizontal(|ui| {
                            text(ui, "LOD 0", theme::mono_medium(14.0), theme::PRIMARY);
                            text(
                                ui,
                                format!("{} Δ", triangles.as_deref().unwrap_or("…")),
                                theme::mono(13.0),
                                theme::TEXT_VARIANT,
                            );
                            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                text(ui, "LOD 1–3: aún no", theme::mono(12.0), theme::OUTLINE)
                            });
                        });
                    });
            });
            section(ui, |ui| {
                text(
                    ui,
                    "POSICIÓN DEL EJEMPLAR SELECCIONADO",
                    theme::mono(12.0),
                    theme::TEXT_VARIANT,
                );
                ui.add_space(4.0);
                let selected = studio
                    .build
                    .selected
                    .and_then(|i| studio.build.placed.get(i))
                    .filter(|plant| plant.species == index)
                    .copied();
                ui.columns(3, |columns| {
                    for (axis, column) in ["X", "Y", "Z"]
                        .into_iter()
                        .enumerate()
                        .zip(columns.iter_mut())
                    {
                        egui::Frame::new()
                            .fill(theme::RECESSED)
                            .stroke(theme::hairline())
                            .inner_margin(egui::Margin::symmetric(6, 4))
                            .show(column, |ui| {
                                ui.horizontal(|ui| {
                                    text(ui, axis.1, theme::mono_medium(13.0), theme::PRIMARY);
                                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                        let value = selected.map_or("—".into(), |plant| {
                                            format!("{:.2} m", plant.at[axis.0])
                                        });
                                        text(ui, value, theme::mono(13.0), theme::TEXT);
                                    });
                                });
                            });
                    }
                });
                if selected.is_none() {
                    text(
                        ui,
                        "Selecciona un ejemplar colocado en Construcción para ver su posición.",
                        theme::body(12.0),
                        theme::OUTLINE,
                    );
                }
            });
            ui.add_space(4.0);
            ui.horizontal(|ui| {
                text(
                    ui,
                    format!(
                        "Colocados en la sesión: {}",
                        studio
                            .build
                            .placed
                            .iter()
                            .filter(|plant| plant.species == index)
                            .count()
                    ),
                    theme::mono(12.0),
                    theme::TEXT_VARIANT,
                );
            });
        });
    ui.add_space(10.0);
    egui::Frame::new()
        .fill(theme::PANEL)
        .stroke(theme::hairline())
        .corner_radius(4)
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                text(
                    ui,
                    "FOLLAJE ESTACIONAL",
                    theme::mono(13.0),
                    theme::TEXT_VARIANT,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    text(ui, "Aún no disponible", theme::mono(12.0), theme::OUTLINE)
                });
            });
            ui.add_space(6.0);
            ui.add_enabled_ui(false, |ui| {
                ui.columns(4, |columns| {
                    for ((glyph, label), column) in [
                        (icons::NATURE, "Verano"),
                        (icons::SPA, "Otoño"),
                        (icons::SNOW, "Invierno"),
                        (icons::FAVORITE, "Primavera"),
                    ]
                    .into_iter()
                    .zip(columns.iter_mut())
                    {
                        column.vertical_centered(|ui| {
                            widgets::icon(ui, glyph, 20.0, theme::TEXT_VARIANT);
                            text(ui, label, theme::body(14.0), theme::TEXT_VARIANT);
                        });
                    }
                });
            });
        });
}

fn section(ui: &mut Ui, add: impl FnOnce(&mut Ui)) {
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            add(ui);
        });
    ui.add_space(8.0);
}

fn row(ui: &mut Ui, glyph: char, label: &str, value: &str, color: Color32) {
    section(ui, |ui| {
        ui.horizontal(|ui| {
            widgets::icon(ui, glyph, 18.0, theme::TEXT);
            text(ui, label, theme::body(16.0), theme::TEXT);
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                text(ui, value, theme::mono(13.0), color)
            });
        });
    });
}
