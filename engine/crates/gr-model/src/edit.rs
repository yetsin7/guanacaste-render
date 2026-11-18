//! Writing changes back into a native project. Only the plantings are edited; every other part of the document,
//! including fields this crate does not read, is written back as it was read, in the same key order.
//!
//! A planting is a structural element of kind [`element_kind::PLANTING`] with the fields the application writes
//! for one. The schema version does not change, so the application opens the file as before.

use crate::container::{Container, ContainerError};
use crate::document::{Document, DocumentError, Id, StructuralElement, element_kind};
use crate::mesh::planting_base;
use crate::plan::Point2;
use serde_json::{Value, json};
use std::fmt;
use std::path::{Path, PathBuf};

/// A procedural plant to add to a project.
#[derive(Clone, Debug, PartialEq)]
pub struct Planting {
    /// Also the plant's seed: the same identifier grows the same plant here and in the application.
    pub id: Id,
    pub name: String,
    pub level_id: Id,
    pub at: Point2,
    /// Metres from [`planting_base`] to the root.
    pub offset: f64,
    /// Canopy spread and height, in metres.
    pub width: f64,
    pub height: f64,
    /// `ElementStyle`: the growth form.
    pub style: i32,
}

impl Planting {
    /// A planting of growth form `style`, `width` by `height` metres, at `at`; [`Planting::rooted`] places it.
    pub fn new(id: Id, name: &str, at: Point2, width: f64, height: f64, style: i32) -> Planting {
        Planting {
            id,
            name: name.to_owned(),
            level_id: Id::default(),
            at,
            offset: 0.0,
            width,
            height,
            style,
        }
    }

    /// The planting with its root at height `z`, on the level whose ground is the highest one at or below it.
    pub fn rooted(mut self, document: &Document, z: f64) -> Planting {
        self.level_id = level_under(document, self.at, z);
        self.offset = z - planting_base(document, self.level_id, self.at);
        self
    }

    fn to_json(&self) -> Value {
        // The fields and their order as `NativeDocumentStore` writes a planting.
        let point = json!({ "X": self.at.x, "Y": self.at.y });
        json!({
            "Id": self.id.to_string(),
            "LevelId": self.level_id.to_string(),
            "Kind": element_kind::PLANTING,
            "Name": self.name,
            "Start": point.clone(),
            "End": point,
            "Width": self.width,
            "Height": self.height,
            "Offset": self.offset,
            "Depth": 0,
            "ComponentShape": 0,
            "ComponentCategory": 0,
            "Rotation": 0,
            "FamilyId": null,
            "GroupId": null,
            "RailingHostId": null,
            "GridWidth": 0,
            "GridHeight": 0,
            "RiserHeight": 0,
            "TreadDepth": 0,
            "Rise": 0,
            "Style": self.style,
            "Cuts": null,
            "Wires": null
        })
    }
}

/// The level a point at height `z` stands on: the one whose ground there is the highest not above `z` (with a
/// little tolerance), or the lowest level when the point is below them all.
pub fn level_under(document: &Document, at: Point2, z: f64) -> Id {
    let mut levels: Vec<(f64, Id)> = document
        .levels
        .iter()
        .map(|level| (planting_base(document, level.id, at), level.id))
        .collect();
    levels.sort_by(|a, b| a.0.total_cmp(&b.0));
    levels
        .iter()
        .rev()
        .find(|(base, _)| *base <= z + 0.25)
        .or(levels.first())
        .map_or(Id::default(), |(_, id)| *id)
}

/// The procedural plants of a project with the height of their root, as [`crate::mesh::build_mesh`] grows them.
pub fn procedural_plants(document: &Document) -> Vec<(StructuralElement, f64)> {
    document
        .structural_elements
        .iter()
        .filter(|element| element.is_procedural_plant())
        .map(|plant| {
            (
                plant.clone(),
                planting_base(document, plant.level_id, plant.start) + plant.offset,
            )
        })
        .collect()
}

#[derive(Debug)]
pub enum EditError {
    Read(ContainerError),
    Write(std::io::Error),
    /// The project's JSON is not what the application writes.
    Unexpected(&'static str),
    /// The written file did not read back; the original was restored from the backup.
    Verification(DocumentError),
    /// The saved file failed verification and restoring the original also failed.
    Restoration {
        verification: DocumentError,
        restore: std::io::Error,
    },
    /// The staged project could not be opened; the original has not been replaced.
    StagingVerification(DocumentError),
}

impl fmt::Display for EditError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            EditError::Read(error) => write!(f, "{error}"),
            EditError::Write(error) => write!(f, "cannot write the project file: {error}"),
            EditError::Unexpected(reason) => write!(f, "unexpected project contents: {reason}"),
            EditError::StagingVerification(error) => write!(
                f,
                "the staged project did not read back ({error}); the original was not replaced"
            ),
            EditError::Verification(error) => write!(
                f,
                "the saved file did not read back ({error}); the original was restored"
            ),
            EditError::Restoration {
                verification,
                restore,
            } => write!(
                f,
                "the saved file did not read back ({verification}) and restoring the original failed ({restore}); recover from the .bak backup"
            ),
        }
    }
}

impl std::error::Error for EditError {}

/// The copy of the project kept before each save: `casa.gr` → `casa.gr.bak`.
pub fn backup_path(path: &Path) -> PathBuf {
    let mut name = path.as_os_str().to_owned();
    name.push(".bak");
    PathBuf::from(name)
}

/// Adds `add` to a project file and removes the plantings whose identifiers are in `remove`.
///
/// The previous file is kept as [`backup_path`]. The new file is written beside the project and moved over it,
/// then read back and verified; if it does not read, the backup is put back and an error returned.
pub fn save_plantings(path: &Path, add: &[Planting], remove: &[Id]) -> Result<(), EditError> {
    let original =
        std::fs::read(path).map_err(|error| EditError::Read(ContainerError::Io(error)))?;
    let mut container = Container::parse(&original).map_err(EditError::Read)?;
    container.json = edit_json(&container.json, add, remove)?;
    let bytes = container.encode().map_err(EditError::Write)?;

    let backup = backup_path(path);
    let mut staging = path.as_os_str().to_owned();
    staging.push(".guardando");
    let staging = PathBuf::from(staging);
    // Never overwrite a leftover staging file from an interrupted save.
    // A caller can inspect or recover it before trying again.
    {
        use std::io::Write;
        let mut file = std::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&staging)
            .map_err(EditError::Write)?;
        // Flush staged bytes to storage before validating or replacing the project.
        // This narrows the window for losing a successfully reported save on power loss.
        if let Err(error) = file.write_all(&bytes).and_then(|()| file.sync_all()) {
            drop(file);
            let _ = std::fs::remove_file(&staging);
            return Err(EditError::Write(error));
        }
    }
    // Validate the complete native container before touching the project or its backup.
    if let Err(error) = Document::load(&staging) {
        let _ = std::fs::remove_file(&staging);
        return Err(EditError::StagingVerification(error));
    }
    if let Err(error) = std::fs::write(&backup, &original) {
        let _ = std::fs::remove_file(&staging);
        return Err(EditError::Write(error));
    }
    if let Err(error) = std::fs::rename(&staging, path) {
        let _ = std::fs::remove_file(&staging);
        return Err(EditError::Write(error));
    }
    if let Err(error) = Document::load(path) {
        if let Err(restore) = std::fs::write(path, &original) {
            return Err(EditError::Restoration {
                verification: error,
                restore,
            });
        }
        return Err(EditError::Verification(error));
    }
    Ok(())
}

/// The project JSON with the plantings edited and everything else untouched.
pub fn edit_json(json: &[u8], add: &[Planting], remove: &[Id]) -> Result<Vec<u8>, EditError> {
    let mut document: Value = serde_json::from_slice(json)
        .map_err(|_| EditError::Unexpected("the payload is not JSON"))?;
    let root = document
        .as_object_mut()
        .ok_or(EditError::Unexpected("the document is not an object"))?;
    let elements = root
        .entry("StructuralElements")
        .or_insert_with(|| Value::Array(Vec::new()));
    if elements.is_null() {
        *elements = Value::Array(Vec::new());
    }
    let elements = elements
        .as_array_mut()
        .ok_or(EditError::Unexpected("StructuralElements is not a list"))?;
    // Removal must not silently delete multiple elements sharing an identifier.
    // A corrupt or ambiguous project should be repaired explicitly instead.
    for id in remove {
        let matches = elements
            .iter()
            .filter(|element| {
                element
                    .get("Id")
                    .and_then(Value::as_str)
                    .and_then(Id::parse)
                    == Some(*id)
                    && element.get("Kind").and_then(Value::as_i64)
                        == Some(element_kind::PLANTING as i64)
            })
            .count();
        if matches > 1 {
            return Err(EditError::Unexpected(
                "planting removal matches multiple existing elements",
            ));
        }
    }
    elements.retain(|element| {
        let id = element
            .get("Id")
            .and_then(Value::as_str)
            .and_then(Id::parse);
        let is_plant =
            element.get("Kind").and_then(Value::as_i64) == Some(element_kind::PLANTING as i64);
        !(is_plant && id.is_some_and(|id| remove.contains(&id)))
    });
    // JSON cannot represent NaN or infinity. Reject them instead of silently
    // serializing invalid coordinates or dimensions as null.
    if add.iter().any(|planting| {
        !planting.at.x.is_finite()
            || !planting.at.y.is_finite()
            || !planting.offset.is_finite()
            || !planting.width.is_finite()
            || !planting.height.is_finite()
    }) {
        return Err(EditError::Unexpected(
            "planting contains non-finite geometry",
        ));
    }
    // Reject ambiguous batches instead of silently keeping only the last planting.
    let mut added_ids = std::collections::HashSet::new();
    if add.iter().any(|planting| !added_ids.insert(planting.id)) {
        return Err(EditError::Unexpected(
            "duplicate planting identifier in add batch",
        ));
    }
    for planting in add {
        // An ambiguous existing document must not be silently repaired by deleting
        // multiple elements with the same identifier during an update.
        let matches = elements
            .iter()
            .filter(|element| {
                element
                    .get("Id")
                    .and_then(Value::as_str)
                    .and_then(Id::parse)
                    == Some(planting.id)
            })
            .count();
        if matches > 1 {
            return Err(EditError::Unexpected(
                "planting identifier matches multiple existing elements",
            ));
        }
        // A planting must never replace a wall, slab, or another non-plant element
        // that happens to have the same identifier.
        if elements.iter().any(|element| {
            element
                .get("Id")
                .and_then(Value::as_str)
                .and_then(Id::parse)
                == Some(planting.id)
                && element.get("Kind").and_then(Value::as_i64)
                    != Some(element_kind::PLANTING as i64)
        }) {
            return Err(EditError::Unexpected(
                "planting identifier belongs to a non-plant element",
            ));
        }
        // Saving twice must not duplicate a plant.
        elements.retain(|element| {
            element
                .get("Id")
                .and_then(Value::as_str)
                .and_then(Id::parse)
                != Some(planting.id)
        });
        elements.push(planting.to_json());
    }
    serde_json::to_vec(&document)
        .map_err(|_| EditError::Unexpected("the document cannot be written"))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::mesh::{SceneMesh, add_plant, build_mesh};

    const GARDEN: &[u8] = include_bytes!("../tests/fixtures/garden.gr");

    fn garden() -> Document {
        Document::parse(GARDEN).expect("the fixture is a valid project")
    }

    #[test]
    fn a_saved_planting_is_read_back_as_the_application_writes_it() {
        let document = garden();
        let before = document.structural_elements.len();
        let planting = Planting::new(
            Id(0x1234_5678_9abc_def0_1234_5678_9abc_def0),
            "Palma de coco",
            Point2::new(3.0, -2.0),
            6.0,
            9.0,
            10,
        )
        .rooted(&document, 0.0);
        let container = Container::parse(GARDEN).expect("valid");
        let json = edit_json(&container.json, std::slice::from_ref(&planting), &[]).expect("edits");
        let edited: Document = serde_json::from_slice(&json).expect("still a document");
        assert_eq!(edited.structural_elements.len(), before + 1);
        let added = edited.structural_elements.last().expect("added");
        assert_eq!(
            (added.id, added.kind, added.style),
            (planting.id, element_kind::PLANTING, 10)
        );
        assert_eq!(
            (added.width, added.height, added.level_id),
            (6.0, 9.0, planting.level_id)
        );

        // Saving again replaces rather than duplicates; removing takes it out.
        let again = edit_json(&json, std::slice::from_ref(&planting), &[]).expect("edits");
        assert_eq!(
            serde_json::from_slice::<Document>(&again)
                .expect("doc")
                .structural_elements
                .len(),
            before + 1
        );
        let removed = edit_json(&again, &[], &[planting.id]).expect("edits");
        assert_eq!(
            serde_json::from_slice::<Document>(&removed)
                .expect("doc")
                .structural_elements
                .len(),
            before
        );
    }

    #[test]
    fn adding_planting_never_replaces_non_plant_with_same_id() {
        let id = Id(987654321);
        let mut source: Value =
            serde_json::from_slice(br#"{"StructuralElements":[]}"#).expect("json");
        source["StructuralElements"] = json!([{
            "Id": id.to_string(),
            "Kind": 1,
            "Name": "Existing wall"
        }]);
        let bytes = serde_json::to_vec(&source).expect("json");
        let planting = Planting::new(id, "Tree", Point2::new(0.0, 0.0), 2.0, 3.0, 10);
        assert!(matches!(
            edit_json(&bytes, &[planting], &[]),
            Err(EditError::Unexpected(_))
        ));
        let unchanged: Value = serde_json::from_slice(&bytes).expect("json");
        assert_eq!(unchanged["StructuralElements"][0]["Name"], "Existing wall");
    }

    #[test]
    fn duplicate_planting_ids_in_one_batch_are_rejected() {
        let id = Id(987654322);
        let first = Planting::new(id, "First", Point2::new(0.0, 0.0), 2.0, 3.0, 10);
        let second = Planting::new(id, "Second", Point2::new(1.0, 1.0), 4.0, 5.0, 10);
        let input = br#"{"StructuralElements":[]}"#;
        assert!(matches!(
            edit_json(input, &[first, second], &[]),
            Err(EditError::Unexpected(
                "duplicate planting identifier in add batch"
            ))
        ));
    }

    #[test]
    fn removing_a_plant_does_not_remove_a_non_plant_with_the_same_id() {
        let id = Id(987654325);
        let input = json!({
            "StructuralElements": [
                {"Id": id.to_string(), "Kind": 1, "Name": "Keep wall"},
                {"Id": id.to_string(), "Kind": element_kind::PLANTING, "Name": "Remove tree"}
            ]
        });
        let bytes = serde_json::to_vec(&input).expect("json");
        let output = edit_json(&bytes, &[], &[id]).expect("remove planting only");
        let parsed: Value = serde_json::from_slice(&output).expect("json");
        let elements = parsed["StructuralElements"].as_array().expect("elements");
        assert_eq!(elements.len(), 1);
        assert_eq!(elements[0]["Name"], "Keep wall");
    }

    #[test]
    fn removing_ambiguous_existing_planting_ids_is_rejected() {
        let id = Id(987654324);
        let input = json!({
            "StructuralElements": [
                {"Id": id.to_string(), "Kind": element_kind::PLANTING},
                {"Id": id.to_string(), "Kind": element_kind::PLANTING}
            ]
        });
        let bytes = serde_json::to_vec(&input).expect("json");
        assert!(matches!(
            edit_json(&bytes, &[], &[id]),
            Err(EditError::Unexpected(
                "planting removal matches multiple existing elements"
            ))
        ));
    }

    #[test]
    fn ambiguous_existing_planting_ids_are_rejected() {
        let id = Id(987654323);
        let input = json!({
            "StructuralElements": [
                {"Id": id.to_string(), "Kind": element_kind::PLANTING},
                {"Id": id.to_string(), "Kind": element_kind::PLANTING}
            ]
        });
        let bytes = serde_json::to_vec(&input).expect("json");
        let planting = Planting::new(id, "Updated", Point2::new(0.0, 0.0), 2.0, 3.0, 10);
        assert!(matches!(
            edit_json(&bytes, &[planting], &[]),
            Err(EditError::Unexpected(
                "planting identifier matches multiple existing elements"
            ))
        ));
    }

    #[test]
    fn non_finite_planting_geometry_is_rejected() {
        let input = br#"{"StructuralElements":[]}"#;
        for invalid in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
            let mut planting =
                Planting::new(Id(123), "Invalid", Point2::new(0.0, 0.0), 2.0, 3.0, 10);
            planting.width = invalid;
            assert!(matches!(
                edit_json(input, &[planting], &[]),
                Err(EditError::Unexpected(
                    "planting contains non-finite geometry"
                ))
            ));
        }
        let mut planting =
            Planting::new(Id(124), "Invalid", Point2::new(f64::NAN, 0.0), 2.0, 3.0, 10);
        assert!(matches!(
            edit_json(input, &[planting.clone()], &[]),
            Err(EditError::Unexpected(
                "planting contains non-finite geometry"
            ))
        ));
        planting.at.x = 0.0;
        planting.offset = f64::INFINITY;
        assert!(matches!(
            edit_json(input, &[planting], &[]),
            Err(EditError::Unexpected(
                "planting contains non-finite geometry"
            ))
        ));
    }

    #[test]
    fn non_finite_y_and_height_are_rejected_without_changing_input() {
        let input = br#"{"StructuralElements":[]}"#;
        for (id, bad_y, bad_height) in [
            (Id(125), f64::INFINITY, 3.0),
            (Id(126), f64::NEG_INFINITY, 3.0),
            (Id(127), 0.0, f64::NAN),
            (Id(128), 0.0, f64::INFINITY),
            (Id(129), 0.0, f64::NEG_INFINITY),
        ] {
            let planting =
                Planting::new(id, "Invalid", Point2::new(0.0, bad_y), 2.0, bad_height, 10);
            assert!(matches!(
                edit_json(input, &[planting], &[]),
                Err(EditError::Unexpected(
                    "planting contains non-finite geometry"
                ))
            ));
            assert_eq!(input, br#"{"StructuralElements":[]}"#);
        }
    }

    #[test]
    fn all_non_finite_coordinate_and_offset_variants_are_rejected() {
        let input = br#"{"StructuralElements":[]}"#;
        for invalid in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
            for field in 0..3 {
                let mut planting =
                    Planting::new(Id(130), "Invalid", Point2::new(0.0, 0.0), 2.0, 3.0, 10);
                match field {
                    0 => planting.at.x = invalid,
                    1 => planting.at.y = invalid,
                    _ => planting.offset = invalid,
                }
                assert!(matches!(
                    edit_json(input, &[planting], &[]),
                    Err(EditError::Unexpected(
                        "planting contains non-finite geometry"
                    ))
                ));
            }
        }
    }

    #[test]
    fn everything_else_in_the_project_is_kept_in_order() {
        let container = Container::parse(GARDEN).expect("valid");
        let unchanged = edit_json(&container.json, &[], &[]).expect("edits");
        let original: Value = serde_json::from_slice(&container.json).expect("json");
        let written: Value = serde_json::from_slice(&unchanged).expect("json");
        assert_eq!(original, written);
        let keys = |value: &Value| {
            value
                .as_object()
                .expect("object")
                .keys()
                .cloned()
                .collect::<Vec<_>>()
        };
        assert_eq!(keys(&original), keys(&written));
    }

    #[test]
    fn a_plant_rooted_at_a_height_grows_from_there_in_the_project_mesh() {
        let document = garden();
        for (plant, z) in procedural_plants(&document) {
            // The plant as the studio grows it, rooted at z...
            let mut studio = SceneMesh::default();
            let mut free = plant.clone();
            free.offset = 0.0;
            add_plant(&mut studio, &free, z);
            // ...is the plant the project mesh contains.
            let (mesh, _) = build_mesh(&document, true);
            let range = mesh
                .element_ranges
                .iter()
                .find(|range| range.element == plant.id)
                .expect("meshed");
            let from_project = &mesh.positions[range.first * 3..range.end * 3];
            assert_eq!(from_project.len(), studio.positions.len());
            for (a, b) in from_project.iter().zip(&studio.positions) {
                assert!((*a - *b).length() < 1e-4, "{a} != {b}");
            }
            // And saving it again with the offset computed from z keeps the same offset.
            let again = Planting::new(
                plant.id,
                "x",
                plant.start,
                plant.width,
                plant.height,
                plant.style,
            )
            .rooted(&document, z);
            assert!((again.offset - plant.offset).abs() < 1e-9 || again.level_id != plant.level_id);
        }
    }

    #[test]
    fn consecutive_project_saves_keep_the_previous_version_as_backup() {
        let folder = std::env::temp_dir().join(format!(
            "gr-model-consecutive-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .expect("clock")
                .as_nanos()
        ));
        std::fs::create_dir_all(&folder).expect("folder");
        let path = folder.join("jardin.gr");
        std::fs::write(&path, GARDEN).expect("fixture");
        let document = garden();
        let planting = Planting::new(Id(43), "Palma", Point2::new(2.0, 1.0), 2.0, 3.0, 10)
            .rooted(&document, 0.0);

        save_plantings(&path, std::slice::from_ref(&planting), &[]).expect("first save");
        let first_version = std::fs::read(&path).expect("first version");
        assert_eq!(std::fs::read(backup_path(&path)).expect("backup"), GARDEN);

        save_plantings(&path, std::slice::from_ref(&planting), &[])
            .expect("second save replaces existing file");
        assert_eq!(
            std::fs::read(backup_path(&path)).expect("backup"),
            first_version
        );
        let mut staging = path.as_os_str().to_owned();
        staging.push(".guardando");
        assert!(
            !PathBuf::from(staging).exists(),
            "staging file must be moved away"
        );
        let saved = Document::load(&path).expect("reads after second save");
        assert_eq!(
            saved
                .structural_elements
                .iter()
                .filter(|element| element.id == planting.id)
                .count(),
            1,
            "saving the same planting twice must not duplicate it"
        );
        std::fs::remove_dir_all(folder).expect("cleanup");
    }

    #[test]
    fn blocked_staging_path_keeps_original_and_existing_backup() {
        let folder = std::env::temp_dir().join(format!(
            "gr-model-staging-blocked-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .expect("clock")
                .as_nanos()
        ));
        std::fs::create_dir_all(&folder).expect("folder");
        let path = folder.join("jardin.gr");
        std::fs::write(&path, GARDEN).expect("fixture");
        let backup = backup_path(&path);
        std::fs::write(&backup, b"previous backup").expect("backup");
        let mut staging = path.as_os_str().to_owned();
        staging.push(".guardando");
        let staging = PathBuf::from(staging);
        // A directory blocks staging writes on Windows and Unix alike.
        std::fs::create_dir(&staging).expect("blocking directory");
        let planting = Planting::new(Id(46), "Palma", Point2::new(2.0, 1.0), 2.0, 3.0, 10)
            .rooted(&garden(), 0.0);

        assert!(matches!(
            save_plantings(&path, &[planting], &[]),
            Err(EditError::Write(_))
        ));
        assert_eq!(std::fs::read(&path).expect("original"), GARDEN);
        assert_eq!(
            std::fs::read(&backup).expect("backup preserved"),
            b"previous backup"
        );
        assert!(
            staging.is_dir(),
            "preexisting staging directory is preserved"
        );
        std::fs::remove_dir_all(folder).expect("cleanup");
    }

    #[test]
    fn existing_staging_file_is_not_overwritten() {
        let folder = std::env::temp_dir().join(format!(
            "gr-model-staging-existing-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .expect("clock")
                .as_nanos()
        ));
        std::fs::create_dir_all(&folder).expect("folder");
        let path = folder.join("jardin.gr");
        std::fs::write(&path, GARDEN).expect("fixture");
        let backup = backup_path(&path);
        std::fs::write(&backup, b"previous backup").expect("backup");
        let mut staging = path.as_os_str().to_owned();
        staging.push(".guardando");
        let staging = PathBuf::from(staging);
        std::fs::write(&staging, b"interrupted save").expect("staging");
        let planting = Planting::new(Id(47), "Palma", Point2::new(2.0, 1.0), 2.0, 3.0, 10)
            .rooted(&garden(), 0.0);

        assert!(matches!(
            save_plantings(&path, &[planting], &[]),
            Err(EditError::Write(_))
        ));
        assert_eq!(std::fs::read(&path).expect("original"), GARDEN);
        assert_eq!(std::fs::read(&backup).expect("backup"), b"previous backup");
        assert_eq!(
            std::fs::read(&staging).expect("staging preserved"),
            b"interrupted save"
        );
        std::fs::remove_dir_all(folder).expect("cleanup");
    }

    #[test]
    fn backup_write_failure_keeps_original_project_and_removes_staging() {
        let folder = std::env::temp_dir().join(format!(
            "gr-model-backup-failure-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .expect("clock")
                .as_nanos()
        ));
        std::fs::create_dir_all(&folder).expect("folder");
        let path = folder.join("jardin.gr");
        std::fs::write(&path, GARDEN).expect("fixture");
        let backup = backup_path(&path);
        // A directory at the backup path reliably prevents writing on Windows and Unix.
        std::fs::create_dir(&backup).expect("blocking directory");
        let planting = Planting::new(Id(45), "Palma", Point2::new(2.0, 1.0), 2.0, 3.0, 10)
            .rooted(&garden(), 0.0);

        assert!(matches!(
            save_plantings(&path, &[planting], &[]),
            Err(EditError::Write(_))
        ));
        assert_eq!(std::fs::read(&path).expect("original"), GARDEN);
        let mut staging = path.as_os_str().to_owned();
        staging.push(".guardando");
        assert!(!PathBuf::from(staging).exists(), "staging must be cleaned");
        assert!(backup.is_dir(), "existing backup path must be preserved");
        std::fs::remove_dir(&backup).expect("remove blocker");
        std::fs::remove_dir_all(folder).expect("cleanup");
    }

    #[test]
    fn a_project_file_is_saved_verified_and_backed_up() {
        let folder = std::env::temp_dir().join(format!("gr-model-edit-{}", std::process::id()));
        std::fs::create_dir_all(&folder).expect("folder");
        let path = folder.join("jardin.gr");
        std::fs::write(&path, GARDEN).expect("copy");
        let document = garden();
        let planting = Planting::new(Id(42), "Arbusto", Point2::new(1.0, 1.0), 1.6, 1.4, 12)
            .rooted(&document, 0.0);
        save_plantings(&path, &[planting], &[]).expect("saves");
        assert_eq!(std::fs::read(backup_path(&path)).expect("backup"), GARDEN);
        let saved = Document::load(&path).expect("reads back");
        assert!(
            saved
                .structural_elements
                .iter()
                .any(|element| element.id == Id(42))
        );
        let _ = std::fs::remove_dir_all(folder);
    }
}
