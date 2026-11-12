//! Opening what the studio can show: `.grscene` files, and projects (`.gr`, `.g3d`).
//!
//! A project is read and meshed here, by `gr-model`, when that mesher covers everything in it. Otherwise the
//! installed C# application converts it, run without a window (`GuanacasteRender.exe --export-scene`). That bridge
//! shrinks as `gr-model` covers more element kinds, and goes away when it covers them all.

use crate::studio::vegetation;
use glam::Vec3;
use gr_core::scene_file::Scene;
use gr_model::document::Document;
use gr_model::mesh::build_mesh;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::{Receiver, channel};

/// A scene ready to show.
pub struct Opened {
    pub scene: Scene,
    /// The scene without the project's plants, when they were taken out to be edited; `scene` adds them back.
    pub base: Option<Scene>,
    /// The procedural plants of a project meshed here, editable in Build.
    pub plants: Vec<OpenedPlant>,
    pub path: PathBuf,
    /// Something the user should know about how it was opened, such as elements that are missing.
    pub notice: Option<String>,
}

/// A plant saved in the project, with its root where the project mesher grows it.
#[derive(Clone, Copy, Debug)]
pub struct OpenedPlant {
    pub id: u128,
    pub style: i32,
    pub at: Vec3,
    pub height: f64,
    pub spread: f64,
}

/// What [`load`] produces before it becomes an [`Opened`].
struct Read {
    scene: Scene,
    base: Option<Scene>,
    plants: Vec<OpenedPlant>,
    notice: Option<String>,
}

impl Read {
    fn scene(scene: Scene, notice: Option<String>) -> Read {
        Read {
            scene,
            base: None,
            plants: Vec::new(),
            notice,
        }
    }
}

/// A scene ready to show, or the reason it could not be opened, in words for the user.
pub type Loaded = Result<Opened, String>;

/// File extensions the open dialog offers.
pub const PROJECT_EXTENSIONS: [&str; 3] = ["gr", "g3d", "grscene"];

/// Loads `path` on another thread so the window keeps responding; the result arrives through the channel.
pub fn load_in_background(path: PathBuf, wake: impl Fn() + Send + 'static) -> Receiver<Loaded> {
    let (sender, receiver) = channel();
    std::thread::spawn(move || {
        let result = load(&path).map(|read| Opened {
            scene: read.scene,
            base: read.base,
            plants: read.plants,
            path,
            notice: read.notice,
        });
        let _ = sender.send(result);
        wake();
    });
    receiver
}

/// Opens a native scene only when its geometry and saved appearance are both supported.
fn load(path: &Path) -> Result<Read, String> {
    let extension = path
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or_default()
        .to_lowercase();
    if extension == "grscene" {
        return Scene::load(path)
            .map(|scene| Read::scene(scene, None))
            .map_err(|error| {
                localized("scene_read_error").replace("{reason}", &error.to_string())
            });
    }

    // First choice: read and mesh the project here. Only a mesh that covers the whole project is shown as it is.
    let native = Document::load(path).map(|mut document| {
        let custom_scene = document.has_scene_customizations() || has_model_links(path);
        // The plants are meshed apart so Build can move and delete them; the scene adds them back as they were.
        let plants: Vec<OpenedPlant> = gr_model::edit::procedural_plants(&document)
            .into_iter()
            .map(|(plant, z)| OpenedPlant {
                id: plant.id.0,
                style: plant.style,
                at: Vec3::new(plant.start.x as f32, plant.start.y as f32, z as f32),
                height: plant.height,
                spread: plant.width,
            })
            .collect();
        document
            .structural_elements
            .retain(|element| !element.is_procedural_plant());
        let (building, coverage) = build_mesh(&document, false);
        // The camera frames the building and the plants, as it did when the plants were part of the mesh.
        let canopies = plants.iter().flat_map(|plant| {
            let reach = Vec3::new(plant.spread as f32 / 2.0, plant.spread as f32 / 2.0, 0.0);
            [
                plant.at - reach,
                plant.at + reach + Vec3::Z * plant.height as f32,
            ]
        });
        let bounds = if building.positions.is_empty() {
            canopies
                .fold(None, |bounds: Option<(Vec3, Vec3)>, point| {
                    Some(bounds.map_or((point, point), |(min, max)| {
                        (min.min(point), max.max(point))
                    }))
                })
                .unwrap_or_else(|| building.bounds())
        } else {
            canopies.fold(building.bounds(), |(min, max), point| {
                (min.min(point), max.max(point))
            })
        };
        let (mesh, _) = build_mesh(&document, true);
        (
            gr_model::scene::to_scene(mesh, bounds),
            coverage,
            custom_scene,
            plants,
        )
    });
    let native = match native {
        Ok((base, coverage, false, plants)) if coverage.is_complete() => {
            return Ok(with_plants(base, plants, None));
        }
        other => other,
    };

    let Some(bridge) = find_bridge() else {
        // Without the application, a partial building is still better than nothing, as long as it says so.
        return match native {
            Ok((base, coverage, custom_scene, plants)) => {
                let message = match (coverage.is_complete(), custom_scene) {
                    (true, true) => localized("partial_appearance").to_owned(),
                    (false, true) => localized("partial_both")
                        .replace("{count}", &coverage.missing().to_string()),
                    _ => localized("partial_geometry")
                        .replace("{count}", &coverage.missing().to_string()),
                };
                Ok(with_plants(base, plants, Some(message)))
            }
            Err(error) => {
                Err(localized("project_open_error").replace("{reason}", &error.to_string()))
            }
        };
    };
    convert_with_bridge(&bridge, path)
        .map(|scene| Read::scene(scene, Some(localized("bridge_preview").to_owned())))
}

/// The scene of a project with its plants grown back in, keeping the scene without them for edits.
fn with_plants(base: Scene, plants: Vec<OpenedPlant>, notice: Option<String>) -> Read {
    if plants.is_empty() {
        return Read::scene(base, notice);
    }
    let mut scene = base.clone();
    for plant in &plants {
        let mesh = vegetation::grow(plant.style, plant.id, plant.at, plant.height, plant.spread);
        vegetation::append_mesh(&mut scene, &mesh);
    }
    Read {
        scene,
        base: Some(base),
        plants,
        notice,
    }
}

/// Detects the original application's sidecar for linked external models without modifying it.
fn has_model_links(path: &Path) -> bool {
    let mut sidecar = path.as_os_str().to_owned();
    sidecar.push(".links.json");
    PathBuf::from(sidecar).is_file()
}

/// Converts a project with the installed application and loads the scene it writes.
fn convert_with_bridge(bridge: &Path, path: &Path) -> Result<Scene, String> {
    let workspace = BridgeWorkspace::create().map_err(|error| {
        localized("temporary_folder_error").replace("{reason}", &error.to_string())
    })?;
    let exported = workspace.folder.join("scene.grscene");
    let error_file = workspace.folder.join("scene.grscene.error.txt");

    let status = Command::new(bridge)
        .arg("--export-scene")
        .arg(path)
        .arg(&exported)
        .status()
        .map_err(|error| localized("bridge_start_error").replace("{reason}", &error.to_string()))?;
    if !status.success() {
        let reason = std::fs::read_to_string(&error_file).unwrap_or_default();
        return Err(if reason.trim().is_empty() {
            localized("conversion_error").to_owned()
        } else {
            localized("conversion_error_reason").replace("{reason}", reason.trim())
        });
    }
    Scene::load(&exported)
        .map_err(|error| localized("exported_scene_error").replace("{reason}", &error.to_string()))
}

/// A private directory prevents overlapping project loads from overwriting each other's bridge output.
struct BridgeWorkspace {
    folder: PathBuf,
}

impl BridgeWorkspace {
    /// Reserves an unused directory without replacing files left by another process or an earlier session.
    fn create() -> std::io::Result<Self> {
        static NEXT_WORKSPACE: AtomicU64 = AtomicU64::new(0);
        let root = std::env::temp_dir().join("guanacaste-studio");
        std::fs::create_dir_all(&root)?;
        loop {
            let sequence = NEXT_WORKSPACE.fetch_add(1, Ordering::Relaxed);
            let folder = root.join(format!("{}-{sequence}", std::process::id()));
            match std::fs::create_dir(&folder) {
                Ok(()) => return Ok(Self { folder }),
                Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => continue,
                Err(error) => return Err(error),
            }
        }
    }
}

impl Drop for BridgeWorkspace {
    /// Removes only files that the bridge itself may have written to this reserved directory.
    fn drop(&mut self) {
        for name in [
            "scene.grscene",
            "scene.grscene.error.txt",
            "scene.grscene.tmp",
        ] {
            let _ = std::fs::remove_file(self.folder.join(name));
        }
        let _ = std::fs::remove_dir(&self.folder);
    }
}

/// Retrieves a user-visible message from the studio's Spanish localization resources.
fn localized(key: &str) -> &'static str {
    crate::localization::text(key)
}

/// The newest installed Guanacaste Render, or the one named by `GUANACASTE_RENDER_BRIDGE`.
fn find_bridge() -> Option<PathBuf> {
    if let Some(explicit) = std::env::var_os("GUANACASTE_RENDER_BRIDGE").map(PathBuf::from) {
        return explicit.is_file().then_some(explicit);
    }
    let root = PathBuf::from(std::env::var_os("LOCALAPPDATA")?)
        .join("Programs")
        .join("Guanacaste Render");
    let mut versions: Vec<(Vec<u32>, PathBuf)> = std::fs::read_dir(root)
        .ok()?
        .flatten()
        .filter_map(|entry| {
            let version: Option<Vec<u32>> = entry
                .file_name()
                .to_str()?
                .split('.')
                .map(|part| part.parse().ok())
                .collect();
            let executable = entry.path().join("GuanacasteRender.exe");
            executable.is_file().then_some((version?, executable))
        })
        .collect();
    versions.sort();
    versions.pop().map(|(_, executable)| executable)
}

#[cfg(test)]
mod tests {
    use super::{BridgeWorkspace, has_model_links};

    /// Independent project loads must never share or delete one another's temporary scene.
    #[test]
    fn overlapping_bridge_loads_have_separate_workspaces() {
        let first = BridgeWorkspace::create().expect("first bridge workspace");
        let second = BridgeWorkspace::create().expect("second bridge workspace");
        assert_ne!(first.folder, second.folder);
        let second_scene = second.folder.join("scene.grscene");
        std::fs::write(&second_scene, b"scene").expect("temporary scene");
        let first_folder = first.folder.clone();
        drop(first);
        assert!(!first_folder.exists());
        assert!(second_scene.exists());
    }

    /// Linked-model sidecars must trigger a partial-preview notice instead of disappearing silently.
    #[test]
    fn detects_linked_model_sidecars() {
        let workspace = BridgeWorkspace::create().expect("bridge workspace");
        let project = workspace.folder.join("scene.grscene");
        let sidecar = workspace.folder.join("scene.grscene.links.json");
        assert!(!has_model_links(&project));
        std::fs::write(&sidecar, "[]").expect("linked-model sidecar");
        assert!(has_model_links(&project));
        std::fs::remove_file(sidecar).expect("remove test sidecar");
    }
}
