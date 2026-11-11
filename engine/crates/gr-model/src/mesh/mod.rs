//! From a project to triangles.

mod builder;
mod openings;
mod prism;
mod scene_mesh;

pub use builder::{Coverage, build_mesh};
pub use scene_mesh::{ElementRange, SceneMesh, SurfaceKind};
