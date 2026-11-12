//! Material editor: the project's live view with the properties of one material beside it. Changes are drafted in
//! the panel and reach the scene with «Aplicar material»; the navigation view shows them at once, the preview and
//! the render with full light transport.

use super::common::{self, floating};
use crate::app::{Page, Studio};
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use eframe::egui::{self, Align, Align2, Color32, Layout, Rect, Sense, Ui, pos2, vec2};
use eframe::egui_wgpu::RenderState;
use gr_core::scene_file::Material;

#[derive(Default)]
pub struct MaterialsUi {
    /// The material being edited and its unapplied values.
    pub draft: Option<(usize, Material)>,
    pub filter: String,
}

pub fn show(
    ui: &mut Ui,
    studio: &mut Studio,
    state: Option<&RenderState>,
    local: &mut MaterialsUi,
) {
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
    header_bar(&mut header_ui, studio, local);

    let body = Rect::from_min_max(pos2(full.left(), header.bottom() + 1.0), full.max);
    let Some(state) = state.filter(|_| studio.viewport.has_scene()) else {
        let mut empty = ui.new_child(egui::UiBuilder::new().max_rect(body));
        common::empty_state(&mut empty, studio, body);
        return;
    };
    let panel_width = 480.0f32.min(body.width() * 0.32);
    let view = Rect::from_min_max(body.min, pos2(body.right() - panel_width, body.bottom()));
    let panel = Rect::from_min_max(pos2(view.right(), body.top()), body.max);
    let mut view_ui = ui.new_child(egui::UiBuilder::new().max_rect(view));
    view_ui.set_clip_rect(view);
    let response = studio.viewport.show(&mut view_ui, view, state);
    common::view_shortcuts(ui, studio);
    if response.clicked()
        && let Some(position) = response.interact_pointer_pos()
        && let Some(material) = studio.viewport.pick_material(view, position)
    {
        studio.materials.selected = material;
    }
    view_overlays(ui, studio, view);

    let selected = studio
        .materials
        .selected
        .min(studio.viewport.materials().len().saturating_sub(1));
    if local
        .draft
        .as_ref()
        .is_none_or(|(index, _)| *index != selected)
    {
        local.draft = studio
            .viewport
            .materials()
            .get(selected)
            .map(|material| (selected, *material));
    }
    ui.painter().rect_filled(panel, 0.0, theme::PANEL);
    ui.painter()
        .vline(panel.left(), panel.y_range(), theme::hairline());
    let apply_bar = Rect::from_min_max(pos2(panel.left(), panel.bottom() - 92.0), panel.max);
    let mut panel_ui = ui.new_child(egui::UiBuilder::new().max_rect(
        Rect::from_min_max(panel.min, pos2(panel.right(), apply_bar.top())).shrink(12.0),
    ));
    egui::ScrollArea::vertical()
        .auto_shrink([false, false])
        .show(&mut panel_ui, |ui| properties(ui, studio, local, selected));
    let mut apply_ui = ui.new_child(egui::UiBuilder::new().max_rect(apply_bar.shrink(10.0)));
    apply(&mut apply_ui, studio, local, selected);
}

fn header_bar(ui: &mut Ui, studio: &mut Studio, local: &mut MaterialsUi) {
    ui.spacing_mut().item_spacing.x = 10.0;
    if widgets::icon_button(ui, icons::ARROW_BACK, 28.0, false, "Volver a Construcción").clicked()
    {
        studio.page = Page::Build;
    }
    widgets::icon(ui, icons::TEXTURE, 20.0, theme::PRIMARY);
    text(
        ui,
        "EDITOR DE MATERIALES",
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
    let megabytes = studio.viewport.video_megabytes();
    let memory = match common::video_memory_total(studio) {
        Some(total) => format!(
            "{} · VRAM {} / {}",
            studio.telemetry.fps_label(),
            common::size(megabytes * 1024.0 * 1024.0),
            common::size(total)
        ),
        None => format!(
            "{} · VRAM {}",
            studio.telemetry.fps_label(),
            common::size(megabytes * 1024.0 * 1024.0)
        ),
    };
    text(ui, memory, theme::mono(14.0), theme::TELEMETRY);
    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
        egui::Frame::new()
            .fill(theme::RECESSED)
            .stroke(theme::hairline())
            .corner_radius(2)
            .inner_margin(egui::Margin::symmetric(8, 3))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    widgets::icon(ui, icons::SEARCH, 15.0, theme::OUTLINE);
                    ui.add_sized(
                        vec2(200.0, 22.0),
                        egui::TextEdit::singleline(&mut local.filter)
                            .hint_text("Filtrar materiales…")
                            .frame(egui::Frame::NONE)
                            .font(theme::mono(13.0)),
                    );
                });
            });
    });
}

fn view_overlays(ui: &Ui, studio: &mut Studio, view: Rect) {
    floating(
        ui,
        "materials-status",
        view.min + vec2(10.0, 10.0),
        Align2::LEFT_TOP,
        0.0,
        theme::hud(),
        |ui| {
            widgets::status(
                ui,
                &format!("VISTA ACTIVA · {}", common::mode_label(studio)),
                common::mode_color(studio),
                theme::mono_medium(13.0),
            );
        },
    );
    floating(
        ui,
        "materials-tools",
        pos2(view.right() - 10.0, view.top() + 10.0),
        Align2::RIGHT_TOP,
        0.0,
        theme::hud().inner_margin(egui::Margin::same(4)),
        |ui| {
            ui.horizontal(|ui| {
                common::trace_buttons(ui, studio);
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
            });
        },
    );
    floating(
        ui,
        "materials-hint",
        view.min + vec2(10.0, 52.0),
        Align2::LEFT_TOP,
        0.0,
        theme::hud(),
        |ui| {
            widgets::icon_text(
                ui,
                icons::TOUCH,
                "Clic sobre una superficie para elegir su material",
                theme::mono(12.0),
                theme::TEXT_VARIANT,
            );
        },
    );
    let selected = studio.materials.selected;
    let material = studio.viewport.materials().get(selected).copied();
    let name = studio
        .materials
        .names
        .get(selected)
        .cloned()
        .unwrap_or_default();
    floating(
        ui,
        "materials-orb",
        pos2(view.left() + 10.0, view.bottom() - 10.0),
        Align2::LEFT_BOTTOM,
        220.0,
        theme::hud().inner_margin(egui::Margin::same(10)),
        |ui| {
            ui.horizontal(|ui| {
                text(ui, "MUESTRA DEL\nMATERIAL", theme::mono(12.0), theme::TEXT);
                ui.with_layout(Layout::right_to_left(Align::Min), |ui| {
                    text(ui, "PBR\nmotor", theme::mono(12.0), theme::PRIMARY)
                });
            });
            let (rect, _) = ui.allocate_exact_size(vec2(200.0, 150.0), Sense::hover());
            ui.painter()
                .rect_filled(rect, 2.0, Color32::from_rgb(0x08, 0x09, 0x0B));
            if let Some(material) = material {
                orb(ui, rect.shrink(14.0), &material);
            }
        },
    );
    floating(
        ui,
        "materials-facts",
        pos2(view.left() + 240.0, view.bottom() - 10.0),
        Align2::LEFT_BOTTOM,
        0.0,
        theme::hud().inner_margin(egui::Margin::symmetric(14, 10)),
        |ui| {
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 26.0;
                fact(ui, "MATERIAL ACTIVO", &name, theme::TEXT);
                fact(
                    ui,
                    "ÍNDICE",
                    &format!("{} de {}", selected + 1, studio.viewport.materials().len()),
                    theme::TEXT,
                );
                let target = studio.viewport.target_samples();
                let samples = if studio.viewport.mode() == crate::viewport::ViewMode::Navigate {
                    "Sin trazado".to_owned()
                } else {
                    format!("{} / {}", studio.viewport.samples().min(target), target)
                };
                fact(ui, "MUESTRAS / OBJETIVO", &samples, theme::TELEMETRY);
            });
        },
    );
}

fn fact(ui: &mut Ui, label: &str, value: &str, color: Color32) {
    ui.vertical(|ui| {
        text(ui, label, theme::mono(12.0), theme::TEXT_VARIANT);
        text(ui, value, theme::mono(15.0), color);
    });
}

/// A sphere shaded with the material's colour, gloss and emission: a sketch to read the settings by, not a render.
fn orb(ui: &Ui, area: Rect, material: &Material) {
    let painter = ui.painter_at(area);
    let radius = area.height().min(area.width()) / 2.0;
    let center = area.center();
    let base = material.base_color;
    let light = glam::Vec3::new(-0.5, -0.6, 0.62).normalize();
    let mut mesh = egui::Mesh::default();
    let rings = 24;
    let segments = 48;
    let shade = |x: f32, y: f32| -> Color32 {
        let z = (1.0 - x * x - y * y).max(0.0).sqrt();
        let normal = glam::Vec3::new(x, y, z);
        let diffuse = normal.dot(light).max(0.0) * 0.85 + 0.12;
        let half = (light + glam::Vec3::Z).normalize();
        let shininess = 2.0 / (material.roughness.max(0.04).powi(2)) - 2.0;
        let specular = normal.dot(half).max(0.0).powf(shininess.max(1.0))
            * (0.04 + 0.96 * material.metallic)
            * (1.0 - material.roughness * 0.6);
        let tint = if material.metallic > 0.5 {
            base
        } else {
            glam::Vec3::ONE
        };
        let mut color = base * diffuse * (1.0 - material.metallic * 0.7)
            + tint * specular
            + material.emission * 0.6;
        if material.transmission > 0.0 {
            color = color.lerp(
                glam::Vec3::new(0.18, 0.22, 0.26) * material.transmission_tint,
                material.transmission * 0.6,
            );
        }
        let encode = |value: f32| {
            ((value / (1.0 + value)).powf(1.0 / 2.2) * 1.15 * 255.0).clamp(0.0, 255.0) as u8
        };
        Color32::from_rgb(encode(color.x), encode(color.y), encode(color.z))
    };
    for ring in 0..=rings {
        let r = ring as f32 / rings as f32;
        for segment in 0..segments {
            let angle = segment as f32 / segments as f32 * std::f32::consts::TAU;
            let (x, y) = (r * angle.cos(), r * angle.sin());
            mesh.colored_vertex(center + vec2(x, y) * radius, shade(x * 0.999, y * 0.999));
        }
    }
    for ring in 0..rings {
        for segment in 0..segments {
            let next = (segment + 1) % segments;
            let a = ring * segments + segment;
            let b = ring * segments + next;
            let c = (ring + 1) * segments + segment;
            let d = (ring + 1) * segments + next;
            mesh.add_triangle(a, b, c);
            mesh.add_triangle(b, d, c);
        }
    }
    painter.add(egui::Shape::mesh(mesh));
}

fn properties(ui: &mut Ui, studio: &mut Studio, local: &mut MaterialsUi, selected: usize) {
    ui.horizontal(|ui| {
        widgets::icon(ui, icons::TUNE, 20.0, theme::PRIMARY);
        text(
            ui,
            "Propiedades del material",
            theme::heading(21.0),
            theme::TEXT,
        );
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            if widgets::button(
                ui,
                Style::Quiet,
                None,
                "Restablecer",
                theme::mono(13.0),
                26.0,
            )
            .on_hover_text("Volver a los valores con que se abrió el proyecto")
            .clicked()
                && let Some(original) = studio.materials.original.get(selected).copied()
            {
                local.draft = Some((selected, original));
            }
        });
    });
    ui.add_space(10.0);
    let Some((_, draft)) = &mut local.draft else {
        return;
    };

    block(
        ui,
        icons::DIAMOND,
        "SOMBREADOR DE SUPERFICIE",
        Some(("MODELO DEL MOTOR", theme::OUTLINE)),
        |ui| {
            let names = &studio.materials.names;
            let current = names
                .get(selected)
                .cloned()
                .unwrap_or_else(|| format!("Material {}", selected + 1));
            let filter = local.filter.trim().to_lowercase();
            egui::ComboBox::from_id_salt("material-pick")
                .width(ui.available_width())
                .selected_text(egui::RichText::new(current).font(theme::body(16.0)))
                .show_ui(ui, |ui| {
                    for (index, name) in names.iter().enumerate() {
                        if (filter.is_empty() || name.to_lowercase().contains(&filter))
                            && ui
                                .selectable_label(
                                    index == selected,
                                    format!("{name}  · #{}", index + 1),
                                )
                                .clicked()
                        {
                            studio.materials.selected = index;
                        }
                    }
                });
            ui.add_space(4.0);
            let kind = if draft.transmission > 0.5 {
                "Dieléctrico transmisivo: refracción con IOR y tinte de volumen."
            } else if draft.metallic > 0.5 {
                "Conductor: reflexión especular GGX teñida por el color base."
            } else if draft.emission.length() > 0.0 {
                "Emisor: aporta luz propia a la escena."
            } else {
                "Microfaceta GGX con difuso Lambert; la rugosidad controla el brillo."
            };
            ui.add(
                egui::Label::new(
                    egui::RichText::new(kind)
                        .font(theme::body(14.0))
                        .color(theme::TEXT_VARIANT),
                )
                .wrap(),
            );
        },
    );

    block(
        ui,
        icons::PALETTE,
        "COLOR BASE",
        Some(("sRGB lineal", theme::TELEMETRY)),
        |ui| {
            ui.horizontal(|ui| {
                let mut rgb = [draft.base_color.x, draft.base_color.y, draft.base_color.z];
                if ui.color_edit_button_rgb(&mut rgb).changed() {
                    draft.base_color = glam::Vec3::from(rgb);
                }
                text(
                    ui,
                    format!("R {:.2}  G {:.2}  B {:.2}", rgb[0], rgb[1], rgb[2]),
                    theme::mono(13.0),
                    theme::TEXT,
                );
            });
        },
    );

    block(
        ui,
        icons::TEXTURE,
        "MAPA DE RUGOSIDAD",
        Some(("No disponible", theme::OUTLINE)),
        |ui| {
            ui.add_enabled_ui(false, |ui| {
                ui.horizontal(|ui| {
                    let (swatch, _) = ui.allocate_exact_size(vec2(56.0, 56.0), Sense::hover());
                    ui.painter().rect_filled(swatch, 2.0, theme::RECESSED);
                    ui.vertical(|ui| {
                        text(ui, "Sin textura", theme::mono_medium(14.0), theme::TEXT);
                        text(
                            ui,
                            "El motor aún no lee mapas de imagen",
                            theme::mono(12.0),
                            theme::TEXT_VARIANT,
                        );
                        widgets::button(
                            ui,
                            Style::Ghost,
                            Some(icons::FOLDER_OPEN),
                            "Examinar",
                            theme::mono(12.0),
                            26.0,
                        );
                    });
                });
            });
        },
    );

    block(ui, icons::TUNE, "SUPERFICIE", None, |ui| {
        let width = ui.available_width();
        slider(
            ui,
            "Rugosidad",
            "Dispersión del reflejo",
            &mut draft.roughness,
            0.0..=1.0,
            width,
            "",
        );
        slider(
            ui,
            "Transmisión",
            "Vidrio y agua: luz que atraviesa",
            &mut draft.transmission,
            0.0..=1.0,
            width,
            "",
        );
        slider(
            ui,
            "Translucidez",
            "Luz difusa a través de hojas y telas",
            &mut draft.translucency,
            0.0..=1.0,
            width,
            "",
        );
    });

    block(ui, icons::TUNE, "MODIFICADORES PBR", None, |ui| {
        ui.columns(3, |columns| {
            let unit = if draft.metallic > 0.5 { "MET" } else { "DIE" };
            channel(
                &mut columns[0],
                "METÁLICO",
                &mut draft.metallic,
                0.0..=1.0,
                unit,
            );
            channel(&mut columns[1], "IOR", &mut draft.ior, 1.0..=2.5, "IOR");
            let mut strength = draft.emission.max_element();
            let tint = if strength > 0.0 {
                draft.emission / strength
            } else {
                glam::Vec3::ONE
            };
            if channel(&mut columns[2], "EMISIÓN", &mut strength, 0.0..=20.0, "W") {
                draft.emission = tint * strength;
            }
        });
    });
}

fn block(
    ui: &mut Ui,
    glyph: char,
    title: &str,
    note: Option<(&str, Color32)>,
    add: impl FnOnce(&mut Ui),
) {
    egui::Frame::new()
        .fill(theme::CONTAINER)
        .corner_radius(2)
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            widgets::section_title(ui, Some(glyph), title, note);
            ui.add_space(6.0);
            add(ui);
        });
    ui.add_space(10.0);
}

fn slider(
    ui: &mut Ui,
    label: &str,
    note: &str,
    value: &mut f32,
    range: std::ops::RangeInclusive<f32>,
    width: f32,
    unit: &str,
) {
    ui.horizontal(|ui| {
        text(ui, label, theme::mono_medium(14.0), theme::TEXT);
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            egui::Frame::new()
                .fill(theme::RECESSED)
                .corner_radius(2)
                .inner_margin(egui::Margin::symmetric(8, 2))
                .show(ui, |ui| {
                    text(
                        ui,
                        format!("{value:.2}{unit}"),
                        theme::mono(14.0),
                        theme::PRIMARY,
                    );
                });
        });
    });
    text(ui, note, theme::body(14.0), theme::TEXT_VARIANT);
    widgets::thin_slider(ui, value, range, width);
    ui.add_space(8.0);
}

fn channel(
    ui: &mut Ui,
    label: &str,
    value: &mut f32,
    range: std::ops::RangeInclusive<f32>,
    unit: &str,
) -> bool {
    let mut changed = false;
    egui::Frame::new()
        .fill(theme::RECESSED)
        .stroke(theme::hairline())
        .corner_radius(2)
        .inner_margin(egui::Margin::same(8))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            text(ui, label, theme::mono(12.0), theme::TEXT_VARIANT);
            ui.horizontal(|ui| {
                changed = ui
                    .add(
                        egui::DragValue::new(value)
                            .range(range.clone())
                            .speed((range.end() - range.start()) / 300.0)
                            .max_decimals(2),
                    )
                    .changed();
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    text(ui, unit, theme::mono(12.0), theme::PRIMARY)
                });
            });
            let (track, _) =
                ui.allocate_exact_size(vec2(ui.available_width(), 3.0), Sense::hover());
            ui.painter().rect_filled(track, 1.0, theme::HIGHEST);
            let fraction =
                ((*value - range.start()) / (range.end() - range.start())).clamp(0.0, 1.0);
            ui.painter().rect_filled(
                Rect::from_min_size(track.min, vec2(track.width() * fraction, 3.0)),
                1.0,
                theme::PRIMARY_CONTAINER,
            );
        });
    changed
}

fn apply(ui: &mut Ui, studio: &mut Studio, local: &mut MaterialsUi, selected: usize) {
    let Some((_, draft)) = local.draft else {
        return;
    };
    let current = studio.viewport.materials().get(selected).copied();
    let pending = current.is_some_and(|current| current != draft);
    ui.add_enabled_ui(pending, |ui| {
        let width = ui.available_width();
        let _ = width;
        if widgets::wide_button(
            ui,
            Style::Pale,
            Some(icons::CHECK_CIRCLE),
            "Aplicar material",
            theme::mono_medium(15.0),
            40.0,
        )
        .clicked()
        {
            studio.viewport.set_material(selected, draft);
            studio.message = Some((
                format!(
                    "Material «{}» aplicado a la escena",
                    studio
                        .materials
                        .names
                        .get(selected)
                        .cloned()
                        .unwrap_or_default()
                ),
                false,
            ));
        }
    });
    ui.horizontal(|ui| {
        let (label, color) = if pending {
            ("Cambios sin aplicar", theme::WARNING)
        } else {
            ("Escena al día", theme::TELEMETRY)
        };
        text(ui, label, theme::mono(12.0), color);
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            text(
                ui,
                format!("Δt = {:.1} ms", studio.viewport.last_draw_ms),
                theme::mono(12.0),
                theme::TELEMETRY,
            );
        });
    });
}
