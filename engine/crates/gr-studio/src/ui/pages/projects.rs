//! Projects: the open project featured with its live navigation view and its real figures, then the recently opened
//! projects as cards with the picture captured the last time each was shown.

use super::common::{self, grouped, size};
use crate::app::{Page, Studio};
use crate::recent_projects::RecentProject;
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use eframe::egui::{self, Align, Align2, Layout, Rect, Sense, Ui, pos2, vec2};
use eframe::egui_wgpu::RenderState;

#[derive(Default)]
pub struct ProjectsUi {
    pub search: String,
    pub list: bool,
}

pub fn show(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>, local: &mut ProjectsUi) {
    let full = ui.max_rect();
    let header = Rect::from_min_size(full.min, vec2(full.width(), 64.0));
    ui.painter().rect_filled(header, 0.0, theme::PANEL);
    ui.painter()
        .hline(full.x_range(), header.bottom(), theme::hairline());
    let mut header_ui = ui.new_child(
        egui::UiBuilder::new()
            .max_rect(header.shrink2(vec2(20.0, 0.0)))
            .layout(Layout::left_to_right(Align::Center)),
    );
    header_bar(&mut header_ui, studio, local);

    let filters = Rect::from_min_size(
        pos2(full.left(), header.bottom() + 1.0),
        vec2(full.width(), 44.0),
    );
    ui.painter().rect_filled(filters, 0.0, theme::PANEL);
    ui.painter()
        .hline(full.x_range(), filters.bottom(), theme::hairline());
    let mut filter_ui = ui.new_child(
        egui::UiBuilder::new()
            .max_rect(filters.shrink2(vec2(20.0, 0.0)))
            .layout(Layout::left_to_right(Align::Center)),
    );
    filter_bar(&mut filter_ui, studio, local);

    let body = Rect::from_min_max(pos2(full.left(), filters.bottom() + 1.0), full.max);
    let mut body_ui = ui.new_child(egui::UiBuilder::new().max_rect(body));
    egui::ScrollArea::vertical()
        .auto_shrink([false, false])
        .show(&mut body_ui, |ui| {
            egui::Frame::new()
                .inner_margin(egui::Margin::symmetric(20, 18))
                .show(ui, |ui| {
                    ui.set_width(ui.available_width());
                    let recent = filtered(studio, &local.search);
                    section_header(ui, &mut local.list, recent.len());
                    ui.add_space(12.0);
                    featured(ui, studio, state);
                    ui.add_space(20.0);
                    if local.list {
                        list(ui, studio, &recent);
                    } else {
                        cards(ui, studio, &recent);
                    }
                    ui.add_space(20.0);
                    batch_bar(ui);
                });
        });
}

fn filtered(studio: &Studio, search: &str) -> Vec<RecentProject> {
    let needle = search.trim().to_lowercase();
    studio
        .recent
        .iter()
        .filter(|project| needle.is_empty() || project.name.to_lowercase().contains(&needle))
        .cloned()
        .collect()
}

fn header_bar(ui: &mut Ui, studio: &mut Studio, local: &mut ProjectsUi) {
    ui.spacing_mut().item_spacing.x = 10.0;
    text(ui, "Proyectos", theme::heading(30.0), theme::TEXT);
    text(
        ui,
        "REPOSITORIO DE TRABAJO",
        theme::mono(12.0),
        theme::TEXT_VARIANT,
    );
    ui.add_space(6.0);
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .stroke(theme::hairline())
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(10, 5))
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 4.0;
                let active = studio.viewport.has_scene();
                widgets::status(
                    ui,
                    if active {
                        "Sesión: activa"
                    } else {
                        "Sesión: sin proyecto"
                    },
                    if active {
                        theme::TELEMETRY
                    } else {
                        theme::OUTLINE
                    },
                    theme::mono_medium(14.0),
                );
                text(ui, "/", theme::mono(14.0), theme::OUTLINE);
                text(
                    ui,
                    format!("Guanacaste Render v{}", env!("CARGO_PKG_VERSION")),
                    theme::mono(14.0),
                    theme::TELEMETRY,
                );
            });
        });
    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
        ui.spacing_mut().item_spacing.x = 10.0;
        if widgets::button(
            ui,
            Style::Primary,
            Some(icons::ADD_BOX),
            "ABRIR PROYECTO",
            theme::mono_medium(14.0),
            34.0,
        )
        .clicked()
        {
            studio.open_dialog(ui.ctx());
        }
        widgets::icon_button(
            ui,
            icons::NOTIFICATIONS,
            34.0,
            false,
            "Sin avisos pendientes",
        );
        egui::Frame::new()
            .fill(theme::RECESSED)
            .stroke(theme::hairline())
            .corner_radius(2)
            .inner_margin(egui::Margin::symmetric(10, 4))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    widgets::icon(ui, icons::SEARCH, 16.0, theme::OUTLINE);
                    ui.add_sized(
                        vec2(260.0, 24.0),
                        egui::TextEdit::singleline(&mut local.search)
                            .hint_text("Buscar proyectos recientes…")
                            .frame(egui::Frame::NONE)
                            .font(theme::body(14.0)),
                    );
                });
            });
    });
}

fn filter_bar(ui: &mut Ui, studio: &Studio, local: &mut ProjectsUi) {
    ui.spacing_mut().item_spacing.x = 6.0;
    widgets::chip(
        ui,
        None,
        "Todos los proyectos",
        Some(studio.recent.len()),
        true,
    );
    for (glyph, label, reason) in [
        (
            icons::CHECK_CIRCLE,
            "Cola de render",
            "La cola de render aún no existe",
        ),
        (
            icons::CLOUD,
            "Sincronizado",
            "La sincronización en la nube aún no existe",
        ),
        (
            icons::ARCHIVE,
            "Archivados",
            "El archivo de proyectos aún no existe",
        ),
    ] {
        ui.add_enabled_ui(false, |ui| {
            widgets::chip(ui, Some(glyph), label, None, false)
        })
        .inner
        .on_disabled_hover_text(reason);
    }
    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
        ui.spacing_mut().item_spacing.x = 4.0;
        if widgets::icon_button(ui, icons::LIST, 28.0, local.list, "Lista").clicked() {
            local.list = true;
        }
        if widgets::icon_button(ui, icons::GRID, 28.0, !local.list, "Cuadrícula").clicked() {
            local.list = false;
        }
        ui.add_space(12.0);
        if let Some(total) = common::video_memory_total(studio) {
            let used = studio.viewport.video_megabytes() * 1024.0 * 1024.0;
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 4.0;
                text(
                    ui,
                    format!("{} / {}", size(used), size(total)),
                    theme::mono(14.0),
                    theme::TELEMETRY,
                );
                text(
                    ui,
                    "Memoria de video de la escena:",
                    theme::mono(14.0),
                    theme::TEXT,
                );
                widgets::icon(ui, icons::MEMORY, 16.0, theme::TELEMETRY);
            });
        }
    });
}

fn section_header(ui: &mut Ui, list: &mut bool, count: usize) {
    ui.horizontal(|ui| {
        let (bar, _) = ui.allocate_exact_size(vec2(4.0, 22.0), Sense::hover());
        ui.painter().rect_filled(bar, 0.0, theme::PRIMARY_CONTAINER);
        text(
            ui,
            "Escenas arquitectónicas recientes",
            theme::heading(24.0),
            theme::TEXT,
        );
        egui::Frame::new()
            .fill(theme::HIGHEST)
            .corner_radius(2)
            .inner_margin(egui::Margin::symmetric(6, 2))
            .show(ui, |ui| {
                text(
                    ui,
                    count.to_string(),
                    theme::mono(12.0),
                    theme::TEXT_VARIANT,
                );
            });
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            let label = if *list {
                "Ver como tarjetas"
            } else {
                "Ver todos en lista"
            };
            let link = widgets::button(
                ui,
                Style::Quiet,
                Some(icons::ARROW_FORWARD),
                label,
                theme::mono_medium(14.0),
                30.0,
            );
            if link.clicked() {
                *list = !*list;
            }
        });
    });
}

/// The open project, large: its live navigation view on the left and its figures on the right.
fn featured(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>) {
    let width = ui.available_width();
    // Like the prototype, the featured project leaves the top of the recent cards in view.
    let height = (width * 0.68 * 9.0 / 16.0)
        .min(ui.clip_rect().height() - 150.0)
        .clamp(380.0, 620.0);
    let (rect, _) = ui.allocate_exact_size(vec2(width, height), Sense::hover());
    ui.painter().rect_filled(rect, 4.0, theme::CARD);
    ui.painter()
        .rect_stroke(rect, 4.0, theme::hairline(), egui::StrokeKind::Inside);
    let view = Rect::from_min_size(rect.min, vec2(width * 0.68, height));
    let info = Rect::from_min_max(pos2(view.right(), rect.top()), rect.max).shrink(20.0);

    let live = state.filter(|_| studio.viewport.has_scene());
    match live {
        Some(state) => {
            let mut view_ui = ui.new_child(egui::UiBuilder::new().max_rect(view));
            view_ui.set_clip_rect(view);
            studio.viewport.show(&mut view_ui, view, state);
            let painter = ui.painter_at(view);
            let badge =
                Rect::from_min_size(view.min + vec2(10.0, 10.0), vec2(view.width() * 0.6, 24.0));
            let fps = studio.telemetry.fps_label();
            painter.rect_filled(
                Rect::from_min_size(badge.min, vec2(330.0, 24.0)),
                2.0,
                theme::PANEL.gamma_multiply(0.85),
            );
            painter.circle_filled(
                pos2(badge.left() + 10.0, badge.center().y),
                4.0,
                theme::TELEMETRY,
            );
            painter.text(
                pos2(badge.left() + 20.0, badge.center().y),
                Align2::LEFT_CENTER,
                format!(
                    "ESCENA ACTIVA  {}  {fps}",
                    common::mode_label(studio).split(" ·").next().unwrap_or("")
                ),
                theme::mono_medium(13.0),
                theme::TELEMETRY,
            );
            let camera = studio.viewport.camera();
            let footer = Rect::from_min_max(pos2(view.left(), view.bottom() - 44.0), view.max);
            painter.rect_filled(footer, 0.0, theme::PANEL.gamma_multiply(0.8));
            let (w, h) = studio.viewport.resolution();
            painter.text(
                pos2(footer.left() + 16.0, footer.center().y),
                Align2::LEFT_CENTER,
                format!(
                    "CÁM: órbita  CDV: {:.0}°  EXP: {:.2}",
                    camera.vertical_fov_degrees, studio.viewport.settings.exposure
                ),
                theme::mono(15.0),
                theme::TEXT,
            );
            painter.text(
                pos2(footer.right() - 16.0, footer.center().y),
                Align2::RIGHT_CENTER,
                format!("{w}×{h}"),
                theme::mono(15.0),
                theme::TEXT_VARIANT,
            );
        }
        None => {
            let painter = ui.painter_at(view);
            painter.rect_filled(view, 4.0, theme::RECESSED);
            let first = studio.recent.first().cloned();
            match first
                .as_ref()
                .and_then(|project| studio.thumbnails.get(ui.ctx(), &project.path))
            {
                Some(texture) => {
                    painter.image(
                        texture.id(),
                        view,
                        Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)),
                        egui::Color32::WHITE.gamma_multiply(0.55),
                    );
                }
                None => {
                    painter.text(
                        view.center() - vec2(0.0, 18.0),
                        Align2::CENTER_CENTER,
                        icons::LAYERS,
                        theme::icon(48.0),
                        theme::HIGHEST,
                    );
                }
            }
            let message = if let Some((_, name)) = &studio.loading {
                format!("Abriendo {name}…")
            } else {
                "Ningún proyecto abierto".into()
            };
            painter.text(
                view.center() + vec2(0.0, 30.0),
                Align2::CENTER_CENTER,
                message,
                theme::heading(18.0),
                theme::TEXT,
            );
        }
    }

    let mut info_ui = ui.new_child(egui::UiBuilder::new().max_rect(info));
    let ui = &mut info_ui;
    ui.horizontal(|ui| {
        text(
            ui,
            "ESPACIO DE TRABAJO ACTIVO",
            theme::mono_medium(14.0),
            theme::PRIMARY,
        );
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            if let Some(age) = studio.modified_ago() {
                text(
                    ui,
                    format!("Modificado hace {}", age_text(age)),
                    theme::mono(13.0),
                    theme::TEXT_VARIANT,
                );
            }
        });
    });
    ui.add_space(8.0);
    let name = studio
        .project_name()
        .unwrap_or_else(|| "Sin proyecto abierto".into());
    text(ui, name, theme::heading(30.0), theme::TEXT);
    ui.add_space(4.0);
    let description = match (&studio.project, &studio.notice) {
        (Some(path), notice) => {
            let folder = path.parent().map(|folder| folder.display().to_string()).unwrap_or_default();
            let kind = path.extension().map(|ext| ext.to_string_lossy().to_uppercase()).unwrap_or_default();
            let base = format!("Proyecto {kind} en {folder}");
            match notice {
                Some(notice) => format!("{base}\n{notice}"),
                None => base,
            }
        }
        (None, _) => "Abre un proyecto .gr o .g3d de Guanacaste 3D para navegarlo, poblarlo de vegetación y renderizarlo.".into(),
    };
    ui.add(
        egui::Label::new(
            egui::RichText::new(description)
                .font(theme::body(15.0))
                .color(theme::TEXT_VARIANT),
        )
        .wrap(),
    );
    ui.add_space(12.0);
    let facts = &studio.viewport.facts;
    let file = studio
        .project
        .as_ref()
        .and_then(|path| std::fs::metadata(path).ok())
        .map(|meta| size(meta.len() as f64));
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            let engine = if facts.backend.is_empty() {
                "—".to_owned()
            } else {
                format!("Trazador GPU · {}", facts.backend)
            };
            let cells = [
                ("ESTADO DEL MOTOR", engine, theme::PRIMARY),
                (
                    "TAMAÑO DEL ARCHIVO",
                    file.unwrap_or_else(|| "—".into()),
                    theme::TEXT,
                ),
                (
                    "GEOMETRÍA",
                    format!("{} triángulos", grouped(facts.triangles)),
                    theme::TEXT,
                ),
                (
                    "ILUMINACIÓN",
                    format!("{} rebotes · cielo", studio.viewport.settings.max_bounces),
                    theme::PRIMARY,
                ),
            ];
            for pair in cells.chunks(2) {
                ui.columns(2, |columns| {
                    for ((label, value, color), column) in pair.iter().zip(columns.iter_mut()) {
                        text(column, *label, theme::mono(12.0), theme::OUTLINE);
                        text(column, value.clone(), theme::mono(15.0), *color);
                    }
                });
                ui.add_space(6.0);
            }
        });
    ui.add_space(12.0);
    if let Some((_, _, average)) = studio.telemetry.stats() {
        ui.horizontal(|ui| {
            text(
                ui,
                "Estabilidad del fotograma",
                theme::mono(13.0),
                theme::TEXT_VARIANT,
            );
            text(
                ui,
                format!("{average:.1} ms prom."),
                theme::mono(13.0),
                theme::TELEMETRY,
            );
        });
        let values: Vec<f32> = studio.telemetry.frame_ms.iter().copied().collect();
        widgets::sparkline(
            ui,
            &values,
            0.0..=50.0,
            vec2(ui.available_width(), 40.0),
            &[],
        );
    }
    let bottom = Rect::from_min_max(pos2(info.left(), info.bottom() - 40.0), info.max);
    let mut actions = ui.new_child(
        egui::UiBuilder::new()
            .max_rect(bottom)
            .layout(Layout::left_to_right(Align::Center)),
    );
    let has_scene = studio.viewport.has_scene();
    actions.add_enabled_ui(has_scene, |ui| {
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            if widgets::icon_button(ui, icons::SETTINGS, 40.0, false, "Ajustes de calidad")
                .clicked()
            {
                studio.page = Page::Settings;
            }
            if widgets::button(
                ui,
                Style::Quiet,
                Some(icons::CAMERA),
                "Render",
                theme::mono_medium(15.0),
                40.0,
            )
            .clicked()
            {
                studio.page = Page::Render;
            }
            if widgets::wide_button(
                ui,
                Style::Pale,
                Some(icons::PLAY),
                "Abrir vista",
                theme::mono_medium(15.0),
                40.0,
            )
            .clicked()
            {
                studio.page = Page::Build;
            }
        });
    });
}

fn age_text(age: std::time::Duration) -> String {
    let minutes = age.as_secs() / 60;
    match minutes {
        0 => "un momento".into(),
        1..60 => format!("{minutes} min"),
        60..1440 => format!("{} h", minutes / 60),
        _ => format!("{} d", minutes / 1440),
    }
}

fn cards(ui: &mut Ui, studio: &mut Studio, recent: &[RecentProject]) {
    if recent.is_empty() {
        text(
            ui,
            "Todavía no has abierto proyectos. Usa «Abrir proyecto».",
            theme::body(14.0),
            theme::TEXT_VARIANT,
        );
        return;
    }
    let columns = 4usize;
    let gap = 16.0;
    let width = (ui.available_width() - gap * (columns - 1) as f32) / columns as f32;
    for row in recent.chunks(columns) {
        ui.horizontal_top(|ui| {
            ui.spacing_mut().item_spacing.x = gap;
            for project in row {
                ui.allocate_ui_with_layout(vec2(width, 0.0), Layout::top_down(Align::Min), |ui| {
                    card(ui, studio, project, width)
                });
            }
        });
        ui.add_space(gap);
    }
}

fn card(ui: &mut Ui, studio: &mut Studio, project: &RecentProject, width: f32) {
    theme::card()
        .inner_margin(egui::Margin::ZERO)
        .show(ui, |ui| {
            ui.set_width(width);
            let (picture, _) = ui.allocate_exact_size(vec2(width, width * 0.58), Sense::hover());
            let painter = ui.painter_at(picture);
            match studio.thumbnails.get(ui.ctx(), &project.path) {
                Some(texture) => {
                    let aspect = texture.aspect_ratio();
                    let frame_aspect = picture.width() / picture.height();
                    // Cover the frame: crop the picture's longer side.
                    let uv = if aspect > frame_aspect {
                        let crop = (1.0 - frame_aspect / aspect) / 2.0;
                        Rect::from_min_max(pos2(crop, 0.0), pos2(1.0 - crop, 1.0))
                    } else {
                        let crop = (1.0 - aspect / frame_aspect) / 2.0;
                        Rect::from_min_max(pos2(0.0, crop), pos2(1.0, 1.0 - crop))
                    };
                    painter.image(texture.id(), picture, uv, egui::Color32::WHITE);
                }
                None => {
                    painter.rect_filled(picture, 0.0, theme::RECESSED);
                    painter.text(
                        picture.center(),
                        Align2::CENTER_CENTER,
                        icons::IMAGE,
                        theme::icon(36.0),
                        theme::HIGHEST,
                    );
                    painter.text(
                        picture.center() + vec2(0.0, 30.0),
                        Align2::CENTER_CENTER,
                        "Sin vista guardada",
                        theme::mono(11.0),
                        theme::OUTLINE,
                    );
                }
            }
            let open = studio.project.as_ref() == Some(&project.path);
            let tag = if open {
                "Abierto"
            } else {
                extension(&project.path)
            };
            let galley = painter.layout_no_wrap(tag.to_owned(), theme::mono(13.0), theme::TEXT);
            let tag_rect = Rect::from_min_size(
                picture.min + vec2(8.0, 8.0),
                galley.size() + vec2(14.0, 8.0),
            );
            painter.rect_filled(tag_rect, 2.0, theme::PANEL.gamma_multiply(0.85));
            painter.galley(
                tag_rect.min + vec2(7.0, 4.0),
                galley,
                if open { theme::TELEMETRY } else { theme::TEXT },
            );

            egui::Frame::new()
                .inner_margin(egui::Margin::same(14))
                .show(ui, |ui| {
                    ui.set_width(width - 28.0);
                    ui.horizontal(|ui| {
                        text(
                            ui,
                            format!("TIPO: .{}", extension(&project.path)),
                            theme::mono(12.0),
                            theme::OUTLINE,
                        );
                        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                            let size_text = project
                                .file_size_mb
                                .map_or("—".into(), |mb| size(mb * 1024.0 * 1024.0));
                            text(ui, size_text, theme::mono(12.0), theme::OUTLINE);
                        });
                    });
                    ui.add_space(4.0);
                    text(ui, &project.name, theme::heading(19.0), theme::TEXT);
                    let folder = project
                        .path
                        .parent()
                        .map(|parent| parent.display().to_string())
                        .unwrap_or_default();
                    ui.add(
                        egui::Label::new(
                            egui::RichText::new(folder)
                                .font(theme::body(13.0))
                                .color(theme::TEXT_VARIANT),
                        )
                        .truncate(),
                    );
                    ui.add_space(8.0);
                    let exists = project.path.is_file();
                    egui::Frame::new()
                        .fill(theme::CONTAINER)
                        .corner_radius(2)
                        .inner_margin(egui::Margin::symmetric(8, 5))
                        .show(ui, |ui| {
                            ui.set_width(ui.available_width());
                            ui.horizontal(|ui| {
                                text(ui, "Archivo:", theme::mono(12.0), theme::OUTLINE);
                                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                    let (label, color) = if exists {
                                        ("disponible", theme::TELEMETRY)
                                    } else {
                                        ("no encontrado", theme::WARNING)
                                    };
                                    text(ui, label, theme::mono(12.0), color);
                                });
                            });
                        });
                    ui.add_space(8.0);
                    ui.add_enabled_ui(exists && studio.loading.is_none(), |ui| {
                        let label = if open { "Continuar" } else { "Abrir proyecto" };
                        ui.horizontal(|ui| {
                            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                if widgets::icon_button(
                                    ui,
                                    icons::OPEN_IN_NEW,
                                    32.0,
                                    false,
                                    "Mostrar en el explorador",
                                )
                                .clicked()
                                {
                                    let _ = std::process::Command::new("explorer")
                                        .arg("/select,")
                                        .arg(&project.path)
                                        .spawn();
                                }
                                if widgets::wide_button(
                                    ui,
                                    Style::Ghost,
                                    Some(icons::FOLDER_OPEN),
                                    label,
                                    theme::mono_medium(13.0),
                                    32.0,
                                )
                                .clicked()
                                {
                                    if open {
                                        studio.page = Page::Build;
                                    } else {
                                        studio.open(project.path.clone(), ui.ctx());
                                    }
                                }
                            });
                        });
                    });
                });
        });
}

fn list(ui: &mut Ui, studio: &mut Studio, recent: &[RecentProject]) {
    theme::card().show(ui, |ui| {
        ui.set_width(ui.available_width());
        for project in recent {
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::FOLDER_OPEN, 18.0, theme::PRIMARY);
                text(ui, &project.name, theme::heading(15.0), theme::TEXT);
                text(
                    ui,
                    project.path.display().to_string(),
                    theme::mono(12.0),
                    theme::OUTLINE,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    if ui
                        .add_enabled_ui(project.path.is_file(), |ui| {
                            widgets::button(
                                ui,
                                Style::Ghost,
                                None,
                                "Abrir",
                                theme::mono_medium(12.0),
                                26.0,
                            )
                        })
                        .inner
                        .clicked()
                    {
                        studio.open(project.path.clone(), ui.ctx());
                    }
                    text(
                        ui,
                        project
                            .file_size_mb
                            .map_or("—".into(), |mb| size(mb * 1024.0 * 1024.0)),
                        theme::mono(12.0),
                        theme::TEXT_VARIANT,
                    );
                });
            });
            widgets::rule(ui);
        }
    });
}

fn extension(path: &std::path::Path) -> &'static str {
    match path
        .extension()
        .and_then(|ext| ext.to_str())
        .map(str::to_lowercase)
        .as_deref()
    {
        Some("g3d") => "g3d",
        Some("grscene") => "grscene",
        _ => "gr",
    }
}

/// The prototype's batch dispatcher, shown for what it is: not available yet.
fn batch_bar(ui: &mut Ui) {
    theme::card().inner_margin(egui::Margin::same(16)).show(ui, |ui| {
        ui.set_width(ui.available_width());
        ui.add_enabled_ui(false, |ui| {
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::HUB, 26.0, theme::PRIMARY);
                ui.vertical(|ui| {
                    text(ui, "Render por lotes", theme::heading(19.0), theme::TEXT);
                    text(ui, "Aún no disponible: por ahora cada render se lanza desde el Estudio de render.", theme::mono(12.0), theme::TEXT_VARIANT);
                });
            });
        });
    });
}
