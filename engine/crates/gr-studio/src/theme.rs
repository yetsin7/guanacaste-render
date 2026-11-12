//! Obsidian Precision CAD dark theme for Guanacaste Render Studio.
//! Calibrated to match the Lumion Pro 2026 workstation specification.

use eframe::egui::{self, Color32, CornerRadius, FontId, Frame, Margin, Stroke, Vec2};

// -----------------------------------------------------------------------------
// Obsidian Precision CAD Color Palette
// -----------------------------------------------------------------------------

/// Base void canvas behind 3D viewports.
pub const BACKGROUND: Color32 = Color32::from_rgb(0x12, 0x13, 0x16);

/// Docked sidebars, top menu bar, and status strips.
pub const PANEL: Color32 = Color32::from_rgb(0x1A, 0x1B, 0x1E);

/// Raised surface cards, property groupings, and dropdown panels.
pub const RAISED: Color32 = Color32::from_rgb(0x1F, 0x1F, 0x23);

/// Elevated cards, hover states, and active tool containers.
pub const CARD_HIGH: Color32 = Color32::from_rgb(0x29, 0x2A, 0x2D);

/// Deepest recessed container for specimen previews, inputs, and dark void slots.
pub const DEEPEST: Color32 = Color32::from_rgb(0x0D, 0x0E, 0x11);

/// Low-contrast structural hairline borders (1px).
pub const BORDER: Color32 = Color32::from_rgb(0x42, 0x47, 0x52);

/// Lighter outline for focused elements or active input edges.
pub const BORDER_BRIGHT: Color32 = Color32::from_rgb(0x8D, 0x90, 0x9D);

/// Primary focal accent (Cerulean blue) for typography, gizmo cues, and selections.
pub const ACCENT: Color32 = Color32::from_rgb(0xAD, 0xC6, 0xFF);

/// Secondary active state (Electric blue) for pressed buttons, toggles, and tracks.
pub const ACCENT_PRESS: Color32 = Color32::from_rgb(0x06, 0x71, 0xD7);

/// Telemetry nominal state (Teal) for FPS, VRAM health, and verified parameters.
pub const TEAL: Color32 = Color32::from_rgb(0x5C, 0xDB, 0xC0);

/// Critical error / warning accent.
pub const ERROR: Color32 = Color32::from_rgb(0xFF, 0xB4, 0xAB);

/// High-contrast primary text on dark surfaces.
pub const TEXT: Color32 = Color32::from_rgb(0xE3, 0xE2, 0xE6);

/// Subdued secondary text for metadata labels and inactive indicators.
pub const MUTED: Color32 = Color32::from_rgb(0xC3, 0xC6, 0xD4);

// -----------------------------------------------------------------------------
// Typography Helpers
// -----------------------------------------------------------------------------

/// Technical monospace font for metrics, numbers, and telemetry readouts.
pub fn mono_font() -> FontId {
    FontId::new(12.0, egui::FontFamily::Monospace)
}

/// Small uppercase technical label font.
pub fn label_font() -> FontId {
    FontId::new(11.0, egui::FontFamily::Monospace)
}

/// Proportional body font for UI descriptions and standard copy.
pub fn body_font() -> FontId {
    FontId::proportional(13.0)
}

/// Uses Windows' system symbol font for the compact workstation rail.
pub fn icon_font() -> FontId {
    FontId::new(17.0, egui::FontFamily::Name("studio-icons".into()))
}

// -----------------------------------------------------------------------------
// Structural Frames & Strokes
// -----------------------------------------------------------------------------

/// Standard 1px architectural hairline containment stroke.
pub fn hairline() -> Stroke {
    Stroke::new(1.0, BORDER)
}

/// Standard card frame container with subtle elevation.
#[allow(dead_code)]
pub fn card_frame() -> Frame {
    Frame::new()
        .fill(RAISED)
        .stroke(hairline())
        .corner_radius(CornerRadius::same(4))
        .inner_margin(Margin::same(8))
}

/// Bordered panel frame used in docked decks.
#[allow(dead_code)]
pub fn panel_frame() -> Frame {
    Frame::new()
        .fill(PANEL)
        .stroke(hairline())
        .corner_radius(CornerRadius::same(2))
        .inner_margin(Margin::same(6))
}

// -----------------------------------------------------------------------------
// Theme Setup
// -----------------------------------------------------------------------------

/// Configures egui visuals and layout metrics to follow Obsidian Precision CAD standards.
pub fn apply(context: &egui::Context) {
    let mut fonts = egui::FontDefinitions::default();
    for (name, path, family) in [
        ("studio-body", r"C:\Windows\Fonts\segoeui.ttf", egui::FontFamily::Proportional),
        ("studio-mono", r"C:\Windows\Fonts\consola.ttf", egui::FontFamily::Monospace),
    ] {
        if let Ok(bytes) = std::fs::read(path) {
            fonts.font_data.insert(name.into(), egui::FontData::from_owned(bytes).into());
            fonts.families.entry(family).or_default().insert(0, name.into());
        }
    }
    if let Ok(symbols) = std::fs::read(r"C:\Windows\Fonts\segmdl2.ttf") {
        fonts
            .font_data
            .insert("studio-icons".into(), egui::FontData::from_owned(symbols).into());
        fonts
            .families
            .insert(egui::FontFamily::Name("studio-icons".into()), vec!["studio-icons".into()]);
    } else {
        fonts
            .families
            .insert(egui::FontFamily::Name("studio-icons".into()), vec!["Ubuntu-Light".into()]);
    }
    context.set_fonts(fonts);
    context.set_theme(egui::Theme::Dark);

    let mut visuals = egui::Visuals::dark();
    visuals.panel_fill = PANEL;
    visuals.window_fill = PANEL;
    visuals.extreme_bg_color = DEEPEST;
    visuals.faint_bg_color = RAISED;
    visuals.override_text_color = Some(TEXT);

    visuals.selection.bg_fill = ACCENT_PRESS.gamma_multiply(0.35);
    visuals.selection.stroke = Stroke::new(1.0, ACCENT);

    visuals.widgets.noninteractive.bg_fill = PANEL;
    visuals.widgets.noninteractive.bg_stroke = hairline();

    visuals.widgets.inactive.bg_fill = RAISED;
    visuals.widgets.inactive.weak_bg_fill = RAISED;
    visuals.widgets.inactive.bg_stroke = hairline();

    visuals.widgets.hovered.bg_fill = CARD_HIGH;
    visuals.widgets.hovered.weak_bg_fill = CARD_HIGH;
    visuals.widgets.hovered.bg_stroke = Stroke::new(1.0, BORDER_BRIGHT);

    visuals.widgets.active.bg_fill = ACCENT_PRESS.gamma_multiply(0.7);
    visuals.widgets.active.weak_bg_fill = ACCENT_PRESS.gamma_multiply(0.7);
    visuals.widgets.active.bg_stroke = Stroke::new(1.0, ACCENT);

    visuals.widgets.open.bg_fill = CARD_HIGH;
    visuals.widgets.open.bg_stroke = Stroke::new(1.0, ACCENT);

    visuals.slider_trailing_fill = true;
    context.set_visuals(visuals);

    context.all_styles_mut(|style| {
        style.spacing.item_spacing = Vec2::new(6.0, 5.0);
        style.spacing.button_padding = Vec2::new(8.0, 4.0);
        style.spacing.slider_width = 120.0;
        style.spacing.indent = 10.0;
    });
}
