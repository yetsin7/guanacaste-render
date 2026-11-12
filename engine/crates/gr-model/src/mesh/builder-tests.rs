//! Coverage and geometric checks for the project mesher.

use super::*;

/// Builds a minimal in-memory document for coverage checks.
fn document(json: &str) -> Document {
    serde_json::from_str(json).expect("valid document")
}

const LEVEL: &str = "11111111-1111-1111-1111-111111111111";

#[test]
/// Verifies that an empty project does not invent building geometry.
fn an_empty_project_gives_only_the_ground() {
    let (mesh, coverage) = build_mesh(&document(r#"{"SchemaVersion":15}"#), true);
    assert_eq!(mesh.triangle_count(), 2);
    assert!(coverage.is_complete() && mesh.element_ranges.is_empty());
    assert!(mesh.kinds.iter().all(|&kind| kind == SurfaceKind::Ground));
}

#[test]
/// Ensures unsupported geometry is disclosed instead of silently omitted.
fn what_is_not_meshed_is_counted() {
    let json = format!(
        r#"{{"SchemaVersion":15,"Levels":[{{"Id":"{LEVEL}","Elevation":0}}],
                "Floors":[{{"Id":"22222222-2222-2222-2222-222222222222","LevelId":"{LEVEL}","Kind":2,"Shape":999,"Pitch":15,"Thickness":1,
                            "Boundary":[{{"X":0,"Y":0}},{{"X":9,"Y":0}},{{"X":9,"Y":9}}]}}],
                "StructuralElements":[{{"Kind":12}},{{"Kind":7}},{{"Kind":8}},{{"Kind":5,"ComponentShape":999}},{{"Kind":29}}],
                "ElectricalDevices":[{{}},{{}}]}}"#
    );
    let (mesh, coverage) = build_mesh(&document(&json), false);
    assert_eq!(mesh.triangle_count(), 0);
    // The unknown component and elevator count; the stair is meshed, and drafting and openings have no solid.
    assert_eq!(
        coverage,
        Coverage {
            pitched_roofs: 1,
            structural_elements: 2,
            electrical_devices: 2,
            ..Default::default()
        }
    );
    assert_eq!(coverage.missing(), 5);
}

#[test]
/// Checks the mitred roof boundary against the expected expanded dimensions.
fn a_roof_overhang_grows_the_slab_evenly() {
    let json = format!(
        r#"{{"SchemaVersion":15,"Levels":[{{"Id":"{LEVEL}","Elevation":3}}],
                "Floors":[{{"Id":"22222222-2222-2222-2222-222222222222","LevelId":"{LEVEL}","Kind":2,"Thickness":0.2,
                            "Overhang":0.5,"Boundary":[{{"X":0,"Y":0}},{{"X":4,"Y":0}},{{"X":4,"Y":6}},{{"X":0,"Y":6}}]}}]}}"#
    );
    let (mesh, coverage) = build_mesh(&document(&json), false);
    assert!(coverage.is_complete());
    let (min, max) = mesh.bounds();
    assert!(
        (min - Vec3::new(-0.5, -0.5, 2.8)).length() < 1e-5
            && (max - Vec3::new(4.5, 6.5, 3.0)).length() < 1e-5
    );
}

/// A hot-water system can produce pipes even when there are no plumbing service points.
#[test]
fn plumbing_systems_without_service_points_remain_incomplete() {
    let project = document(r#"{"SchemaVersion":15,"PlumbingSystems":[{}]}"#);
    let (_, coverage) = build_mesh(&project, false);
    assert_eq!(coverage.plumbing_points, 0);
    assert_eq!(coverage.plumbing_systems, 1);
    assert!(!coverage.is_complete());
}
