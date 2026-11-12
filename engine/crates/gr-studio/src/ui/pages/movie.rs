//! Movie studio: a camera path through keyframes over the live view, a timeline of the camera, its height, its
//! lens and the sun, and the export of the clip as a numbered PNG sequence traced frame by frame.

use super::common::{self, floating};
use crate::app::{Page, Studio};
use crate::studio::timeline::{Export, clock};
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use eframe::egui::{self, Align, Align2, Color32, Layout, Rect, Sense, Stroke, Ui, pos2, vec2};
use eframe::egui_wgpu::RenderState;

/// Height of the timeline panel under the view.
const TIMELINE_HEIGHT: f32 = 300.0;
const SENSOR_HEIGHT_MM: f32 = 24.0;

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
    let view = Rect::from_min_max(
        body.min,
        pos2(body.right(), body.bottom() - TIMELINE_HEIGHT),
    );
    let mut view_ui = ui.new_child(egui::UiBuilder::new().max_rect(view));
    view_ui.set_clip_rect(view);
    studio.viewport.show(&mut view_ui, view, state);
    common::view_shortcuts(ui, studio);
    draw_path(ui, studio, view);
    status_overlay(ui, studio, view);
    clip_card(ui, studio, view);
    camera_inspector(ui, studio, view);

    let panel = Rect::from_min_max(pos2(body.left(), view.bottom()), body.max);
    ui.painter().rect_filled(panel, 0.0, theme::PANEL);
    ui.painter()
        .hline(panel.x_range(), panel.top(), theme::hairline());
    let mut panel_ui = ui.new_child(egui::UiBuilder::new().max_rect(panel));
    timeline(&mut panel_ui, studio, panel);
}

fn header_bar(ui: &mut Ui, studio: &mut Studio) {
    ui.spacing_mut().item_spacing.x = 10.0;
    if widgets::icon_button(ui, icons::CLOSE, 28.0, false, "Volver a Construcción").clicked() {
        studio.page = Page::Build;
    }
    widgets::icon(ui, icons::MOVIE, 20.0, theme::TEXT);
    text(ui, "Estudio de película", theme::heading(21.0), theme::TEXT);
    text(
        ui,
        "EDITOR DE SECUENCIA",
        theme::mono(13.0),
        theme::TEXT_VARIANT,
    );
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(8, 3))
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                text(ui, "ESCENA:", theme::mono(13.0), theme::TEXT_VARIANT);
                text(
                    ui,
                    studio.project_name().unwrap_or_else(|| "ninguna".into()),
                    theme::mono(13.0),
                    theme::TEXT,
                );
                widgets::status(
                    ui,
                    &common::mode_label(studio),
                    common::mode_color(studio),
                    theme::mono(13.0),
                );
            });
        });
    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
        ui.spacing_mut().item_spacing.x = 6.0;
        let exporting = studio.timeline.export.is_some();
        let label = if exporting {
            "Cancelar exportación"
        } else {
            "Exportar clip"
        };
        let enabled = !studio.timeline.keys.is_empty();
        if ui
            .add_enabled_ui(enabled, |ui| {
                widgets::button(
                    ui,
                    Style::Primary,
                    Some(icons::DOWNLOAD),
                    label,
                    theme::mono_medium(14.0),
                    30.0,
                )
            })
            .inner
            .clicked()
        {
            if exporting {
                studio.timeline.export = None;
                studio.message = Some(("Exportación cancelada".into(), true));
            } else {
                start_export(studio);
            }
        }
        ui.add_enabled_ui(false, |ui| {
            widgets::button(
                ui,
                Style::Ghost,
                Some(icons::BOOK),
                "Guiones gráficos",
                theme::mono(13.0),
                30.0,
            )
        })
        .inner
        .on_disabled_hover_text("Aún no disponible");
    });
}

/// Asks for a folder and starts writing one traced PNG per frame of the clip.
fn start_export(studio: &mut Studio) {
    let Some(folder) = rfd::FileDialog::new()
        .set_title("Carpeta para los fotogramas")
        .pick_folder()
    else {
        return;
    };
    let (width, height) = studio.viewport.render_resolution.unwrap_or((1920, 1080));
    studio.timeline.export = Some(Export {
        folder,
        frame: 0,
        total: studio.timeline.frame_count(),
        width,
        height,
        samples: studio.timeline.samples_per_frame,
        started: std::time::Instant::now(),
    });
    studio.timeline.playing = false;
}

/// The camera's path through the keyframes, projected into the view, with a marker on every keyframe.
fn draw_path(ui: &Ui, studio: &mut Studio, view: Rect) {
    let timeline = &studio.timeline;
    if timeline.keys.is_empty() {
        return;
    }
    let painter = ui.painter_at(view);
    let samples = 96;
    let mut previous: Option<egui::Pos2> = None;
    let end = timeline.keys.last().map_or(0.0, |key| key.time).max(0.01);
    for step in 0..=samples {
        let time = end * step as f32 / samples as f32;
        let point = timeline
            .pose_at(time)
            .and_then(|pose| common::project(studio, view, pose.camera.position()));
        if let (Some(a), Some(b)) = (previous, point)
            && step % 2 == 0
        {
            painter.line_segment([a, b], Stroke::new(2.0, theme::TELEMETRY));
        }
        previous = point;
    }
    let selected = studio.timeline.selected;
    let mut clicked = None;
    for (index, key) in studio.timeline.keys.iter().enumerate() {
        let Some(at) = common::project(studio, view, key.camera.position()) else {
            continue;
        };
        let active = selected == Some(index);
        painter.circle_stroke(
            at,
            if active { 9.0 } else { 7.0 },
            Stroke::new(
                2.0,
                if active {
                    theme::TEXT
                } else {
                    theme::PRIMARY_CONTAINER
                },
            ),
        );
        painter.circle_filled(at, 3.0, theme::PRIMARY_CONTAINER);
        let label = format!("CF #{:02} [{}]", index + 1, clock(key.time));
        painter.text(
            at + vec2(14.0, 0.0),
            Align2::LEFT_CENTER,
            label,
            theme::mono_medium(13.0),
            if active {
                theme::TELEMETRY
            } else {
                theme::PRIMARY
            },
        );
        let hit = ui.interact(
            Rect::from_center_size(at, vec2(22.0, 22.0)),
            egui::Id::new(("movie-key", index)),
            Sense::click(),
        );
        if hit
            .on_hover_cursor(egui::CursorIcon::PointingHand)
            .clicked()
        {
            clicked = Some(index);
        }
    }
    if let Some(index) = clicked {
        select_key(studio, index);
    }
}

fn select_key(studio: &mut Studio, index: usize) {
    studio.timeline.selected = Some(index);
    if let Some(key) = studio.timeline.keys.get(index) {
        studio.timeline.playhead = key.time;
        let pose = studio.timeline.pose_at(key.time);
        if let Some(pose) = pose {
            studio.apply_pose(pose);
        }
    }
}

fn status_overlay(ui: &Ui, studio: &mut Studio, view: Rect) {
    floating(
        ui,
        "movie-status",
        view.min + vec2(10.0, 10.0),
        Align2::LEFT_TOP,
        0.0,
        theme::hud(),
        |ui| {
            ui.horizontal(|ui| {
                let (w, h) = studio.viewport.resolution();
                widgets::status(
                    ui,
                    &studio.telemetry.fps_label(),
                    theme::TELEMETRY,
                    theme::mono(13.0),
                );
                text(ui, format!("| {w}×{h} |"), theme::mono(13.0), theme::TEXT);
                text(
                    ui,
                    studio.viewport.facts.adapter.to_uppercase(),
                    theme::mono(13.0),
                    theme::PRIMARY,
                );
            });
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::SUN, 14.0, theme::PRIMARY);
                let settings = &studio.viewport.settings;
                text(
                    ui,
                    format!(
                        "Altura del sol: {:.1}° · Orientación {:.0}°",
                        settings.sun_elevation_degrees, settings.sun_azimuth_degrees
                    ),
                    theme::mono(13.0),
                    theme::TEXT,
                );
            });
        },
    );
    floating(
        ui,
        "movie-trace",
        pos2(view.center().x, view.top() + 10.0),
        Align2::CENTER_TOP,
        0.0,
        theme::hud().inner_margin(egui::Margin::same(4)),
        |ui| {
            ui.horizontal(|ui| common::trace_buttons(ui, studio));
        },
    );
    let camera = studio.viewport.camera().position();
    floating(
        ui,
        "movie-camera-pos",
        pos2(view.right() - 10.0, view.bottom() - 10.0),
        Align2::RIGHT_BOTTOM,
        0.0,
        theme::hud(),
        |ui| {
            text(
                ui,
                format!(
                    "POS. CÁMARA:  X: {:.2} m  Y: {:.2} m  Z: {:.2} m",
                    camera.x, camera.y, camera.z
                ),
                theme::mono(13.0),
                theme::TEXT,
            );
        },
    );
}

fn clip_card(ui: &Ui, studio: &mut Studio, view: Rect) {
    floating(
        ui,
        "movie-clip",
        view.min + vec2(10.0, 76.0),
        Align2::LEFT_TOP,
        380.0,
        theme::floating(),
        |ui| {
            let timeline = &studio.timeline;
            ui.horizontal(|ui| {
                widgets::tag(
                    ui,
                    &format!(
                        "{} fotogramas a {} fps",
                        timeline.frame_count(),
                        timeline.fps
                    ),
                    theme::HIGHEST,
                    theme::PRIMARY,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    text(
                        ui,
                        clock(timeline.duration),
                        theme::mono(13.0),
                        theme::TEXT_VARIANT,
                    );
                    widgets::icon(ui, icons::SCHEDULE, 15.0, theme::TEXT_VARIANT);
                });
            });
            ui.add_space(6.0);
            text(ui, "Recorrido de cámara", theme::heading(24.0), theme::TEXT);
            let description = if timeline.keys.is_empty() {
                "Coloca la cámara y pulsa «Añadir fotograma clave». La curva pasa suavemente por cada clave, con la cámara y el sol.".to_owned()
            } else {
                format!(
                    "{} fotogramas clave unidos por una curva Catmull-Rom; el sol se anima con la cámara.",
                    timeline.keys.len()
                )
            };
            ui.add(
                egui::Label::new(
                    egui::RichText::new(description)
                        .font(theme::body(15.0))
                        .color(theme::TEXT_VARIANT),
                )
                .wrap(),
            );
            ui.add_space(8.0);
            let selected = timeline
                .selected
                .and_then(|index| timeline.keys.get(index).map(|key| (index, *key)));
            ui.columns(2, |columns| {
                let (index, key) =
                    selected.map_or((None, None), |(index, key)| (Some(index), Some(key)));
                cell(
                    &mut columns[0],
                    icons::TIMELINE,
                    &index.map_or("Sin clave elegida".into(), |i| {
                        format!("Clave {:02}", i + 1)
                    }),
                    &key.map_or("—".into(), |key| format!("en {}", clock(key.time))),
                );
                cell(
                    &mut columns[1],
                    icons::SUN,
                    &key.map_or("Nodo del sol".into(), |key| {
                        format!("Sol {:.0}°", key.sun_azimuth)
                    }),
                    &key.map_or("—".into(), |key| {
                        format!("Altura {:.1}°", key.sun_elevation)
                    }),
                );
                let focal = key.map(|key| focal_length(key.camera.vertical_fov_degrees));
                cell(
                    &mut columns[0],
                    icons::CAMERA,
                    &focal.map_or("Distancia focal".into(), |mm| format!("Focal {mm:.0} mm")),
                    "Equivalente 35 mm",
                );
                cell(
                    &mut columns[1],
                    icons::SHOW_CHART,
                    "Curva suave",
                    "Catmull-Rom",
                );
            });
            ui.add_space(6.0);
            ui.horizontal(|ui| {
                text(
                    ui,
                    format!(
                        "Muestras de trazado:\n{}/fotograma",
                        studio.timeline.samples_per_frame
                    ),
                    theme::mono(12.0),
                    theme::TEXT_VARIANT,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    let has = studio.timeline.selected.is_some();
                    ui.add_enabled_ui(has, |ui| {
                        if widgets::button(
                            ui,
                            Style::Pale,
                            None,
                            "Actualizar clave",
                            theme::mono_medium(13.0),
                            34.0,
                        )
                        .on_hover_text("Guarda la cámara y el sol actuales en la clave elegida")
                        .clicked()
                        {
                            let pose = studio.pose();
                            studio.timeline.update_selected(pose);
                        }
                        if widgets::button(
                            ui,
                            Style::Quiet,
                            None,
                            "Duplicar",
                            theme::mono(13.0),
                            34.0,
                        )
                        .clicked()
                        {
                            studio.timeline.duplicate_selected();
                        }
                    });
                });
            });
        },
    );
}

fn cell(ui: &mut Ui, glyph: char, title: &str, note: &str) {
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(8, 6))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                widgets::icon(ui, glyph, 16.0, theme::TEXT_VARIANT);
                ui.vertical(|ui| {
                    ui.spacing_mut().item_spacing.y = 0.0;
                    text(ui, title, theme::mono_medium(13.0), theme::TEXT);
                    text(ui, note, theme::mono(11.0), theme::OUTLINE);
                });
            });
        });
}

/// The focal length of a full-frame lens with this vertical field of view.
fn focal_length(vertical_fov_degrees: f32) -> f32 {
    SENSOR_HEIGHT_MM / 2.0 / (vertical_fov_degrees.to_radians() / 2.0).tan()
}

fn camera_inspector(ui: &Ui, studio: &mut Studio, view: Rect) {
    floating(
        ui,
        "movie-inspector",
        pos2(view.right() - 10.0, view.top() + 10.0),
        Align2::RIGHT_TOP,
        330.0,
        theme::floating(),
        |ui| {
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::VIDEOCAM, 18.0, theme::TEXT);
                text(
                    ui,
                    "INSPECTOR DE CÁMARA",
                    theme::mono_medium(15.0),
                    theme::TEXT,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    widgets::tag(ui, "CÁMARA FÍSICA", theme::CONTAINER, theme::PRIMARY)
                });
            });
            ui.add_space(8.0);
            let mut focal = focal_length(studio.viewport.field_of_view());
            ui.horizontal(|ui| {
                text(ui, "Distancia focal", theme::body(16.0), theme::TEXT);
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    text(
                        ui,
                        format!("{focal:.1} mm"),
                        theme::mono_medium(15.0),
                        theme::PRIMARY,
                    )
                });
            });
            ui.horizontal(|ui| {
                text(ui, "14 mm", theme::mono(12.0), theme::TEXT_VARIANT);
                let width = ui.available_width() - 50.0;
                if widgets::thin_slider(ui, &mut focal, 14.0..=85.0, width) {
                    let fov = 2.0 * (SENSOR_HEIGHT_MM / 2.0 / focal).atan().to_degrees();
                    studio.viewport.set_field_of_view(fov);
                }
                text(ui, "85 mm", theme::mono(12.0), theme::TEXT_VARIANT);
            });
            ui.add_space(8.0);
            ui.add_enabled_ui(false, |ui| {
            ui.horizontal(|ui| {
                text(ui, "Apertura", theme::body(16.0), theme::TEXT);
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| text(ui, "Estenopeica", theme::mono(13.0), theme::TEXT_VARIANT));
            });
            let mut aperture = 1;
            widgets::segmented(ui, &["f/1.4", "f/2.8", "f/5.6", "f/11"], &mut aperture, ui.available_width());
            ui.add_space(6.0);
            ui.horizontal(|ui| {
                text(ui, "Desenfoque de movimiento", theme::body(15.0), theme::TEXT);
            });
            let mut blur = 0.0;
            widgets::thin_slider(ui, &mut blur, 0.0..=1.0, ui.available_width());
            ui.horizontal(|ui| {
                text(ui, "Profundidad de campo", theme::body(15.0), theme::TEXT);
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    let mut on = false;
                    widgets::toggle(ui, &mut on);
                });
            });
        })
        .response
        .on_disabled_hover_text("La cámara del motor es estenopeica: todo queda enfocado y sin desenfoque de movimiento");
            text(
                ui,
                "Apertura, desenfoque y profundidad de campo aún no están en el motor.",
                theme::body(12.0),
                theme::OUTLINE,
            );
            ui.add_space(6.0);
            widgets::rule(ui);
            text(ui, "EXPOSICIÓN", theme::mono(12.0), theme::TEXT_VARIANT);
            let width = ui.available_width();
            widgets::thin_slider(
                ui,
                &mut studio.viewport.settings.exposure,
                0.05..=2.0,
                width,
            );
        },
    );
}

fn timeline(ui: &mut Ui, studio: &mut Studio, panel: Rect) {
    let bar = Rect::from_min_size(panel.min, vec2(panel.width(), 52.0));
    let mut bar_ui = ui.new_child(
        egui::UiBuilder::new()
            .max_rect(bar.shrink2(vec2(10.0, 0.0)))
            .layout(Layout::left_to_right(Align::Center)),
    );
    transport(&mut bar_ui, studio);
    ui.painter()
        .hline(panel.x_range(), bar.bottom(), theme::hairline());

    let tracks = Rect::from_min_max(pos2(panel.left(), bar.bottom()), panel.max);
    let names_width = 240.0;
    let names = Rect::from_min_max(
        tracks.min,
        pos2(tracks.left() + names_width, tracks.bottom()),
    );
    let lanes = Rect::from_min_max(
        pos2(names.right() + 20.0, tracks.top()),
        pos2(tracks.right() - 20.0, tracks.bottom()),
    );
    ui.painter().rect_filled(names, 0.0, theme::CONTAINER);
    let painter = ui.painter().clone();
    painter.text(
        pos2(names.left() + 12.0, names.top() + 16.0),
        Align2::LEFT_CENTER,
        "PISTAS",
        theme::mono(13.0),
        theme::TEXT_VARIANT,
    );
    let duration = studio.timeline.duration.max(0.1);
    let x_of = |time: f32| lanes.left() + lanes.width() * (time / duration).clamp(0.0, 1.0);
    // Ruler.
    let ruler_y = tracks.top() + 16.0;
    let step = if duration > 30.0 {
        5.0
    } else if duration > 12.0 {
        2.0
    } else {
        1.0
    };
    let mut time = 0.0;
    while time <= duration + 1e-3 {
        painter.text(
            pos2(x_of(time), ruler_y),
            Align2::CENTER_CENTER,
            clock(time),
            theme::mono(12.0),
            theme::TEXT_VARIANT,
        );
        time += step;
    }
    let lane_height = (tracks.height() - 32.0) / 4.0;
    let lane = |index: usize| {
        Rect::from_min_size(
            pos2(
                lanes.left(),
                tracks.top() + 32.0 + lane_height * index as f32,
            ),
            vec2(lanes.width(), lane_height),
        )
    };
    let track_names = [
        ("Órbita de cámara", theme::PRIMARY),
        ("Altura de cámara (Z)", theme::TELEMETRY),
        ("Distancia focal", theme::PRIMARY),
        ("Sol", theme::PRIMARY),
    ];
    for (index, (name, color)) in track_names.into_iter().enumerate() {
        let row = lane(index);
        painter.circle_filled(pos2(names.left() + 18.0, row.center().y), 4.0, color);
        painter.text(
            pos2(names.left() + 32.0, row.center().y),
            Align2::LEFT_CENTER,
            name,
            theme::mono(14.0),
            theme::TEXT,
        );
        painter.rect_filled(
            Rect::from_min_max(
                pos2(row.left(), row.center().y - 3.0),
                pos2(row.right(), row.center().y + 3.0),
            ),
            1.0,
            theme::HIGHEST,
        );
    }
    // Lane 0: keyframes as diamonds.
    let mut clicked = None;
    for (index, key) in studio.timeline.keys.iter().enumerate() {
        let at = pos2(x_of(key.time), lane(0).center().y);
        let active = studio.timeline.selected == Some(index);
        let size = if active { 11.0 } else { 9.0 };
        let diamond = vec![
            at + vec2(0.0, -size),
            at + vec2(size, 0.0),
            at + vec2(0.0, size),
            at + vec2(-size, 0.0),
        ];
        painter.add(egui::Shape::convex_polygon(
            diamond,
            if active {
                theme::TEXT
            } else {
                theme::PRIMARY_CONTAINER
            },
            Stroke::NONE,
        ));
        let sun = pos2(at.x, lane(3).center().y);
        let diamond = vec![
            sun + vec2(0.0, -8.0),
            sun + vec2(8.0, 0.0),
            sun + vec2(0.0, 8.0),
            sun + vec2(-8.0, 0.0),
        ];
        painter.add(egui::Shape::convex_polygon(
            diamond,
            theme::PRIMARY_CONTAINER.gamma_multiply(0.8),
            Stroke::NONE,
        ));
        if ui
            .interact(
                Rect::from_center_size(at, vec2(24.0, 24.0)),
                egui::Id::new(("lane-key", index)),
                Sense::click(),
            )
            .clicked()
        {
            clicked = Some(index);
        }
    }
    if let Some(index) = clicked {
        select_key(studio, index);
    }
    // Lanes 1 and 2: the camera height and focal length along the clip.
    if !studio.timeline.keys.is_empty() {
        let samples = 120;
        let poses: Vec<_> = (0..=samples)
            .filter_map(|i| {
                studio
                    .timeline
                    .pose_at(duration * i as f32 / samples as f32)
            })
            .collect();
        let heights: Vec<f32> = poses.iter().map(|pose| pose.camera.position().z).collect();
        let (low, high) = heights
            .iter()
            .fold((f32::MAX, f32::MIN), |(lo, hi), &h| (lo.min(h), hi.max(h)));
        let span = (high - low).max(0.5);
        let row = lane(1);
        let points: Vec<egui::Pos2> = heights
            .iter()
            .enumerate()
            .map(|(i, h)| {
                pos2(
                    row.left() + row.width() * i as f32 / samples as f32,
                    row.bottom() - 8.0 - (row.height() - 16.0) * (h - low) / span,
                )
            })
            .collect();
        painter.add(egui::Shape::line(
            points,
            Stroke::new(2.0, theme::TELEMETRY),
        ));
        let row = lane(2);
        let focal = poses
            .first()
            .map_or(35.0, |pose| focal_length(pose.camera.vertical_fov_degrees));
        let band = Rect::from_min_max(
            pos2(row.left(), row.center().y - 10.0),
            pos2(row.right(), row.center().y + 10.0),
        );
        painter.rect_filled(band, 1.0, theme::ACTIVE.gamma_multiply(0.35));
        painter.text(
            pos2(band.left() + 12.0, band.center().y),
            Align2::LEFT_CENTER,
            format!("Lente {focal:.0} mm al inicio · cambia entre claves"),
            theme::mono(12.0),
            theme::PRIMARY,
        );
    }
    // Playhead, draggable along the ruler and the lanes.
    let scrub = ui.interact(
        Rect::from_min_max(
            pos2(lanes.left(), tracks.top()),
            pos2(lanes.right(), tracks.top() + 28.0),
        ),
        egui::Id::new("movie-scrub"),
        Sense::click_and_drag(),
    );
    if (scrub.dragged() || scrub.clicked())
        && let Some(pointer) = scrub.interact_pointer_pos()
    {
        studio.timeline.playhead =
            ((pointer.x - lanes.left()) / lanes.width()).clamp(0.0, 1.0) * duration;
        studio.timeline.playing = false;
        if let Some(pose) = studio.timeline.pose_at(studio.timeline.playhead) {
            studio.apply_pose(pose);
        }
    }
    let x = x_of(studio.timeline.playhead);
    painter.vline(
        x,
        (tracks.top() + 26.0)..=tracks.bottom(),
        Stroke::new(1.5, theme::PRIMARY),
    );
    painter.rect_filled(
        Rect::from_center_size(pos2(x, tracks.top() + 26.0), vec2(12.0, 12.0)),
        2.0,
        theme::PRIMARY,
    );
}

fn transport(ui: &mut Ui, studio: &mut Studio) {
    ui.spacing_mut().item_spacing.x = 6.0;
    egui::Frame::new()
        .fill(theme::RECESSED)
        .stroke(theme::hairline())
        .corner_radius(2)
        .inner_margin(egui::Margin::symmetric(10, 4))
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                text(
                    ui,
                    clock(studio.timeline.playhead),
                    theme::mono_medium(16.0),
                    theme::PRIMARY,
                );
                text(
                    ui,
                    format!("/ {}", clock(studio.timeline.duration)),
                    theme::mono(16.0),
                    theme::TEXT_VARIANT,
                );
            });
        });
    let has_keys = !studio.timeline.keys.is_empty();
    let first = studio.timeline.keys.first().map(|key| key.time);
    let last = studio.timeline.keys.last().map(|key| key.time);
    if widgets::icon_button(ui, icons::FIRST_PAGE, 32.0, false, "Al inicio").clicked() {
        studio.timeline.playhead = 0.0;
    }
    if widgets::icon_button(ui, icons::SKIP_PREVIOUS, 32.0, false, "Clave anterior").clicked() {
        let previous = studio
            .timeline
            .keys
            .iter()
            .rposition(|key| key.time < studio.timeline.playhead - 1e-3);
        if let Some(index) = previous {
            select_key(studio, index);
        }
    }
    let (rect, response) = ui.allocate_exact_size(vec2(40.0, 40.0), Sense::click());
    ui.painter()
        .circle_filled(rect.center(), 20.0, theme::PRIMARY);
    ui.painter().text(
        rect.center(),
        Align2::CENTER_CENTER,
        if studio.timeline.playing {
            icons::PAUSE
        } else {
            icons::PLAY
        },
        theme::icon(22.0),
        theme::ON_PRIMARY,
    );
    if response
        .on_hover_text("Reproducir / pausa (barra espaciadora en el visor mueve la cámara)")
        .clicked()
        && has_keys
    {
        if studio.timeline.playhead >= studio.timeline.duration - 1e-3 {
            studio.timeline.playhead = 0.0;
        }
        studio.timeline.playing = !studio.timeline.playing;
    }
    if widgets::icon_button(ui, icons::SKIP_NEXT, 32.0, false, "Clave siguiente").clicked() {
        let next = studio
            .timeline
            .keys
            .iter()
            .position(|key| key.time > studio.timeline.playhead + 1e-3);
        if let Some(index) = next {
            select_key(studio, index);
        }
    }
    if widgets::icon_button(ui, icons::LAST_PAGE, 32.0, false, "Al final").clicked() {
        studio.timeline.playhead = last.unwrap_or(studio.timeline.duration);
    }
    let looping = studio.timeline.looping;
    if widgets::icon_button(ui, icons::REPEAT, 32.0, looping, "Repetir").clicked() {
        studio.timeline.looping = !looping;
    }
    let _ = first;
    ui.add_space(6.0);
    if widgets::button(
        ui,
        Style::Ghost,
        Some(icons::ADD),
        "Añadir fotograma clave",
        theme::mono_medium(14.0),
        34.0,
    )
    .clicked()
    {
        let pose = studio.pose();
        studio.timeline.add_key(pose);
    }
    ui.add_enabled_ui(studio.timeline.selected.is_some(), |ui| {
        if widgets::button(
            ui,
            Style::Quiet,
            Some(icons::DELETE),
            "Quitar clave",
            theme::mono(14.0),
            34.0,
        )
        .clicked()
        {
            studio.timeline.delete_selected();
        }
    });
    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
        ui.spacing_mut().item_spacing.x = 8.0;
        let exporting = studio
            .timeline
            .export
            .as_ref()
            .map(|export| (export.frame, export.total));
        let label = match exporting {
            Some((frame, total)) => format!("Trazando {}/{}", frame + 1, total),
            None => format!(
                "Renderizar clip ({} fotogramas)",
                studio.timeline.frame_count()
            ),
        };
        if ui
            .add_enabled_ui(has_keys && exporting.is_none(), |ui| {
                widgets::button(
                    ui,
                    Style::Primary,
                    Some(icons::MOVIE),
                    &label,
                    theme::mono_medium(14.0),
                    36.0,
                )
            })
            .inner
            .clicked()
        {
            start_export(studio);
        }
        // Right to left: each group's items are added last to first.
        egui::Frame::new()
            .fill(theme::RECESSED)
            .stroke(theme::hairline())
            .corner_radius(2)
            .inner_margin(egui::Margin::symmetric(8, 4))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.add(
                        egui::DragValue::new(&mut studio.timeline.samples_per_frame)
                            .range(1..=4096)
                            .speed(1.0),
                    );
                    text(ui, "MUESTRAS:", theme::mono(13.0), theme::TEXT_VARIANT);
                    ui.add(
                        egui::DragValue::new(&mut studio.timeline.duration)
                            .range(1.0..=600.0)
                            .speed(0.1)
                            .suffix(" s")
                            .max_decimals(1),
                    );
                    text(ui, "DURACIÓN:", theme::mono(13.0), theme::TEXT_VARIANT);
                });
            });
    });
    let _ = Color32::WHITE;
}
