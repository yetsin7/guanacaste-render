//! GPU path tracer: a WGSL compute shader that walks the hierarchy built by `gr-core` and accumulates samples in a
//! storage buffer. It needs no ray-tracing extension, so it runs on any DirectX 12 or Vulkan adapter, integrated
//! graphics included. Hardware ray tracing (DXR / Vulkan ray query) is a later, optional path.
//!
//! What this prototype shades: flat-coloured PBR surfaces (base colour, roughness, metallic, emission), thin glass,
//! direct sun with soft shadows through glass, and an analytic sky. It does not yet read textures, procedural
//! patterns, smooth normals, point lights, HDRI or the sky model of the C# engine.

mod camera;
mod tracer;

pub use camera::CameraFrame;
pub use tracer::{GpuError, GpuOptions, GpuTracer, Mode};
pub use wgpu;
