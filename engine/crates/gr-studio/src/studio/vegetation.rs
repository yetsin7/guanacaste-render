//! The nature library: the plant species the engine grows, their specimen renders and placing them in a scene.
//!
//! Every plant is the engine's procedural vegetation (`gr_model::mesh::add_plant`), the same that projects use.
//! Thumbnails are path-traced on the window's graphics card, on another thread, the first time the library is
//! shown; nothing here is a stock picture.

use eframe::egui;
use eframe::egui_wgpu::RenderState;
use glam::Vec3;
use gr_core::scene_file::{Camera, Environment, Material, Scene};
use gr_gpu::{GpuOptions, GpuTracer};
use gr_model::document::{Id, StructuralElement, element_kind, element_style};
use gr_model::mesh::{SceneMesh, SurfaceKind, add_plant};
use std::collections::HashMap;
use std::sync::mpsc::{Receiver, channel};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Taxon {
    Broadleaf,
    Conifer,
    Palm,
    Shrub,
}

impl Taxon {
    pub const ALL: [Taxon; 4] = [Taxon::Broadleaf, Taxon::Conifer, Taxon::Palm, Taxon::Shrub];

    pub fn label(self) -> &'static str {
        match self {
            Taxon::Broadleaf => "Hoja ancha",
            Taxon::Conifer => "Coníferas",
            Taxon::Palm => "Palmas",
            Taxon::Shrub => "Arbustos",
        }
    }
}

/// A plant the engine can grow.
pub struct Species {
    pub name: &'static str,
    pub latin: &'static str,
    pub description: &'static str,
    pub taxon: Taxon,
    /// `ElementStyle` of the planting.
    pub style: i32,
    /// Default height and canopy spread, in metres.
    pub height: f64,
    pub spread: f64,
    /// Fixed identifier of the specimen: a plant grows from its identifier, so the library always shows the same.
    seed: u128,
}

pub const CATALOG: [Species; 5] = [
    Species {
        name: "Guanacaste",
        latin: "Enterolobium cyclocarpum",
        description: "Copa muy ancha y plana sobre ramas casi horizontales",
        taxon: Taxon::Broadleaf,
        style: element_style::GUANACASTE,
        height: 12.0,
        spread: 16.0,
        seed: 0x6a1c_2f40_8e3b_4d71_9a26_c5e0_17b4_f381,
    },
    Species {
        name: "Árbol de hoja ancha",
        latin: "Frondosa genérica",
        description: "Tronco recto, ramas en abanico y copa redondeada",
        taxon: Taxon::Broadleaf,
        style: element_style::DEFAULT,
        height: 8.0,
        spread: 6.0,
        seed: 0x72f9_1165_936e_4b63_bc4c_715d_d60c_f860,
    },
    Species {
        name: "Palma de coco",
        latin: "Cocos nucifera",
        description: "Tronco anillado e inclinado, frondas pinnadas y cocos",
        taxon: Taxon::Palm,
        style: element_style::PALM,
        height: 9.0,
        spread: 6.0,
        seed: 0x1b7a_4d63_8c2f_4e1b_ad46_62f8_03b5_c912,
    },
    Species {
        name: "Conífera",
        latin: "Pinus / Cupressus",
        description: "Pisos de ramas colgantes con agujas en abanico",
        taxon: Taxon::Conifer,
        style: element_style::CONIFER,
        height: 10.0,
        spread: 4.5,
        seed: 0x2c8b_5e74_9d30_4f2c_be57_7309_14c6_da23,
    },
    Species {
        name: "Arbusto",
        latin: "Arbusto ornamental",
        description: "Varios tallos desde el suelo y follaje denso",
        taxon: Taxon::Shrub,
        style: element_style::SHRUB,
        height: 1.4,
        spread: 1.6,
        seed: 0x0a6f_3c52_7b1e_4d0a_9c35_51e7_f2a4_b801,
    },
];

impl Species {
    /// The plant grown at `at`, `height` metres tall. `instance` varies the shape between copies.
    pub fn mesh(&self, at: Vec3, height: f64, spread: f64, instance: u128) -> SceneMesh {
        let plant = StructuralElement {
            id: Id(self.seed ^ instance.wrapping_mul(0x9e37_79b9_7f4a_7c15)),
            kind: element_kind::PLANTING,
            start: gr_model::plan::Point2::new(at.x as f64, at.y as f64),
            end: gr_model::plan::Point2::new(at.x as f64, at.y as f64),
            width: spread,
            height,
            style: self.style,
            ..Default::default()
        };
        let mut mesh = SceneMesh::default();
        add_plant(&mut mesh, &plant, at.z as f64);
        mesh
    }
}

/// Facts about a grown specimen, for the cards and the inspector.
#[derive(Clone, Copy, Debug, Default)]
pub struct SpecimenFacts {
    pub triangles: usize,
    /// Leaf, bark and other triangles at each tenth of the height, for the canopy profile.
    pub profile: [usize; 10],
}

/// Specimen pictures and facts, rendered on demand.
#[derive(Default)]
pub struct Library {
    pub thumbnails: HashMap<usize, egui::TextureHandle>,
    pub facts: HashMap<usize, SpecimenFacts>,
    /// The large inspector picture of one species.
    pub inspector: Option<(usize, egui::TextureHandle)>,
    /// Renders in flight: the thumbnails and, beside them, at most one inspector picture.
    pending: Vec<Receiver<(usize, bool, egui::ColorImage, SpecimenFacts)>>,
    thumbnails_started: bool,
    requested_inspector: Option<usize>,
}

impl Library {
    /// Starts rendering every thumbnail that is missing; returns at once.
    pub fn ensure_thumbnails(&mut self, state: &RenderState) {
        if self.thumbnails_started || self.thumbnails.len() == CATALOG.len() {
            return;
        }
        self.thumbnails_started = true;
        let (sender, receiver) = channel();
        let (device, queue, info) = (
            state.device.clone(),
            state.queue.clone(),
            state.adapter.get_info(),
        );
        std::thread::spawn(move || {
            for (index, species) in CATALOG.iter().enumerate() {
                if let Some((image, facts)) =
                    specimen(&device, &queue, &info, species, 420, 420, 40)
                    && sender.send((index, false, image, facts)).is_err()
                {
                    return;
                }
            }
        });
        self.pending.push(receiver);
    }

    /// Starts rendering the large inspector picture of a species.
    pub fn ensure_inspector(&mut self, state: &RenderState, index: usize) {
        if self
            .inspector
            .as_ref()
            .is_some_and(|(shown, _)| *shown == index)
            || self.requested_inspector == Some(index)
        {
            return;
        }
        self.requested_inspector = Some(index);
        let (sender, receiver) = channel();
        let (device, queue, info) = (
            state.device.clone(),
            state.queue.clone(),
            state.adapter.get_info(),
        );
        std::thread::spawn(move || {
            if let Some((image, facts)) =
                specimen(&device, &queue, &info, &CATALOG[index], 1100, 640, 96)
            {
                let _ = sender.send((index, true, image, facts));
            }
        });
        self.pending.push(receiver);
    }

    /// Collects finished pictures; returns true when there is still work in flight.
    pub fn poll(&mut self, context: &egui::Context) -> bool {
        let mut finished = Vec::new();
        self.pending.retain(|receiver| {
            loop {
                match receiver.try_recv() {
                    Ok(result) => finished.push(result),
                    Err(std::sync::mpsc::TryRecvError::Empty) => return true,
                    Err(std::sync::mpsc::TryRecvError::Disconnected) => return false,
                }
            }
        });
        for (index, large, image, facts) in finished {
            self.facts.insert(index, facts);
            let texture = context.load_texture(
                format!("specimen-{index}-{large}"),
                image,
                egui::TextureOptions::LINEAR,
            );
            if large {
                self.inspector = Some((index, texture));
                self.requested_inspector = None;
            } else {
                self.thumbnails.insert(index, texture);
            }
        }
        !self.pending.is_empty()
    }
}

/// Renders a species on a dark studio floor next to a 1.8 m figure, as a library card or the inspector shows it.
fn specimen(
    device: &gr_gpu::wgpu::Device,
    queue: &gr_gpu::wgpu::Queue,
    info: &gr_gpu::wgpu::AdapterInfo,
    species: &Species,
    width: u32,
    height: u32,
    samples: u32,
) -> Option<(egui::ColorImage, SpecimenFacts)> {
    let plant = species.mesh(Vec3::ZERO, species.height, species.spread, 0);
    let facts = profile(&plant, species.height as f32);
    let mut scene = empty_scene();
    append_mesh(&mut scene, &plant);
    let figure_at = Vec3::new((species.spread as f32 * 0.42).max(0.9), -0.6, 0.0);
    append_mesh(&mut scene, &figure(figure_at));
    // A floor that fades into a dark backdrop: a wide dark-grey plane under a dim sky.
    let floor_material = push_material(
        &mut scene,
        Material {
            base_color: Vec3::splat(0.045),
            roughness: 0.85,
            ..stock(SurfaceKind::Floor)
        },
    );
    let extent = 400.0;
    add_quad(
        &mut scene,
        [
            Vec3::new(-extent, -extent, 0.0),
            Vec3::new(extent, -extent, 0.0),
            Vec3::new(extent, extent, 0.0),
            Vec3::new(-extent, extent, 0.0),
        ],
        floor_material,
    );
    let size = species.height.max(species.spread * 0.8) as f32;
    let aspect = width as f32 / height as f32;
    let target = Vec3::new(0.0, 0.0, species.height as f32 * 0.48);
    let fov = 34.0f32;
    let distance = size * 0.62 / (fov.to_radians() / 2.0).tan() / aspect.clamp(0.75, 1.0);
    scene.camera = Camera {
        position: target + Vec3::new(0.0, -distance, distance * 0.12),
        target,
        vertical_fov_degrees: fov,
    };
    scene.environment = Environment {
        sun_direction: Vec3::new(-0.45, -0.55, 0.70).normalize(),
        sun_intensity: 2.4,
        sky_intensity: 0.22,
        exposure: 0.85,
    };
    let mut tracer = GpuTracer::with_device(
        device.clone(),
        queue.clone(),
        info,
        &scene,
        width,
        height,
        &GpuOptions::default(),
    )
    .ok()?;
    tracer.set_denoise(true);
    let mut done = 0;
    while done < samples {
        let batch = (samples - done).min(16);
        tracer.render(batch);
        tracer.wait().ok()?;
        done += batch;
    }
    tracer.resolve(scene.environment.exposure);
    let pixels = tracer.read_display().ok()?;
    Some((
        egui::ColorImage::from_rgba_unmultiplied([width as usize, height as usize], &pixels),
        facts,
    ))
}

fn profile(mesh: &SceneMesh, height: f32) -> SpecimenFacts {
    let mut facts = SpecimenFacts {
        triangles: mesh.triangle_count(),
        profile: [0; 10],
    };
    for triangle in 0..mesh.triangle_count() {
        let z = mesh.triangle(triangle)[0].z;
        let band = ((z / height.max(0.01)) * 10.0).clamp(0.0, 9.0) as usize;
        facts.profile[band] += 1;
    }
    facts
}

/// A standing figure 1.8 m tall, made of boxes, for scale.
fn figure(at: Vec3) -> SceneMesh {
    let mut mesh = SceneMesh::default();
    let mut block =
        |min: Vec3, max: Vec3| add_box(&mut mesh, at + min, at + max, SurfaceKind::Rubber);
    block(Vec3::new(-0.16, -0.09, 0.0), Vec3::new(-0.03, 0.09, 0.85));
    block(Vec3::new(0.03, -0.09, 0.0), Vec3::new(0.16, 0.09, 0.85));
    block(Vec3::new(-0.21, -0.12, 0.85), Vec3::new(0.21, 0.12, 1.50));
    block(Vec3::new(-0.29, -0.07, 0.88), Vec3::new(-0.21, 0.07, 1.45));
    block(Vec3::new(0.21, -0.07, 0.88), Vec3::new(0.29, 0.07, 1.45));
    block(Vec3::new(-0.10, -0.11, 1.55), Vec3::new(0.10, 0.11, 1.80));
    mesh
}

fn add_box(mesh: &mut SceneMesh, min: Vec3, max: Vec3, kind: SurfaceKind) {
    let corner = |x: bool, y: bool, z: bool| {
        Vec3::new(
            if x { max.x } else { min.x },
            if y { max.y } else { min.y },
            if z { max.z } else { min.z },
        )
    };
    let faces = [
        [
            (false, false, false),
            (false, true, false),
            (true, true, false),
            (true, false, false),
        ],
        [
            (false, false, true),
            (true, false, true),
            (true, true, true),
            (false, true, true),
        ],
        [
            (false, false, false),
            (true, false, false),
            (true, false, true),
            (false, false, true),
        ],
        [
            (true, false, false),
            (true, true, false),
            (true, true, true),
            (true, false, true),
        ],
        [
            (true, true, false),
            (false, true, false),
            (false, true, true),
            (true, true, true),
        ],
        [
            (false, true, false),
            (false, false, false),
            (false, false, true),
            (false, true, true),
        ],
    ];
    for face in faces {
        let [a, b, c, d] = face.map(|(x, y, z)| corner(x, y, z));
        mesh.add_quad(a, b, c, d, kind);
    }
}

/// The engine's stock material of a surface kind.
pub fn stock(kind: SurfaceKind) -> Material {
    gr_model::scene::stock_materials()[kind as usize]
}

fn empty_scene() -> Scene {
    Scene {
        positions: Vec::new(),
        indices: Vec::new(),
        triangle_material: Vec::new(),
        normals: None,
        corner_uvs: None,
        materials: Vec::new(),
        lights: Vec::new(),
        camera: Camera {
            position: Vec3::new(10.0, -10.0, 5.0),
            target: Vec3::ZERO,
            vertical_fov_degrees: 45.0,
        },
        environment: Environment {
            sun_direction: Vec3::Z,
            sun_intensity: 2.0,
            sky_intensity: 1.2,
            exposure: 0.4,
        },
    }
}

fn push_material(scene: &mut Scene, material: Material) -> u32 {
    scene.materials.push(material);
    (scene.materials.len() - 1) as u32
}

fn add_quad(scene: &mut Scene, corners: [Vec3; 4], material: u32) {
    let base = scene.positions.len() as u32;
    scene.positions.extend(corners);
    if let Some(normals) = &mut scene.normals {
        normals.extend([Vec3::ZERO; 4]);
    }
    scene.indices.push([base, base + 1, base + 2]);
    scene.indices.push([base, base + 2, base + 3]);
    scene.triangle_material.extend([material, material]);
}

/// Adds a mesh to a scene, giving each surface kind it uses the engine's stock material.
pub fn append_mesh(scene: &mut Scene, mesh: &SceneMesh) {
    let stock_materials = gr_model::scene::stock_materials();
    let mut material_of: HashMap<SurfaceKind, u32> = HashMap::new();
    let base = scene.positions.len() as u32;
    let had_normals = scene.normals.is_some();
    if mesh.normals.is_some() && !had_normals {
        scene.normals = Some(vec![Vec3::ZERO; scene.positions.len()]);
    }
    scene.positions.extend_from_slice(&mesh.positions);
    if let Some(normals) = &mut scene.normals {
        match &mesh.normals {
            Some(theirs) => normals.extend_from_slice(theirs),
            None => normals.extend(std::iter::repeat_n(Vec3::ZERO, mesh.positions.len())),
        }
    }
    for (triangle, &kind) in mesh.kinds.iter().enumerate() {
        let material = *material_of.entry(kind).or_insert_with(|| {
            scene.materials.push(stock_materials[kind as usize]);
            (scene.materials.len() - 1) as u32
        });
        let first = base + triangle as u32 * 3;
        scene.indices.push([first, first + 1, first + 2]);
        scene.triangle_material.push(material);
    }
    if let Some(uvs) = &mut scene.corner_uvs {
        uvs.extend(std::iter::repeat_n(
            [glam::Vec2::ZERO; 3],
            mesh.triangle_count(),
        ));
    }
}
