//! What frames every page: the 28 px title bar (the window has no system frame) and the 48 px tool rail.

use crate::app::{Page, Studio};
use crate::theme;
use crate::ui::icons;
use crate::ui::widgets::{self, text};
use crate::viewport::ViewMode;
use eframe::egui::{
    self, Align, Align2, Color32, CornerRadius, Layout, Rect, Sense, Ui, ViewportCommand, pos2,
    vec2,
};

/// The title bar: product and project on the left, frame rate and window buttons on the right. Dragging it moves
/// the window and a double click maximises it.
pub fn title_bar(ui: &mut Ui, studio: &mut Studio) {
    let bar = ui.max_rect();
    let drag = ui.interact(bar, ui.id().with("title-drag"), Sense::click_and_drag());
    if drag.drag_started() {
        ui.ctx().send_viewport_cmd(ViewportCommand::StartDrag);
    }
    if drag.double_clicked() {
        let maximized = ui
            .ctx()
            .input(|input| input.viewport().maximized.unwrap_or(false));
        ui.ctx()
            .send_viewport_cmd(ViewportCommand::Maximized(!maximized));
    }

    ui.horizontal_centered(|ui| {
        ui.add_space(8.0);
        ui.spacing_mut().item_spacing.x = 6.0;
        match &studio.logo {
            Some(logo) => {
                ui.add(egui::Image::new(logo).fit_to_exact_size(vec2(16.0, 16.0)));
            }
            None => {
                widgets::icon(ui, icons::LAYERS, 16.0, theme::PRIMARY);
            }
        }
        text(
            ui,
            "GUANACASTE RENDER",
            theme::mono_medium(12.0),
            theme::TEXT,
        );
        separator(ui);
        // The open project, where the prototype shows its layer selector.
        let project = studio
            .project_name()
            .unwrap_or_else(|| "Sin proyecto".into());
        egui::Frame::new()
            .fill(theme::CONTAINER)
            .stroke(egui::Stroke::new(1.0, theme::HIGHEST))
            .corner_radius(CornerRadius::same(2))
            .inner_margin(egui::Margin::symmetric(6, 1))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.spacing_mut().item_spacing.x = 4.0;
                    widgets::icon(ui, icons::LAYERS, 12.0, theme::OUTLINE);
                    text(ui, project, theme::mono(11.0), theme::TEXT_VARIANT);
                    widgets::icon(ui, icons::EXPAND_MORE, 12.0, theme::OUTLINE);
                });
            });

        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            ui.spacing_mut().item_spacing.x = 0.0;
            window_buttons(ui);
            ui.add_space(6.0);
            ui.spacing_mut().item_spacing.x = 8.0;
            let (rect, _) = ui.allocate_exact_size(vec2(20.0, 20.0), Sense::hover());
            ui.painter()
                .circle_filled(rect.center(), 10.0, theme::PRIMARY);
            ui.painter().text(
                rect.center(),
                Align2::CENTER_CENTER,
                icons::PERSON,
                theme::icon(14.0),
                theme::ON_PRIMARY,
            );
            frame_rate_pill(ui, studio);
        });
    });
}

/// "● 60 FPS | 1.6K": the measured interface frame rate and the width of the picture being drawn.
fn frame_rate_pill(ui: &mut Ui, studio: &Studio) {
    let fps = studio.telemetry.fps();
    let (width, _) = studio.viewport.resolution();
    let mode = match studio.viewport.mode() {
        ViewMode::Navigate => "NAV",
        ViewMode::Preview => "PREV",
        ViewMode::Render => "RT",
    };
    egui::Frame::new()
        .fill(theme::RECESSED)
        .stroke(egui::Stroke::new(1.0, theme::HIGHEST))
        .corner_radius(CornerRadius::same(2))
        .inner_margin(egui::Margin::symmetric(6, 1))
        .show(ui, |ui| {
            let size = if width >= 1000 {
                format!("{:.1}K", width as f32 / 1000.0)
            } else {
                width.to_string()
            };
            let detail = if studio.viewport.has_scene() {
                format!("  |  {size} {mode}")
            } else {
                String::new()
            };
            let color = if studio.telemetry.is_idle() || fps >= 30.0 {
                theme::TELEMETRY
            } else {
                theme::WARNING
            };
            widgets::status(
                ui,
                &format!("{}{detail}", studio.telemetry.fps_label()),
                color,
                theme::mono(11.0),
            );
        });
}

fn window_buttons(ui: &mut Ui) {
    let maximized = ui
        .ctx()
        .input(|input| input.viewport().maximized.unwrap_or(false));
    for (glyph, command, danger) in [
        (icons::CLOSE, ViewportCommand::Close, true),
        (
            if maximized {
                icons::RESTORE_WINDOW
            } else {
                icons::MAXIMIZE
            },
            ViewportCommand::Maximized(!maximized),
            false,
        ),
        (icons::MINIMIZE, ViewportCommand::Minimized(true), false),
    ] {
        let (rect, response) = ui.allocate_exact_size(vec2(32.0, 28.0), Sense::click());
        let hovered = response.hovered();
        if hovered {
            ui.painter().rect_filled(
                rect,
                0.0,
                if danger {
                    Color32::from_rgb(0x93, 0x00, 0x0A)
                } else {
                    theme::CONTAINER
                },
            );
        }
        let color = if hovered { theme::TEXT } else { theme::OUTLINE };
        ui.painter().text(
            rect.center(),
            Align2::CENTER_CENTER,
            glyph,
            theme::icon(15.0),
            color,
        );
        if response.clicked() {
            ui.ctx().send_viewport_cmd(command);
        }
    }
    let (rect, _) = ui.allocate_exact_size(vec2(1.0, 28.0), Sense::hover());
    ui.painter().rect_filled(rect, 0.0, theme::HIGHEST);
}

fn separator(ui: &mut Ui) {
    let (rect, _) = ui.allocate_exact_size(vec2(9.0, 12.0), Sense::hover());
    ui.painter().rect_filled(
        Rect::from_center_size(rect.center(), vec2(1.0, 12.0)),
        0.0,
        theme::FLOATING_BORDER,
    );
}

/// The tool rail: one 40 px button per workspace, settings and help at the bottom.
pub fn rail(ui: &mut Ui, studio: &mut Studio) {
    let full = ui.max_rect();
    let top: [(char, &str, Page, &[Page]); 5] = [
        (
            icons::FOLDER_OPEN,
            "Proyectos",
            Page::Projects,
            &[Page::Projects],
        ),
        (
            icons::BUILD,
            "Construcción",
            Page::Build,
            &[Page::Build, Page::Materials],
        ),
        (
            icons::LIBRARY,
            "Biblioteca",
            Page::Library,
            &[Page::Library, Page::Inspector],
        ),
        (
            icons::MOVIE,
            "Película y animación",
            Page::Movie,
            &[Page::Movie],
        ),
        (
            icons::CAMERA,
            "Estudio de render",
            Page::Render,
            &[Page::Render],
        ),
    ];
    let bottom: [(char, &str, Page); 3] = [
        (icons::SPEED, "Rendimiento del equipo", Page::Settings),
        (icons::SETTINGS, "Ajustes", Page::Settings),
        (icons::HELP, "Ayuda y diagnóstico", Page::Help),
    ];
    let mut y = full.top() + 4.0;
    for (glyph, tooltip, page, active_on) in top {
        if rail_button(
            ui,
            pos2(full.center().x, y + 20.0),
            glyph,
            20.0,
            active_on.contains(&studio.page),
            tooltip,
        ) {
            studio.page = page;
        }
        y += 44.0;
    }
    let mut y = full.bottom() - 4.0 - 44.0 * bottom.len() as f32;
    ui.painter().rect_filled(
        Rect::from_min_size(pos2(full.left(), y - 6.0), vec2(full.width(), 1.0)),
        0.0,
        theme::HIGHEST,
    );
    for (index, (glyph, tooltip, page)) in bottom.into_iter().enumerate() {
        // The speed and settings buttons both open Settings; only the settings one shows as active.
        let active = studio.page == page && index != 0;
        if rail_button(
            ui,
            pos2(full.center().x, y + 20.0),
            glyph,
            18.0,
            active,
            tooltip,
        ) {
            studio.page = page;
        }
        y += 44.0;
    }
}

fn rail_button(
    ui: &mut Ui,
    center: egui::Pos2,
    glyph: char,
    size: f32,
    active: bool,
    tooltip: &str,
) -> bool {
    let rect = Rect::from_center_size(center, vec2(40.0, 40.0));
    let response = ui
        .interact(rect, ui.id().with(("rail", tooltip)), Sense::click())
        .on_hover_text(tooltip);
    let painter = ui.painter();
    if active {
        painter.rect_filled(rect, CornerRadius::same(2), theme::RAISED);
        painter.rect_filled(
            Rect::from_min_size(rect.min, vec2(2.0, rect.height())),
            0.0,
            theme::PRIMARY_CONTAINER,
        );
    } else if response.hovered() {
        painter.rect_filled(rect, CornerRadius::same(2), theme::CONTAINER);
    }
    let color = if active {
        theme::PRIMARY
    } else if response.hovered() {
        theme::TEXT
    } else {
        theme::TEXT_VARIANT
    };
    painter.text(
        rect.center(),
        Align2::CENTER_CENTER,
        glyph,
        theme::icon(size),
        color,
    );
    response
        .on_hover_cursor(egui::CursorIcon::PointingHand)
        .clicked()
}

/// A window without a system frame still resizes from its edges and corners.
pub fn resize_edges(context: &egui::Context) {
    let maximized = context.input(|input| input.viewport().maximized.unwrap_or(false));
    if maximized {
        return;
    }
    let screen = context.content_rect();
    let edge = 5.0;
    use egui::ResizeDirection as Direction;
    let zones = [
        (
            Rect::from_min_max(screen.min, pos2(screen.left() + edge, screen.top() + edge)),
            Direction::NorthWest,
            egui::CursorIcon::ResizeNorthWest,
        ),
        (
            Rect::from_min_max(
                pos2(screen.right() - edge, screen.top()),
                pos2(screen.right(), screen.top() + edge),
            ),
            Direction::NorthEast,
            egui::CursorIcon::ResizeNorthEast,
        ),
        (
            Rect::from_min_max(
                pos2(screen.left(), screen.bottom() - edge),
                pos2(screen.left() + edge, screen.bottom()),
            ),
            Direction::SouthWest,
            egui::CursorIcon::ResizeSouthWest,
        ),
        (
            Rect::from_min_max(
                pos2(screen.right() - edge, screen.bottom() - edge),
                screen.max,
            ),
            Direction::SouthEast,
            egui::CursorIcon::ResizeSouthEast,
        ),
        (
            Rect::from_min_max(screen.min, pos2(screen.right(), screen.top() + edge)),
            Direction::North,
            egui::CursorIcon::ResizeVertical,
        ),
        (
            Rect::from_min_max(pos2(screen.left(), screen.bottom() - edge), screen.max),
            Direction::South,
            egui::CursorIcon::ResizeVertical,
        ),
        (
            Rect::from_min_max(screen.min, pos2(screen.left() + edge, screen.bottom())),
            Direction::West,
            egui::CursorIcon::ResizeHorizontal,
        ),
        (
            Rect::from_min_max(pos2(screen.right() - edge, screen.top()), screen.max),
            Direction::East,
            egui::CursorIcon::ResizeHorizontal,
        ),
    ];
    egui::Area::new(egui::Id::new("resize-edges"))
        .order(egui::Order::Foreground)
        .fixed_pos(screen.min)
        .interactable(true)
        .show(context, |ui| {
            for (index, (rect, direction, cursor)) in zones.into_iter().enumerate() {
                let response = ui
                    .interact(rect, egui::Id::new(("resize-edge", index)), Sense::drag())
                    .on_hover_cursor(cursor);
                if response.drag_started() {
                    context.send_viewport_cmd(ViewportCommand::BeginResize(direction));
                }
            }
        });
}
