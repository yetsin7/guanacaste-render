//! Electrical simulation compared with the original application's exported mesh and emission scales.

use glam::Vec3;
use gr_model::document::Document;
use gr_model::mesh::{SceneMesh, SurfaceKind, build_mesh};
use gr_model::scene::to_scene;

/// Checks breaker state, switch parity, dimming, surface types, and emitted material strength against C#.
#[test]
fn electrical_simulation_matches_the_application() {
    let document = Document::parse(include_bytes!("fixtures/electrical-simulation.gr")).expect("valid electrical fixture");
    let reference = include_bytes!("fixtures/electrical-simulation.refmesh");
    let reference_scales = include_bytes!("fixtures/electrical-simulation.scales");
    let reference_emissions = include_bytes!("fixtures/electrical-simulation.emissions");
    let read_u32 = |offset| u32::from_le_bytes(reference[offset..offset + 4].try_into().unwrap()) as usize;
    assert_eq!(&reference[..8], b"GRMESH01");
    let triangles = read_u32(8);
    let positions_start = 16 + read_u32(12) * 44;
    let kinds_start = positions_start + triangles * 36;
    let (mesh, coverage) = build_mesh(&document, false);
    assert!(coverage.is_complete(), "{coverage:?}");
    assert_eq!(mesh.triangle_count(), triangles);
    assert_eq!(reference_scales.len(), triangles * 4);
    let mut found_dimmed = false;
    for index in 0..triangles {
        assert_eq!(mesh.kinds[index] as u8, reference[kinds_start + index], "surface {index}");
        for coordinate in 0..9 {
            let offset = positions_start + index * 36 + coordinate * 4;
            let expected = f32::from_le_bytes(reference[offset..offset + 4].try_into().unwrap());
            let position = mesh.positions[index * 3 + coordinate / 3][coordinate % 3];
            assert!((position - expected).abs() < 2e-5, "position {index}:{coordinate}");
        }
        let offset = index * 4;
        let expected = f32::from_le_bytes(reference_scales[offset..offset + 4].try_into().unwrap());
        let actual = mesh.emission_scale.as_ref().map_or(1.0, |scales| scales[index]);
        assert!((actual - expected).abs() < 1e-6, "emission scale {index}");
        found_dimmed |= expected < 1.0 && mesh.kinds[index] == SurfaceKind::LightEmitter;
    }
    assert!(found_dimmed, "fixture must include a dimmed emitting surface");
    let bounds = mesh.bounds();
    let scene = to_scene(mesh, bounds);
    assert_eq!(reference_emissions.len(), triangles * 12);
    for index in 0..triangles {
        let offset = index * 12;
        let value = |at| f32::from_le_bytes(reference_emissions[at..at + 4].try_into().unwrap());
        let expected = Vec3::new(value(offset), value(offset + 4), value(offset + 8));
        let actual = scene.materials[scene.triangle_material[index] as usize].emission;
        assert!((actual - expected).length() < 1e-5, "scene emission {index}");
    }
}

/// Ensures later smooth geometry remains aligned with the per-triangle dimming information.
#[test]
fn smooth_geometry_after_dimmed_light_retains_emission_alignment() {
    let mut mesh = SceneMesh::default();
    mesh.add_triangle(Vec3::ZERO, Vec3::X, Vec3::Y, SurfaceKind::LightEmitter);
    mesh.dim_since(0, 0.4);
    mesh.add_smooth_triangle([Vec3::ZERO, Vec3::X, Vec3::Y], [Vec3::Z; 3], SurfaceKind::Leaf);
    assert_eq!(mesh.emission_scale.as_deref(), Some(&[0.4, 1.0][..]));
    let scene = to_scene(mesh, (Vec3::ZERO, Vec3::ONE));
    assert_eq!(scene.triangle_count(), 2);
}

/// Keeps dimming aligned when independent meshes are combined in either order.
#[test]
fn appended_meshes_preserve_emission_alignment() {
    let make_triangle = || {
        let mut mesh = SceneMesh::default();
        mesh.add_triangle(Vec3::ZERO, Vec3::X, Vec3::Y, SurfaceKind::LightEmitter);
        mesh
    };
    let mut first = make_triangle();
    first.dim_since(0, 0.4);
    first.append(make_triangle());
    let mut second = make_triangle();
    second.dim_since(0, 0.7);
    first.append(second);
    assert_eq!(first.emission_scale.as_deref(), Some(&[0.4, 1.0, 0.7][..]));
    let mut plain = make_triangle();
    plain.append(first);
    assert_eq!(plain.emission_scale.as_deref(), Some(&[1.0, 0.4, 1.0, 0.7][..]));
}
