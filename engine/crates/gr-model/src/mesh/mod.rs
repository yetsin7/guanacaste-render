//! From a project to triangles.

mod builder;
mod dotnet;
mod openings;
mod plants;
mod prism;
mod scene_mesh;

pub use builder::{Coverage, build_mesh};
pub use scene_mesh::{ElementRange, SceneMesh, SurfaceKind};
