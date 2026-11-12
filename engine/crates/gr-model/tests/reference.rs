//! The mesher against the C# application, triangle by triangle.
//!
//! `fixtures/*.refmesh` is the mesh `BuildingMesher.Build` produced for `fixtures/*.gr`; both were written by
//! `dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --mesh-reference`. Regenerate them when
//! the application's mesher changes.

use glam::Vec3;
use gr_model::document::{Document, Id};
use gr_model::mesh::{SceneMesh, SurfaceKind, build_mesh};
use std::collections::HashMap;

/// Largest distance, in metres, between a corner here and the same corner in the application, for geometry built
/// from plan arithmetic (walls, joinery, slabs, columns): in practice it is exactly zero.
const TOLERANCE: f32 = 2e-5;
/// The same for plants. They are grown with sines and cosines, whose last bit differs between the two runtimes and
/// accumulates along a branch; a tenth of a millimetre is far below anything a picture can show.
const PLANT_TOLERANCE: f32 = 1e-4;

struct Reference {
    /// Element id, first triangle, end triangle.
    ranges: Vec<(Id, usize, usize)>,
    positions: Vec<Vec3>,
    kinds: Vec<u8>,
}

impl Reference {
    fn parse(bytes: &[u8]) -> Reference {
        assert_eq!(&bytes[..8], b"GRMESH01");
        let i32_at = |at: usize| i32::from_le_bytes(bytes[at..at + 4].try_into().expect("four bytes")) as usize;
        let f32_at = |at: usize| f32::from_le_bytes(bytes[at..at + 4].try_into().expect("four bytes"));
        let (triangles, range_count) = (i32_at(8), i32_at(12));
        let mut at = 16;
        let mut ranges = Vec::new();
        for _ in 0..range_count {
            let id = Id::parse(std::str::from_utf8(&bytes[at..at + 36]).expect("ASCII id")).expect("a GUID");
            ranges.push((id, i32_at(at + 36), i32_at(at + 40)));
            at += 44;
        }
        let positions = (0..triangles * 3)
            .map(|corner| Vec3::new(f32_at(at + corner * 12), f32_at(at + corner * 12 + 4), f32_at(at + corner * 12 + 8)))
            .collect();
        let kinds = bytes[at + triangles * 36..].to_vec();
        assert_eq!(kinds.len(), triangles);
        Reference { ranges, positions, kinds }
    }

    fn triangle(&self, index: usize) -> [Vec3; 3] {
        [self.positions[index * 3], self.positions[index * 3 + 1], self.positions[index * 3 + 2]]
    }
}

/// Compares the triangles `ours[first..]` with `reference[reference_first..]`, `count` of them, in order.
fn assert_same_triangles(mesh: &SceneMesh, first: usize, reference: &Reference, reference_first: usize, count: usize, what: &str) {
    assert_triangles_within(TOLERANCE, mesh, first, reference, reference_first, count, what);
}

fn assert_triangles_within(tolerance: f32, mesh: &SceneMesh, first: usize, reference: &Reference, reference_first: usize, count: usize, what: &str) {
    for offset in 0..count {
        let (ours, theirs) = (mesh.triangle(first + offset), reference.triangle(reference_first + offset));
        for corner in 0..3 {
            let distance = (ours[corner] - theirs[corner]).length();
            assert!(
                distance <= tolerance,
                "{what}: triangle {offset} corner {corner} is {:?} here and {:?} in the application ({distance} m apart)",
                ours[corner],
                theirs[corner]
            );
        }
        assert_eq!(
            mesh.kinds[first + offset] as u8,
            reference.kinds[reference_first + offset],
            "{what}: triangle {offset} has another surface kind"
        );
    }
}

#[test]
fn the_demonstration_house_is_meshed_exactly_as_the_application_does() {
    let document = Document::parse(include_bytes!("fixtures/demo-house.gr")).expect("valid project");
    let reference = Reference::parse(include_bytes!("fixtures/demo-house.refmesh"));
    let (mesh, coverage) = build_mesh(&document, true);

    // Walls, openings and a floor slab only: everything in this project is covered.
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_eq!(mesh.element_ranges.len(), reference.ranges.len());
    for (ours, theirs) in mesh.element_ranges.iter().zip(&reference.ranges) {
        assert_eq!((ours.element, ours.first, ours.end), *theirs);
    }
    assert_same_triangles(&mesh, 0, &reference, 0, mesh.triangle_count(), "demonstration house");
    assert_eq!(*mesh.kinds.last().expect("the ground"), SurfaceKind::Ground);
}

#[test]
fn every_element_the_mesher_covers_matches_the_application_in_a_richer_project() {
    // Doors and windows of many operations and leaf styles, wall constructions, columns of several shapes, and
    // fifteen structural elements that are not meshed yet.
    let document = Document::parse(include_bytes!("fixtures/showcase.gr")).expect("valid project");
    let reference = Reference::parse(include_bytes!("fixtures/showcase.refmesh"));
    let (mesh, coverage) = build_mesh(&document, false);
    assert!(!coverage.is_complete() && coverage.structural_elements > 0);

    let theirs: HashMap<Id, (usize, usize)> = reference.ranges.iter().map(|&(id, first, end)| (id, (first, end))).collect();
    assert!(!mesh.element_ranges.is_empty());
    for range in &mesh.element_ranges {
        let &(first, end) = theirs.get(&range.element).unwrap_or_else(|| panic!("element {} is not in the application's mesh", range.element));
        assert_eq!(range.end - range.first, end - first, "element {} has another number of triangles", range.element);
        assert_same_triangles(&mesh, range.first, &reference, first, end - first, &format!("element {}", range.element));
    }
    // Everything of the kinds that are covered is there: walls, openings with a type, flat slabs and columns.
    let covered = document.walls.len() + document.floors.len() + document.columns.len();
    assert!(mesh.element_ranges.len() >= covered);
}

#[test]
fn plants_grow_as_they_do_in_the_application() {
    // A shrub, a palm and a conifer. The number of triangles alone shows the random sequence is the same: every
    // branch decides whether to fork by comparing a random number with a threshold.
    let document = Document::parse(include_bytes!("fixtures/garden.gr")).expect("valid project");
    let reference = Reference::parse(include_bytes!("fixtures/garden.refmesh"));
    let (mesh, coverage) = build_mesh(&document, true);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_eq!(mesh.element_ranges.len(), 3);
    for (ours, theirs) in mesh.element_ranges.iter().zip(&reference.ranges) {
        assert_eq!((ours.element, ours.first, ours.end), *theirs);
    }
    assert_triangles_within(PLANT_TOLERANCE, &mesh, 0, &reference, 0, mesh.triangle_count(), "garden");
}
