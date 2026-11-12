//! Help and diagnostics: a guided tour of the studio, the controls as the view actually reads them, and what this
//! computer is drawing with.

use super::common;
use crate::app::{Page, Studio};
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use eframe::egui::{self, Align, Align2, Color32, Layout, Rect, Sense, Ui, pos2, vec2};
use eframe::egui_wgpu::RenderState;

/// The guided tour: title, what to do, where it happens.
const STEPS: [(&str, &str, &str, Page); 6] = [
    (
        "Abrir un proyecto",
        "Proyectos",
        "En Proyectos pulsa «Abrir proyecto» y elige un archivo .gr o .g3d de Guanacaste 3D. Los recientes quedan como tarjetas con su vista.",
        Page::Projects,
    ),
    (
        "Navegar la vista 3D",
        "Vista en tiempo real",
        "Arrastra con el botón izquierdo para orbitar, con el derecho para desplazar y usa la rueda para acercar. WASD camina, Q y E bajan y suben, Mayús corre ×4.",
        Page::Build,
    ),
    (
        "Vista previa y render",
        "Calidad de vista",
        "La navegación no traza rayos: es rápida y ligera. «Vista previa» (V) hace una pasada corta de trazado; «Render» (R) traza la imagen final.",
        Page::Build,
    ),
    (
        "Poblar con vegetación",
        "Construcción",
        "Elige una especie en la Biblioteca, activa Colocar y haz clic sobre el terreno. Seleccionar y Eliminar actúan sobre las plantas de esta sesión.",
        Page::Library,
    ),
    (
        "Ajustar materiales",
        "Editor de materiales",
        "Haz clic en una superficie para elegir su material, cambia color, rugosidad, metal o transmisión y pulsa «Aplicar material».",
        Page::Materials,
    ),
    (
        "Animar y exportar",
        "Estudio de película",
        "Añade fotogramas clave desde distintas cámaras; la curva pasa por todas. «Renderizar clip» guarda un PNG trazado por fotograma.",
        Page::Movie,
    ),
];

pub fn show(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>) {
    let full = ui.max_rect().shrink(14.0);
    let header = Rect::from_min_size(full.min, vec2(full.width(), 44.0));
    ui.painter().rect_filled(header, 2.0, theme::PANEL);
    let mut header_ui = ui.new_child(
        egui::UiBuilder::new()
            .max_rect(header.shrink2(vec2(14.0, 0.0)))
            .layout(Layout::left_to_right(Align::Center)),
    );
    header_bar(&mut header_ui, studio);

    let body = Rect::from_min_max(pos2(full.left(), header.bottom() + 14.0), full.max);
    let mut body_ui = ui.new_child(egui::UiBuilder::new().max_rect(body));
    egui::ScrollArea::vertical()
        .auto_shrink([false, false])
        .show(&mut body_ui, |ui| {
            let width = ui.available_width();
            let left_width = (width * 0.43).min(640.0);
            ui.horizontal_top(|ui| {
                ui.spacing_mut().item_spacing.x = 14.0;
                ui.allocate_ui_with_layout(
                    vec2(left_width, 0.0),
                    Layout::top_down(Align::Min),
                    |ui| {
                        ui.set_width(left_width);
                        tour(ui, studio, state);
                        ui.add_space(14.0);
                        knowledge(ui);
                    },
                );
                ui.allocate_ui_with_layout(
                    vec2(width - left_width - 14.0, 0.0),
                    Layout::top_down(Align::Min),
                    |ui| {
                        ui.set_width(width - left_width - 14.0);
                        input_mapping(ui);
                        ui.add_space(14.0);
                        ui.columns(2, |columns| {
                            shortcuts(
                                &mut columns[0],
                                "COLOCACIÓN",
                                icons::NATURE,
                                &[
                                    ("Herramienta Colocar", "P"),
                                    ("Plantar en el punto", "Clic"),
                                    ("Volver a Seleccionar", "Esc"),
                                    ("Borrar planta elegida", "Supr"),
                                ],
                            );
                            shortcuts(
                                &mut columns[1],
                                "VISTA Y SALIDA",
                                icons::CAMERA,
                                &[
                                    ("Vista previa trazada", "V"),
                                    ("Render final", "R"),
                                    ("Vista inicial", "H"),
                                    ("Guardar imagen", "Ctrl + S"),
                                ],
                            );
                        });
                    },
                );
            });
            ui.add_space(14.0);
            diagnostics(ui, studio);
        });
}

fn header_bar(ui: &mut Ui, studio: &mut Studio) {
    ui.spacing_mut().item_spacing.x = 10.0;
    if widgets::icon_button(ui, icons::CLOSE, 28.0, false, "Cerrar la ayuda").clicked() {
        studio.page = if studio.viewport.has_scene() {
            Page::Build
        } else {
            Page::Projects
        };
    }
    text(ui, "Ayuda y diagnóstico", theme::heading(21.0), theme::TEXT);
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(6, 2))
        .show(ui, |ui| {
            text(
                ui,
                studio
                    .project_name()
                    .unwrap_or_else(|| "Sin proyecto".into()),
                theme::mono(13.0),
                theme::TEXT_VARIANT,
            );
        });
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(6, 2))
        .show(ui, |ui| {
            widgets::status(
                ui,
                &format!(
                    "{} · {}",
                    studio.telemetry.fps_label(),
                    common::mode_label(studio).split(" ·").next().unwrap_or("")
                ),
                theme::TELEMETRY,
                theme::mono(13.0),
            );
        });
}

fn tour(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>) {
    let step = studio.help_step.min(STEPS.len() - 1);
    let (title, place, description, page) = STEPS[step];
    egui::Frame::new()
        .fill(theme::PANEL)
        .stroke(theme::hairline())
        .corner_radius(4)
        .inner_margin(egui::Margin::same(14))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                widgets::tag(
                    ui,
                    &format!("PASO {} DE {}", step + 1, STEPS.len()),
                    theme::ACTIVE,
                    Color32::WHITE,
                );
                text(ui, place, theme::mono(13.0), theme::TEXT_VARIANT);
            });
            ui.add_space(6.0);
            text(ui, title, theme::heading(26.0), theme::TEXT);
            ui.add(
                egui::Label::new(
                    egui::RichText::new(description)
                        .font(theme::body(16.0))
                        .color(theme::TEXT_VARIANT),
                )
                .wrap(),
            );
            ui.add_space(8.0);
            let width = ui.available_width();
            let (rect, _) = ui.allocate_exact_size(vec2(width, width * 0.38), Sense::hover());
            match state.filter(|_| studio.viewport.has_scene()) {
                Some(state) => {
                    let mut view_ui = ui.new_child(egui::UiBuilder::new().max_rect(rect));
                    view_ui.set_clip_rect(rect);
                    studio.viewport.show(&mut view_ui, rect, state);
                }
                None => {
                    ui.painter().rect_filled(rect, 2.0, theme::RECESSED);
                    ui.painter().text(
                        rect.center(),
                        Align2::CENTER_CENTER,
                        icons::LAYERS,
                        theme::icon(40.0),
                        theme::HIGHEST,
                    );
                }
            }
            let painter = ui.painter_at(rect);
            let footer = Rect::from_min_max(pos2(rect.left(), rect.bottom() - 30.0), rect.max);
            painter.rect_filled(footer, 0.0, theme::PANEL.gamma_multiply(0.85));
            painter.text(
                pos2(footer.left() + 10.0, footer.center().y),
                Align2::LEFT_CENTER,
                format!("{}  Prueba aquí los controles", icons::VIDEOCAM),
                theme::mono(13.0),
                theme::TEXT,
            );
            painter.text(
                pos2(footer.right() - 10.0, footer.center().y),
                Align2::RIGHT_CENTER,
                "Vista en vivo",
                theme::mono(13.0),
                theme::PRIMARY,
            );
            ui.add_space(8.0);
            ui.columns(2, |columns| {
                feature(
                    &mut columns[0],
                    icons::MOUSE,
                    "Ratón: orbitar",
                    "Clic izquierdo y arrastrar",
                );
                feature(
                    &mut columns[1],
                    icons::KEYBOARD,
                    "WASD: caminar",
                    "Traslación en el plano",
                );
                feature(
                    &mut columns[0],
                    icons::SWAP_VERTICAL,
                    "Q / E: altura",
                    "Bajar y subir la cámara",
                );
                feature(
                    &mut columns[1],
                    icons::GRID_ON,
                    "Escala métrica",
                    "Unidades del proyecto en metros",
                );
            });
            ui.add_space(8.0);
            ui.horizontal(|ui| {
                let (dots, _) = ui.allocate_exact_size(vec2(120.0, 8.0), Sense::hover());
                for index in 0..STEPS.len() {
                    let x = dots.left() + index as f32 * 18.0;
                    let width = if index == step { 28.0 } else { 10.0 };
                    let x = if index > step { x + 18.0 } else { x };
                    ui.painter().rect_filled(
                        Rect::from_min_size(pos2(x, dots.center().y - 2.0), vec2(width, 4.0)),
                        2.0,
                        if index == step {
                            theme::PRIMARY
                        } else {
                            theme::HIGHEST
                        },
                    );
                }
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    if widgets::button(
                        ui,
                        Style::Pale,
                        Some(icons::ARROW_FORWARD),
                        "Siguiente paso",
                        theme::mono_medium(14.0),
                        34.0,
                    )
                    .clicked()
                    {
                        studio.help_step = (step + 1) % STEPS.len();
                    }
                    if ui
                        .add_enabled_ui(step > 0, |ui| {
                            widgets::button(
                                ui,
                                Style::Quiet,
                                Some(icons::ARROW_BACK),
                                "Anterior",
                                theme::mono(14.0),
                                34.0,
                            )
                        })
                        .inner
                        .clicked()
                    {
                        studio.help_step = step - 1;
                    }
                    if widgets::button(
                        ui,
                        Style::Quiet,
                        Some(icons::OPEN_IN_NEW),
                        "Ir allí",
                        theme::mono(14.0),
                        34.0,
                    )
                    .clicked()
                    {
                        studio.page = page;
                    }
                });
            });
        });
}

fn feature(ui: &mut Ui, glyph: char, title: &str, note: &str) {
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(10, 6))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                widgets::icon(ui, glyph, 20.0, theme::TEXT);
                ui.vertical(|ui| {
                    ui.spacing_mut().item_spacing.y = 0.0;
                    text(ui, title, theme::mono_medium(14.0), theme::TEXT);
                    text(ui, note, theme::body(14.0), theme::TEXT_VARIANT);
                });
            });
        });
}

fn knowledge(ui: &mut Ui) {
    egui::Frame::new().fill(theme::PANEL).stroke(theme::hairline()).corner_radius(4).inner_margin(egui::Margin::same(10)).show(ui, |ui| {
        ui.set_width(ui.available_width());
        ui.horizontal(|ui| {
            text(ui, "CÓMO FUNCIONA EL MOTOR", theme::mono_medium(14.0), theme::TEXT_VARIANT);
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| text(ui, format!("v{}", env!("CARGO_PKG_VERSION")), theme::mono(13.0), theme::PRIMARY));
        });
        ui.add_space(6.0);
        for (glyph, title, note) in [
            (icons::SPARKLE, "Trazado de rayos en la GPU", "BVH plano, rebotes de luz, vidrio con refracción y reducción de ruido À-trous"),
            (icons::NATURE, "Vegetación procedural", "Cada planta crece del mismo generador que usa Guanacaste 3D, con hojas reales"),
            (icons::BOLT, "Navegación sin trazado", "Rasterizado con MSAA ×4 y el mismo sol y cielo: solo redibuja cuando algo cambia"),
        ] {
            egui::Frame::new().fill(theme::CONTAINER).corner_radius(2).inner_margin(egui::Margin::same(10)).show(ui, |ui| {
                ui.set_width(ui.available_width());
                ui.horizontal(|ui| {
                    let (square, _) = ui.allocate_exact_size(vec2(38.0, 38.0), Sense::hover());
                    ui.painter().rect_filled(square, 2.0, theme::HIGHEST);
                    ui.painter().text(square.center(), Align2::CENTER_CENTER, glyph, theme::icon(20.0), theme::TELEMETRY);
                    ui.vertical(|ui| {
                        text(ui, title, theme::heading(17.0), theme::TEXT);
                        text(ui, note, theme::body(14.0), theme::TEXT_VARIANT);
                    });
                });
            });
            ui.add_space(4.0);
        }
    });
}

fn input_mapping(ui: &mut Ui) {
    egui::Frame::new()
        .fill(theme::PANEL)
        .stroke(theme::hairline())
        .corner_radius(4)
        .inner_margin(egui::Margin::same(14))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                ui.vertical(|ui| {
                    text(
                        ui,
                        "MAPA DE ENTRADA",
                        theme::mono_medium(15.0),
                        theme::TELEMETRY,
                    );
                    text(
                        ui,
                        "Ratón y teclado en la vista 3D",
                        theme::heading(20.0),
                        theme::TEXT,
                    );
                });
                ui.with_layout(Layout::right_to_left(Align::Min), |ui| {
                    widgets::icon_text(
                        ui,
                        icons::MOUSE,
                        "Ratón y teclado: activos",
                        theme::mono(13.0),
                        theme::TEXT,
                    )
                });
            });
            ui.add_space(10.0);
            let (area, _) =
                ui.allocate_exact_size(vec2(ui.available_width(), 290.0), Sense::hover());
            let painter = ui.painter_at(area);
            painter.rect_filled(area, 2.0, theme::CONTAINER);
            // The mouse.
            let mouse = Rect::from_min_size(
                area.min + vec2(area.width() * 0.08, 16.0),
                vec2(180.0, 250.0),
            );
            painter.rect_filled(mouse, 60.0, theme::RECESSED);
            painter.rect_stroke(
                mouse,
                60.0,
                egui::Stroke::new(6.0, theme::HIGHEST),
                egui::StrokeKind::Inside,
            );
            let left = Rect::from_min_max(
                mouse.min + vec2(8.0, 8.0),
                pos2(mouse.center().x - 4.0, mouse.top() + 106.0),
            );
            let right = Rect::from_min_max(
                pos2(mouse.center().x + 4.0, mouse.top() + 8.0),
                pos2(mouse.right() - 8.0, mouse.top() + 106.0),
            );
            painter.rect_filled(left, 6.0, theme::HIGHEST);
            painter.rect_filled(right, 6.0, theme::HIGHEST);
            painter.rect_filled(
                Rect::from_center_size(
                    pos2(mouse.center().x, mouse.top() + 50.0),
                    vec2(10.0, 26.0),
                ),
                4.0,
                theme::ACTIVE,
            );
            painter.text(
                left.center(),
                Align2::CENTER_CENTER,
                "IZQ\nOrbitar",
                theme::mono_medium(14.0),
                theme::PRIMARY,
            );
            painter.text(
                right.center(),
                Align2::CENTER_CENTER,
                "DER\nDesplazar",
                theme::mono_medium(14.0),
                theme::TEXT,
            );
            painter.text(
                pos2(mouse.center().x, mouse.top() + 172.0),
                Align2::CENTER_CENTER,
                "Rueda: acercar\nClic: colocar\no elegir",
                theme::mono(13.0),
                theme::TEXT_VARIANT,
            );
            painter.text(
                pos2(mouse.center().x, mouse.bottom() + 14.0),
                Align2::CENTER_CENTER,
                "Puntero de precisión",
                theme::mono(13.0),
                theme::TEXT,
            );
            // The flight keys.
            let keys_left = area.left() + area.width() * 0.5;
            painter.text(
                pos2(keys_left + 140.0, area.top() + 46.0),
                Align2::CENTER_CENTER,
                "VUELO DE CÁMARA",
                theme::mono(13.0),
                theme::TEXT_VARIANT,
            );
            let cell = vec2(64.0, 58.0);
            for (row, cells) in [
                [("Q", "Bajar"), ("W", "Avanzar"), ("E", "Subir")],
                [("A", "Izquierda"), ("S", "Retroceder"), ("D", "Derecha")],
            ]
            .iter()
            .enumerate()
            {
                for (column, (key, label)) in cells.iter().enumerate() {
                    let rect = Rect::from_min_size(
                        pos2(
                            keys_left + 40.0 + column as f32 * (cell.x + 6.0),
                            area.top() + 66.0 + row as f32 * (cell.y + 6.0),
                        ),
                        cell,
                    );
                    let active = *key == "W";
                    painter.rect_filled(
                        rect,
                        2.0,
                        if active {
                            theme::HIGHEST
                        } else {
                            theme::RAISED
                        },
                    );
                    painter.text(
                        rect.center() - vec2(0.0, 9.0),
                        Align2::CENTER_CENTER,
                        *key,
                        theme::mono_medium(16.0),
                        theme::PRIMARY,
                    );
                    painter.text(
                        rect.center() + vec2(0.0, 13.0),
                        Align2::CENTER_CENTER,
                        *label,
                        theme::mono(11.0),
                        if active {
                            theme::PRIMARY
                        } else {
                            theme::TEXT_VARIANT
                        },
                    );
                }
            }
            for (index, label) in ["MAYÚS (correr ×4)", "ESPACIO (subir)"]
                .into_iter()
                .enumerate()
            {
                let rect = Rect::from_min_size(
                    pos2(keys_left + index as f32 * 172.0, area.top() + 206.0),
                    vec2(164.0, 36.0),
                );
                painter.rect_filled(rect, 2.0, theme::RAISED);
                painter.text(
                    rect.center(),
                    Align2::CENTER_CENTER,
                    label,
                    theme::mono_medium(14.0),
                    if index == 0 {
                        theme::TELEMETRY
                    } else {
                        theme::TEXT
                    },
                );
            }
        });
}

fn shortcuts(ui: &mut Ui, title: &str, glyph: char, rows: &[(&str, &str)]) {
    egui::Frame::new()
        .fill(theme::PANEL)
        .stroke(theme::hairline())
        .corner_radius(4)
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                text(ui, title, theme::mono_medium(15.0), theme::TELEMETRY);
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    widgets::icon(ui, glyph, 18.0, theme::TEXT_VARIANT)
                });
            });
            ui.add_space(6.0);
            for (action, key) in rows {
                egui::Frame::new()
                    .fill(theme::CONTAINER)
                    .corner_radius(2)
                    .inner_margin(egui::Margin::symmetric(8, 6))
                    .show(ui, |ui| {
                        ui.set_width(ui.available_width());
                        ui.horizontal(|ui| {
                            text(ui, *action, theme::body(16.0), theme::TEXT);
                            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                widgets::key(ui, key, theme::TELEMETRY)
                            });
                        });
                    });
                ui.add_space(4.0);
            }
        });
}

fn diagnostics(ui: &mut Ui, studio: &mut Studio) {
    egui::Frame::new().fill(theme::PANEL).stroke(theme::hairline()).corner_radius(4).inner_margin(egui::Margin::same(12)).show(ui, |ui| {
        ui.set_width(ui.available_width());
        ui.horizontal(|ui| {
            let facts = &studio.viewport.facts;
            let adapter = if facts.adapter.is_empty() { "sin proyecto abierto".to_owned() } else { format!("{} ({})", facts.adapter, facts.backend) };
            widgets::icon_text(ui, icons::CHECK_CIRCLE, &format!("Tarjeta en uso: {adapter}"), theme::body(16.0), theme::TELEMETRY);
            ui.add_space(12.0);
            let memory = match common::video_memory_total(studio) {
                Some(total) => format!("Memoria de video: {} dedicada", common::size(total)),
                None => "Memoria de video: no informada".into(),
            };
            widgets::icon_text(ui, icons::MEMORY, &memory, theme::body(16.0), theme::TEXT);
            ui.add_space(12.0);
            let (w, h) = studio.viewport.resolution();
            text(ui, format!("Imagen: {w} × {h} · {}", studio.telemetry.fps_label()), theme::mono(13.0), theme::TEXT_VARIANT);
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                if widgets::button(ui, Style::Quiet, Some(icons::COPY), "Copiar diagnóstico", theme::mono(13.0), 30.0).clicked() {
                    let report = format!(
                        "Guanacaste Render {}\nTarjeta: {}\nCPU: {}\nImagen: {w}x{h}\nFPS: {:.1}\nTriángulos: {}",
                        env!("CARGO_PKG_VERSION"),
                        adapter,
                        studio.telemetry.hardware.processor.clone().unwrap_or_default(),
                        studio.telemetry.fps(),
                        studio.viewport.facts.triangles
                    );
                    ui.ctx().copy_text(report);
                    studio.message = Some(("Diagnóstico copiado al portapapeles".into(), false));
                }
            });
        });
    });
}
