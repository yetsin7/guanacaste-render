//! Measures what the GPU denoiser buys and what it costs.
//!
//! ```text
//! cargo run --release --manifest-path engine/Cargo.toml -p gr-bench --bin denoise -- <scene.grscene | project.gr>...
//!     [--out folder] [--width 1280] [--reference 2048] [--adapter intel]
//! ```
//!
//! For every scene it renders a reference picture with many samples and then, at 1, 4, 16 and 64 samples, compares
//! the raw and the denoised picture with it (PSNR of the displayed 8-bit image, higher is closer) and times the
//! filter. Pictures are saved as `<scene>-<samples>spp-raw.png` and `-denoised.png` so the numbers can be checked
//! by eye: a filter can raise PSNR and still smear what matters.

use gr_core::scene_file::Scene;
use gr_gpu::{GpuOptions, GpuTracer, Mode};
use gr_model::document::Document;
use gr_model::mesh::build_mesh;
use std::path::{Path, PathBuf};
use std::time::Instant;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let option = |name: &str| arguments.iter().position(|a| a == name).and_then(|at| arguments.get(at + 1)).cloned();
    let width: u32 = option("--width").map_or(Ok(1280), |v| v.parse())?;
    let height = width * 9 / 16;
    let reference_samples: u32 = option("--reference").map_or(Ok(2048), |v| v.parse())?;
    let folder = PathBuf::from(option("--out").unwrap_or_else(|| "artifacts/denoise".into()));
    let adapter = option("--adapter");
    std::fs::create_dir_all(&folder)?;

    let mut skip = false;
    let inputs: Vec<&String> = arguments
        .iter()
        .filter(|argument| {
            let is_value = std::mem::replace(&mut skip, argument.starts_with("--"));
            !is_value && !argument.starts_with("--")
        })
        .collect();
    if inputs.is_empty() {
        return Err("name at least one .grscene or .gr file".into());
    }

    println!("| Escena | Muestras | PSNR sin filtrar | PSNR filtrada | Ganancia | Coste del filtro |\n|---|---:|---:|---:|---:|---:|");
    for input in inputs {
        let path = Path::new(input);
        let name = path.file_stem().map(|stem| stem.to_string_lossy().into_owned()).unwrap_or_default();
        let scene = load(path)?;
        let exposure = scene.environment.exposure;
        let options = GpuOptions { adapter: adapter.as_deref(), ..Default::default() };
        let mut tracer = GpuTracer::new(&scene, width, height, &options)?;

        tracer.reset(Mode::PathTrace);
        render(&mut tracer, reference_samples)?;
        let reference = picture(&mut tracer, exposure, false)?.0;
        save(&folder.join(format!("{name}-reference.png")), &reference, width, height)?;

        for samples in [1u32, 4, 16, 64] {
            tracer.reset(Mode::PathTrace);
            render(&mut tracer, samples)?;
            let (raw, raw_ms) = picture(&mut tracer, exposure, false)?;
            let (denoised, denoised_ms) = picture(&mut tracer, exposure, true)?;
            save(&folder.join(format!("{name}-{samples}spp-raw.png")), &raw, width, height)?;
            save(&folder.join(format!("{name}-{samples}spp-denoised.png")), &denoised, width, height)?;
            let (before, after) = (psnr(&raw, &reference), psnr(&denoised, &reference));
            println!(
                "| {name} | {samples} | {before:.1} dB | {after:.1} dB | {:+.1} dB | {:.2} ms |",
                after - before,
                (denoised_ms - raw_ms).max(0.0)
            );
        }
    }
    println!("\nResolución {width} x {height}; referencia de {reference_samples} muestras; imágenes en {}", folder.display());
    Ok(())
}

/// A scene file as it is, or a project meshed by `gr-model` (which carries the smooth normals of plants and terrain).
fn load(path: &Path) -> Result<Scene, Box<dyn std::error::Error>> {
    if path.extension().is_some_and(|extension| extension == "grscene") {
        return Ok(Scene::load(path)?);
    }
    let document = Document::load(path)?;
    let bounds = build_mesh(&document, false).0.bounds();
    Ok(gr_model::scene::to_scene(build_mesh(&document, true).0, bounds))
}

/// Adds samples in batches the driver can retire, then waits for them.
fn render(tracer: &mut GpuTracer, samples: u32) -> Result<(), gr_gpu::GpuError> {
    let mut done = 0;
    while done < samples {
        let batch = (samples - done).min(64);
        tracer.render(batch);
        tracer.wait()?;
        done += batch;
    }
    Ok(())
}

/// The displayed picture and the milliseconds it took to produce on the GPU (best of five).
fn picture(tracer: &mut GpuTracer, exposure: f32, denoise: bool) -> Result<(Vec<u8>, f64), gr_gpu::GpuError> {
    tracer.set_denoise(denoise);
    let mut best = f64::MAX;
    for _ in 0..5 {
        let start = Instant::now();
        tracer.resolve(exposure);
        tracer.wait()?;
        best = best.min(start.elapsed().as_secs_f64() * 1e3);
    }
    Ok((tracer.read_display()?, best))
}

/// Peak signal-to-noise ratio of the colour channels of two RGBA pictures, in decibels.
fn psnr(picture: &[u8], reference: &[u8]) -> f64 {
    let (mut sum, mut count) = (0f64, 0u64);
    for (pixel, expected) in picture.chunks_exact(4).zip(reference.chunks_exact(4)) {
        for channel in 0..3 {
            let difference = pixel[channel] as f64 - expected[channel] as f64;
            sum += difference * difference;
            count += 1;
        }
    }
    if sum == 0.0 { f64::INFINITY } else { 10.0 * (255.0f64 * 255.0 * count as f64 / sum).log10() }
}

fn save(path: &Path, rgba: &[u8], width: u32, height: u32) -> Result<(), Box<dyn std::error::Error>> {
    let mut encoder = png::Encoder::new(std::io::BufWriter::new(std::fs::File::create(path)?), width, height);
    encoder.set_color(png::ColorType::Rgba);
    encoder.set_depth(png::BitDepth::Eight);
    encoder.write_header()?.write_image_data(rgba)?;
    Ok(())
}
