//! Pieces several pages share: panels floating over the 3D view, the empty state, and the preview and render
//! controls.

use crate::app::Studio;
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, Style, text};
use crate::viewport::ViewMode;
use eframe::egui::{self, Align2, Frame, Pos2, Rect, Ui, vec2};

/// Draws `add` in a panel floating over the 3D view at `pivot`: `anchor` says which corner of the panel sits there.
/// The panel takes the pointer, so dragging on it does not turn the camera.
pub fn floating<R>(
    ui: &Ui,
    id: &str,
    pivot: Pos2,
    anchor: Align2,
    width: f32,
    frame: Frame,
    add: impl FnOnce(&mut Ui) -> R,
) -> R {
    egui::Area::new(egui::Id::new(id))
        .order(egui::Order::Middle)
        .pivot(anchor)
        .fixed_pos(pivot)
        .constrain(false)
        .show(ui.ctx(), |ui| {
            frame
                .show(ui, |ui| {
                    if width > 0.0 {
                        ui.set_width(width);
                    }
                    add(ui)
                })
                .inner
        })
        .inner
}

/// What a page shows in place of the 3D view when no project is open.
pub fn empty_state(ui: &mut Ui, studio: &mut Studio, rect: Rect) {
    ui.painter().rect_filled(rect, 0.0, theme::CANVAS);
    let card = Rect::from_center_size(rect.center(), vec2(440.0, 190.0));
    let mut child = ui.new_child(egui::UiBuilder::new().max_rect(card));
    theme::card()
        .inner_margin(egui::Margin::same(24))
        .show(&mut child, |ui| {
            ui.set_width(card.width() - 48.0);
            ui.vertical_centered(|ui| {
                widgets::icon(ui, icons::FOLDER_OPEN, 32.0, theme::PRIMARY);
                ui.add_space(6.0);
                if let Some((_, name)) = &studio.loading {
                    ui.add(egui::Spinner::new().color(theme::PRIMARY));
                    text(
                        ui,
                        format!("Abriendo {name}…"),
                        theme::heading(16.0),
                        theme::TEXT,
                    );
                } else {
                    text(
                        ui,
                        "Ningún proyecto abierto",
                        theme::heading(16.0),
                        theme::TEXT,
                    );
                    text(
                        ui,
                        "Abre un proyecto .gr o .g3d para verlo en esta pantalla.",
                        theme::body(13.0),
                        theme::TEXT_VARIANT,
                    );
                    ui.add_space(8.0);
                    if widgets::button(
                        ui,
                        Style::Pale,
                        Some(icons::FOLDER_OPEN),
                        "Abrir proyecto",
                        theme::mono_medium(13.0),
                        30.0,
                    )
                    .clicked()
                    {
                        studio.open_dialog(ui.ctx());
                    }
                }
            });
        });
}

/// "Navegación", "Vista previa" or "Render", with its progress.
pub fn mode_label(studio: &Studio) -> String {
    let viewport = &studio.viewport;
    match viewport.mode() {
        ViewMode::Navigate => "NAVEGACIÓN · SIN TRAZADO".into(),
        ViewMode::Preview => format!(
            "VISTA PREVIA · TRAZADO ({}/{} SPP)",
            viewport.samples().min(viewport.target_samples()),
            viewport.target_samples()
        ),
        ViewMode::Render => format!(
            "RENDER · TRAZADO ({}/{} SPP)",
            viewport.samples().min(viewport.target_samples()),
            viewport.target_samples()
        ),
    }
}

/// The colour of the status dot for the current mode: teal while navigating or when finished, blue while tracing.
pub fn mode_color(studio: &Studio) -> egui::Color32 {
    if studio.viewport.mode() == ViewMode::Navigate || studio.viewport.is_finished() {
        theme::TELEMETRY
    } else {
        theme::PRIMARY
    }
}

/// The two buttons that leave navigation: a quick preview and the final render. Pressing the active one returns to
/// navigation.
pub fn trace_buttons(ui: &mut Ui, studio: &mut Studio) {
    let mode = studio.viewport.mode();
    let preview = if mode == ViewMode::Preview {
        Style::Primary
    } else {
        Style::Ghost
    };
    if widgets::button(
        ui,
        preview,
        Some(icons::VISIBILITY),
        "Vista previa",
        theme::mono_medium(12.0),
        30.0,
    )
    .on_hover_text("Una pasada ligera de trazado de rayos para ver cómo quedará la luz (V)")
    .clicked()
    {
        studio.viewport.set_mode(if mode == ViewMode::Preview {
            ViewMode::Navigate
        } else {
            ViewMode::Preview
        });
    }
    let render = if mode == ViewMode::Render {
        Style::Primary
    } else {
        Style::Pale
    };
    if widgets::button(
        ui,
        render,
        Some(icons::CAMERA),
        "Render",
        theme::mono_medium(12.0),
        30.0,
    )
    .on_hover_text("Render final a calidad completa (R)")
    .clicked()
    {
        studio.viewport.set_mode(if mode == ViewMode::Render {
            ViewMode::Navigate
        } else {
            ViewMode::Render
        });
    }
}

/// The preview and render buttons on a line of their own.
pub fn trace_buttons_row(ui: &mut Ui, studio: &mut Studio) {
    ui.horizontal(|ui| trace_buttons(ui, studio));
}

/// Keys that work over every view: V preview, R render, H home view, Ctrl+S save the picture.
pub fn view_shortcuts(ui: &Ui, studio: &mut Studio) {
    if ui.ctx().egui_wants_keyboard_input() {
        return;
    }
    let (preview, render, home, save) = ui.input(|input| {
        (
            input.key_pressed(egui::Key::V),
            input.key_pressed(egui::Key::R),
            input.key_pressed(egui::Key::H),
            input.modifiers.command && input.key_pressed(egui::Key::S),
        )
    });
    let mode = studio.viewport.mode();
    if preview {
        studio.viewport.set_mode(if mode == ViewMode::Preview {
            ViewMode::Navigate
        } else {
            ViewMode::Preview
        });
    }
    if render {
        studio.viewport.set_mode(if mode == ViewMode::Render {
            ViewMode::Navigate
        } else {
            ViewMode::Render
        });
    }
    if home {
        studio.viewport.go_home();
    }
    if save && studio.viewport.has_scene() {
        studio.save_image_dialog();
    }
}

/// A projected point of the scene in the view, or `None` behind the camera.
pub fn project(studio: &Studio, rect: Rect, point: glam::Vec3) -> Option<Pos2> {
    let camera = studio.viewport.camera().to_scene();
    let frame = gr_gpu::CameraFrame::new(
        &camera,
        rect.width().max(1.0) as u32,
        rect.height().max(1.0) as u32,
    );
    let d = point - frame.position;
    let depth = d.dot(frame.forward);
    if depth <= 0.05 {
        return None;
    }
    let x = d.dot(frame.right) / frame.right.length_squared() / depth;
    let y = d.dot(frame.up) / frame.up.length_squared() / depth;
    Some(egui::pos2(
        rect.center().x + x * rect.width() / 2.0,
        rect.center().y - y * rect.height() / 2.0,
    ))
}

/// Numbers grouped by thousands with thin spaces: 2 201 200.
pub fn grouped(value: usize) -> String {
    let digits = value.to_string();
    let mut out = String::new();
    for (index, digit) in digits.chars().enumerate() {
        if index > 0 && (digits.len() - index).is_multiple_of(3) {
            out.push(' ');
        }
        out.push(digit);
    }
    out
}

/// "142k", "2.2M": a triangle count at a glance.
pub fn compact(value: usize) -> String {
    match value {
        0..1_000 => value.to_string(),
        1_000..1_000_000 => format!("{:.0}k", value as f32 / 1000.0),
        _ => format!("{:.1}M", value as f32 / 1_000_000.0),
    }
}

/// Bytes as megabytes or gigabytes.
pub fn size(bytes: f64) -> String {
    if bytes >= 1024.0 * 1024.0 * 1024.0 {
        format!("{:.1} GB", bytes / 1024.0 / 1024.0 / 1024.0)
    } else if bytes >= 1024.0 * 1024.0 {
        format!("{:.1} MB", bytes / 1024.0 / 1024.0)
    } else {
        format!("{:.1} KB", bytes / 1024.0)
    }
}

/// The dedicated video memory of the adapter the studio draws with, when Windows reports it.
pub fn video_memory_total(studio: &Studio) -> Option<f64> {
    studio
        .telemetry
        .hardware
        .memory_of(&studio.viewport.facts.adapter)
        .map(|bytes| bytes as f64)
}

#[cfg(test)]
mod tests {
    #[test]
    fn numbers_read_at_a_glance() {
        assert_eq!(super::grouped(2_201_200), "2 201 200");
        assert_eq!(super::compact(142_300), "142k");
        assert_eq!(super::compact(2_201_200), "2.2M");
        assert_eq!(super::size(1536.0 * 1024.0 * 1024.0), "1.5 GB");
    }
}
