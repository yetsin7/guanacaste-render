//! Guanacaste projects without the C# application: read a `.gr` or `.g3d` file, mesh the building and hand the
//! result to the render engine.
//!
//! The mesher is a port of the application's, verified against it triangle by triangle (`tests/reference.rs`).
//! It does not cover every element kind yet; [`mesh::Coverage`] says what a given project still needs.

pub mod container;
pub mod document;
pub mod edit;
pub mod mesh;
pub mod plan;
pub mod scene;
