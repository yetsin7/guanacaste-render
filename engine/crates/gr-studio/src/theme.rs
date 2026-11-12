//! The look of the studio, taken from the visual prototype's design system ("Obsidian Precision CAD"): dark matte
//! surfaces in luminance steps, hairline borders, a cerulean accent for focus and a teal one for live readouts;
//! Inter for interface text and JetBrains Mono for numbers and technical labels.

use eframe::egui::{self, Color32, CornerRadius, FontFamily, FontId, Frame, Margin, Stroke, Vec2};
use std::sync::Arc;

// ------------------------------------------------------------------------------------------------ surfaces

/// The void behind the 3D view.
pub const CANVAS: Color32 = Color32::from_rgb(0x12, 0x13, 0x16);
/// Docked bars, the rail and status strips.
pub const PANEL: Color32 = Color32::from_rgb(0x1A, 0x1B, 0x1E);
/// Containers inside panels.
pub const CONTAINER: Color32 = Color32::from_rgb(0x1F, 0x1F, 0x23);
/// Cards and inspector groups.
pub const CARD: Color32 = Color32::from_rgb(0x25, 0x26, 0x2A);
/// Raised controls and hovered rows.
pub const RAISED: Color32 = Color32::from_rgb(0x29, 0x2A, 0x2D);
/// The highest surface step: selected tabs and pressed chips.
pub const HIGHEST: Color32 = Color32::from_rgb(0x34, 0x35, 0x38);
/// Recessed slots: numeric fields, previews, the timeline.
pub const RECESSED: Color32 = Color32::from_rgb(0x0D, 0x0E, 0x11);
/// Floating panels over the 3D view.
pub const FLOATING: Color32 = Color32::from_rgb(0x1E, 0x1F, 0x23);

// ------------------------------------------------------------------------------------------------ lines and text

pub const HAIRLINE: Color32 = Color32::from_rgb(0x32, 0x35, 0x3A);
pub const FLOATING_BORDER: Color32 = Color32::from_rgb(0x45, 0x48, 0x4E);
pub const OUTLINE: Color32 = Color32::from_rgb(0x8D, 0x90, 0x9D);
pub const TEXT: Color32 = Color32::from_rgb(0xE3, 0xE2, 0xE6);
pub const TEXT_VARIANT: Color32 = Color32::from_rgb(0xC3, 0xC6, 0xD4);

// ------------------------------------------------------------------------------------------------ accents

/// Focal typography, active rail items and selection frames.
pub const PRIMARY: Color32 = Color32::from_rgb(0xAD, 0xC6, 0xFF);
/// Text on a `PRIMARY` fill.
pub const ON_PRIMARY: Color32 = Color32::from_rgb(0x00, 0x2E, 0x69);
/// Bright cerulean: the active-item strip and filled progress.
pub const PRIMARY_CONTAINER: Color32 = Color32::from_rgb(0x75, 0xA4, 0xFF);
/// Electric blue: pressed toggles, primary buttons, scrub bars.
pub const ACTIVE: Color32 = Color32::from_rgb(0x1F, 0x7A, 0xE0);
/// Live readouts: frame rate, valid states.
pub const TELEMETRY: Color32 = Color32::from_rgb(0x5C, 0xDB, 0xC0);
pub const WARNING: Color32 = Color32::from_rgb(0xF5, 0xA6, 0x23);

// ------------------------------------------------------------------------------------------------ type

const SEMIBOLD: &str = "inter-semibold";
const MONO_MEDIUM: &str = "mono-medium";
const ICONS: &str = "material-icons";

/// Interface text (Inter).
pub fn body(size: f32) -> FontId {
    FontId::new(size, FontFamily::Proportional)
}

/// Headings (Inter SemiBold).
pub fn heading(size: f32) -> FontId {
    FontId::new(size, FontFamily::Name(SEMIBOLD.into()))
}

/// Numbers and technical labels (JetBrains Mono).
pub fn mono(size: f32) -> FontId {
    FontId::new(size, FontFamily::Monospace)
}

/// Emphasised technical labels (JetBrains Mono Medium).
pub fn mono_medium(size: f32) -> FontId {
    FontId::new(size, FontFamily::Name(MONO_MEDIUM.into()))
}

/// Material icons; see [`crate::ui::icons`].
pub fn icon(size: f32) -> FontId {
    FontId::new(size, FontFamily::Name(ICONS.into()))
}

// ------------------------------------------------------------------------------------------------ frames

pub fn hairline() -> Stroke {
    Stroke::new(1.0, HAIRLINE)
}

/// A card: raised surface, hairline border, 4 px corners.
pub fn card() -> Frame {
    Frame::new()
        .fill(CARD)
        .stroke(hairline())
        .corner_radius(CornerRadius::same(4))
        .inner_margin(Margin::same(12))
}

/// A panel floating over the 3D view, with the prototype's ambient shadow.
pub fn floating() -> Frame {
    Frame::new()
        .fill(FLOATING.gamma_multiply(0.96))
        .stroke(Stroke::new(1.0, FLOATING_BORDER))
        .corner_radius(CornerRadius::same(4))
        .inner_margin(Margin::same(14))
        .shadow(egui::Shadow {
            offset: [0, 8],
            blur: 24,
            spread: 0,
            color: Color32::from_black_alpha(165),
        })
}

/// A heads-up readout over the 3D view: 85 % opaque panel colour.
pub fn hud() -> Frame {
    Frame::new()
        .fill(Color32::from_rgba_unmultiplied(0x1A, 0x1B, 0x1E, 217))
        .stroke(hairline())
        .corner_radius(CornerRadius::same(3))
        .inner_margin(Margin::symmetric(10, 6))
}

// ------------------------------------------------------------------------------------------------ setup

/// Installs the fonts, colours and spacing of the studio.
pub fn apply(context: &egui::Context) {
    let mut fonts = egui::FontDefinitions::default();
    let mut add = |name: &str, bytes: &'static [u8], family: FontFamily, first: bool| {
        fonts
            .font_data
            .insert(name.into(), Arc::new(egui::FontData::from_static(bytes)));
        let list = fonts.families.entry(family).or_default();
        if first {
            list.insert(0, name.into());
        } else {
            list.push(name.into());
        }
    };
    add(
        "inter",
        include_bytes!("../assets/fonts/Inter-Regular.otf"),
        FontFamily::Proportional,
        true,
    );
    add(
        "jetbrains-mono",
        include_bytes!("../assets/fonts/JetBrainsMono-Regular.ttf"),
        FontFamily::Monospace,
        true,
    );
    add(
        SEMIBOLD,
        include_bytes!("../assets/fonts/Inter-SemiBold.otf"),
        FontFamily::Name(SEMIBOLD.into()),
        true,
    );
    add(
        MONO_MEDIUM,
        include_bytes!("../assets/fonts/JetBrainsMono-Medium.ttf"),
        FontFamily::Name(MONO_MEDIUM.into()),
        true,
    );
    add(
        ICONS,
        include_bytes!("../assets/fonts/MaterialIcons-Regular.ttf"),
        FontFamily::Name(ICONS.into()),
        true,
    );
    // Named families fall back to the interface fonts for any character they lack (accents, symbols).
    for family in [SEMIBOLD, MONO_MEDIUM] {
        let fallback = if family == SEMIBOLD {
            "inter"
        } else {
            "jetbrains-mono"
        };
        fonts
            .families
            .entry(FontFamily::Name(family.into()))
            .or_default()
            .push(fallback.into());
    }
    context.set_fonts(fonts);
    context.set_theme(egui::Theme::Dark);

    let mut visuals = egui::Visuals::dark();
    visuals.panel_fill = PANEL;
    visuals.window_fill = FLOATING;
    visuals.extreme_bg_color = RECESSED;
    visuals.faint_bg_color = CONTAINER;
    visuals.override_text_color = Some(TEXT);
    visuals.selection.bg_fill = ACTIVE.gamma_multiply(0.45);
    visuals.selection.stroke = Stroke::new(1.0, PRIMARY);
    visuals.widgets.noninteractive.bg_fill = PANEL;
    visuals.widgets.noninteractive.bg_stroke = hairline();
    visuals.widgets.noninteractive.fg_stroke = Stroke::new(1.0, TEXT_VARIANT);
    visuals.widgets.inactive.bg_fill = CARD;
    visuals.widgets.inactive.weak_bg_fill = CARD;
    visuals.widgets.inactive.bg_stroke = hairline();
    visuals.widgets.inactive.fg_stroke = Stroke::new(1.0, TEXT);
    visuals.widgets.hovered.bg_fill = RAISED;
    visuals.widgets.hovered.weak_bg_fill = RAISED;
    visuals.widgets.hovered.bg_stroke = Stroke::new(1.0, FLOATING_BORDER);
    visuals.widgets.hovered.fg_stroke = Stroke::new(1.0, TEXT);
    visuals.widgets.active.bg_fill = ACTIVE;
    visuals.widgets.active.weak_bg_fill = ACTIVE;
    visuals.widgets.active.bg_stroke = Stroke::new(1.0, PRIMARY);
    visuals.widgets.active.fg_stroke = Stroke::new(1.0, Color32::WHITE);
    for state in [
        &mut visuals.widgets.noninteractive,
        &mut visuals.widgets.inactive,
        &mut visuals.widgets.hovered,
        &mut visuals.widgets.active,
        &mut visuals.widgets.open,
    ] {
        state.corner_radius = CornerRadius::same(2);
    }
    visuals.slider_trailing_fill = true;
    visuals.window_corner_radius = CornerRadius::same(4);
    visuals.window_stroke = Stroke::new(1.0, FLOATING_BORDER);
    context.set_visuals(visuals);

    context.all_styles_mut(|style| {
        style.spacing.item_spacing = Vec2::new(8.0, 6.0);
        style.spacing.button_padding = Vec2::new(10.0, 5.0);
        style.spacing.interact_size.y = 24.0;
        style.spacing.slider_width = 160.0;
        style.spacing.scroll.bar_width = 6.0;
        style.text_styles = [
            (egui::TextStyle::Body, body(13.0)),
            (egui::TextStyle::Button, body(13.0)),
            (egui::TextStyle::Small, body(11.0)),
            (egui::TextStyle::Heading, heading(20.0)),
            (egui::TextStyle::Monospace, mono(12.0)),
        ]
        .into();
    });
}
