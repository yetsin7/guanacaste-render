//! Embedded application artwork shared by the window and workstation.

use eframe::egui::IconData;
use std::io::Cursor;

/// Decodes the bundled RGBA icon for native windows and interface textures.
pub fn load() -> Option<IconData> {
    let bytes = include_bytes!("../assets/guanacaste-render-icon.png");
    let mut reader = png::Decoder::new(Cursor::new(bytes)).read_info().ok()?;
    let mut rgba = vec![0; reader.output_buffer_size()?];
    let frame = reader.next_frame(&mut rgba).ok()?;
    if frame.color_type != png::ColorType::Rgba || frame.bit_depth != png::BitDepth::Eight {
        return None;
    }
    rgba.truncate(frame.buffer_size());
    Some(IconData {
        rgba,
        width: frame.width,
        height: frame.height,
    })
}

#[cfg(test)]
mod tests {
    /// The bundled artwork must remain readable by the native window and UI.
    #[test]
    fn embedded_icon_is_valid_rgba() {
        let icon = super::load().expect("The embedded icon must decode");
        assert_eq!((icon.width, icon.height), (256, 256));
        assert_eq!(icon.rgba.len(), 256 * 256 * 4);
    }
}
