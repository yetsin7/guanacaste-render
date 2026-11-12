//! UI sub-modules for Guanacaste Render Studio.
//! Structured following the Obsidian Precision CAD design system.

#[path = "environment-panel.rs"]
pub mod environment_panel;
pub mod hud;
#[path = "material-panel.rs"]
pub mod material_panel;
#[path = "mode-rail.rs"]
pub mod mode_rail;
#[path = "render-panel.rs"]
pub mod render_panel;
#[path = "scene-panel.rs"]
pub mod scene_panel;
#[path = "settings-page.rs"]
pub mod settings_page;
#[path = "start-screen.rs"]
pub mod start_screen;
#[path = "top-bar.rs"]
pub mod top_bar;
pub mod widgets;
