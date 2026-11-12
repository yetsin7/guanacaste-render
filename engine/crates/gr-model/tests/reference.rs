//! The mesher against the C# application, triangle by triangle.
//!
//! `fixtures/*.refmesh` is the mesh `BuildingMesher.Build` produced for `fixtures/*.gr`; both were written by
//! `dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --mesh-reference`. Regenerate them when
//! the application's mesher changes.

use glam::Vec3;
use gr_model::document::{Document, Id, electrical_device_kind};
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
    /// Reads the binary mesh exported by the original application.
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

    /// Retrieves a reference triangle in the order written by the exporter.
    fn triangle(&self, index: usize) -> [Vec3; 3] {
        [
            self.positions[index * 3],
            self.positions[index * 3 + 1],
            self.positions[index * 3 + 2],
        ]
    }
}

/// Compares the triangles `ours[first..]` with `reference[reference_first..]`, `count` of them, in order.
fn assert_same_triangles(mesh: &SceneMesh, first: usize, reference: &Reference, reference_first: usize, count: usize, what: &str) {
    assert_triangles_within(TOLERANCE, mesh, first, reference, reference_first, count, what);
}

/// Checks corresponding corner positions and surface kinds within the requested tolerance.
fn assert_triangles_within(
    tolerance: f32,
    mesh: &SceneMesh,
    first: usize,
    reference: &Reference,
    reference_first: usize,
    count: usize,
    what: &str,
) {
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
/// Guards the complete basic house and its ground against geometric regressions.
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
/// Checks architectural elements against their individually attributed reference ranges.
fn every_element_the_mesher_covers_matches_the_application_in_a_richer_project() {
    // Doors and windows of many operations and leaf styles, wall constructions, columns of several shapes, a beam,
    // a ramp, an open stair, railings, a curtain wall, gutters, downspouts and a skylight.
    let document = Document::parse(include_bytes!("fixtures/showcase.gr")).expect("valid project");
    let reference = Reference::parse(include_bytes!("fixtures/showcase.refmesh"));
    let (mesh, coverage) = build_mesh(&document, false);
    assert!(coverage.is_complete(), "{coverage:?}");

    let theirs: HashMap<Id, (usize, usize)> = reference.ranges.iter().map(|&(id, first, end)| (id, (first, end))).collect();
    assert!(!mesh.element_ranges.is_empty());
    for range in &mesh.element_ranges {
        let &(first, end) = theirs
            .get(&range.element)
            .unwrap_or_else(|| panic!("element {} is not in the application's mesh", range.element));
        assert_eq!(
            range.end - range.first,
            end - first,
            "element {} has another number of triangles",
            range.element
        );
        assert_same_triangles(
            &mesh,
            range.first,
            &reference,
            first,
            end - first,
            &format!("element {}", range.element),
        );
    }
    // Everything of the kinds that are covered is there: walls, openings with a type, flat slabs and columns.
    let covered = document.walls.len() + document.floors.len() + document.columns.len();
    assert!(mesh.element_ranges.len() >= covered);
}

/// Compares shed, gable, and hip roofs with rectangular and concave footprints from the original mesher.
#[test]
fn pitched_roofs_match_the_application() {
    let document = Document::parse(include_bytes!("fixtures/pitched-roofs.gr")).expect("valid roof fixture");
    let reference = Reference::parse(include_bytes!("fixtures/pitched-roofs.refmesh"));
    let (mesh, coverage) = build_mesh(&document, false);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.element_ranges.len(), 12);
    assert_eq!(mesh.element_ranges.len(), reference.ranges.len());
    for (ours, &(id, first, end)) in mesh.element_ranges.iter().zip(&reference.ranges) {
        assert_eq!(ours.element, id);
        assert_eq!(ours.end - ours.first, end - first, "roof {id} has a different triangle count");
        assert_same_triangles(&mesh, ours.first, &reference, first, end - first, &format!("roof {id}"));
    }
}

/// Checks surveyed terrain, grounded vegetation, the ground plane, and smooth normals against C# output.
#[test]
fn terrain_and_grounded_plants_match_the_application() {
    let document = Document::parse(include_bytes!("fixtures/terrain.gr")).expect("valid terrain fixture");
    let reference = Reference::parse(include_bytes!("fixtures/terrain.refmesh"));
    let normal_bytes = include_bytes!("fixtures/terrain.normals");
    let (mesh, coverage) = build_mesh(&document, true);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_eq!(mesh.element_ranges.len(), reference.ranges.len());
    for (ours, &(id, first, end)) in mesh.element_ranges.iter().zip(&reference.ranges) {
        assert_eq!((ours.element, ours.first, ours.end), (id, first, end));
    }
    assert_triangles_within(
        PLANT_TOLERANCE,
        &mesh,
        0,
        &reference,
        0,
        mesh.triangle_count(),
        "terrain and plants",
    );
    let normals = mesh.normals.as_ref().expect("terrain has smooth normals");
    assert_eq!(normal_bytes.len(), normals.len() * 12);
    for (index, normal) in normals.iter().enumerate() {
        let offset = index * 12;
        let component = |at| f32::from_le_bytes(normal_bytes[offset + at..offset + at + 4].try_into().expect("normal component"));
        let expected = Vec3::new(component(0), component(4), component(8));
        assert!(
            (*normal - expected).length() < 1e-4,
            "normal {index} differs: {normal:?} vs {expected:?}"
        );
    }
}

/// Checks meter boxes and outlet caps, including the ground height above buried service geometry.
#[test]
fn plumbing_service_points_match_the_application() {
    let document = Document::parse(include_bytes!("fixtures/plumbing-points.gr")).expect("valid plumbing fixture");
    let reference = Reference::parse(include_bytes!("fixtures/plumbing-points.refmesh"));
    let (mesh, coverage) = build_mesh(&document, true);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_same_triangles(&mesh, 0, &reference, 0, mesh.triangle_count(), "plumbing service points");
}

/// Compares routed cold, hot, and sanitary systems, their fixture traps, and vent stacks with C#.
#[test]
fn connected_plumbing_systems_match_the_application() {
    let document = Document::parse(include_bytes!("fixtures/plumbing-systems.gr")).expect("valid plumbing fixture");
    let reference = Reference::parse(include_bytes!("fixtures/plumbing-systems.refmesh"));
    let (mesh, coverage) = build_mesh(&document, true);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_same_triangles(&mesh, 0, &reference, 0, mesh.triangle_count(), "connected plumbing systems");
}

/// Compares roof runoff, storm branch sizing, and downspout connections with the original application.
#[test]
fn storm_drainage_matches_the_application() {
    let document = Document::parse(include_bytes!("fixtures/storm.gr")).expect("valid storm fixture");
    let reference = Reference::parse(include_bytes!("fixtures/storm.refmesh"));
    let (mesh, coverage) = build_mesh(&document, true);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_same_triangles(&mesh, 0, &reference, 0, mesh.triangle_count(), "storm drainage");
}

/// Compares explicit and household-sized tanks, including their connected supply systems.
#[test]
fn water_storage_matches_the_application() {
    let document = Document::parse(include_bytes!("fixtures/water-storage.gr")).expect("valid water-storage fixture");
    let reference = Reference::parse(include_bytes!("fixtures/water-storage.refmesh"));
    let (mesh, coverage) = build_mesh(&document, true);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_same_triangles(&mesh, 0, &reference, 0, mesh.triangle_count(), "water storage");
}

/// Compares a sanitary connection, septic tank, and multi-trench drain field with C#.
#[test]
fn septic_system_matches_the_application() {
    let document = Document::parse(include_bytes!("fixtures/septic.gr")).expect("valid septic fixture");
    let reference = Reference::parse(include_bytes!("fixtures/septic.refmesh"));
    let (mesh, coverage) = build_mesh(&document, true);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_same_triangles(&mesh, 0, &reference, 0, mesh.triangle_count(), "septic system");
}

/// Compares the migrated electrical catalog in two orientations, with unconnected lights switched off by a panel.
#[test]
fn electrical_devices_match_the_application() {
    let document = Document::parse(include_bytes!("fixtures/electrical.gr")).expect("valid electrical fixture");
    let reference = Reference::parse(include_bytes!("fixtures/electrical.refmesh"));
    let (mesh, coverage) = build_mesh(&document, false);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_same_triangles(&mesh, 0, &reference, 0, mesh.triangle_count(), "electrical devices");
}

/// Compares standalone luminaires that the source application energizes before a panel is added.
#[test]
fn standalone_lights_match_the_application() {
    let document = Document::parse(include_bytes!("fixtures/electrical-lights.gr")).expect("valid light fixture");
    let reference = Reference::parse(include_bytes!("fixtures/electrical-lights.refmesh"));
    let (mesh, coverage) = build_mesh(&document, false);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), reference.kinds.len());
    assert_same_triangles(&mesh, 0, &reference, 0, mesh.triangle_count(), "standalone lights");
}

/// Keeps circuit-connected or switched lighting on the source-application fallback until simulation is ported.
#[test]
fn controlled_lights_remain_incomplete() {
    let mut document = Document::parse(include_bytes!("fixtures/electrical.gr")).expect("valid electrical fixture");
    let light = document.electrical_devices.iter().find(|device| device.is_luminaire()).expect("fixture contains a light").id;
    let switch = document.electrical_devices.iter_mut().find(|device| device.kind == electrical_device_kind::SWITCH).expect("fixture contains a switch");
    switch.controls.push(light);
    let (_, coverage) = build_mesh(&document, false);
    assert_eq!(coverage.electrical_devices, 2);
    assert!(!coverage.is_complete());

    document.electrical_devices.iter_mut().find(|device| device.kind == electrical_device_kind::SWITCH).unwrap().controls.clear();
    document.electrical_devices.iter_mut().find(|device| device.id == light).unwrap().circuit_id = Some(Id(42));
    let (_, coverage) = build_mesh(&document, false);
    assert_eq!(coverage.electrical_devices, 1);
}

#[test]
/// Verifies that procedural plants preserve both geometry and seeded branching.
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

/// Compares every ported residential component with the original application's triangle positions and materials.
#[test]
fn residential_components_match_the_application() {
    let document = Document::parse(include_bytes!("fixtures/residential-components.gr")).expect("valid project");
    let reference = Reference::parse(include_bytes!("fixtures/residential-components.refmesh"));
    let (mesh, coverage) = build_mesh(&document, false);
    let references: HashMap<Id, (usize, usize)> = reference.ranges.iter().map(|&(id, first, end)| (id, (first, end))).collect();

    assert_eq!(document.structural_elements.len(), 41);
    assert_eq!(mesh.element_ranges.len(), 41);
    assert!(coverage.is_complete(), "{coverage:?}");
    for range in &mesh.element_ranges {
        let &(first, end) = references.get(&range.element).expect("component exists in the original mesh");
        assert_eq!(
            range.end - range.first,
            end - first,
            "component {} has a different triangle count",
            range.element
        );
        assert_same_triangles(
            &mesh,
            range.first,
            &reference,
            first,
            end - first,
            &format!("component {}", range.element),
        );
    }
}

/// Checks two orientations of every currently supported catalog recipe against meshes generated by C#.
#[test]
fn supported_residential_catalog_shapes_match_in_both_orientations() {
    let document = Document::parse(include_bytes!("fixtures/residential-catalog.gr")).expect("valid catalog fixture");
    let reference = Reference::parse(include_bytes!("fixtures/residential-catalog.refmesh"));
    let (mesh, coverage) = build_mesh(&document, false);
    let references: HashMap<Id, (usize, usize)> = reference.ranges.iter().map(|&(id, first, end)| (id, (first, end))).collect();

    assert_eq!(document.structural_elements.len(), 88);
    assert_eq!(mesh.element_ranges.len(), 88);
    assert!(coverage.is_complete(), "{coverage:?}");
    for range in &mesh.element_ranges {
        let &(first, end) = references
            .get(&range.element)
            .expect("catalog component exists in the original mesh");
        assert_eq!(
            range.end - range.first,
            end - first,
            "component {} has a different triangle count",
            range.element
        );
        assert_same_triangles(
            &mesh,
            range.first,
            &reference,
            first,
            end - first,
            &format!("component {}", range.element),
        );
    }
}
