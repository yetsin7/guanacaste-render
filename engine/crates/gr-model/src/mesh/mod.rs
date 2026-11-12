//! From a project to triangles.

mod builder;
mod dotnet;
#[path = "element-part.rs"]
mod element_part;
mod elements;
#[path = "interior-test.rs"]
mod interior_test;
mod openings;
mod plants;
#[path = "plumbing-geometry.rs"]
mod plumbing_geometry;
#[path = "plumbing-paths.rs"]
mod plumbing_paths;
#[path = "plumbing-routing.rs"]
mod plumbing_routing;
#[path = "plumbing-specifications.rs"]
mod plumbing_specifications;
#[path = "pitched-roof.rs"]
mod pitched_roof;
mod prism;
#[path = "residential-components.rs"]
mod residential_components;
#[path = "residential-decor.rs"]
mod residential_decor;
#[path = "residential-appliances.rs"]
mod residential_appliances;
#[path = "residential-bedding.rs"]
mod residential_bedding;
#[path = "residential-fixtures.rs"]
mod residential_fixtures;
#[path = "residential-furniture.rs"]
mod residential_furniture;
#[path = "residential-seating.rs"]
mod residential_seating;
#[path = "residential-vehicles.rs"]
mod residential_vehicles;
mod scene_mesh;
mod stairs;
#[path = "terrain-surface.rs"]
mod terrain_surface;
#[path = "terrain-mesh.rs"]
mod terrain_mesh;

pub use builder::{Coverage, build_mesh};
pub use element_part::ElementPart;
pub use scene_mesh::{ElementRange, SceneMesh, SurfaceKind};
