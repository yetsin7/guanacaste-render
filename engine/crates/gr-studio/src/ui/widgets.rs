//! Controls of the design system, painted to its measurements: 2 px corners, hairline borders, rectangular
//! scrubbers and pill toggles. Every control respects `ui.is_enabled()`, so a feature that does not exist yet is
//! shown greyed out instead of pretending to work.

use crate::theme;
use eframe::egui::{
    self, Align2, Color32, CornerRadius, FontId, Rect, Response, Sense, Stroke, StrokeKind, Ui,
    Vec2, pos2, vec2,
};

/// Colour of a control's content, faded when the control is disabled.
fn ink(ui: &Ui, color: Color32) -> Color32 {
    if ui.is_enabled() {
        color
    } else {
        color.gamma_multiply(0.38)
    }
}

/// Paints an icon at its natural size and returns its response.
pub fn icon(ui: &mut Ui, glyph: char, size: f32, color: Color32) -> Response {
    let (rect, response) = ui.allocate_exact_size(Vec2::splat(size), Sense::hover());
    let color = ink(ui, color);
    ui.painter().text(
        rect.center(),
        Align2::CENTER_CENTER,
        glyph,
        theme::icon(size),
        color,
    );
    response
}

/// Text in a given font and colour, faded when disabled.
pub fn text(ui: &mut Ui, value: impl Into<String>, font: FontId, color: Color32) -> Response {
    let color = ink(ui, color);
    ui.add(egui::Label::new(egui::RichText::new(value).font(font).color(color)).selectable(false))
}

/// An icon followed by text, centred on one line.
/// Painted as one piece, so it reads left to right in right-to-left rows too.
pub fn icon_text(ui: &mut Ui, glyph: char, value: &str, font: FontId, color: Color32) -> Response {
    let color = ink(ui, color);
    let icon_size = font.size + 3.0;
    let galley = ui.painter().layout_no_wrap(value.to_owned(), font, color);
    let size = vec2(
        icon_size + 6.0 + galley.size().x,
        galley.size().y.max(icon_size),
    );
    let (rect, response) = ui.allocate_exact_size(size, Sense::hover());
    ui.painter().text(
        pos2(rect.left() + icon_size / 2.0, rect.center().y),
        Align2::CENTER_CENTER,
        glyph,
        theme::icon(icon_size),
        color,
    );
    ui.painter().galley(
        pos2(
            rect.left() + icon_size + 6.0,
            rect.center().y - galley.size().y / 2.0,
        ),
        galley,
        color,
    );
    response
}

#[derive(Clone, Copy, PartialEq, Eq)]
pub enum Style {
    /// Electric blue fill, white text: the one main action of a panel.
    Primary,
    /// Pale cerulean fill, dark blue text: the prototype's call to action ("Open Viewport", "Apply Material").
    Pale,
    /// Card fill with a hairline border.
    Ghost,
    /// No fill until hovered.
    Quiet,
}

/// A button of the design system. `height` is the control height in points.
pub fn button(
    ui: &mut Ui,
    style: Style,
    glyph: Option<char>,
    label: &str,
    font: FontId,
    height: f32,
) -> Response {
    sized_button(ui, style, glyph, label, font, height, None)
}

/// A button filling the width left in the row, as the prototype's card actions do.
pub fn wide_button(
    ui: &mut Ui,
    style: Style,
    glyph: Option<char>,
    label: &str,
    font: FontId,
    height: f32,
) -> Response {
    let width = ui.available_width();
    sized_button(ui, style, glyph, label, font, height, Some(width))
}

fn sized_button(
    ui: &mut Ui,
    style: Style,
    glyph: Option<char>,
    label: &str,
    font: FontId,
    height: f32,
    width: Option<f32>,
) -> Response {
    let galley = ui
        .painter()
        .layout_no_wrap(label.to_owned(), font.clone(), Color32::WHITE);
    let icon_size = font.size + 3.0;
    let icon_width = glyph.map_or(0.0, |_| {
        icon_size + if label.is_empty() { 0.0 } else { 6.0 }
    });
    let padding = if label.is_empty() {
        (height - icon_size) / 2.0
    } else {
        12.0
    };
    let natural = galley.size().x + icon_width + padding * 2.0;
    let size = vec2(width.map_or(natural, |width| width.max(natural)), height);
    let (rect, response) = ui.allocate_exact_size(size, Sense::click());
    let hovered = response.hovered() && ui.is_enabled();
    let (fill, stroke, color) = match style {
        Style::Primary => (
            if hovered {
                theme::PRIMARY_CONTAINER
            } else {
                theme::ACTIVE
            },
            None,
            Color32::WHITE,
        ),
        Style::Pale => (
            if hovered {
                Color32::from_rgb(0xC4, 0xD6, 0xFF)
            } else {
                theme::PRIMARY
            },
            None,
            theme::ON_PRIMARY,
        ),
        Style::Ghost => (
            if hovered { theme::RAISED } else { theme::CARD },
            Some(theme::hairline()),
            theme::TEXT,
        ),
        Style::Quiet => (
            if hovered {
                theme::RAISED
            } else {
                Color32::TRANSPARENT
            },
            None,
            theme::TEXT_VARIANT,
        ),
    };
    let (fill, color) = if ui.is_enabled() {
        (fill, color)
    } else {
        (fill.gamma_multiply(0.45), color.gamma_multiply(0.45))
    };
    let painter = ui.painter();
    painter.rect_filled(rect, CornerRadius::same(2), fill);
    if let Some(stroke) = stroke {
        painter.rect_stroke(rect, CornerRadius::same(2), stroke, StrokeKind::Inside);
    }
    let content = galley.size().x + icon_width;
    let mut x = rect.center().x - content / 2.0;
    if let Some(glyph) = glyph {
        painter.text(
            pos2(x + icon_size / 2.0, rect.center().y),
            Align2::CENTER_CENTER,
            glyph,
            theme::icon(icon_size),
            color,
        );
        x += icon_width;
    }
    painter.text(
        pos2(x, rect.center().y),
        Align2::LEFT_CENTER,
        label,
        font,
        color,
    );
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

/// A square icon button (32 px by default), highlighted when `active`.
pub fn icon_button(ui: &mut Ui, glyph: char, size: f32, active: bool, tooltip: &str) -> Response {
    let (rect, response) = ui.allocate_exact_size(Vec2::splat(size), Sense::click());
    let hovered = response.hovered() && ui.is_enabled();
    if active || hovered {
        ui.painter().rect_filled(
            rect,
            CornerRadius::same(2),
            if active {
                theme::RAISED
            } else {
                theme::CONTAINER
            },
        );
    }
    let color = ink(
        ui,
        if active {
            theme::PRIMARY
        } else if hovered {
            theme::TEXT
        } else {
            theme::TEXT_VARIANT
        },
    );
    ui.painter().text(
        rect.center(),
        Align2::CENTER_CENTER,
        glyph,
        theme::icon(size * 0.56),
        color,
    );
    let response = if tooltip.is_empty() {
        response
    } else {
        response.on_hover_text(tooltip)
    };
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

/// A filter chip: text, an optional count, filled blue when selected.
pub fn chip(
    ui: &mut Ui,
    glyph: Option<char>,
    label: &str,
    count: Option<usize>,
    selected: bool,
) -> Response {
    let font = theme::mono_medium(12.0);
    let label_galley = ui
        .painter()
        .layout_no_wrap(label.to_owned(), font.clone(), Color32::WHITE);
    let count_text = count.map(|n| n.to_string()).unwrap_or_default();
    let count_galley =
        ui.painter()
            .layout_no_wrap(count_text.clone(), theme::mono(10.0), Color32::WHITE);
    let icon_width = glyph.map_or(0.0, |_| 18.0);
    let count_width = if count.is_some() {
        count_galley.size().x + 6.0
    } else {
        0.0
    };
    let size = vec2(
        label_galley.size().x + icon_width + count_width + 20.0,
        26.0,
    );
    let (rect, response) = ui.allocate_exact_size(size, Sense::click());
    let hovered = response.hovered() && ui.is_enabled();
    let fill = if selected {
        theme::ACTIVE
    } else if hovered {
        theme::RAISED
    } else {
        Color32::TRANSPARENT
    };
    ui.painter().rect_filled(rect, CornerRadius::same(2), fill);
    let color = ink(
        ui,
        if selected {
            Color32::WHITE
        } else {
            theme::TEXT
        },
    );
    let mut x = rect.left() + 10.0;
    if let Some(glyph) = glyph {
        ui.painter().text(
            pos2(x + 7.0, rect.center().y),
            Align2::CENTER_CENTER,
            glyph,
            theme::icon(15.0),
            color,
        );
        x += icon_width;
    }
    ui.painter().text(
        pos2(x, rect.center().y),
        Align2::LEFT_CENTER,
        label,
        font,
        color,
    );
    if count.is_some() {
        let count_color = ink(
            ui,
            if selected {
                Color32::WHITE.gamma_multiply(0.85)
            } else {
                theme::OUTLINE
            },
        );
        ui.painter().text(
            pos2(x + label_galley.size().x + 5.0, rect.center().y - 1.0),
            Align2::LEFT_CENTER,
            count_text,
            theme::mono(10.0),
            count_color,
        );
    }
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

/// A row of mutually exclusive options; returns true when the selection changed.
pub fn segmented(ui: &mut Ui, options: &[&str], selected: &mut usize, width: f32) -> bool {
    let height = 32.0;
    let (rect, _) = ui.allocate_exact_size(vec2(width, height), Sense::hover());
    ui.painter()
        .rect_filled(rect, CornerRadius::same(2), theme::RECESSED);
    let cell = width / options.len() as f32;
    let mut changed = false;
    for (index, option) in options.iter().enumerate() {
        let cell_rect = Rect::from_min_size(
            pos2(rect.left() + cell * index as f32, rect.top()),
            vec2(cell, height),
        )
        .shrink(2.0);
        let response = ui.interact(
            cell_rect,
            ui.id().with(("segment", option, index)),
            Sense::click(),
        );
        let active = *selected == index;
        if active {
            ui.painter()
                .rect_filled(cell_rect, CornerRadius::same(2), theme::ACTIVE);
        } else if response.hovered() && ui.is_enabled() {
            ui.painter()
                .rect_filled(cell_rect, CornerRadius::same(2), theme::RAISED);
        }
        let color = ink(
            ui,
            if active {
                Color32::WHITE
            } else {
                theme::TEXT_VARIANT
            },
        );
        let font = if active {
            theme::mono_medium(13.0)
        } else {
            theme::mono(13.0)
        };
        ui.painter().text(
            cell_rect.center(),
            Align2::CENTER_CENTER,
            *option,
            font,
            color,
        );
        if response.clicked() && ui.is_enabled() && !active {
            *selected = index;
            changed = true;
        }
    }
    changed
}

/// A pill toggle: blue track and white knob when on.
pub fn toggle(ui: &mut Ui, on: &mut bool) -> Response {
    let (rect, mut response) = ui.allocate_exact_size(vec2(40.0, 20.0), Sense::click());
    if response.clicked() && ui.is_enabled() {
        *on = !*on;
        response.mark_changed();
    }
    let t = ui.ctx().animate_bool(response.id, *on);
    let track = if *on { theme::ACTIVE } else { theme::HIGHEST };
    let painter = ui.painter();
    painter.rect_filled(rect, CornerRadius::same(10), ink(ui, track));
    let knob_x = egui::lerp(rect.left() + 10.0..=rect.right() - 10.0, t);
    let knob = if *on { Color32::WHITE } else { theme::OUTLINE };
    painter.circle_filled(pos2(knob_x, rect.center().y), 7.0, ink(ui, knob));
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

/// A wide bar slider, as in the Build properties: a rounded 8 px bar filled in pale blue. Returns true while it
/// changes the value.
pub fn bar_slider(
    ui: &mut Ui,
    value: &mut f32,
    range: std::ops::RangeInclusive<f32>,
    width: f32,
) -> bool {
    let (rect, response) = ui.allocate_exact_size(vec2(width, 14.0), Sense::click_and_drag());
    let changed = drag_value(ui, &response, rect, value, &range);
    let bar = Rect::from_center_size(rect.center(), vec2(rect.width(), 8.0));
    let fraction = ((*value - range.start()) / (range.end() - range.start())).clamp(0.0, 1.0);
    let painter = ui.painter();
    painter.rect_filled(bar, CornerRadius::same(4), theme::HIGHEST);
    painter.rect_filled(
        Rect::from_min_size(bar.min, vec2(bar.width() * fraction, bar.height())),
        CornerRadius::same(4),
        ink(ui, theme::PRIMARY),
    );
    changed
}

/// A thin precision slider: 2 px track, blue progress and a round thumb, as in the material editor.
pub fn thin_slider(
    ui: &mut Ui,
    value: &mut f32,
    range: std::ops::RangeInclusive<f32>,
    width: f32,
) -> bool {
    let (rect, response) = ui.allocate_exact_size(vec2(width, 18.0), Sense::click_and_drag());
    let changed = drag_value(ui, &response, rect, value, &range);
    let fraction = ((*value - range.start()) / (range.end() - range.start())).clamp(0.0, 1.0);
    let y = rect.center().y;
    let x = rect.left() + rect.width() * fraction;
    let painter = ui.painter();
    painter.rect_filled(
        Rect::from_min_max(pos2(rect.left(), y - 2.0), pos2(rect.right(), y + 2.0)),
        CornerRadius::same(2),
        theme::HIGHEST,
    );
    painter.rect_filled(
        Rect::from_min_max(pos2(rect.left(), y - 2.0), pos2(x, y + 2.0)),
        CornerRadius::same(2),
        ink(ui, theme::PRIMARY_CONTAINER),
    );
    painter.circle_filled(pos2(x, y), 8.0, ink(ui, theme::PRIMARY_CONTAINER));
    painter.circle_stroke(pos2(x, y), 8.0, Stroke::new(2.0, theme::CANVAS));
    changed
}

fn drag_value(
    ui: &Ui,
    response: &Response,
    rect: Rect,
    value: &mut f32,
    range: &std::ops::RangeInclusive<f32>,
) -> bool {
    if !ui.is_enabled() {
        return false;
    }
    let Some(pointer) = response
        .interact_pointer_pos()
        .filter(|_| response.dragged() || response.clicked())
    else {
        return false;
    };
    let fraction = ((pointer.x - rect.left()) / rect.width()).clamp(0.0, 1.0);
    let new_value = range.start() + (range.end() - range.start()) * fraction;
    let changed = (new_value - *value).abs() > f32::EPSILON;
    *value = new_value;
    changed
}

/// A section heading: icon, uppercase label in the accent colour, and an optional note on the right.
pub fn section_title(ui: &mut Ui, glyph: Option<char>, title: &str, note: Option<(&str, Color32)>) {
    ui.horizontal(|ui| {
        ui.spacing_mut().item_spacing.x = 6.0;
        if let Some(glyph) = glyph {
            icon(ui, glyph, 17.0, theme::PRIMARY);
        }
        text(
            ui,
            title.to_uppercase(),
            theme::mono_medium(13.0),
            theme::PRIMARY,
        );
        if let Some((note, color)) = note {
            ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                text(ui, note, theme::mono(11.0), color);
            });
        }
    });
}

/// A key cap, as in the help and status strips.
pub fn key(ui: &mut Ui, label: &str, color: Color32) -> Response {
    let font = theme::mono_medium(12.0);
    let galley = ui
        .painter()
        .layout_no_wrap(label.to_owned(), font.clone(), color);
    let (rect, response) =
        ui.allocate_exact_size(vec2(galley.size().x + 16.0, 24.0), Sense::hover());
    ui.painter()
        .rect_filled(rect, CornerRadius::same(2), theme::HIGHEST);
    ui.painter()
        .text(rect.center(), Align2::CENTER_CENTER, label, font, color);
    response
}

/// A status dot and its text, as in "● 37 FPS".
/// Painted as one piece, so it reads left to right in right-to-left rows too.
pub fn status(ui: &mut Ui, value: &str, color: Color32, font: FontId) {
    let color = ink(ui, color);
    let galley = ui.painter().layout_no_wrap(value.to_owned(), font, color);
    let (rect, _) = ui.allocate_exact_size(
        vec2(14.0 + galley.size().x, galley.size().y),
        Sense::hover(),
    );
    ui.painter()
        .circle_filled(pos2(rect.left() + 4.0, rect.center().y), 3.5, color);
    ui.painter()
        .galley(pos2(rect.left() + 14.0, rect.top()), galley, color);
}

/// A small boxed tag: "Step 1 of 4", "Active", "HD".
pub fn tag(ui: &mut Ui, value: &str, fill: Color32, color: Color32) -> Response {
    let font = theme::mono_medium(12.0);
    let galley = ui
        .painter()
        .layout_no_wrap(value.to_owned(), font.clone(), color);
    let (rect, response) =
        ui.allocate_exact_size(vec2(galley.size().x + 14.0, 22.0), Sense::hover());
    ui.painter().rect_filled(rect, CornerRadius::same(2), fill);
    ui.painter()
        .text(rect.center(), Align2::CENTER_CENTER, value, font, color);
    response
}

/// A thin horizontal rule.
pub fn rule(ui: &mut Ui) {
    let width = ui.available_width();
    let (rect, _) = ui.allocate_exact_size(vec2(width, 1.0), Sense::hover());
    ui.painter()
        .rect_filled(rect, CornerRadius::ZERO, theme::HAIRLINE);
}

/// A line chart of recent values (frame times, profiles), drawn in the telemetry colour over a recessed slot.
pub fn sparkline(
    ui: &mut Ui,
    values: &[f32],
    range: std::ops::RangeInclusive<f32>,
    size: Vec2,
    guides: &[(f32, &str)],
) {
    let (rect, _) = ui.allocate_exact_size(size, Sense::hover());
    let painter = ui.painter();
    painter.rect_filled(rect, CornerRadius::same(2), theme::RECESSED);
    let to_y = |value: f32| {
        let fraction = ((value - range.start()) / (range.end() - range.start())).clamp(0.0, 1.0);
        rect.bottom() - 4.0 - fraction * (rect.height() - 8.0)
    };
    for (value, label) in guides {
        let y = to_y(*value);
        let mut x = rect.left() + 6.0;
        while x < rect.right() - 6.0 {
            painter.line_segment(
                [pos2(x, y), pos2((x + 3.0).min(rect.right() - 6.0), y)],
                Stroke::new(1.0, theme::HIGHEST),
            );
            x += 6.0;
        }
        painter.text(
            pos2(rect.left() + 6.0, y - 2.0),
            Align2::LEFT_BOTTOM,
            *label,
            theme::mono(10.0),
            theme::OUTLINE,
        );
    }
    if values.len() >= 2 {
        let step = rect.width() / (values.len() - 1) as f32;
        let points: Vec<_> = values
            .iter()
            .enumerate()
            .map(|(i, v)| pos2(rect.left() + step * i as f32, to_y(*v)))
            .collect();
        painter.add(egui::Shape::line(
            points,
            Stroke::new(1.5, theme::TELEMETRY),
        ));
    }
}
