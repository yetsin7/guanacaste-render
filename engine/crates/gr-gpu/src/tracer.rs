//! The path tracer on a wgpu device: scene upload, sample passes, the on-screen picture and readback.

use crate::camera::CameraFrame;
use glam::Vec3;
use gr_core::bvh::{FlatBvh, FlatNode};
use gr_core::scene_file::Scene;
use std::fmt;
use std::sync::Arc;
use std::sync::atomic::{AtomicU32, Ordering};
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
struct ResolveParams {
    width: u32,
    height: u32,
    exposure: f32,
    pad: f32,
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

/// Buffers and views that depend on the image size.
struct Target {
    accumulation: wgpu::Buffer,
    display: wgpu::Texture,
    display_view: wgpu::TextureView,
    trace_bind_group: wgpu::BindGroup,
    resolve_bind_group: wgpu::BindGroup,
}

pub struct GpuTracer {
    device: wgpu::Device,
    queue: wgpu::Queue,
    trace_pipeline: wgpu::ComputePipeline,
    resolve_pipeline: wgpu::ComputePipeline,
    uniform_buffer: wgpu::Buffer,
    resolve_buffer: wgpu::Buffer,
    nodes: wgpu::Buffer,
    triangles: wgpu::Buffer,
    materials: wgpu::Buffer,
    counter: wgpu::Buffer,
    target: Target,
    uniforms: Uniforms,
    adapter_name: String,
    backend: String,
    frames: u32,
    /// Sample passes submitted and not yet finished by the GPU.
    in_flight: Arc<AtomicU32>,
}

impl GpuTracer {
    /// Creates its own device, for rendering without a window.
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
        let (device, queue) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
            label: Some("guanacaste path tracer"),
            required_limits: adapter.limits(),
            ..Default::default()
        }))
        .map_err(|error| GpuError::Device(error.to_string()))?;
        GpuTracer::with_device(device, queue, &adapter.get_info(), scene, width, height, options)
    }

    /// Uses a device that already exists, such as the one a window draws with, so the picture can be shown without
    /// leaving the GPU.
    pub fn with_device(
        device: wgpu::Device,
        queue: wgpu::Queue,
        adapter: &wgpu::AdapterInfo,
        scene: &Scene,
        width: u32,
        height: u32,
        options: &GpuOptions<'_>,
    ) -> Result<GpuTracer, GpuError> {
        let limits = device.limits();
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
        let nodes = storage("nodes", bytemuck::cast_slice(&flat.nodes), size_of::<FlatNode>())?;
        let triangle_buffer = storage("triangles", bytemuck::cast_slice(&triangles), size_of::<GpuTriangle>())?;
        let material_buffer = storage("materials", bytemuck::cast_slice(&materials), size_of::<GpuMaterial>())?;
        drop((flat, triangles, materials));

        let counter = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("ray counter"),
            size: 4,
            usage: wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_SRC | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });

        let (width, height) = (width.max(1), height.max(1));
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
        let resolve_buffer = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("resolve parameters"),
            size: size_of::<ResolveParams>() as u64,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });

        let pipeline = |label: &str, source: &str| {
            let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
                label: Some(label),
                source: wgpu::ShaderSource::Wgsl(source.into()),
            });
            device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
                label: Some(label),
                layout: None,
                module: &module,
                entry_point: Some("main"),
                compilation_options: Default::default(),
                cache: None,
            })
        };
        let trace_pipeline = pipeline("path tracer", include_str!("../shaders/path_tracer.wgsl"));
        let resolve_pipeline = pipeline("resolve", include_str!("../shaders/resolve.wgsl"));

        let target = Target::new(
            &device,
            &trace_pipeline,
            &resolve_pipeline,
            [&uniform_buffer, &nodes, &triangle_buffer, &material_buffer, &counter, &resolve_buffer],
            width,
            height,
        );
        Ok(GpuTracer {
            device,
            queue,
            trace_pipeline,
            resolve_pipeline,
            uniform_buffer,
            resolve_buffer,
            nodes,
            triangles: triangle_buffer,
            materials: material_buffer,
            counter,
            target,
            uniforms,
            adapter_name: adapter.name.clone(),
            backend: format!("{:?}", adapter.backend),
            frames: 0,
            in_flight: Arc::new(AtomicU32::new(0)),
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
        self.nodes.size() + self.triangles.size() + self.materials.size()
    }

    /// Bytes of video memory holding the image being accumulated and the picture shown.
    pub fn image_bytes(&self) -> u64 {
        self.target.accumulation.size() + self.uniforms.width as u64 * self.uniforms.height as u64 * 4
    }

    pub fn width(&self) -> u32 {
        self.uniforms.width
    }

    pub fn height(&self) -> u32 {
        self.uniforms.height
    }

    /// Samples accumulated per pixel since the image was last cleared.
    pub fn samples(&self) -> u32 {
        self.frames
    }

    /// Sample passes queued on the GPU that have not finished yet.
    pub fn pending_passes(&self) -> u32 {
        self.in_flight.load(Ordering::Relaxed)
    }

    /// Clears the image and the ray counter and selects what the next passes compute.
    pub fn reset(&mut self, mode: Mode) {
        self.uniforms.mode = mode as u32;
        self.clear();
    }

    fn clear(&mut self) {
        self.frames = 0;
        let mut encoder = self.device.create_command_encoder(&Default::default());
        encoder.clear_buffer(&self.target.accumulation, 0, None);
        encoder.clear_buffer(&self.counter, 0, None);
        self.queue.submit([encoder.finish()]);
    }

    /// Moves the camera and starts the image again.
    pub fn set_camera(&mut self, frame: &CameraFrame) {
        self.uniforms.camera_position = frame.position.to_array();
        self.uniforms.camera_forward = frame.forward.to_array();
        self.uniforms.camera_right = frame.right.to_array();
        self.uniforms.camera_up = frame.up.to_array();
        self.clear();
    }

    /// Changes the daylight and starts the image again. `sun_direction` points from the scene towards the sun.
    pub fn set_daylight(&mut self, sun_direction: Vec3, sun_intensity: f32, sky_intensity: f32) {
        self.uniforms.sun_direction = sun_direction.normalize_or(Vec3::Z).to_array();
        self.uniforms.sun_intensity = sun_intensity.max(0.0);
        self.uniforms.sky_intensity = sky_intensity.max(0.0);
        self.clear();
    }

    pub fn set_max_bounces(&mut self, bounces: u32) {
        self.uniforms.max_bounces = bounces.min(16);
        self.clear();
    }

    /// Changes the image size, keeping the scene on the GPU, and starts the image again.
    pub fn resize(&mut self, width: u32, height: u32) {
        let (width, height) = (width.max(1), height.max(1));
        if (width, height) == (self.uniforms.width, self.uniforms.height) {
            return;
        }
        self.uniforms.width = width;
        self.uniforms.height = height;
        self.target = Target::new(
            &self.device,
            &self.trace_pipeline,
            &self.resolve_pipeline,
            [&self.uniform_buffer, &self.nodes, &self.triangles, &self.materials, &self.counter, &self.resolve_buffer],
            width,
            height,
        );
        self.frames = 0;
        let mut encoder = self.device.create_command_encoder(&Default::default());
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
                pass.set_pipeline(&self.trace_pipeline);
                pass.set_bind_group(0, &self.target.trace_bind_group, &[]);
                pass.dispatch_workgroups(self.uniforms.width.div_ceil(8), self.uniforms.height.div_ceil(8), 1);
            }
            self.queue.submit([encoder.finish()]);
            self.in_flight.fetch_add(1, Ordering::Relaxed);
            let in_flight = self.in_flight.clone();
            self.queue.on_submitted_work_done(move || {
                in_flight.fetch_sub(1, Ordering::Relaxed);
            });
            self.frames += 1;
            if self.frames.is_multiple_of(8) {
                // Let the driver retire finished work so a long batch does not pile up in the queue.
                let _ = self.device.poll(wgpu::PollType::Poll);
            }
        }
    }

    /// Writes the picture for the screen from what has been accumulated so far.
    pub fn resolve(&self, exposure: f32) {
        let params = ResolveParams { width: self.uniforms.width, height: self.uniforms.height, exposure, pad: 0.0 };
        self.queue.write_buffer(&self.resolve_buffer, 0, bytemuck::bytes_of(&params));
        let mut encoder = self.device.create_command_encoder(&Default::default());
        {
            let mut pass = encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&self.resolve_pipeline);
            pass.set_bind_group(0, &self.target.resolve_bind_group, &[]);
            pass.dispatch_workgroups(self.uniforms.width.div_ceil(8), self.uniforms.height.div_ceil(8), 1);
        }
        self.queue.submit([encoder.finish()]);
    }

    /// The picture written by [`GpuTracer::resolve`], as sRGB-encoded bytes in a plain 8-bit texture. It is replaced by
    /// [`GpuTracer::resize`].
    pub fn display_view(&self) -> &wgpu::TextureView {
        &self.target.display_view
    }

    /// The picture written by [`GpuTracer::resolve`] as tightly packed RGBA bytes, for saving to a file.
    pub fn read_display(&self) -> Result<Vec<u8>, GpuError> {
        let (width, height) = (self.uniforms.width, self.uniforms.height);
        // Rows of a texture copy are padded to a multiple of 256 bytes.
        let stride = (width * 4).div_ceil(wgpu::COPY_BYTES_PER_ROW_ALIGNMENT) * wgpu::COPY_BYTES_PER_ROW_ALIGNMENT;
        let staging = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("picture readback"),
            size: stride as u64 * height as u64,
            usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let mut encoder = self.device.create_command_encoder(&Default::default());
        encoder.copy_texture_to_buffer(
            self.target.display.as_image_copy(),
            wgpu::TexelCopyBufferInfo {
                buffer: &staging,
                layout: wgpu::TexelCopyBufferLayout { offset: 0, bytes_per_row: Some(stride), rows_per_image: Some(height) },
            },
            wgpu::Extent3d { width, height, depth_or_array_layers: 1 },
        );
        self.queue.submit([encoder.finish()]);
        let padded = self.map(&staging)?;
        let mut pixels = Vec::with_capacity((width * height * 4) as usize);
        for row in padded.chunks(stride as usize) {
            pixels.extend_from_slice(&row[..(width * 4) as usize]);
        }
        Ok(pixels)
    }

    /// Blocks until every queued pass has finished on the GPU.
    pub fn wait(&self) -> Result<(), GpuError> {
        self.device.poll(wgpu::PollType::wait_indefinitely()).map(|_| ()).map_err(|error| GpuError::Device(error.to_string()))
    }

    /// Rays traced since the image was last cleared. The counter is 32 bits wide; clear before it can wrap.
    pub fn rays(&self) -> Result<u32, GpuError> {
        let bytes = self.read(&self.counter)?;
        Ok(u32::from_le_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]))
    }

    /// The accumulated image, one `[r, g, b, samples]` per pixel in row order. Divide the colour by `samples`.
    pub fn read_accumulation(&self) -> Result<Vec<[f32; 4]>, GpuError> {
        Ok(bytemuck::pod_collect_to_vec(&self.read(&self.target.accumulation)?))
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
        self.map(&staging)
    }

    fn map(&self, staging: &wgpu::Buffer) -> Result<Vec<u8>, GpuError> {
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

impl Target {
    /// `buffers` are, in order: uniforms, nodes, triangles, materials, ray counter, resolve parameters.
    fn new(
        device: &wgpu::Device,
        trace: &wgpu::ComputePipeline,
        resolve: &wgpu::ComputePipeline,
        buffers: [&wgpu::Buffer; 6],
        width: u32,
        height: u32,
    ) -> Target {
        let [uniforms, nodes, triangles, materials, counter, resolve_params] = buffers;
        let accumulation = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("accumulation"),
            size: width as u64 * height as u64 * 16,
            usage: wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_SRC | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        // The shader stores sRGB-encoded bytes and the window samples them as they are (egui composes in gamma
        // space), so both views keep the plain format. Each view declares the one use it has.
        let display = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("picture"),
            size: wgpu::Extent3d { width, height, depth_or_array_layers: 1 },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Rgba8Unorm,
            usage: wgpu::TextureUsages::STORAGE_BINDING | wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_SRC,
            view_formats: &[],
        });
        let storage_view = display.create_view(&wgpu::TextureViewDescriptor {
            usage: Some(wgpu::TextureUsages::STORAGE_BINDING),
            ..Default::default()
        });
        let display_view = display.create_view(&wgpu::TextureViewDescriptor {
            usage: Some(wgpu::TextureUsages::TEXTURE_BINDING),
            ..Default::default()
        });

        fn entry(binding: u32, buffer: &wgpu::Buffer) -> wgpu::BindGroupEntry<'_> {
            wgpu::BindGroupEntry { binding, resource: buffer.as_entire_binding() }
        }
        let trace_bind_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("path tracer"),
            layout: &trace.get_bind_group_layout(0),
            entries: &[
                entry(0, uniforms),
                entry(1, nodes),
                entry(2, triangles),
                entry(3, materials),
                entry(4, &accumulation),
                entry(5, counter),
            ],
        });
        let resolve_bind_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("resolve"),
            layout: &resolve.get_bind_group_layout(0),
            entries: &[
                entry(0, resolve_params),
                entry(1, &accumulation),
                wgpu::BindGroupEntry { binding: 2, resource: wgpu::BindingResource::TextureView(&storage_view) },
            ],
        });
        Target { accumulation, display, display_view, trace_bind_group, resolve_bind_group }
    }
}
