//! Times the navigation view (rasterised) against the path-traced view on the same camera, and saves both so the
//! look can be compared.
//!
//! ```text
//! cargo run --release --manifest-path engine/Cargo.toml -p gr-bench --bin raster -- <scene.grscene | project.gr>...
//!     [--out folder] [--width 1600] [--adapter intel]
//! ```

use gr_core::scene_file::Scene;
use gr_gpu::{CameraFrame, GpuOptions, GpuTracer, Mode};
use gr_model::document::Document;
use gr_model::mesh::build_mesh;
use std::path::{Path, PathBuf};
use std::time::Instant;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let option = |name: &str| {
        arguments
            .iter()
            .position(|a| a == name)
            .and_then(|at| arguments.get(at + 1))
            .cloned()
    };
    let width: u32 = option("--width").map_or(Ok(1600), |v| v.parse())?;
    let height = width * 9 / 16;
    let folder = PathBuf::from(option("--out").unwrap_or_else(|| "artifacts/raster".into()));
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

    println!(
        "| Escena | Triángulos | Navegación (raster) | Trazado, 1 muestra | Trazado, 64 muestras |\n|---|---:|---:|---:|---:|"
    );
    for input in inputs {
        let path = Path::new(input);
        let name = path
            .file_stem()
            .map(|stem| stem.to_string_lossy().into_owned())
            .unwrap_or_default();
        let scene = load(path)?;
        let exposure = scene.environment.exposure;
        let frame = CameraFrame::new(&scene.camera, width, height);
        let options = GpuOptions {
            adapter: adapter.as_deref(),
            ..Default::default()
        };
        let mut tracer = GpuTracer::new(&scene, width, height, &options)?;

        // Warm up (pipeline creation), then the best of many frames, each waited for.
        tracer.rasterize(&frame, width, height, exposure);
        tracer.wait()?;
        let mut raster_ms = f64::MAX;
        for _ in 0..30 {
            let start = Instant::now();
            tracer.rasterize(&frame, width, height, exposure);
            tracer.wait()?;
            raster_ms = raster_ms.min(start.elapsed().as_secs_f64() * 1e3);
        }
        let raster = tracer
            .read_raster()?
            .ok_or("the navigation view drew nothing")?;
        save(
            &folder.join(format!("{name}-navegacion.png")),
            &raster,
            width,
            height,
        )?;

        tracer.reset(Mode::PathTrace);
        tracer.render(1);
        tracer.wait()?;
        tracer.reset(Mode::PathTrace);
        let start = Instant::now();
        tracer.render(1);
        tracer.wait()?;
        let one_sample_ms = start.elapsed().as_secs_f64() * 1e3;
        tracer.reset(Mode::PathTrace);
        let start = Instant::now();
        for _ in 0..4 {
            tracer.render(16);
            tracer.wait()?;
        }
        let sixty_four_ms = start.elapsed().as_secs_f64() * 1e3;
        tracer.resolve(exposure);
        save(
            &folder.join(format!("{name}-trazado-64.png")),
            &tracer.read_display()?,
            width,
            height,
        )?;
        println!(
            "| {name} | {} | {raster_ms:.2} ms | {one_sample_ms:.2} ms | {sixty_four_ms:.0} ms |",
            scene.triangle_count()
        );
    }
    println!("\n{width} x {height}; imágenes en {}", folder.display());
    Ok(())
}

fn load(path: &Path) -> Result<Scene, Box<dyn std::error::Error>> {
    if path
        .extension()
        .is_some_and(|extension| extension == "grscene")
    {
        return Ok(Scene::load(path)?);
    }
    let document = Document::load(path)?;
    let bounds = build_mesh(&document, false).0.bounds();
    Ok(gr_model::scene::to_scene(
        build_mesh(&document, true).0,
        bounds,
    ))
}

fn save(
    path: &Path,
    rgba: &[u8],
    width: u32,
    height: u32,
) -> Result<(), Box<dyn std::error::Error>> {
    let mut encoder = png::Encoder::new(
        std::io::BufWriter::new(std::fs::File::create(path)?),
        width,
        height,
    );
    encoder.set_color(png::ColorType::Rgba);
    encoder.set_depth(png::BitDepth::Eight);
    encoder.write_header()?.write_image_data(rgba)?;
    Ok(())
}
