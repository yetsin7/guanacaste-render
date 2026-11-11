//! Renders the exported scenes on the GPU, verifies the traversal against the CPU hierarchy and measures throughput.
//!
//! ```text
//! cargo run --release --manifest-path engine/Cargo.toml -p gr-bench --bin gpu -- artifacts/engine-bench [--adapter intel]
//!     [--width 1280] [--samples 256] [--scene cobogo]
//! ```
//!
//! For every scene it writes `<scene>-gpu.png` and appends a row to `baseline-gpu-<adapter>.json`. A scene is only
//! timed after the first-hit distance of every pixel matches `gr_core::bvh::Bvh` on the same camera rays.

use gr_core::bvh::Bvh;
use gr_core::scene_file::Scene;
use gr_gpu::{CameraFrame, GpuOptions, GpuTracer, Mode};
use serde_json::{Value, json};
use std::path::Path;
use std::time::Instant;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let option = |name: &str| arguments.iter().position(|a| a == name).and_then(|at| arguments.get(at + 1)).cloned();
    let folder = arguments.first().filter(|a| !a.starts_with("--")).cloned().unwrap_or_else(|| "artifacts/engine-bench".into());
    let folder = Path::new(&folder);
    let width: u32 = option("--width").map_or(Ok(1280), |v| v.parse())?;
    let height = width * 9 / 16;
    let samples: u32 = option("--samples").map_or(Ok(256), |v| v.parse())?;
    let adapter = option("--adapter");
    let only = option("--scene");

    let baseline: Value = serde_json::from_str(&std::fs::read_to_string(folder.join("baseline-csharp.json"))?)?;
    let mut rows = Vec::new();
    let mut adapter_name = String::new();
    for reference in baseline["scenes"].as_array().ok_or("baseline has no scenes")? {
        let name = reference["scene"].as_str().ok_or("scene without a name")?;
        if only.as_deref().is_some_and(|wanted| wanted != name) {
            continue;
        }
        let scene = Scene::load(folder.join(format!("{name}.grscene")))?;
        let start = Instant::now();
        let options = GpuOptions { adapter: adapter.as_deref(), ..Default::default() };
        let mut tracer = match GpuTracer::new(&scene, width, height, &options) {
            Ok(tracer) => tracer,
            Err(error) => {
                println!("{name:<16} skipped: {error}");
                rows.push(json!({ "scene": name, "skipped": error.to_string() }));
                continue;
            }
        };
        let setup_ms = start.elapsed().as_secs_f64() * 1e3;
        adapter_name = format!("{} ({})", tracer.adapter_name(), tracer.backend());

        // Verification: the shader must find the same first surface as the CPU hierarchy through every pixel.
        tracer.reset(Mode::PrimaryDistance);
        tracer.render(1);
        let distances = tracer.read_accumulation()?;
        let cpu = Bvh::build(&scene.positions, &scene.indices);
        let frame = CameraFrame::new(&scene.camera, width, height);
        let mut mismatches = 0u32;
        for y in 0..height {
            for x in 0..width {
                let expected = cpu
                    .intersect(frame.position, frame.pixel_direction(x, y, width, height), 1e-4, f32::MAX)
                    .map_or(-1.0, |hit| hit.distance);
                let actual = distances[(y * width + x) as usize][0];
                if (expected < 0.0) != (actual < 0.0) || (expected - actual).abs() > 2e-3 * expected.abs().max(1.0) {
                    mismatches += 1;
                }
            }
        }
        let pixels = width * height;
        // Pixels whose ray grazes a silhouette edge may fall on either side of a rounding difference.
        let verified = mismatches as f64 <= pixels as f64 * 1e-3;

        // Throughput: a warm-up batch, then timed batches short enough for the 32-bit ray counter.
        tracer.reset(Mode::PathTrace);
        tracer.render(2);
        tracer.wait()?;
        let batch = samples.clamp(1, 32);
        let (mut rays_total, mut seconds, mut done) = (0u64, 0f64, 0u32);
        tracer.reset(Mode::PathTrace);
        let mut image = Vec::new();
        let mut accumulated_rays = 0u64;
        while done < samples {
            let passes = batch.min(samples - done);
            let before = tracer.rays()? as u64;
            let timer = Instant::now();
            tracer.render(passes);
            tracer.wait()?;
            seconds += timer.elapsed().as_secs_f64();
            let after = tracer.rays()? as u64;
            // Unsigned wrap-around of the counter inside one batch is recovered by the modular difference.
            rays_total += after.wrapping_sub(before) & 0xffff_ffff;
            accumulated_rays = rays_total;
            done += passes;
            if done >= samples {
                image = tracer.read_accumulation()?;
            }
        }
        let mega_rays = accumulated_rays as f64 / seconds / 1e6;
        let ms_per_sample = seconds * 1e3 / samples as f64;
        let rays_per_pixel = accumulated_rays as f64 / (pixels as f64 * samples as f64);
        save_png(&folder.join(format!("{name}-gpu.png")), &image, width, height, scene.environment.exposure)?;

        let cpu_rate = reference["pathTracerMraysPerSecond"].as_f64().unwrap_or(0.0);
        println!(
            "{name:<16} {:>8} tris · setup {setup_ms:>6.0} ms · {ms_per_sample:>7.2} ms/sample at {width}x{height} · {mega_rays:>7.1} Mray/s ({rays_per_pixel:.2} rays/pixel) · C# CPU {cpu_rate:.1} Mray/s · VRAM scene {:.1} MB + image {:.1} MB · first hit {}",
            scene.triangle_count(),
            tracer.scene_bytes() as f64 / 1048576.0,
            tracer.image_bytes() as f64 / 1048576.0,
            if verified { format!("OK ({mismatches} of {pixels} px differ)") } else { format!("MISMATCH ({mismatches} px)") }
        );
        rows.push(json!({
            "scene": name, "triangles": scene.triangle_count(), "width": width, "height": height, "samples": samples,
            "setupMs": setup_ms, "msPerSample": ms_per_sample, "mraysPerSecond": mega_rays,
            "raysPerPixel": rays_per_pixel, "sceneVramMegabytes": tracer.scene_bytes() as f64 / 1048576.0,
            "imageVramMegabytes": tracer.image_bytes() as f64 / 1048576.0,
            "firstHitMismatchedPixels": mismatches, "firstHitVerified": verified,
            "csharpCpuPathTracerMraysPerSecond": cpu_rate,
        }));
    }
    let tag: String = adapter_name.to_lowercase().chars().filter(|c| c.is_ascii_alphanumeric()).take(24).collect();
    let report = json!({ "engine": "rust-gpu", "adapter": adapter_name, "scenes": rows });
    std::fs::write(folder.join(format!("baseline-gpu-{tag}.json")), serde_json::to_string_pretty(&report)?)?;
    Ok(())
}

/// Averages the samples, applies exposure and an ACES-like curve and writes an 8-bit sRGB picture.
fn save_png(path: &Path, image: &[[f32; 4]], width: u32, height: u32, exposure: f32) -> Result<(), Box<dyn std::error::Error>> {
    let tone = |value: f32| {
        let x = (value * exposure).max(0.0);
        let mapped = (x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14);
        let srgb = if mapped <= 0.0031308 { mapped * 12.92 } else { 1.055 * mapped.powf(1.0 / 2.4) - 0.055 };
        (srgb.clamp(0.0, 1.0) * 255.0 + 0.5) as u8
    };
    let mut bytes = Vec::with_capacity(image.len() * 3);
    for pixel in image {
        let scale = 1.0 / pixel[3].max(1.0);
        bytes.extend([tone(pixel[0] * scale), tone(pixel[1] * scale), tone(pixel[2] * scale)]);
    }
    let mut encoder = png::Encoder::new(std::io::BufWriter::new(std::fs::File::create(path)?), width, height);
    encoder.set_color(png::ColorType::Rgb);
    encoder.set_depth(png::BitDepth::Eight);
    encoder.write_header()?.write_image_data(&bytes)?;
    Ok(())
}
