//! Settings the studio keeps between sessions, in `%LOCALAPPDATA%\GuanacasteStudio\preferences.json`.

use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct Preferences {
    /// Quality profile: 0 Económico, 1 Equilibrado, 2 Alto, 3 Ultra; `None` follows the suggestion.
    pub tier: Option<u8>,
    pub denoise: bool,
    /// Waits for the screen's refresh; takes effect at the next start.
    pub vsync: bool,
    pub preview_scale: f32,
    pub preview_samples: u32,
    pub render_samples: u32,
    pub bounces: u32,
}

impl Default for Preferences {
    fn default() -> Preferences {
        Preferences {
            tier: None,
            denoise: true,
            vsync: true,
            preview_scale: 0.6,
            preview_samples: 64,
            render_samples: 512,
            bounces: 3,
        }
    }
}

fn location() -> Option<PathBuf> {
    Some(
        PathBuf::from(std::env::var_os("LOCALAPPDATA")?)
            .join("GuanacasteStudio")
            .join("preferences.json"),
    )
}

impl Preferences {
    pub fn load() -> Preferences {
        location()
            .and_then(|path| std::fs::read(path).ok())
            .and_then(|bytes| serde_json::from_slice(&bytes).ok())
            .unwrap_or_default()
    }

    /// Saves the preferences; a failure is not worth interrupting the user for.
    pub fn save(&self) {
        let Some(path) = location() else { return };
        if let Some(folder) = path.parent() {
            let _ = std::fs::create_dir_all(folder);
        }
        if let Ok(json) = serde_json::to_vec_pretty(self) {
            let temporary = path.with_extension("json.tmp");
            if std::fs::write(&temporary, json).is_ok() {
                let _ = std::fs::rename(&temporary, &path);
            }
        }
    }
}
