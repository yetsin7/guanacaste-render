//! Checks for native project records and identifier handling.

use super::*;

/// Saved render materials and camera settings must not be mistaken for the native scene defaults.
#[test]
fn saved_scene_customizations_are_detected() {
    let defaults: Document =
        serde_json::from_str(r#"{"SchemaVersion":15}"#).expect("default project");
    let materials: Document =
        serde_json::from_str(r#"{"SchemaVersion":15,"RenderMaterials":[{}]}"#)
            .expect("material assignment");
    let camera: Document = serde_json::from_str(r#"{"SchemaVersion":15,"RenderSetup":{}}"#)
        .expect("saved render setup");
    let scene: Document =
        serde_json::from_str(r#"{"SchemaVersion":15,"RenderScenes":[{}]}"#).expect("saved scene");
    let effects: Document =
        serde_json::from_str(r#"{"SchemaVersion":15,"Extensions":{"render.ocean":"{}"}}"#)
            .expect("render effects");
    let sync: Document =
        serde_json::from_str(r#"{"SchemaVersion":15,"Extensions":{"render.modelsync":"token"}}"#)
            .expect("sync token");
    assert!(!defaults.has_scene_customizations());
    assert!(materials.has_scene_customizations());
    assert!(camera.has_scene_customizations());
    assert!(scene.has_scene_customizations());
    assert!(effects.has_scene_customizations());
    assert!(!sync.has_scene_customizations());
}

#[test]
/// Ensures stored GUIDs retain their canonical spelling and reject malformed values.
fn identifiers_round_trip_through_text() {
    let text = "7d256c39-bda6-4166-b3c7-5ecde5c223ac";
    assert_eq!(Id::parse(text).expect("canonical GUID").to_string(), text);
    assert!(Id::parse("7d256c39bda64166b3c75ecde5c223ac").is_none());
    assert!(Id::parse("7d256c39-bda6-4166-b3c7-5ecde5c223zz").is_none());
}

#[test]
/// Checks the native reader against a project written by the application.
fn loads_the_demonstration_house() {
    let document =
        Document::parse(include_bytes!("../tests/fixtures/demo-house.gr")).expect("valid project");
    assert_eq!(
        (
            document.levels.len(),
            document.walls.len(),
            document.openings.len()
        ),
        (2, 6, 11)
    );
    assert_eq!(document.floors.len(), 1);
    assert!(document.walls.iter().all(|wall| wall.length() > 0.0));
}

#[test]
/// Checks interpolation between profile stations and clipping to the wall height.
fn a_profiled_wall_top_is_interpolated_and_capped() {
    let wall: Wall = serde_json::from_str(
        r#"{"Id":"7d256c39-bda6-4166-b3c7-5ecde5c223ac","LevelId":"7d256c39-bda6-4166-b3c7-5ecde5c223ac",
                "Start":{"X":0,"Y":0},"End":{"X":4,"Y":0},"Thickness":0.2,"Height":3,
                "WallTypeId":"7d256c39-bda6-4166-b3c7-5ecde5c223ac",
                "TopProfile":[{"X":0,"Y":2},{"X":2,"Y":4},{"X":4,"Y":2}]}"#,
    )
    .expect("valid wall");
    assert_eq!(wall.top_height_at(-1.0), 2.0);
    assert_eq!(wall.top_height_at(1.0), 3.0); // 3 on the profile, and exactly the cap
    assert_eq!(wall.top_height_at(2.0), 3.0); // the profile reaches 4, the wall is 3 tall
    assert_eq!(wall.top_height_at(3.5), 2.5);
}
