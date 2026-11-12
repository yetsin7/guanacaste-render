//! The navigation view: the scene drawn by rasterisation instead of ray tracing, for moving around a project at
//! the refresh rate of the screen. Shares the path tracer's scene buffers, so it adds only its own image.

use super::{GpuMaterial, GpuTracer};
use crate::camera::CameraFrame;
use gr_core::scene_file::Material;

/// Multisampling of the navigation view: four samples per pixel smooth the edges of thin members.
const SAMPLES: u32 = 4;
/// Distance to the near plane, in metres. Reversed depth keeps precision from here to the horizon.
const NEAR: f32 = 0.05;
const COLOR: wgpu::TextureFormat = wgpu::TextureFormat::Rgba8Unorm;
const DEPTH: wgpu::TextureFormat = wgpu::TextureFormat::Depth32Float;

/// The parameters of `raster.wgsl`; the layout of `View` there.
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
#[repr(C)]
struct ViewUniforms {
    position: [f32; 3],
    near: f32,
    right: [f32; 3],
    exposure: f32,
    up: [f32; 3],
    sky_intensity: f32,
    forward: [f32; 3],
    sun_intensity: f32,
    sun_direction: [f32; 3],
    sun_cos_radius: f32,
}

/// Pipelines and the image they draw into; created on the first navigation frame.
pub(super) struct Raster {
    sky: wgpu::RenderPipeline,
    opaque: wgpu::RenderPipeline,
    glass: wgpu::RenderPipeline,
    uniforms: wgpu::Buffer,
    bind_group: wgpu::BindGroup,
    target: Option<RasterTarget>,
}

struct RasterTarget {
    width: u32,
    height: u32,
    color: wgpu::TextureView,
    depth: wgpu::TextureView,
    picture: wgpu::Texture,
    picture_view: wgpu::TextureView,
}

/// What [`GpuTracer::rasterize`] drew.
pub struct RasterFrame<'a> {
    /// The picture as sRGB-encoded bytes, ready to be shown as it is.
    pub view: &'a wgpu::TextureView,
    /// True when the picture is a new texture (first frame or new size): a window must register it again.
    pub replaced: bool,
}

/// Whether an adapter can draw the navigation view: it reads the scene from storage buffers in the vertex stage.
/// Every DirectX 12 and Vulkan adapter can; some OpenGL ones cannot.
pub fn raster_supported(adapter: &wgpu::Adapter) -> bool {
    adapter
        .get_downlevel_capabilities()
        .flags
        .contains(wgpu::DownlevelFlags::VERTEX_STORAGE)
}

impl Raster {
    fn new(tracer: &GpuTracer) -> Raster {
        let device = &tracer.device;
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("navigation view"),
            source: wgpu::ShaderSource::Wgsl(include_str!("../shaders/raster.wgsl").into()),
        });
        let uniforms = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("navigation view parameters"),
            size: size_of::<ViewUniforms>() as u64,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let visible = wgpu::ShaderStages::VERTEX | wgpu::ShaderStages::FRAGMENT;
        let storage = |binding| wgpu::BindGroupLayoutEntry {
            binding,
            visibility: visible,
            ty: wgpu::BindingType::Buffer {
                ty: wgpu::BufferBindingType::Storage { read_only: true },
                has_dynamic_offset: false,
                min_binding_size: None,
            },
            count: None,
        };
        let layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("navigation view"),
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: visible,
                    ty: wgpu::BindingType::Buffer {
                        ty: wgpu::BufferBindingType::Uniform,
                        has_dynamic_offset: false,
                        min_binding_size: None,
                    },
                    count: None,
                },
                storage(1),
                storage(2),
                storage(3),
            ],
        });
        let bind_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("navigation view"),
            layout: &layout,
            entries: &[
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: uniforms.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: tracer.triangles.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 2,
                    resource: tracer.materials.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 3,
                    resource: tracer.corner_normals.as_entire_binding(),
                },
            ],
        });
        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("navigation view"),
            bind_group_layouts: &[Some(&layout)],
            immediate_size: 0,
        });
        let pipeline = |label: &str,
                        vertex: &str,
                        fragment: &str,
                        blend: Option<wgpu::BlendState>,
                        depth_write: bool| {
            device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
                label: Some(label),
                layout: Some(&pipeline_layout),
                vertex: wgpu::VertexState {
                    module: &module,
                    entry_point: Some(vertex),
                    compilation_options: Default::default(),
                    buffers: &[],
                },
                primitive: wgpu::PrimitiveState {
                    topology: wgpu::PrimitiveTopology::TriangleList,
                    // Triangles of a project are not wound consistently; both sides are drawn.
                    cull_mode: None,
                    ..Default::default()
                },
                depth_stencil: Some(wgpu::DepthStencilState {
                    format: DEPTH,
                    depth_write_enabled: Some(depth_write),
                    depth_compare: Some(wgpu::CompareFunction::GreaterEqual),
                    stencil: Default::default(),
                    bias: Default::default(),
                }),
                multisample: wgpu::MultisampleState {
                    count: SAMPLES,
                    ..Default::default()
                },
                fragment: Some(wgpu::FragmentState {
                    module: &module,
                    entry_point: Some(fragment),
                    compilation_options: Default::default(),
                    targets: &[Some(wgpu::ColorTargetState {
                        format: COLOR,
                        blend,
                        write_mask: wgpu::ColorWrites::ALL,
                    })],
                }),
                multiview_mask: None,
                cache: None,
            })
        };
        Raster {
            // The sky is drawn first at depth 0, the far end of reversed depth, and everything else covers it.
            sky: pipeline("navigation sky", "vs_sky", "fs_sky", None, false),
            opaque: pipeline("navigation surfaces", "vs_main", "fs_opaque", None, true),
            glass: pipeline(
                "navigation glass",
                "vs_main",
                "fs_glass",
                Some(wgpu::BlendState::ALPHA_BLENDING),
                false,
            ),
            uniforms,
            bind_group,
            target: None,
        }
    }
}

impl RasterTarget {
    fn new(device: &wgpu::Device, width: u32, height: u32) -> RasterTarget {
        let size = wgpu::Extent3d {
            width,
            height,
            depth_or_array_layers: 1,
        };
        let texture = |label: &str, format, samples, usage| {
            device.create_texture(&wgpu::TextureDescriptor {
                label: Some(label),
                size,
                mip_level_count: 1,
                sample_count: samples,
                dimension: wgpu::TextureDimension::D2,
                format,
                usage,
                view_formats: &[],
            })
        };
        let color = texture(
            "navigation samples",
            COLOR,
            SAMPLES,
            wgpu::TextureUsages::RENDER_ATTACHMENT,
        );
        let depth = texture(
            "navigation depth",
            DEPTH,
            SAMPLES,
            wgpu::TextureUsages::RENDER_ATTACHMENT,
        );
        let picture = texture(
            "navigation picture",
            COLOR,
            1,
            wgpu::TextureUsages::RENDER_ATTACHMENT
                | wgpu::TextureUsages::TEXTURE_BINDING
                | wgpu::TextureUsages::COPY_SRC,
        );
        RasterTarget {
            width,
            height,
            color: color.create_view(&Default::default()),
            depth: depth.create_view(&Default::default()),
            picture_view: picture.create_view(&Default::default()),
            picture,
        }
    }
}

impl GpuTracer {
    /// Draws the navigation view of `frame` at `width` x `height` and returns the picture. No ray is traced and
    /// the path-traced image is left as it is. The picture lives on the GPU until the next call at another size.
    pub fn rasterize(
        &mut self,
        frame: &CameraFrame,
        width: u32,
        height: u32,
        exposure: f32,
    ) -> RasterFrame<'_> {
        let (width, height) = (width.max(1), height.max(1));
        if self.raster.is_none() {
            self.raster = Some(Raster::new(self));
        }
        let device = self.device.clone();
        let raster = self.raster.as_mut().expect("created above");
        let replaced = raster
            .target
            .as_ref()
            .is_none_or(|target| (target.width, target.height) != (width, height));
        if replaced {
            raster.target = Some(RasterTarget::new(&device, width, height));
        }
        let view = ViewUniforms {
            position: frame.position.to_array(),
            near: NEAR,
            right: frame.right.to_array(),
            exposure,
            up: frame.up.to_array(),
            sky_intensity: self.uniforms.sky_intensity,
            forward: frame.forward.to_array(),
            sun_intensity: self.uniforms.sun_intensity,
            sun_direction: self.uniforms.sun_direction,
            sun_cos_radius: self.uniforms.sun_cos_radius,
        };
        self.queue
            .write_buffer(&raster.uniforms, 0, bytemuck::bytes_of(&view));
        let target = raster.target.as_ref().expect("created above");
        let vertices = (self.triangles.size() / size_of::<super::GpuTriangle>() as u64 * 3) as u32;
        let mut encoder = device.create_command_encoder(&Default::default());
        {
            let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("navigation view"),
                color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                    view: &target.color,
                    depth_slice: None,
                    resolve_target: Some(&target.picture_view),
                    ops: wgpu::Operations {
                        load: wgpu::LoadOp::Clear(wgpu::Color::BLACK),
                        store: wgpu::StoreOp::Discard,
                    },
                })],
                depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
                    view: &target.depth,
                    depth_ops: Some(wgpu::Operations {
                        load: wgpu::LoadOp::Clear(0.0),
                        store: wgpu::StoreOp::Discard,
                    }),
                    stencil_ops: None,
                }),
                timestamp_writes: None,
                occlusion_query_set: None,
                multiview_mask: None,
            });
            pass.set_bind_group(0, &raster.bind_group, &[]);
            pass.set_pipeline(&raster.sky);
            pass.draw(0..3, 0..1);
            pass.set_pipeline(&raster.opaque);
            pass.draw(0..vertices, 0..1);
            pass.set_pipeline(&raster.glass);
            pass.draw(0..vertices, 0..1);
        }
        self.queue.submit([encoder.finish()]);
        RasterFrame {
            view: &target.picture_view,
            replaced,
        }
    }

    /// The navigation picture last drawn, as tightly packed RGBA bytes; `None` before the first one.
    pub fn read_raster(&self) -> Result<Option<Vec<u8>>, super::GpuError> {
        let Some(target) = self
            .raster
            .as_ref()
            .and_then(|raster| raster.target.as_ref())
        else {
            return Ok(None);
        };
        self.read_texture(&target.picture, target.width, target.height)
            .map(Some)
    }

    /// Replaces one material of the scene, for both the navigation view and the path tracer, and starts the
    /// path-traced image again. Indices outside the scene's materials are ignored.
    pub fn set_material(&mut self, index: usize, material: &Material) {
        let stride = size_of::<GpuMaterial>() as u64;
        if (index as u64 + 1) * stride > self.materials.size() {
            return;
        }
        let gpu = GpuMaterial::from(material);
        self.queue.write_buffer(
            &self.materials,
            index as u64 * stride,
            bytemuck::bytes_of(&gpu),
        );
        self.clear();
    }
}
