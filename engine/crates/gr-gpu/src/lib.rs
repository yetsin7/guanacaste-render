//! GPU path tracer: a WGSL compute shader that walks the hierarchy built by `gr-core` and accumulates samples in a
//! storage buffer. It needs no ray-tracing extension, so it runs on any DirectX 12 or Vulkan adapter, integrated
//! graphics included. Hardware ray tracing (DXR / Vulkan ray query) is a later, optional path.
//!
//! What this prototype shades: flat-coloured PBR surfaces (base colour, roughness, metallic, emission), thin glass,
//! direct sun with soft shadows through glass, and an analytic sky. It does not yet read textures, procedural
//! patterns, smooth normals, point lights, HDRI or the sky model of the C# engine.

use glam::Vec3;
use gr_core::bvh::{FlatBvh, FlatNode};
use gr_core::scene_file::{Camera, Scene};
use std::fmt;
use wgpu::util::DeviceExt;

#[derive(Debug)]
pub enum GpuError {
    NoAdapter(String),
    Device(String),
    /// The scene needs a buffer larger than the adapter allows.
    TooLarge { needed: u64, limit: u64 },
    Readback(String),
}

impl fmt::Display for GpuError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            GpuError::NoAdapter(reason) => write!(f, "no usable graphics adapter: {reason}"),
            GpuError::Device(reason) => write!(f, "the graphics device could not be created: {reason}"),
            GpuError::TooLarge { needed, limit } => {
                write!(f, "the scene needs a {needed}-byte buffer and the adapter allows {limit}")
            }
            GpuError::Readback(reason) => write!(f, "reading the image back failed: {reason}"),
        }
    }
}

impl std::error::Error for GpuError {}

/// What a render pass computes.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Mode {
    PathTrace = 0,
    /// Distance of the first hit through each pixel centre (-1 for the sky); used to verify traversal.
    PrimaryDistance = 1,
}

#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
#[repr(C)]
struct Uniforms {
    camera_position: [f32; 3],
    frame: u32,
    camera_right: [f32; 3],
    width: u32,
    camera_up: [f32; 3],
    height: u32,
    camera_forward: [f32; 3],
    max_bounces: u32,
    sun_direction: [f32; 3],
    sun_intensity: f32,
    sky_intensity: f32,
    sun_cos_radius: f32,
    seed: u32,
    mode: u32,
}

#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
#[repr(C)]
struct GpuTriangle {
    v0: [f32; 3],
    material: u32,
    e1: [f32; 3],
    original: u32,
    e2: [f32; 3],
    pad: u32,
}

#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
#[repr(C)]
struct GpuMaterial {
    base_color: [f32; 3],
    roughness: f32,
    emission: [f32; 3],
    metallic: f32,
    transmission_tint: [f32; 3],
    transmission: f32,
    ior: f32,
    pad: [f32; 3],
}

/// Camera frame shared by the shader and by CPU code that must shoot the same rays.
#[derive(Clone, Copy, Debug)]
pub struct CameraFrame {
    pub position: Vec3,
    pub forward: Vec3,
    /// Scaled by `tan(fov / 2) * aspect`.
    pub right: Vec3,
    /// Scaled by `tan(fov / 2)`.
    pub up: Vec3,
}

impl CameraFrame {
    pub fn new(camera: &Camera, width: u32, height: u32) -> CameraFrame {
        let forward = (camera.target - camera.position).normalize_or(Vec3::Y);
        let side = forward.cross(Vec3::Z).normalize_or(Vec3::X);
        let half = (camera.vertical_fov_degrees.to_radians() * 0.5).tan();
        CameraFrame {
            position: camera.position,
            forward,
            right: side * half * (width as f32 / height as f32),
            up: side.cross(forward) * half,
        }
    }

    /// Direction through the centre of a pixel; the same formula as `primary_ray` in the shader.
    pub fn pixel_direction(&self, x: u32, y: u32, width: u32, height: u32) -> Vec3 {
        let px = (x as f32 + 0.5) / width as f32 * 2.0 - 1.0;
        let py = 1.0 - (y as f32 + 0.5) / height as f32 * 2.0;
        (self.forward + self.right * px + self.up * py).normalize()
    }
}

/// Adapter name fragment to prefer, e.g. "nvidia" or "intel"; `None` takes the high-performance adapter.
pub struct GpuOptions<'a> {
    pub adapter: Option<&'a str>,
    pub max_bounces: u32,
    pub seed: u32,
}

impl Default for GpuOptions<'_> {
    fn default() -> Self {
        GpuOptions { adapter: None, max_bounces: 3, seed: 0 }
    }
}

pub struct GpuTracer {
    device: wgpu::Device,
    queue: wgpu::Queue,
    pipeline: wgpu::ComputePipeline,
    bind_group: wgpu::BindGroup,
    uniform_buffer: wgpu::Buffer,
    accumulation: wgpu::Buffer,
    counter: wgpu::Buffer,
    uniforms: Uniforms,
    adapter_name: String,
    backend: String,
    scene_bytes: u64,
    frames: u32,
}

impl GpuTracer {
    pub fn new(scene: &Scene, width: u32, height: u32, options: &GpuOptions<'_>) -> Result<GpuTracer, GpuError> {
        let instance = wgpu::Instance::new(wgpu::InstanceDescriptor::new_without_display_handle_from_env());
        let adapter = match options.adapter {
            Some(wanted) => {
                let wanted = wanted.to_lowercase();
                pollster::block_on(instance.enumerate_adapters(wgpu::Backends::all()))
                    .into_iter()
                    .find(|adapter| adapter.get_info().name.to_lowercase().contains(&wanted))
                    .ok_or_else(|| GpuError::NoAdapter(format!("none is named like '{wanted}'")))?
            }
            None => pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
                power_preference: wgpu::PowerPreference::HighPerformance,
                ..Default::default()
            }))
            .map_err(|error| GpuError::NoAdapter(error.to_string()))?,
        };
        let info = adapter.get_info();
        let limits = adapter.limits();
        let (device, queue) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
            label: Some("guanacaste path tracer"),
            required_limits: limits.clone(),
            ..Default::default()
        }))
        .map_err(|error| GpuError::Device(error.to_string()))?;

        let flat = FlatBvh::build(&scene.positions, &scene.indices);
        let triangles: Vec<GpuTriangle> = flat
            .triangle_order
            .iter()
            .map(|&triangle| {
                let [a, b, c] = scene.indices[triangle as usize];
                let v0 = scene.positions[a as usize];
                GpuTriangle {
                    v0: v0.to_array(),
                    material: scene.triangle_material[triangle as usize],
                    e1: (scene.positions[b as usize] - v0).to_array(),
                    original: triangle,
                    e2: (scene.positions[c as usize] - v0).to_array(),
                    pad: 0,
                }
            })
            .collect();
        let materials: Vec<GpuMaterial> = scene
            .materials
            .iter()
            .map(|material| GpuMaterial {
                base_color: material.base_color.to_array(),
                roughness: material.roughness.clamp(0.0, 1.0),
                emission: material.emission.to_array(),
                metallic: material.metallic.clamp(0.0, 1.0),
                transmission_tint: material.transmission_tint.to_array(),
                transmission: material.transmission.clamp(0.0, 1.0),
                ior: material.ior.max(1.0),
                pad: [0.0; 3],
            })
            .collect();

        // Storage buffers cannot be empty; an empty scene still gets one inert element of each kind.
        let storage = |label: &str, bytes: &[u8], minimum: usize| -> Result<wgpu::Buffer, GpuError> {
            let needed = bytes.len().max(minimum) as u64;
            let limit = limits.max_storage_buffer_binding_size.min(limits.max_buffer_size);
            if needed > limit {
                return Err(GpuError::TooLarge { needed, limit });
            }
            let mut padded;
            let contents = if bytes.len() < minimum {
                padded = vec![0u8; minimum];
                padded[..bytes.len()].copy_from_slice(bytes);
                &padded[..]
            } else {
                bytes
            };
            Ok(device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
                label: Some(label),
                contents,
                usage: wgpu::BufferUsages::STORAGE,
            }))
        };
        let node_buffer = storage("nodes", bytemuck::cast_slice(&flat.nodes), size_of::<FlatNode>())?;
        let triangle_buffer = storage("triangles", bytemuck::cast_slice(&triangles), size_of::<GpuTriangle>())?;
        let material_buffer = storage("materials", bytemuck::cast_slice(&materials), size_of::<GpuMaterial>())?;
        let scene_bytes = node_buffer.size() + triangle_buffer.size() + material_buffer.size();
        drop((flat, triangles, materials));

        let accumulation = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("accumulation"),
            size: width as u64 * height as u64 * 16,
            usage: wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_SRC | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let counter = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("ray counter"),
            size: 4,
            usage: wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_SRC | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });

        let frame = CameraFrame::new(&scene.camera, width, height);
        let sun = scene.environment.sun_direction.normalize_or(Vec3::Z);
        let uniforms = Uniforms {
            camera_position: frame.position.to_array(),
            frame: 0,
            camera_right: frame.right.to_array(),
            width,
            camera_up: frame.up.to_array(),
            height,
            camera_forward: frame.forward.to_array(),
            max_bounces: options.max_bounces,
            sun_direction: sun.to_array(),
            sun_intensity: scene.environment.sun_intensity.max(0.0),
            sky_intensity: scene.environment.sky_intensity.max(0.0),
            // Half a degree of angular radius: a slightly enlarged sun, for visibly soft shadow edges.
            sun_cos_radius: 0.5f32.to_radians().cos(),
            seed: options.seed,
            mode: Mode::PathTrace as u32,
        };
        let uniform_buffer = device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: Some("uniforms"),
            contents: bytemuck::bytes_of(&uniforms),
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
        });

        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("path tracer"),
            source: wgpu::ShaderSource::Wgsl(include_str!("../shaders/path_tracer.wgsl").into()),
        });
        let pipeline = device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("path tracer"),
            layout: None,
            module: &module,
            entry_point: Some("main"),
            compilation_options: Default::default(),
            cache: None,
        });
        fn entry(binding: u32, buffer: &wgpu::Buffer) -> wgpu::BindGroupEntry<'_> {
            wgpu::BindGroupEntry { binding, resource: buffer.as_entire_binding() }
        }
        let bind_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("path tracer"),
            layout: &pipeline.get_bind_group_layout(0),
            entries: &[
                entry(0, &uniform_buffer),
                entry(1, &node_buffer),
                entry(2, &triangle_buffer),
                entry(3, &material_buffer),
                entry(4, &accumulation),
                entry(5, &counter),
            ],
        });

        Ok(GpuTracer {
            device,
            queue,
            pipeline,
            bind_group,
            uniform_buffer,
            accumulation,
            counter,
            uniforms,
            adapter_name: info.name,
            backend: format!("{:?}", info.backend),
            scene_bytes,
            frames: 0,
        })
    }

    pub fn adapter_name(&self) -> &str {
        &self.adapter_name
    }

    pub fn backend(&self) -> &str {
        &self.backend
    }

    /// Bytes of video memory holding the hierarchy, triangles and materials.
    pub fn scene_bytes(&self) -> u64 {
        self.scene_bytes
    }

    /// Bytes of video memory holding the image being accumulated.
    pub fn image_bytes(&self) -> u64 {
        self.accumulation.size()
    }

    pub fn width(&self) -> u32 {
        self.uniforms.width
    }

    pub fn height(&self) -> u32 {
        self.uniforms.height
    }

    /// Samples accumulated per pixel since the last [`GpuTracer::reset`].
    pub fn samples(&self) -> u32 {
        self.frames
    }

    /// Clears the image and the ray counter and selects what the next passes compute.
    pub fn reset(&mut self, mode: Mode) {
        self.uniforms.mode = mode as u32;
        self.frames = 0;
        let mut encoder = self.device.create_command_encoder(&Default::default());
        encoder.clear_buffer(&self.accumulation, 0, None);
        encoder.clear_buffer(&self.counter, 0, None);
        self.queue.submit([encoder.finish()]);
    }

    /// Queues `passes` samples per pixel. Returns immediately; [`GpuTracer::wait`] blocks until they are done.
    pub fn render(&mut self, passes: u32) {
        for _ in 0..passes {
            self.uniforms.frame = self.frames;
            self.queue.write_buffer(&self.uniform_buffer, 0, bytemuck::bytes_of(&self.uniforms));
            let mut encoder = self.device.create_command_encoder(&Default::default());
            {
                let mut pass = encoder.begin_compute_pass(&Default::default());
                pass.set_pipeline(&self.pipeline);
                pass.set_bind_group(0, &self.bind_group, &[]);
                pass.dispatch_workgroups(self.uniforms.width.div_ceil(8), self.uniforms.height.div_ceil(8), 1);
            }
            self.queue.submit([encoder.finish()]);
            self.frames += 1;
            if self.frames % 8 == 0 {
                // Let the driver retire finished work so a long batch does not pile up in the queue.
                let _ = self.device.poll(wgpu::PollType::Poll);
            }
        }
    }

    /// Blocks until every queued pass has finished on the GPU.
    pub fn wait(&self) -> Result<(), GpuError> {
        self.device.poll(wgpu::PollType::wait_indefinitely()).map(|_| ()).map_err(|error| GpuError::Device(error.to_string()))
    }

    /// Rays traced since the last reset. The counter is 32 bits wide; reset before it can wrap.
    pub fn rays(&self) -> Result<u32, GpuError> {
        let bytes = self.read(&self.counter)?;
        Ok(u32::from_le_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]))
    }

    /// The accumulated image, one `[r, g, b, samples]` per pixel in row order. Divide the colour by `samples`.
    pub fn read_accumulation(&self) -> Result<Vec<[f32; 4]>, GpuError> {
        Ok(bytemuck::pod_collect_to_vec(&self.read(&self.accumulation)?))
    }

    fn read(&self, source: &wgpu::Buffer) -> Result<Vec<u8>, GpuError> {
        let staging = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("readback"),
            size: source.size(),
            usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let mut encoder = self.device.create_command_encoder(&Default::default());
        encoder.copy_buffer_to_buffer(source, 0, &staging, 0, source.size());
        self.queue.submit([encoder.finish()]);
        let (sender, receiver) = std::sync::mpsc::channel();
        staging.slice(..).map_async(wgpu::MapMode::Read, move |result| {
            let _ = sender.send(result);
        });
        self.wait()?;
        receiver
            .recv()
            .map_err(|error| GpuError::Readback(error.to_string()))?
            .map_err(|error| GpuError::Readback(error.to_string()))?;
        let bytes = staging.slice(..).get_mapped_range().map_err(|error| GpuError::Readback(error.to_string()))?.to_vec();
        staging.unmap();
        Ok(bytes)
    }
}
