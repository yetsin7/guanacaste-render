//! Opening what the studio can show: `.grscene` files, and projects (`.gr`, `.g3d`).
//!
//! A project is read and meshed here, by `gr-model`, when that mesher covers everything in it. Otherwise the
//! installed C# application converts it, run without a window (`GuanacasteRender.exe --export-scene`). That bridge
//! shrinks as `gr-model` covers more element kinds, and goes away when it covers them all.

use gr_core::scene_file::Scene;
use gr_model::document::Document;
use gr_model::mesh::build_mesh;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::mpsc::{Receiver, channel};

/// A scene ready to show.
pub struct Opened {
    pub scene: Scene,
    pub path: PathBuf,
    /// Something the user should know about how it was opened, such as elements that are missing.
    pub notice: Option<String>,
}

/// A scene ready to show, or the reason it could not be opened, in words for the user.
pub type Loaded = Result<Opened, String>;

/// File extensions the open dialog offers.
pub const PROJECT_EXTENSIONS: [&str; 3] = ["gr", "g3d", "grscene"];

/// Loads `path` on another thread so the window keeps responding; the result arrives through the channel.
pub fn load_in_background(path: PathBuf, wake: impl Fn() + Send + 'static) -> Receiver<Loaded> {
    let (sender, receiver) = channel();
    std::thread::spawn(move || {
        let result = load(&path).map(|(scene, notice)| Opened { scene, path, notice });
        let _ = sender.send(result);
        wake();
    });
    receiver
}

fn load(path: &Path) -> Result<(Scene, Option<String>), String> {
    let extension = path.extension().and_then(|e| e.to_str()).unwrap_or_default().to_lowercase();
    if extension == "grscene" {
        return Scene::load(path).map(|scene| (scene, None)).map_err(|error| format!("No se pudo leer la escena: {error}"));
    }

    // First choice: read and mesh the project here. Only a mesh that covers the whole project is shown as it is.
    let native = Document::load(path).map(|document| {
        let (building, coverage) = build_mesh(&document, false);
        let bounds = building.bounds();
        let (mesh, _) = build_mesh(&document, true);
        (gr_model::scene::to_scene(mesh, bounds), coverage)
    });
    let native = match native {
        Ok((scene, coverage)) if coverage.is_complete() => return Ok((scene, None)),
        other => other,
    };

    let Some(bridge) = find_bridge() else {
        // Without the application, a partial building is still better than nothing, as long as it says so.
        return match native {
            Ok((scene, coverage)) => Ok((
                scene,
                Some(format!(
                    "Vista parcial: faltan {} elementos que este visor aún no genera (instala Guanacaste Render para verlos).",
                    coverage.missing()
                )),
            )),
            Err(error) => Err(format!("No se pudo abrir el proyecto: {error}")),
        };
    };
    convert_with_bridge(&bridge, path).map(|scene| (scene, None))
}

/// Converts a project with the installed application and loads the scene it writes.
fn convert_with_bridge(bridge: &Path, path: &Path) -> Result<Scene, String> {
    let folder = std::env::temp_dir().join("guanacaste-studio");
    std::fs::create_dir_all(&folder).map_err(|error| format!("No se pudo crear la carpeta temporal: {error}"))?;
    let exported = folder.join(format!("{}.grscene", std::process::id()));
    let error_file = PathBuf::from(format!("{}.error.txt", exported.display()));
    let _ = std::fs::remove_file(&error_file);

    let status = Command::new(bridge)
        .arg("--export-scene")
        .arg(path)
        .arg(&exported)
        .status()
        .map_err(|error| format!("No se pudo ejecutar el puente de exportación: {error}"))?;
    if !status.success() {
        let reason = std::fs::read_to_string(&error_file).unwrap_or_default();
        let _ = std::fs::remove_file(&error_file);
        return Err(if reason.trim().is_empty() {
            "El proyecto no se pudo convertir.".to_string()
        } else {
            format!("El proyecto no se pudo convertir: {}", reason.trim())
        });
    }
    let scene = Scene::load(&exported).map_err(|error| format!("La escena exportada no es válida: {error}"));
    let _ = std::fs::remove_file(&exported);
    scene
}

/// The newest installed Guanacaste Render, or the one named by `GUANACASTE_RENDER_BRIDGE`.
fn find_bridge() -> Option<PathBuf> {
    if let Some(explicit) = std::env::var_os("GUANACASTE_RENDER_BRIDGE").map(PathBuf::from) {
        return explicit.is_file().then_some(explicit);
    }
    let root = PathBuf::from(std::env::var_os("LOCALAPPDATA")?).join("Programs").join("Guanacaste Render");
    let mut versions: Vec<(Vec<u32>, PathBuf)> = std::fs::read_dir(root)
        .ok()?
        .flatten()
        .filter_map(|entry| {
            let version: Option<Vec<u32>> = entry.file_name().to_str()?.split('.').map(|part| part.parse().ok()).collect();
            let executable = entry.path().join("GuanacasteRender.exe");
            executable.is_file().then_some((version?, executable))
        })
        .collect();
    versions.sort();
    versions.pop().map(|(_, executable)| executable)
}
