//! Opening what the studio can show: `.grscene` files directly, and projects (`.gr`, `.g3d`) through the bridge.
//!
//! The bridge is the installed C# application run without a window (`GuanacasteRender.exe --export-scene`). It is
//! temporary: it goes away when the engine reads and meshes projects itself.

use gr_core::scene_file::Scene;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::mpsc::{Receiver, channel};

/// A scene ready to show, or the reason it could not be opened, in words for the user.
pub type Loaded = Result<(Scene, PathBuf), String>;

/// File extensions the open dialog offers.
pub const PROJECT_EXTENSIONS: [&str; 3] = ["gr", "g3d", "grscene"];

/// Loads `path` on another thread so the window keeps responding; the result arrives through the channel.
pub fn load_in_background(path: PathBuf, wake: impl Fn() + Send + 'static) -> Receiver<Loaded> {
    let (sender, receiver) = channel();
    std::thread::spawn(move || {
        let result = load(&path).map(|scene| (scene, path));
        let _ = sender.send(result);
        wake();
    });
    receiver
}

fn load(path: &Path) -> Result<Scene, String> {
    let extension = path.extension().and_then(|e| e.to_str()).unwrap_or_default().to_lowercase();
    if extension == "grscene" {
        return Scene::load(path).map_err(|error| format!("No se pudo leer la escena: {error}"));
    }
    let bridge = find_bridge().ok_or_else(|| {
        "Para abrir proyectos .gr hace falta Guanacaste Render instalado (el puente de exportación no se encontró).".to_string()
    })?;
    let folder = std::env::temp_dir().join("guanacaste-studio");
    std::fs::create_dir_all(&folder).map_err(|error| format!("No se pudo crear la carpeta temporal: {error}"))?;
    let exported = folder.join(format!("{}.grscene", std::process::id()));
    let error_file = PathBuf::from(format!("{}.error.txt", exported.display()));
    let _ = std::fs::remove_file(&error_file);

    let status = Command::new(&bridge)
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
