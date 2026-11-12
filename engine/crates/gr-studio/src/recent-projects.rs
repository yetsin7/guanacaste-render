//! Local history of projects opened successfully in the studio.

use std::path::{Path, PathBuf};

/// A recent project's display information and original file location.
#[derive(Clone, Debug)]
pub struct RecentProject {
    pub name: String,
    pub path: PathBuf,
    pub file_size_mb: Option<f64>,
}

/// Finds a user-specific configuration folder without placing project paths in the repository.
fn storage_folder() -> Option<PathBuf> {
    std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("XDG_CONFIG_HOME").map(PathBuf::from))
        .or_else(|| std::env::var_os("HOME").map(|home| PathBuf::from(home).join(".config")))
        .map(|folder| folder.join("guanacaste-render-studio"))
}

/// Reads a history file only when its JSON and path entries are valid.
fn read_paths(path: &Path) -> Option<Vec<PathBuf>> {
    let bytes = std::fs::read(path).ok()?;
    serde_json::from_slice(&bytes).ok()
}

/// Builds display metadata without discarding projects on temporarily unavailable drives.
fn describe(path: PathBuf) -> RecentProject {
    RecentProject {
        name: path.file_name().map(|name| name.to_string_lossy().into_owned()).unwrap_or_default(),
        file_size_mb: std::fs::metadata(&path).ok().map(|metadata| metadata.len() as f64 / 1_048_576.0),
        path,
    }
}

/// Loads the last valid history, recovering its backup if an interrupted write damaged the main file.
pub fn load() -> Vec<RecentProject> {
    let Some(folder) = storage_folder() else { return Vec::new() };
    load_from(&folder)
}

/// Reads the primary and backup lists from one configuration folder.
fn load_from(folder: &Path) -> Vec<RecentProject> {
    read_paths(&folder.join("recent-projects.json"))
        .or_else(|| read_paths(&folder.join("recent-projects.backup.json")))
        .unwrap_or_default()
        .into_iter()
        .take(16)
        .map(describe)
        .collect()
}

/// Moves a successfully loaded project to the front and records the list for later sessions.
pub fn remember(projects: &mut Vec<RecentProject>, path: &Path) {
    projects.retain(|project| project.path != path);
    projects.insert(0, describe(path.to_path_buf()));
    projects.truncate(16);
    if let Err(error) = persist(projects) {
        eprintln!("Could not save the recent project list: {error}");
    }
}

/// Preserves the previous valid list before writing, so an interrupted update is recoverable.
fn persist(projects: &[RecentProject]) -> Result<(), Box<dyn std::error::Error>> {
    let Some(folder) = storage_folder() else { return Ok(()) };
    persist_to(&folder, projects)
}

/// Writes history to a known folder while preserving its last valid version.
fn persist_to(folder: &Path, projects: &[RecentProject]) -> Result<(), Box<dyn std::error::Error>> {
    let paths: Vec<&Path> = projects.iter().map(|project| project.path.as_path()).collect();
    let bytes = serde_json::to_vec(&paths)?;
    std::fs::create_dir_all(&folder)?;
    let current = folder.join("recent-projects.json");
    if read_paths(&current).is_some() {
        std::fs::copy(&current, folder.join("recent-projects.backup.json"))?;
    }
    std::fs::write(current, bytes)?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{describe, load_from, persist_to};
    use std::path::PathBuf;

    /// Recovers the previous list after an interrupted or corrupted history write.
    #[test]
    fn recovers_the_previous_valid_history() {
        let folder = std::env::temp_dir().join(format!(
            "guanacaste-history-test-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        let first = describe(PathBuf::from("first-project.gr"));
        persist_to(&folder, &[first]).unwrap();
        let second = describe(PathBuf::from("second-project.gr"));
        persist_to(&folder, &[second]).unwrap();
        std::fs::write(folder.join("recent-projects.json"), b"{").unwrap();
        let recovered = load_from(&folder);
        assert_eq!(recovered.len(), 1);
        assert_eq!(recovered[0].path, PathBuf::from("first-project.gr"));
        std::fs::remove_file(folder.join("recent-projects.json")).unwrap();
        std::fs::remove_file(folder.join("recent-projects.backup.json")).unwrap();
        std::fs::remove_dir(folder).unwrap();
    }
}
