//! Settings: the quality of the three view modes, with the live view and the measured telemetry of this computer
//! beside them. Every value is saved for the next session.

use super::common::{self, size};
use crate::app::{Studio, tier_index};
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use eframe::egui::{self, Align, Color32, Layout, Rect, Sense, Ui, pos2, vec2};
use eframe::egui_wgpu::RenderState;
use gr_core::hardware::QualityTier;

const TIERS: [(QualityTier, &str); 4] = [
    (QualityTier::Economico, "Económico"),
    (QualityTier::Equilibrado, "Equilibrado"),
    (QualityTier::Alto, "Alto"),
    (QualityTier::Ultra, "Ultra"),
];

pub fn show(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>) {
    let full = ui.max_rect();
    let header = Rect::from_min_size(full.min, vec2(full.width(), 56.0));
    ui.painter().rect_filled(header, 0.0, theme::PANEL);
    ui.painter()
        .hline(full.x_range(), header.bottom(), theme::hairline());
    let mut header_ui = ui.new_child(
        egui::UiBuilder::new()
            .max_rect(header.shrink2(vec2(20.0, 0.0)))
            .layout(Layout::left_to_right(Align::Center)),
    );
    header_bar(&mut header_ui, studio);

    let body = Rect::from_min_max(pos2(full.left(), header.bottom() + 1.0), full.max).shrink(20.0);
    let right_width = (body.width() * 0.4).min(620.0);
    let left = Rect::from_min_max(
        body.min,
        pos2(body.right() - right_width - 20.0, body.bottom()),
    );
    let right = Rect::from_min_max(pos2(left.right() + 20.0, body.top()), body.max);
    let mut left_ui = ui.new_child(egui::UiBuilder::new().max_rect(left));
    egui::ScrollArea::vertical()
        .id_salt("settings-left")
        .auto_shrink([false, false])
        .show(&mut left_ui, |ui| {
            engine(ui, studio);
            ui.add_space(16.0);
            precision(ui, studio);
            ui.add_space(16.0);
            profiles(ui, studio);
        });
    let mut right_ui = ui.new_child(egui::UiBuilder::new().max_rect(right));
    egui::ScrollArea::vertical()
        .id_salt("settings-right")
        .auto_shrink([false, false])
        .show(&mut right_ui, |ui| {
            live_view(ui, studio, state);
            ui.add_space(16.0);
            telemetry(ui, studio);
        });
    studio.save_preferences_if_changed();
}

fn header_bar(ui: &mut Ui, studio: &mut Studio) {
    ui.spacing_mut().item_spacing.x = 10.0;
    widgets::icon(ui, icons::TUNE, 22.0, theme::TEXT);
    text(ui, "AJUSTES", theme::heading(22.0), theme::TEXT);
    text(ui, "/", theme::mono(16.0), theme::OUTLINE);
    let scene = studio
        .project_name()
        .unwrap_or_else(|| "Sin proyecto".into());
    widgets::status(
        ui,
        &scene,
        if studio.viewport.has_scene() {
            theme::TELEMETRY
        } else {
            theme::OUTLINE
        },
        theme::mono(14.0),
    );
    text(
        ui,
        format!("· perfil sugerido: {}", tier_name(studio.suggested)),
        theme::mono(14.0),
        theme::TEXT_VARIANT,
    );
    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
        ui.spacing_mut().item_spacing.x = 8.0;
        if widgets::button(
            ui,
            Style::Ghost,
            Some(icons::RESTORE),
            "Valores por defecto",
            theme::mono_medium(14.0),
            32.0,
        )
        .on_hover_text("Vuelve al perfil sugerido para este equipo")
        .clicked()
        {
            let defaults = crate::viewport::ViewSettings::default();
            let settings = &mut studio.viewport.settings;
            settings.denoise = defaults.denoise;
            settings.preview_samples = defaults.preview_samples;
            settings.render_samples = defaults.render_samples;
            studio.apply_tier(studio.suggested);
        }
    });
}

fn tier_name(tier: QualityTier) -> &'static str {
    TIERS
        .iter()
        .find(|(candidate, _)| *candidate == tier)
        .map_or("—", |(_, name)| name)
}

fn engine(ui: &mut Ui, studio: &mut Studio) {
    section(
        ui,
        icons::CAMERA,
        "MOTOR DE RENDER",
        &format!("WGPU · {}", studio.viewport.facts.backend.to_uppercase()),
        |ui| {
            let bounces = studio.viewport.settings.max_bounces;
            option_row(
                ui,
                icons::SPARKLE,
                "Calidad del trazado",
                Some((tier_name(studio.tier), theme::ACTIVE)),
                "Iluminación global por trazado de rayos en la tarjeta gráfica: sombras, reflejos y luz indirecta",
                |ui| {
                    text(
                        ui,
                        format!(
                            "{bounces} rebotes ({} spp)",
                            studio.viewport.settings.preview_samples
                        ),
                        theme::mono(14.0),
                        theme::TEXT_VARIANT,
                    );
                },
            );
            let denoise = studio.viewport.settings.denoise;
            option_row(
                ui,
                icons::BLUR,
                "Reducción de ruido",
                Some((
                    if denoise { "Activa" } else { "Inactiva" },
                    if denoise {
                        theme::TELEMETRY
                    } else {
                        theme::HIGHEST
                    },
                )),
                "Filtro À-trous guiado por normales y profundidad en la vista previa y al iniciar el render",
                |ui| {
                    widgets::toggle(ui, &mut studio.viewport.settings.denoise);
                },
            );
            let vsync = studio.preferences.vsync;
            option_row(
                ui,
                icons::SYNC,
                "Sincronización vertical",
                Some((if vsync { "Activa" } else { "Inactiva" }, theme::HIGHEST)),
                "Limita la navegación a la frecuencia del monitor. Se aplica al reiniciar el estudio",
                |ui| {
                    if widgets::toggle(ui, &mut studio.preferences.vsync).changed() {
                        studio.preferences.save();
                    }
                },
            );
        },
    );
}

fn precision(ui: &mut Ui, studio: &mut Studio) {
    let (view_width, view_height) = studio.viewport.resolution();
    section(
        ui,
        icons::TUNE,
        "CONTROLES DE PRECISIÓN",
        "CAMPOS INTERACTIVOS",
        |ui| {
            let width = ui.available_width() - 20.0;
            let settings = &mut studio.viewport.settings;
            let mut scale = settings.preview_scale * 100.0;
            let (value, pixels) = (
                format!("{scale:.0} %"),
                format!("{:.2}× píxeles", (scale / 100.0).powi(2)),
            );
            scrub(
                ui,
                "Resolución de la vista previa",
                &format!("(vista {view_width}×{view_height})"),
                &value,
                "25 %",
                "100 %",
                &mut scale,
                25.0..=100.0,
                width,
                theme::PRIMARY_CONTAINER,
                "Fracción de la vista que traza la vista previa",
                &pixels,
            );
            if (scale / 100.0 - settings.preview_scale).abs() > 1e-4 {
                settings.preview_scale = scale / 100.0;
            }
            let mut samples = settings.preview_samples as f32;
            let value = settings.preview_samples.to_string();
            scrub(
                ui,
                "Muestras de la vista previa",
                "(se detiene al llegar)",
                &value,
                "8",
                "256",
                &mut samples,
                8.0..=256.0,
                width,
                theme::PRIMARY_CONTAINER,
                "Más muestras, menos ruido",
                "Por píxel",
            );
            settings.preview_samples = samples.round() as u32;
            let mut render = settings.render_samples as f32;
            let value = settings.render_samples.to_string();
            scrub(
                ui,
                "Muestras del render final",
                "(calidad de entrega)",
                &value,
                "16",
                "4096",
                &mut render,
                16.0..=4096.0,
                width,
                theme::TELEMETRY,
                "Cada imagen final traza hasta aquí",
                "Por píxel",
            );
            settings.render_samples = render.round() as u32;
            let mut bounces = settings.max_bounces as f32;
            let value = settings.max_bounces.to_string();
            scrub(
                ui,
                "Rebotes de luz",
                "(luz indirecta)",
                &value,
                "1",
                "12",
                &mut bounces,
                1.0..=12.0,
                width,
                theme::TELEMETRY,
                "Interiores necesitan más rebotes",
                "Vista previa y render",
            );
            settings.max_bounces = bounces.round() as u32;
        },
    );
}

#[allow(clippy::too_many_arguments)]
fn scrub(
    ui: &mut Ui,
    title: &str,
    note: &str,
    value: &str,
    low: &str,
    high: &str,
    number: &mut f32,
    range: std::ops::RangeInclusive<f32>,
    width: f32,
    color: Color32,
    left: &str,
    right: &str,
) {
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                text(ui, title, theme::body(16.0), theme::TEXT);
                text(ui, note, theme::mono(13.0), theme::TEXT_VARIANT);
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    egui::Frame::new()
                        .fill(theme::RECESSED)
                        .corner_radius(2)
                        .inner_margin(egui::Margin::symmetric(8, 2))
                        .show(ui, |ui| text(ui, value, theme::mono_medium(14.0), color));
                });
            });
            ui.horizontal(|ui| {
                text(ui, low, theme::mono(12.0), theme::TEXT_VARIANT);
                let inner = width - 90.0;
                widgets::bar_slider(ui, number, range, inner);
                text(ui, high, theme::mono(12.0), theme::TEXT_VARIANT);
            });
            ui.horizontal(|ui| {
                text(ui, left, theme::mono(12.0), theme::TEXT_VARIANT);
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    text(ui, right, theme::mono(12.0), theme::TEXT_VARIANT)
                });
            });
        });
    ui.add_space(8.0);
}

fn profiles(ui: &mut Ui, studio: &mut Studio) {
    egui::Frame::new()
        .fill(theme::PANEL)
        .stroke(theme::hairline())
        .corner_radius(4)
        .inner_margin(egui::Margin::same(12))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                widgets::icon_text(
                    ui,
                    icons::BOLT,
                    "PERFILES DE RENDER:",
                    theme::mono_medium(14.0),
                    theme::TEXT,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    let mut selected = tier_index(studio.tier) as usize;
                    let names: Vec<&str> = TIERS.iter().map(|(_, name)| *name).collect();
                    if widgets::segmented(ui, &names, &mut selected, 440.0) {
                        studio.apply_tier(TIERS[selected].0);
                    }
                });
            });
        });
}

fn section(ui: &mut Ui, glyph: char, title: &str, note: &str, add: impl FnOnce(&mut Ui)) {
    egui::Frame::new()
        .fill(theme::PANEL)
        .stroke(theme::hairline())
        .corner_radius(4)
        .inner_margin(egui::Margin::same(14))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                widgets::icon(ui, glyph, 20.0, theme::PRIMARY);
                text(ui, title, theme::mono_medium(17.0), theme::PRIMARY);
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    text(ui, note, theme::mono(13.0), theme::TEXT_VARIANT)
                });
            });
            ui.add_space(10.0);
            add(ui);
        });
}

fn option_row(
    ui: &mut Ui,
    glyph: char,
    title: &str,
    badge: Option<(&str, Color32)>,
    description: &str,
    control: impl FnOnce(&mut Ui),
) {
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                let (square, _) = ui.allocate_exact_size(vec2(40.0, 40.0), Sense::hover());
                ui.painter().rect_filled(square, 2.0, theme::RECESSED);
                ui.painter().text(
                    square.center(),
                    egui::Align2::CENTER_CENTER,
                    glyph,
                    theme::icon(20.0),
                    theme::TEXT,
                );
                ui.vertical(|ui| {
                    ui.horizontal(|ui| {
                        text(ui, title, theme::heading(20.0), theme::TEXT);
                        if let Some((label, fill)) = badge {
                            let ink = if fill == theme::ACTIVE {
                                Color32::WHITE
                            } else if fill == theme::TELEMETRY {
                                theme::RECESSED
                            } else {
                                theme::TEXT_VARIANT
                            };
                            widgets::tag(ui, label, fill, ink);
                        }
                    });
                    text(ui, description, theme::body(14.0), theme::TEXT_VARIANT);
                });
                ui.with_layout(Layout::right_to_left(Align::Center), control);
            });
        });
    ui.add_space(8.0);
}

fn live_view(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>) {
    egui::Frame::new()
        .fill(theme::PANEL)
        .stroke(theme::hairline())
        .corner_radius(4)
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                widgets::status(
                    ui,
                    "VISTA EN VIVO",
                    theme::TELEMETRY,
                    theme::mono_medium(15.0),
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    text(
                        ui,
                        common::mode_label(studio),
                        theme::mono(12.0),
                        theme::TEXT_VARIANT,
                    )
                });
            });
            ui.add_space(6.0);
            let width = ui.available_width();
            let (rect, _) = ui.allocate_exact_size(vec2(width, width * 9.0 / 16.0), Sense::hover());
            match state.filter(|_| studio.viewport.has_scene()) {
                Some(state) => {
                    let mut view_ui = ui.new_child(egui::UiBuilder::new().max_rect(rect));
                    view_ui.set_clip_rect(rect);
                    studio.viewport.show(&mut view_ui, rect, state);
                    let painter = ui.painter_at(rect);
                    let tag = Rect::from_min_size(rect.min + vec2(6.0, 6.0), vec2(260.0, 22.0));
                    painter.rect_filled(tag, 2.0, theme::PANEL.gamma_multiply(0.85));
                    painter.text(
                        tag.left_center() + vec2(8.0, 0.0),
                        egui::Align2::LEFT_CENTER,
                        format!(
                            "{} | {}",
                            studio.telemetry.fps_label(),
                            studio.project_name().unwrap_or_default()
                        ),
                        theme::mono(13.0),
                        theme::TELEMETRY,
                    );
                }
                None => {
                    ui.painter().rect_filled(rect, 2.0, theme::RECESSED);
                    ui.painter().text(
                        rect.center(),
                        egui::Align2::CENTER_CENTER,
                        "Abre un proyecto para ver los cambios en vivo",
                        theme::mono(13.0),
                        theme::OUTLINE,
                    );
                }
            }
            common::trace_buttons_row(ui, studio);
        });
}

fn telemetry(ui: &mut Ui, studio: &mut Studio) {
    egui::Frame::new()
        .fill(theme::PANEL)
        .stroke(theme::hairline())
        .corner_radius(4)
        .inner_margin(egui::Margin::same(14))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::MEMORY, 20.0, theme::PRIMARY);
                text(
                    ui,
                    "TELEMETRÍA DEL EQUIPO",
                    theme::mono_medium(17.0),
                    theme::PRIMARY,
                );
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    widgets::status(ui, "Medida", theme::TELEMETRY, theme::mono(13.0))
                });
            });
            ui.add_space(10.0);
            let facts = studio.viewport.facts.clone();
            block(ui, |ui| {
                ui.horizontal(|ui| {
                    widgets::icon(ui, icons::DEVICES, 20.0, theme::TEXT);
                    ui.vertical(|ui| {
                        let adapter = if facts.adapter.is_empty() {
                            "Sin tarjeta en uso todavía".to_owned()
                        } else {
                            facts.adapter.clone()
                        };
                        text(ui, adapter, theme::mono_medium(15.0), theme::TEXT);
                        let cpu = studio
                            .telemetry
                            .hardware
                            .processor
                            .clone()
                            .unwrap_or_default();
                        text(ui, cpu, theme::mono(12.0), theme::TEXT_VARIANT);
                    });
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        if !facts.backend.is_empty() {
                            widgets::tag(
                                ui,
                                &format!("{} activo", facts.backend),
                                theme::CONTAINER,
                                theme::TEXT_VARIANT,
                            );
                        }
                    });
                });
            });
            let used = studio.viewport.video_megabytes() * 1024.0 * 1024.0;
            let total = common::video_memory_total(studio);
            block(ui, |ui| {
                ui.horizontal(|ui| {
                    text(
                        ui,
                        "Memoria de video de la escena",
                        theme::body(16.0),
                        theme::TEXT,
                    );
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        let label = match total {
                            Some(total) => format!("{} / {}", size(used), size(total)),
                            None => size(used),
                        };
                        text(ui, label, theme::mono_medium(15.0), theme::PRIMARY);
                    });
                });
                let (bar, _) =
                    ui.allocate_exact_size(vec2(ui.available_width(), 6.0), Sense::hover());
                ui.painter().rect_filled(bar, 2.0, theme::HIGHEST);
                if let Some(total) = total {
                    let fraction = (used / total).clamp(0.0, 1.0) as f32;
                    ui.painter().rect_filled(
                        Rect::from_min_size(bar.min, vec2(bar.width() * fraction.max(0.004), 6.0)),
                        2.0,
                        theme::PRIMARY,
                    );
                }
                ui.horizontal(|ui| {
                    text(
                        ui,
                        format!(
                            "Geometría y BVH ({})",
                            size(facts.scene_megabytes * 1024.0 * 1024.0)
                        ),
                        theme::mono(12.0),
                        theme::TEXT_VARIANT,
                    );
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        if let Some(total) = total {
                            text(
                                ui,
                                format!("Libre ({})", size((total - used).max(0.0))),
                                theme::mono(12.0),
                                theme::TEXT_VARIANT,
                            );
                        }
                    });
                });
            });
            ui.columns(2, |columns| {
                for (column, (label, note)) in columns.iter_mut().zip([
                    (
                        "TEMPERATURA",
                        "Windows no la expone sin controladores del fabricante",
                    ),
                    ("CONSUMO", "Sin lectura de potencia disponible"),
                ]) {
                    egui::Frame::new()
                        .fill(theme::CONTAINER)
                        .corner_radius(2)
                        .inner_margin(egui::Margin::same(10))
                        .show(column, |ui| {
                            ui.set_width(ui.available_width());
                            text(ui, label, theme::mono(12.0), theme::TEXT_VARIANT);
                            text(ui, "—", theme::heading(26.0), theme::OUTLINE);
                            ui.add(
                                egui::Label::new(
                                    egui::RichText::new(note)
                                        .font(theme::mono(11.0))
                                        .color(theme::OUTLINE),
                                )
                                .wrap(),
                            );
                        });
                }
            });
            ui.add_space(8.0);
            block(ui, |ui| {
                let stats = studio.telemetry.stats();
                ui.horizontal(|ui| {
                    text(
                        ui,
                        "Historial de fotogramas",
                        theme::body(16.0),
                        theme::TEXT,
                    );
                    text(
                        ui,
                        format!("(últimos {})", studio.telemetry.frame_ms.len()),
                        theme::mono(13.0),
                        theme::TEXT_VARIANT,
                    );
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        if let Some((_, _, average)) = stats {
                            widgets::status(
                                ui,
                                &format!("{average:.1} ms prom."),
                                theme::TELEMETRY,
                                theme::mono(13.0),
                            );
                        }
                    });
                });
                let values: Vec<f32> = studio.telemetry.frame_ms.iter().copied().collect();
                widgets::sparkline(
                    ui,
                    &values,
                    0.0..=50.0,
                    vec2(ui.available_width(), 80.0),
                    &[(33.3, "33,3 ms (30 FPS)"), (16.6, "16,6 ms (60 FPS)")],
                );
                if let Some((low, high, average)) = stats {
                    let jitter = values
                        .iter()
                        .map(|value| (value - average).abs())
                        .sum::<f32>()
                        / values.len().max(1) as f32;
                    ui.horizontal(|ui| {
                        text(
                            ui,
                            format!("Mín: {low:.1} ms"),
                            theme::mono(12.0),
                            theme::TEXT_VARIANT,
                        );
                        ui.add_space(40.0);
                        text(
                            ui,
                            format!("Variación: ±{jitter:.1} ms"),
                            theme::mono(12.0),
                            theme::TEXT_VARIANT,
                        );
                        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                            text(
                                ui,
                                format!("Máx: {high:.1} ms"),
                                theme::mono(12.0),
                                theme::TEXT_VARIANT,
                            )
                        });
                    });
                }
            });
            ui.horizontal(|ui| {
                widgets::icon(ui, icons::PERSON, 16.0, theme::TEXT_VARIANT);
                text(
                    ui,
                    format!(
                        "Sesión local · Guanacaste Render {}",
                        env!("CARGO_PKG_VERSION")
                    ),
                    theme::mono(13.0),
                    theme::TEXT_VARIANT,
                );
            });
        });
}

fn block(ui: &mut Ui, add: impl FnOnce(&mut Ui)) {
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
