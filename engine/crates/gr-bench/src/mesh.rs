//! Loads and meshes the reference projects with `gr-model`, compares every covered element with the mesh of the
//! C# application and reports times and coverage.
//!
//! ```text
//! dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --mesh-reference --out artifacts/mesh-reference
//! cargo run --release --manifest-path engine/Cargo.toml -p gr-bench --bin mesh -- artifacts/mesh-reference
//! ```

use gr_model::document::{Document, Id};
use gr_model::mesh::build_mesh;
use std::collections::HashMap;
use std::path::Path;
use std::time::Instant;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let folder = std::env::args().nth(1).unwrap_or_else(|| "artifacts/mesh-reference".into());
    let mut projects: Vec<_> = std::fs::read_dir(&folder)?
        .flatten()
        .map(|entry| entry.path())
        .filter(|path| path.extension().is_some_and(|extension| extension == "gr"))
        .collect();
    projects.sort();
    println!("| Proyecto | Elementos cubiertos | Triángulos Rust / aplicación | Sin cubrir | Leer + mallar | Diferencia máxima |\n|---|---:|---:|---:|---:|---:|");
    for project in projects {
        let name = project.file_stem().map(|stem| stem.to_string_lossy().into_owned()).unwrap_or_default();
        let start = Instant::now();
        let document = Document::load(&project)?;
        let (mesh, coverage) = build_mesh(&document, false);
        let milliseconds = start.elapsed().as_secs_f64() * 1e3;

        let reference = Reference::read(&project.with_extension("refmesh"))?;
        let theirs: HashMap<Id, (usize, usize)> = reference.ranges.iter().map(|&(id, first, end)| (id, (first, end))).collect();
        let (mut matched, mut worst) = (0usize, 0f32);
        let mut problems = Vec::new();
        for range in &mesh.element_ranges {
            let Some(&(first, end)) = theirs.get(&range.element) else {
                problems.push(format!("{} no está en la malla de la aplicación", range.element));
                continue;
            };
            if end - first != range.end - range.first {
                problems.push(format!("{}: {} triángulos aquí, {} en la aplicación", range.element, range.end - range.first, end - first));
                continue;
            }
            let mut element_worst = 0f32;
            let mut same_kinds = true;
            for offset in 0..end - first {
                for corner in 0..3 {
                    let ours = mesh.positions[(range.first + offset) * 3 + corner];
                    let other = reference.positions[(first + offset) * 3 + corner];
                    element_worst = element_worst.max((ours[0] - other[0]).abs().max((ours[1] - other[1]).abs()).max((ours[2] - other[2]).abs()));
                }
                same_kinds &= mesh.kinds[range.first + offset] as u8 == reference.kinds[first + offset];
            }
            worst = worst.max(element_worst);
            if element_worst > 2e-5 || !same_kinds {
                problems.push(format!("{}: difiere {element_worst:e} m, superficies iguales: {same_kinds}", range.element));
            } else {
                matched += 1;
            }
        }
        println!(
            "| {name} | {matched} de {} | {} / {} | {} | {milliseconds:.1} ms | {:.1e} m |",
            reference.ranges.len(),
            mesh.triangle_count(),
            reference.kinds.len(),
            coverage.missing(),
            worst
        );
        for problem in problems.iter().take(5) {
            println!("    DIFERENCIA {problem}");
        }
    }
    Ok(())
}

struct Reference {
    ranges: Vec<(Id, usize, usize)>,
    positions: Vec<[f32; 3]>,
    kinds: Vec<u8>,
}

impl Reference {
    /// The `.refmesh` layout is described in `tests/GuanacasteRender.RenderBench/mesh-reference.cs`.
    fn read(path: &Path) -> Result<Reference, Box<dyn std::error::Error>> {
        let bytes = std::fs::read(path)?;
        if bytes.len() < 16 || &bytes[..8] != b"GRMESH01" {
            return Err("not a reference mesh".into());
        }
        let i32_at = |at: usize| i32::from_le_bytes([bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]]) as usize;
        let f32_at = |at: usize| f32::from_le_bytes([bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]]);
        let (triangles, range_count) = (i32_at(8), i32_at(12));
        let mut at = 16;
        let mut ranges = Vec::with_capacity(range_count);
        for _ in 0..range_count {
            let id = Id::parse(std::str::from_utf8(&bytes[at..at + 36])?).ok_or("an element id is not a GUID")?;
            ranges.push((id, i32_at(at + 36), i32_at(at + 40)));
            at += 44;
        }
        let positions = (0..triangles * 3).map(|c| [f32_at(at + c * 12), f32_at(at + c * 12 + 4), f32_at(at + c * 12 + 8)]).collect();
        Ok(Reference { ranges, positions, kinds: bytes[at + triangles * 36..].to_vec() })
    }
}
