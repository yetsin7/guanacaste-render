//! Pictures of recently opened projects for the Projects page: captured from the navigation view the first time a
//! project is shown, and kept in `%LOCALAPPDATA%\GuanacasteStudio\thumbnails`.

use eframe::egui;
use std::collections::HashMap;
use std::path::{Path, PathBuf};

/// Width of a stored thumbnail; the height follows the view's aspect.
const WIDTH: u32 = 640;

#[derive(Default)]
pub struct Thumbnails {
    loaded: HashMap<PathBuf, Option<egui::TextureHandle>>,
}

fn folder() -> Option<PathBuf> {
    Some(
        PathBuf::from(std::env::var_os("LOCALAPPDATA")?)
            .join("GuanacasteStudio")
            .join("thumbnails"),
    )
}

/// A stable file name for a project path (FNV-1a of its text).
fn file_for(project: &Path) -> Option<PathBuf> {
    let mut hash = 0xcbf2_9ce4_8422_2325u64;
    for byte in project.to_string_lossy().to_lowercase().bytes() {
        hash = (hash ^ byte as u64).wrapping_mul(0x0100_0000_01b3);
    }
    Some(folder()?.join(format!("{hash:016x}.png")))
}

impl Thumbnails {
    /// The thumbnail of a project, loaded from disk the first time it is asked for.
    pub fn get(&mut self, context: &egui::Context, project: &Path) -> Option<egui::TextureHandle> {
        self.loaded
            .entry(project.to_path_buf())
            .or_insert_with(|| {
                let image = read_png(&file_for(project)?)?;
                Some(context.load_texture(
                    format!("thumbnail-{}", project.display()),
                    image,
                    egui::TextureOptions::LINEAR,
                ))
            })
            .clone()
    }

    /// Stores a picture of a project, scaled down, and shows it from now on.
    pub fn store(
        &mut self,
        context: &egui::Context,
        project: &Path,
        rgba: &[u8],
        width: u32,
        height: u32,
    ) {
        let Some(path) = file_for(project) else {
            return;
        };
        let (small, small_width, small_height) = downscale(rgba, width, height, WIDTH);
        if let Some(parent) = path.parent() {
            let _ = std::fs::create_dir_all(parent);
        }
        let _ = crate::viewport::save_png(&path, &small, small_width, small_height);
        let image = egui::ColorImage::from_rgba_unmultiplied(
            [small_width as usize, small_height as usize],
            &small,
        );
        let texture = context.load_texture(
            format!("thumbnail-{}", project.display()),
            image,
            egui::TextureOptions::LINEAR,
        );
        self.loaded.insert(project.to_path_buf(), Some(texture));
    }

    pub fn has(&self, project: &Path) -> bool {
        self.loaded.get(project).is_some_and(Option::is_some)
            || file_for(project).is_some_and(|path| path.is_file())
    }
}

/// Box-filters an RGBA picture down to `width` pixels across.
fn downscale(rgba: &[u8], width: u32, height: u32, target: u32) -> (Vec<u8>, u32, u32) {
    if width <= target {
        return (rgba.to_vec(), width, height);
    }
    let factor = width as f32 / target as f32;
    let target_height = ((height as f32 / factor).round() as u32).max(1);
    let mut out = vec![0u8; (target * target_height * 4) as usize];
    for y in 0..target_height {
        for x in 0..target {
            let (x0, y0) = ((x as f32 * factor) as u32, (y as f32 * factor) as u32);
            let (x1, y1) = (
                (((x + 1) as f32 * factor) as u32).min(width).max(x0 + 1),
                (((y + 1) as f32 * factor) as u32).min(height).max(y0 + 1),
            );
            let mut sum = [0u32; 4];
            for sy in y0..y1 {
                for sx in x0..x1 {
                    let at = ((sy * width + sx) * 4) as usize;
                    for channel in 0..4 {
                        sum[channel] += rgba[at + channel] as u32;
                    }
                }
            }
            let count = (x1 - x0) * (y1 - y0);
            let at = ((y * target + x) * 4) as usize;
            for channel in 0..4 {
                out[at + channel] = (sum[channel] / count) as u8;
            }
        }
    }
    (out, target, target_height)
}

fn read_png(path: &Path) -> Option<egui::ColorImage> {
    let decoder = png::Decoder::new(std::io::BufReader::new(std::fs::File::open(path).ok()?));
    let mut reader = decoder.read_info().ok()?;
    let mut buffer = vec![0; reader.output_buffer_size()?];
    let info = reader.next_frame(&mut buffer).ok()?;
    let bytes = &buffer[..info.buffer_size()];
    let size = [info.width as usize, info.height as usize];
    match info.color_type {
        png::ColorType::Rgba => Some(egui::ColorImage::from_rgba_unmultiplied(size, bytes)),
        png::ColorType::Rgb => Some(egui::ColorImage::from_rgb(size, bytes)),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn a_picture_is_averaged_down_to_the_target_width() {
        let rgba: Vec<u8> = (0..4 * 2)
            .flat_map(|pixel| [pixel as u8 * 10, 0, 0, 255])
            .collect();
        let (small, width, height) = super::downscale(&rgba, 4, 2, 2);
        assert_eq!((width, height), (2, 1));
        // The first output pixel averages input pixels 0, 1, 4 and 5: red 0, 10, 40 and 50.
        assert_eq!(small[0], 25);
    }
}
