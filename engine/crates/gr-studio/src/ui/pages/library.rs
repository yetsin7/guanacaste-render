//! Nature library: every species the engine grows, each with a specimen path-traced on this computer next to a
//! 1.8 m figure for scale.

use super::common::compact;
use crate::app::{Page, Studio};
use crate::studio::vegetation::{CATALOG, Taxon};
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use eframe::egui::{self, Align, Align2, Layout, Rect, Sense, Ui, pos2, vec2};
use eframe::egui_wgpu::RenderState;

pub fn show(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>) {
    if let Some(state) = state {
        studio.library.ensure_thumbnails(state);
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
    header_bar(&mut header_ui, studio);

    let footer = Rect::from_min_max(pos2(full.left(), full.bottom() - 46.0), full.max);
    ui.painter().rect_filled(footer, 0.0, theme::PANEL);
    ui.painter()
        .hline(full.x_range(), footer.top(), theme::hairline());
    let mut footer_ui = ui.new_child(
        egui::UiBuilder::new()
            .max_rect(footer.shrink2(vec2(16.0, 0.0)))
            .layout(Layout::left_to_right(Align::Center)),
    );
    footer_bar(&mut footer_ui, studio);

    let body = Rect::from_min_max(
        pos2(full.left(), header.bottom() + 1.0),
        pos2(full.right(), footer.top() - 1.0),
    );
    let mut body_ui = ui.new_child(egui::UiBuilder::new().max_rect(body.shrink2(vec2(12.0, 12.0))));
    let ui = &mut body_ui;
    search_bar(ui, studio);
    ui.add_space(8.0);
    egui::ScrollArea::vertical()
        .auto_shrink([false, false])
        .show(ui, |ui| grid(ui, studio));
}

fn header_bar(ui: &mut Ui, studio: &Studio) {
    ui.spacing_mut().item_spacing.x = 10.0;
    widgets::icon(ui, icons::NATURE, 20.0, theme::PRIMARY);
    text(
        ui,
        "BIBLIOTECA DE NATURALEZA",
        theme::heading(20.0),
        theme::TEXT,
    );
    let (line, _) = ui.allocate_exact_size(vec2(1.0, 18.0), Sense::hover());
    ui.painter().rect_filled(line, 0.0, theme::FLOATING_BORDER);
    text(
        ui,
        "Escena de contexto:",
        theme::mono(13.0),
        theme::TEXT_VARIANT,
    );
    let scene = studio.project_name().unwrap_or_else(|| "ninguna".into());
    text(
        ui,
        format!("{scene}  {}", studio.telemetry.fps_label()),
        theme::mono(13.0),
        theme::PRIMARY,
    );
    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
        let ready = studio.library.thumbnails.len();
        egui::Frame::new()
            .stroke(theme::hairline())
            .corner_radius(2)
            .inner_margin(egui::Margin::symmetric(10, 4))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    text(
                        ui,
                        format!("{ready} / {}", CATALOG.len()),
                        theme::mono(13.0),
                        theme::TELEMETRY,
                    );
                    text(
                        ui,
                        "Ejemplares renderizados:",
                        theme::mono(13.0),
                        theme::TEXT_VARIANT,
                    );
                });
            });
    });
}

fn search_bar(ui: &mut Ui, studio: &mut Studio) {
    egui::Frame::new()
        .fill(theme::RECESSED)
        .stroke(theme::hairline())
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(14, 8))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::SEARCH, 20.0, theme::TEXT_VARIANT);
                let width = ui.available_width() - 200.0;
                ui.add_sized(
                    vec2(width, 26.0),
                    egui::TextEdit::singleline(&mut studio.library_state.search)
                        .hint_text("Buscar Guanacaste, palmas, nombre científico…")
                        .frame(egui::Frame::NONE)
                        .font(theme::body(16.0)),
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    if let Some(taxon) = studio.library_state.filter {
                        let count = CATALOG
                            .iter()
                            .filter(|species| species.taxon == taxon)
                            .count();
                        egui::Frame::new()
                            .fill(theme::ACTIVE)
                            .corner_radius(2)
                            .inner_margin(egui::Margin::symmetric(6, 2))
                            .show(ui, |ui| {
                                text(
                                    ui,
                                    count.to_string(),
                                    theme::mono_medium(13.0),
                                    egui::Color32::WHITE,
                                );
                            });
                        text(ui, taxon.label(), theme::mono(13.0), theme::PRIMARY);
                    }
                });
            });
        });
    ui.add_space(8.0);
    ui.horizontal(|ui| {
        ui.spacing_mut().item_spacing.x = 4.0;
        text(ui, "FILTRO TAXONÓMICO:", theme::mono(12.0), theme::OUTLINE);
        if widgets::chip(
            ui,
            None,
            "Todas",
            Some(CATALOG.len()),
            studio.library_state.filter.is_none(),
        )
        .clicked()
        {
            studio.library_state.filter = None;
        }
        for taxon in Taxon::ALL {
            let count = CATALOG
                .iter()
                .filter(|species| species.taxon == taxon)
                .count();
            let selected = studio.library_state.filter == Some(taxon);
            if widgets::chip(
                ui,
                selected.then_some(icons::CHECK),
                taxon.label(),
                Some(count),
                selected,
            )
            .clicked()
            {
                studio.library_state.filter = if selected { None } else { Some(taxon) };
            }
        }
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            text(
                ui,
                format!(
                    "Mostrando {} de {} especies",
                    visible(studio).len(),
                    CATALOG.len()
                ),
                theme::mono(12.0),
                theme::TEXT_VARIANT,
            );
        });
    });
}

fn visible(studio: &Studio) -> Vec<usize> {
    let needle = studio.library_state.search.trim().to_lowercase();
    (0..CATALOG.len())
        .filter(|&index| {
            let species = &CATALOG[index];
            studio
                .library_state
                .filter
                .is_none_or(|taxon| species.taxon == taxon)
                && (needle.is_empty()
                    || species.name.to_lowercase().contains(&needle)
                    || species.latin.to_lowercase().contains(&needle)
                    || species.taxon.label().to_lowercase().contains(&needle))
        })
        .collect()
}

fn grid(ui: &mut Ui, studio: &mut Studio) {
    let shown = visible(studio);
    if shown.is_empty() {
        text(
            ui,
            "Ninguna especie coincide con la búsqueda.",
            theme::body(14.0),
            theme::TEXT_VARIANT,
        );
        return;
    }
    let columns = if ui.available_width() > 1200.0 { 4 } else { 3 };
    let gap = 16.0;
    let width = (ui.available_width() - gap * (columns - 1) as f32) / columns as f32;
    for row in shown.chunks(columns) {
        ui.horizontal_top(|ui| {
            ui.spacing_mut().item_spacing.x = gap;
            for &index in row {
                ui.allocate_ui_with_layout(vec2(width, 0.0), Layout::top_down(Align::Min), |ui| {
                    card(ui, studio, index, width)
                });
            }
        });
        ui.add_space(gap);
    }
}

fn card(ui: &mut Ui, studio: &mut Studio, index: usize, width: f32) {
    let species = &CATALOG[index];
    let active = studio.build.species == index;
    let frame = egui::Frame::new()
        .fill(theme::PANEL)
        .corner_radius(2)
        .stroke(if active {
            egui::Stroke::new(1.0, theme::PRIMARY_CONTAINER)
        } else {
            theme::hairline()
        });
    frame.show(ui, |ui| {
        ui.set_width(width);
        ui.spacing_mut().item_spacing.y = 4.0;
        let (picture, response) = ui.allocate_exact_size(vec2(width, width), Sense::click());
        let painter = ui.painter_at(picture);
        match studio.library.thumbnails.get(&index) {
            Some(texture) => {
                painter.image(
                    texture.id(),
                    picture,
                    Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)),
                    egui::Color32::WHITE,
                );
            }
            None => {
                painter.rect_filled(picture, 0.0, theme::RECESSED);
                painter.text(
                    picture.center(),
                    Align2::CENTER_CENTER,
                    icons::NATURE,
                    theme::icon(40.0),
                    theme::HIGHEST,
                );
                painter.text(
                    picture.center() + vec2(0.0, 34.0),
                    Align2::CENTER_CENTER,
                    "Renderizando ejemplar…",
                    theme::mono(12.0),
                    theme::OUTLINE,
                );
            }
        }
        if active {
            painter.rect_filled(
                Rect::from_min_size(picture.min, vec2(width, 3.0)),
                0.0,
                theme::PRIMARY_CONTAINER,
            );
        }
        let badge = if active {
            ("ACTIVO", theme::ACTIVE, egui::Color32::WHITE)
        } else {
            (
                "GPU",
                theme::PANEL.gamma_multiply(0.85),
                theme::TEXT_VARIANT,
            )
        };
        let galley = painter.layout_no_wrap(badge.0.to_owned(), theme::mono_medium(13.0), badge.2);
        let badge_rect = Rect::from_min_size(
            pos2(
                picture.right() - galley.size().x - 18.0,
                picture.top() + 8.0,
            ),
            galley.size() + vec2(12.0, 6.0),
        );
        painter.rect_filled(badge_rect, 1.0, badge.1);
        painter.galley(badge_rect.min + vec2(6.0, 3.0), galley, badge.2);
        let scale = Rect::from_min_size(
            pos2(picture.left() + 6.0, picture.bottom() - 28.0),
            vec2(150.0, 22.0),
        );
        painter.rect_filled(scale, 1.0, theme::PANEL.gamma_multiply(0.85));
        painter.text(
            pos2(scale.left() + 8.0, scale.center().y),
            Align2::LEFT_CENTER,
            format!("{} Figura 1,8 m", icons::PERSON),
            theme::mono(12.0),
            theme::TEXT,
        );
        if response
            .on_hover_text("Ver en el inspector")
            .on_hover_cursor(egui::CursorIcon::PointingHand)
            .clicked()
        {
            studio.library_state.inspected = index;
            studio.page = Page::Inspector;
        }

        egui::Frame::new()
            .inner_margin(egui::Margin::symmetric(4, 6))
            .show(ui, |ui| {
                ui.set_width(width - 8.0);
                ui.horizontal(|ui| {
                    text(ui, species.name, theme::heading(19.0), theme::TEXT);
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        text(ui, species.taxon.label(), theme::mono(12.0), theme::PRIMARY)
                    });
                });
                text(
                    ui,
                    species.latin,
                    egui::FontId::new(14.0, egui::FontFamily::Proportional),
                    theme::TEXT_VARIANT,
                );
                ui.add_space(4.0);
                widgets::rule(ui);
                ui.columns(2, |columns| {
                    text(&mut columns[0], "ALTURA", theme::mono(12.0), theme::OUTLINE);
                    text(
                        &mut columns[0],
                        format!("{:.1} m", species.height),
                        theme::mono(14.0),
                        theme::TEXT,
                    );
                    text(&mut columns[1], "MALLA", theme::mono(12.0), theme::OUTLINE);
                    let triangles = studio
                        .library
                        .facts
                        .get(&index)
                        .map_or("…".into(), |facts| {
                            format!("{} tri.", compact(facts.triangles))
                        });
                    text(
                        &mut columns[1],
                        triangles,
                        theme::mono(14.0),
                        theme::TELEMETRY,
                    );
                });
            });
        let (style, glyph, label) = if active {
            (Style::Pale, icons::TOUCH, "Colocar en escena")
        } else {
            (Style::Ghost, icons::ADD, "Seleccionar")
        };
        let response = widgets::wide_button(
            ui,
            style,
            Some(glyph),
            label,
            theme::mono_medium(14.0),
            34.0,
        );
        if response.clicked() {
            select(studio, index);
            if active {
                studio.page = Page::Build;
            }
        }
    });
}

/// Makes a species the one the Build placement tool plants.
pub fn select(studio: &mut Studio, index: usize) {
    studio.build.species = index;
    studio.build.height = CATALOG[index].height as f32;
    studio.build.spread = CATALOG[index].spread as f32;
    studio.build.tool = crate::app::Tool::Place;
}

fn footer_bar(ui: &mut Ui, studio: &Studio) {
    ui.spacing_mut().item_spacing.x = 8.0;
    for (glyph, label) in [
        (icons::SCATTER, "Pintura de grupos"),
        (icons::HUB, "Dispersión"),
    ] {
        ui.add_enabled_ui(false, |ui| {
            widgets::button(
                ui,
                Style::Quiet,
                Some(glyph),
                label,
                theme::mono(13.0),
                30.0,
            )
        })
        .inner
        .on_disabled_hover_text("Aún no disponible");
    }
    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
        ui.spacing_mut().item_spacing.x = 4.0;
        text(
            ui,
            studio.build.placed.len().to_string(),
            theme::mono_medium(14.0),
            theme::TEXT,
        );
        text(
            ui,
            "Ejemplares en escena:",
            theme::mono(13.0),
            theme::TEXT_VARIANT,
        );
        ui.add_space(10.0);
        let backend = if studio.viewport.facts.backend.is_empty() {
            "listo".to_owned()
        } else {
            format!("trazador GPU {} listo", studio.viewport.facts.backend)
        };
        text(ui, backend, theme::mono(13.0), theme::PRIMARY);
        widgets::status(ui, "Motor:", theme::TELEMETRY, theme::mono(13.0));
    });
}
