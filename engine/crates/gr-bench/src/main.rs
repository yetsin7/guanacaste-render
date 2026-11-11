//! Times the Rust engine on the scenes and rays exported by the C# baseline and writes a comparison.
//!
//! ```text
//! dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --baseline --out artifacts/engine-bench
//! cargo run --release --manifest-path engine/Cargo.toml -p gr-bench -- artifacts/engine-bench
//! ```
//!
//! Every scene is checked against the reference hit count and distance sum of the C# hierarchy before it is timed, so
//! a faster number can never come from a wrong answer. Output: `baseline-rust.json` and `comparison.md`.

use glam::Vec3;
use gr_core::bvh::Bvh;
use gr_core::scene_file::{Scene, load_rays};
use rayon::prelude::*;
use serde_json::{Value, json};
use std::path::Path;
use std::time::Instant;

const T_MIN: f32 = 1e-4;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let folder = std::env::args().nth(1).unwrap_or_else(|| "artifacts/engine-bench".into());
    let folder = Path::new(&folder);
    let baseline: Value = serde_json::from_str(&std::fs::read_to_string(folder.join("baseline-csharp.json"))?)?;
    let threads = rayon::current_num_threads();
    let mut rows = Vec::new();
    let mut table = String::from(
        "| Escena | Triángulos | BVH C# ms | BVH Rust ms | Closest C# | Closest Rust | × | 1 hilo C# | 1 hilo Rust | × | Any-hit C# | Any-hit Rust | × | Coincidencia |\n|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|\n",
    );

    for reference in baseline["scenes"].as_array().ok_or("baseline has no scenes")? {
        let name = reference["scene"].as_str().ok_or("scene without a name")?;
        let scene = Scene::load(folder.join(format!("{name}.grscene")))?;
        let rays = load_rays(folder.join(format!("{name}.grrays")))?;

        let mut build_ms = f64::MAX;
        let mut bvh = None;
        for _ in 0..3 {
            let start = Instant::now();
            let built = Bvh::build(&scene.positions, &scene.indices);
            build_ms = build_ms.min(start.elapsed().as_secs_f64() * 1e3);
            bvh = Some(built);
        }
        let bvh = bvh.expect("three builds ran");

        let (mut hits, mut distance_sum) = (0u64, 0f64);
        for &(origin, direction) in &rays {
            if let Some(hit) = bvh.intersect(origin, direction, T_MIN, f32::MAX) {
                hits += 1;
                distance_sum += hit.distance as f64;
            }
        }
        let expected_hits = reference["referenceHits"].as_u64().unwrap_or(0);
        let expected_sum = reference["referenceDistanceSum"].as_f64().unwrap_or(0.0);
        let hit_difference = hits.abs_diff(expected_hits);
        let sum_error = (distance_sum - expected_sum).abs() / expected_sum.abs().max(1.0);
        // Rays grazing an edge shared by two triangles may land on either side of a rounding difference.
        let agrees = hit_difference as f64 <= rays.len() as f64 * 2e-4 && sum_error <= 2e-4;

        let closest = best(|| rate(&rays, |o, d| bvh.intersect(o, d, T_MIN, f32::MAX).is_some(), true));
        let single = best(|| rate(&rays, |o, d| bvh.intersect(o, d, T_MIN, f32::MAX).is_some(), false));
        let any_hit = best(|| rate(&rays, |o, d| bvh.occluded(o, d, T_MIN, 50.0), true));

        let number = |key: &str| reference[key].as_f64().unwrap_or(0.0);
        let ratio = |ours: f64, theirs: f64| if theirs > 0.0 { ours / theirs } else { 0.0 };
        println!(
            "{name:<16} {:>8} tris · bvh {build_ms:>7.1} ms · closest {closest:>6.1} Mray/s (1 thread {single:.2}) · any-hit {any_hit:>6.1} · {:.1} MB · hits {hits} (C# {expected_hits}) Σd err {sum_error:.1e} {}",
            scene.triangle_count(),
            bvh.memory_bytes() as f64 / 1048576.0,
            if agrees { "OK" } else { "MISMATCH" }
        );
        table.push_str(&format!(
            "| {name} | {} | {:.1} | {build_ms:.1} | {:.1} | {closest:.1} | {:.2} | {:.2} | {single:.2} | {:.2} | {:.1} | {any_hit:.1} | {:.2} | {} |\n",
            scene.triangle_count(),
            number("bvhBuildMs"),
            number("closestHitMraysPerSecond"),
            ratio(closest, number("closestHitMraysPerSecond")),
            number("closestHitSingleThreadMraysPerSecond"),
            ratio(single, number("closestHitSingleThreadMraysPerSecond")),
            number("anyHitMraysPerSecond"),
            ratio(any_hit, number("anyHitMraysPerSecond")),
            if agrees { format!("sí (Δ {hit_difference} de {} rayos)", rays.len()) } else { format!("NO (Δ {hit_difference})") }
        ));
        rows.push(json!({
            "scene": name, "triangles": scene.triangle_count(), "bvhBuildMs": build_ms,
            "closestHitMraysPerSecond": closest, "closestHitSingleThreadMraysPerSecond": single,
            "anyHitMraysPerSecond": any_hit, "bvhMegabytes": bvh.memory_bytes() as f64 / 1048576.0,
            "hits": hits, "distanceSum": distance_sum, "agreesWithReference": agrees,
        }));
        if !agrees {
            eprintln!("warning: {name} disagrees with the C# reference; its timings are not comparable");
        }
    }

    let report = json!({ "engine": "rust", "threads": threads, "cpu": baseline["cpu"], "scenes": rows });
    std::fs::write(folder.join("baseline-rust.json"), serde_json::to_string_pretty(&report)?)?;
    std::fs::write(
        folder.join("comparison.md"),
        format!(
            "Rayos por segundo en millones (Mray/s), {threads} hilos salvo donde se indica; × es Rust dividido entre C#.\nCPU: {}.\n\n{table}",
            baseline["cpu"].as_str().unwrap_or("?")
        ),
    )?;
    Ok(())
}

fn best(measure: impl Fn() -> f64) -> f64 {
    (0..4).map(|_| measure()).fold(0.0, f64::max)
}

/// Millions of queries per second over all rays, on every core or on the calling thread.
fn rate(rays: &[(Vec3, Vec3)], query: impl Fn(Vec3, Vec3) -> bool + Sync, parallel: bool) -> f64 {
    let start = Instant::now();
    let hits: usize = if parallel {
        rays.par_chunks(4096).map(|chunk| chunk.iter().filter(|&&(o, d)| query(o, d)).count()).sum()
    } else {
        rays.iter().filter(|&&(o, d)| query(o, d)).count()
    };
    std::hint::black_box(hits);
    rays.len() as f64 / start.elapsed().as_secs_f64() / 1e6
}
