//! Render-quality vegetation, grown rather than sculpted: a trunk splits into limbs that branch recursively, kept
//! inside the crown of the species, and the twigs carry clusters of individual leaves. Palms get a ringed trunk and
//! fronds of single pinnae; conifers get whorls of drooping branches with needle sprays.
//!
//! Ported from `geometry/plant-detail.cs` and the helpers it uses in `geometry/plant-geometry.cs`. A plant is a
//! function of its identifier, height and spread: the random sequence, the order in which it is consumed and the
//! arithmetic all follow the original, so the same tree grows here as in the application.

use super::dotnet::{DotNetRandom, lerp, normalize};
use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::{Id, StructuralElement, element_style};
use glam::Vec3;
use std::f32::consts::{PI, TAU};

/// Appends the plant standing at `base_elevation` (the ground under it, in metres).
pub fn add_plant(mesh: &mut SceneMesh, plant: &StructuralElement, base_elevation: f64) {
    let mut local = SceneMesh::default();
    let root = Vec3::new(plant.start.x as f32, plant.start.y as f32, (base_elevation + plant.offset) as f32);
    let height = plant.height.max(0.2) as f32;
    let spread = plant.width.max(0.2) as f32;
    let mut tree = Tree::new(&mut local, DotNetRandom::new(seed(plant.id)));
    match plant.style {
        element_style::PALM => grow_palm(&mut tree, root, height, spread),
        element_style::CONIFER => grow_conifer(&mut tree, root, height, spread),
        element_style::SHRUB => grow_shrub(&mut tree, root, height, spread),
        element_style::GUANACASTE => grow_guanacaste(&mut tree, root, height, spread),
        _ => grow_broadleaf(&mut tree, root, height, spread),
    }
    fit_to_box(&mut local, root, height, spread);
    mesh.append(local);
}

/// The seed of a plant: the four 32-bit words of its GUID, as .NET lays them out in memory, combined.
fn seed(id: Id) -> i32 {
    let t = id.0.to_be_bytes();
    let first = u32::from_be_bytes([t[0], t[1], t[2], t[3]]);
    let second = u32::from_le_bytes([t[5], t[4], t[7], t[6]]);
    let third = u32::from_le_bytes([t[8], t[9], t[10], t[11]]);
    let fourth = u32::from_le_bytes([t[12], t[13], t[14], t[15]]);
    (first ^ second ^ third ^ fourth) as i32
}

/// Height of the bare trunk, underside of the canopy and trunk radius of a growth style.
fn proportions(style: i32, height: f32) -> (f64, f64, f64) {
    let height = height as f64;
    match style {
        element_style::GUANACASTE => (height * 0.45, height * 0.42, (height * 0.055).clamp(0.15, 1.2)),
        element_style::PALM => (height * 0.88, height * 0.70, (height * 0.018).clamp(0.10, 0.35)),
        element_style::CONIFER => (height * 0.20, height * 0.12, (height * 0.022).clamp(0.06, 0.6)),
        element_style::SHRUB => (0.0, 0.0, 0.0),
        _ => (height * 0.45, height * 0.34, (height * 0.03).clamp(0.08, 0.6)),
    }
}

/// Two unit vectors that, with `axis`, form a right-handed frame.
fn basis(axis: Vec3) -> (Vec3, Vec3) {
    let helper = if axis.z.abs() < 0.9 { Vec3::Z } else { Vec3::X };
    let u = normalize(axis.cross(helper));
    (u, axis.cross(u))
}

/// Scales the plant just grown so it stands on its base and is exactly as tall and as wide as its parameters: the
/// random shape of a crown must not change the height a schedule reports. Normals follow the inverse scale.
fn fit_to_box(mesh: &mut SceneMesh, root: Vec3, height: f32, spread: f32) {
    if mesh.positions.is_empty() {
        return;
    }
    let (min, max) = mesh.positions.iter().fold((Vec3::MAX, Vec3::MIN), |(min, max), &p| (min.min(p), max.max(p)));
    let across = (max.x - root.x).max(root.x - min.x).max((max.y - root.y).max(root.y - min.y)) * 2.0;
    let horizontal = if across > 1e-4 { spread / across } else { 1.0 };
    let vertical = if max.z - min.z > 1e-4 { height / (max.z - min.z) } else { 1.0 };
    for point in &mut mesh.positions {
        *point = Vec3::new(
            root.x + (point.x - root.x) * horizontal,
            root.y + (point.y - root.y) * horizontal,
            root.z + (point.z - min.z) * vertical,
        );
    }
    if let Some(normals) = &mut mesh.normals {
        for normal in normals.iter_mut().filter(|normal| **normal != Vec3::ZERO) {
            *normal = normalize(Vec3::new(normal.x / horizontal, normal.y / horizontal, normal.z / vertical));
        }
    }
}

// ---------------------------------------------------------------------------------------------- species

fn grow_broadleaf(tree: &mut Tree<'_>, root: Vec3, height: f32, spread: f32) {
    let (trunk_top, canopy_bottom, trunk_radius) = proportions(element_style::DEFAULT, height);
    let crown_height = height - canopy_bottom as f32;
    tree.envelope = Envelope {
        center: root + Vec3::new(0.0, 0.0, canopy_bottom as f32 + crown_height * 0.5),
        radii: Vec3::new(spread / 2.0, spread / 2.0, crown_height / 2.0),
    };
    tree.leaf_size = 0.11;
    tree.leaves_per_twig = 46;
    tree.max_depth = 6;
    let top = tree.trunk(root, trunk_top as f32, trunk_radius as f32, 0.05);
    let limbs = 4 + tree.random.next_below(2);
    for index in 0..limbs {
        let azimuth = (index as f32 + tree.random.jitter(0.3)) * TAU / limbs as f32;
        let tilt = 0.55 + 0.35 * tree.random.next_f32();
        let direction = normalize(Vec3::new(azimuth.cos() * tilt, azimuth.sin() * tilt, 1.0));
        tree.branch(top, direction, crown_height * 0.42, trunk_radius as f32 * 0.62, 1);
    }
    // A leader carries the top of the crown.
    tree.branch(top, Vec3::Z, crown_height * 0.45, trunk_radius as f32 * 0.55, 1);
    tree.fill_crown(340, 0.55, false);
}

/// Enterolobium cyclocarpum: a short massive trunk forking low into heavy limbs that run out almost level, under a
/// very wide, flat crown of fine foliage.
fn grow_guanacaste(tree: &mut Tree<'_>, root: Vec3, height: f32, spread: f32) {
    let (trunk_top, _, trunk_radius) = proportions(element_style::GUANACASTE, height);
    let crown_base = height * 0.55;
    let crown_thickness = height - crown_base;
    tree.envelope = Envelope {
        center: root + Vec3::new(0.0, 0.0, crown_base + crown_thickness * 0.5),
        radii: Vec3::new(spread / 2.0, spread / 2.0, crown_thickness * 0.62),
    };
    // Its bipinnate leaves read from any distance as small feathery blades.
    tree.leaf_size = 0.16;
    tree.leaf_width = 0.55;
    tree.leaves_per_twig = 44;
    tree.max_depth = 6;
    tree.upward_bias = 0.05;
    let fork = tree.trunk(root, trunk_top as f32 * 0.6, trunk_radius as f32 * 1.1, 0.02);
    let limbs = 5 + tree.random.next_below(2);
    for index in 0..limbs {
        let azimuth = (index as f32 + tree.random.jitter(0.25)) * TAU / limbs as f32;
        let direction = normalize(Vec3::new(azimuth.cos(), azimuth.sin(), 0.75 + 0.2 * tree.random.next_f32()));
        tree.branch(fork, direction, spread * 0.3, trunk_radius as f32 * 0.55, 1);
    }
    tree.fill_crown(1500, 0.5, true);
}

fn grow_shrub(tree: &mut Tree<'_>, root: Vec3, height: f32, spread: f32) {
    tree.envelope = Envelope {
        center: root + Vec3::new(0.0, 0.0, height * 0.5),
        radii: Vec3::new(spread / 2.0, spread / 2.0, height / 2.0),
    };
    tree.leaf_size = 0.06;
    tree.leaves_per_twig = 30;
    tree.max_depth = 4;
    let stems = 6 + tree.random.next_below(3);
    for index in 0..stems {
        let azimuth = index as f32 * TAU / stems as f32 + tree.random.jitter(0.4);
        let direction = normalize(Vec3::new(azimuth.cos() * 0.5, azimuth.sin() * 0.5, 1.0));
        let start = root + Vec3::new(tree.random.jitter(0.08), tree.random.jitter(0.08), 0.0);
        tree.branch(start, direction, height * 0.5, 0.018, 1);
    }
    tree.fill_crown((spread * spread * 40.0).clamp(60.0, 400.0) as i32, 0.35, false);
}

fn grow_conifer(tree: &mut Tree<'_>, root: Vec3, height: f32, spread: f32) {
    let (_, canopy_bottom, trunk_radius) = proportions(element_style::CONIFER, height);
    let (canopy_bottom, trunk_radius) = (canopy_bottom as f32, trunk_radius as f32);
    tree.tube(root, root + Vec3::new(0.0, 0.0, height), trunk_radius * 1.3, trunk_radius * 0.08, SurfaceKind::Bark, 10);
    tree.leaf_size = 0.075;
    tree.leaf_width = 0.45;
    tree.leaves_per_twig = 22;
    let tiers = ((height - canopy_bottom) / 0.32) as i32;
    for tier in 0..tiers {
        let t = tier as f32 / tiers as f32;
        let z = canopy_bottom + (height - canopy_bottom) * t * 0.97;
        let reach = spread / 2.0 * (1.0 - t) * (0.85 + 0.25 * tree.random.next_f32());
        let branches = 5 + tree.random.next_below(3);
        let twist = tree.random.next_f32() * TAU;
        for index in 0..branches {
            let azimuth = twist + index as f32 * TAU / branches as f32;
            let outward = Vec3::new(azimuth.cos(), azimuth.sin(), 0.0);
            let start = root + Vec3::new(0.0, 0.0, z);
            // A drooping branch: out and slightly down, with sprays of needles on side shoots.
            let end = start + outward * reach + Vec3::new(0.0, 0.0, -reach * 0.25);
            tree.tube(start, end, (reach * 0.025).max(0.006), 0.004, SurfaceKind::Bark, 4);
            let shoots = ((reach / 0.12) as i32).max(2);
            for shoot in 1..=shoots {
                let at = lerp(start, end, shoot as f32 / (shoots + 1) as f32);
                let side = normalize(outward.cross(Vec3::Z)) * if shoot % 2 == 0 { 1.0 } else { -1.0 };
                let shoot_end = at
                    + (side * 0.6 + outward * 0.6) * (reach * 0.22 * (1.0 - shoot as f32 / (shoots + 2) as f32))
                    + Vec3::new(0.0, 0.0, -0.03);
                tree.needle_spray(at, shoot_end);
            }
            tree.needle_spray(lerp(start, end, 0.85), end);
        }
    }
    tree.needle_spray(root + Vec3::new(0.0, 0.0, height * 0.94), root + Vec3::new(0.0, 0.0, height));
}

fn grow_palm(tree: &mut Tree<'_>, root: Vec3, height: f32, spread: f32) {
    let (trunk_top, _, trunk_radius) = proportions(element_style::PALM, height);
    let (trunk_top, trunk_radius) = (trunk_top as f32, trunk_radius as f32);
    let lean_angle = tree.random.next_f32() * TAU;
    let lean = Vec3::new(lean_angle.cos(), lean_angle.sin(), 0.0) * (height * 0.10);
    const SEGMENTS: i32 = 18;
    let mut previous = root;
    for index in 1..=SEGMENTS {
        let t = index as f32 / SEGMENTS as f32;
        let point = root + lean * (t * t) + Vec3::new(0.0, 0.0, trunk_top * t);
        // Leaf scars ring the trunk: each segment swells at its base.
        tree.tube(previous, point, trunk_radius * (1.35 - 0.35 * t + 0.06), trunk_radius * (1.35 - 0.35 * t), SurfaceKind::Bark, 12);
        previous = point;
    }
    let crown = previous;
    // Coconuts hang under the crown.
    for index in 0..7 {
        let azimuth = index as f32 * TAU / 7.0 + tree.random.jitter(0.3);
        tree.sphere(crown + Vec3::new(azimuth.cos() * 0.22, azimuth.sin() * 0.22, -0.25), 0.12, SurfaceKind::DoorLeaf);
    }
    let fronds = 15 + tree.random.next_below(4);
    for index in 0..fronds {
        let azimuth = (index as f32 + tree.random.jitter(0.35)) * TAU / fronds as f32;
        let rise = 0.9 - 1.4 * tree.random.next_f32();
        let length = spread * (0.5 + 0.08 * tree.random.next_f32());
        tree.frond(crown, Vec3::new(azimuth.cos(), azimuth.sin(), 0.0), rise, length);
    }
}

// ---------------------------------------------------------------------------------------- growth engine

/// The ellipsoid a crown stays inside.
#[derive(Clone, Copy)]
struct Envelope {
    center: Vec3,
    radii: Vec3,
}

impl Envelope {
    /// Normalised distance from the centre: below 1 inside the crown.
    fn measure(&self, point: Vec3) -> f32 {
        ((point - self.center) / self.radii).length()
    }

    fn outward(&self, point: Vec3) -> Vec3 {
        let d = point - self.center;
        if d.length_squared() < 1e-8 { Vec3::Z } else { normalize(d / (self.radii * self.radii)) }
    }
}

struct Tree<'a> {
    mesh: &'a mut SceneMesh,
    random: DotNetRandom,
    /// Branch ends that leaf clusters can hang from: position and radius.
    tips: Vec<(Vec3, f32)>,
    /// Leaf width relative to its length: 1 for broad leaves, about 0.5 for fine foliage and needles.
    leaf_width: f32,
    envelope: Envelope,
    leaf_size: f32,
    leaves_per_twig: i32,
    max_depth: i32,
    upward_bias: f32,
}

impl<'a> Tree<'a> {
    fn new(mesh: &'a mut SceneMesh, random: DotNetRandom) -> Tree<'a> {
        Tree {
            mesh,
            random,
            tips: Vec::new(),
            leaf_width: 1.0,
            envelope: Envelope { center: Vec3::ZERO, radii: Vec3::ONE },
            leaf_size: 0.1,
            leaves_per_twig: 40,
            max_depth: 5,
            upward_bias: 0.18,
        }
    }

    /// A trunk with a flared base and a slight lean; returns its top.
    fn trunk(&mut self, root: Vec3, length: f32, radius: f32, lean: f32) -> Vec3 {
        let lean_direction = Vec3::new(self.random.jitter(1.0), self.random.jitter(1.0), 0.0) * lean;
        let flare = root + Vec3::new(0.0, 0.0, (length * 0.15).min(0.45));
        self.tube(root, flare, radius * 1.55, radius * 1.05, SurfaceKind::Bark, 14);
        let top = root + Vec3::new(lean_direction.x * length, lean_direction.y * length, length);
        let middle = lerp(flare, top, 0.5) + Vec3::new(self.random.jitter(0.03), self.random.jitter(0.03), 0.0) * length;
        self.tube(flare, middle, radius * 1.05, radius * 0.92, SurfaceKind::Bark, 14);
        self.tube(middle, top, radius * 0.92, radius * 0.8, SurfaceKind::Bark, 12);
        top
    }

    /// One branch and, recursively, its children; twigs end in leaf clusters.
    fn branch(&mut self, start: Vec3, mut direction: Vec3, mut length: f32, radius: f32, depth: i32) {
        // Keep the branch inside the crown: turn it back toward the middle rather than letting it poke out.
        let mut end = start + direction * length;
        let mut attempt = 0;
        while attempt < 4 && self.envelope.measure(end) > 1.0 {
            direction = normalize(direction + normalize(self.envelope.center - end) * 0.6);
            length *= 0.9;
            end = start + direction * length;
            attempt += 1;
        }
        // A gentle bend toward the light along the branch.
        let bend = Vec3::Z * (length * 0.12 * self.upward_bias * 4.0)
            + Vec3::new(self.random.jitter(1.0), self.random.jitter(1.0), 0.0) * (length * 0.08);
        let middle = lerp(start, end, 0.5) + bend * 0.5;
        end += bend;
        let tip_radius = radius * 0.72;
        let sides = if radius > 0.06 {
            9
        } else if radius > 0.018 {
            6
        } else {
            4
        };
        self.tube(start, middle, radius, (radius + tip_radius) / 2.0, SurfaceKind::Bark, sides);
        self.tube(middle, end, (radius + tip_radius) / 2.0, tip_radius, SurfaceKind::Bark, sides);

        if depth >= self.max_depth || length < 0.08 || tip_radius < 0.003 {
            self.tips.push((end, tip_radius));
            self.leaf_cluster(end, normalize(end - start), length, None);
            return;
        }
        if depth >= 3 {
            self.tips.push((end, tip_radius));
        }
        let children = if depth < 2 { 3 } else { 2 + (self.random.next_double() < 0.45) as i32 };
        let heading = normalize(end - start);
        let child_radius = tip_radius / (children as f32).sqrt();
        for index in 0..children {
            let leader = index == 0;
            let spread_angle = if leader { 0.25 } else { 0.45 + 0.35 * self.random.next_f32() };
            let (u, v) = basis(heading);
            let azimuth = self.random.next_f32() * TAU;
            let deviation = u * azimuth.cos() + v * azimuth.sin();
            let child_direction = normalize(
                heading * spread_angle.cos() + deviation * spread_angle.sin() + self.envelope.outward(end) * 0.25 + Vec3::Z * self.upward_bias,
            );
            let child_length = length * if leader { 0.82 } else { 0.62 + 0.18 * self.random.next_f32() };
            let child_start = if leader { end } else { lerp(middle, end, 0.4 + 0.6 * self.random.next_f32()) };
            self.branch(child_start, child_direction, child_length, if leader { tip_radius * 0.82 } else { child_radius }, depth + 1);
        }
        // Inner twigs keep the crown dense rather than hollow.
        if depth >= self.max_depth - 2 {
            self.leaf_cluster(middle, heading, length * 0.6, Some(self.leaves_per_twig / 3));
        }
    }

    /// Scatters leaf clusters through the outer shell of the crown, where a real crown carries its leaves, and
    /// reaches each with a twig from the nearest branch end, so the crown is full whatever the branching produced.
    fn fill_crown(&mut self, clusters: i32, shell: f32, upper_only: bool) {
        if self.tips.is_empty() {
            return;
        }
        for _ in 0..clusters {
            let mut point;
            let mut attempts = 0;
            loop {
                let mut direction = normalize(
                    Vec3::new(self.random.jitter(1.0), self.random.jitter(1.0), self.random.jitter(1.0) + 0.25) + Vec3::new(0.0, 0.0, 1e-4),
                );
                if upper_only && direction.z < -0.35 {
                    direction.z = -direction.z * 0.5;
                }
                let radial = shell + (1.0 - shell) * self.random.next_f32().sqrt();
                point = self.envelope.center + direction * self.envelope.radii * radial;
                attempts += 1;
                if !(attempts < 4 && point.z < self.envelope.center.z - self.envelope.radii.z * 0.95) {
                    break;
                }
            }
            let mut nearest = self.tips[0];
            let mut best = f32::MAX;
            for &tip in &self.tips {
                let distance = tip.0.distance_squared(point);
                if distance < best {
                    best = distance;
                    nearest = tip;
                }
            }
            let reach = best.sqrt();
            // Long straight twigs would draw a wire frame through the crown; far clusters hang on hidden twigs.
            if reach > 0.05 && reach < 1.2 {
                self.tube(nearest.0, point, nearest.1.min(0.012).max(0.004), 0.003, SurfaceKind::Bark, 4);
            }
            self.leaf_cluster(point, normalize(point - nearest.0 + Vec3::new(0.0, 0.0, 1e-4)), 0.4, None);
        }
    }

    /// A cloud of single leaves around the end of a twig, each facing roughly up and outward.
    fn leaf_cluster(&mut self, tip: Vec3, heading: Vec3, twig_length: f32, count: Option<i32>) {
        let leaves = count.unwrap_or(self.leaves_per_twig);
        let radius = (self.leaf_size * 2.2).max((twig_length * 0.8).min(0.35));
        for _ in 0..leaves {
            let mut offset = Vec3::new(self.random.jitter(1.0), self.random.jitter(1.0), self.random.jitter(1.0));
            if offset.length_squared() > 1.0 {
                offset = normalize(offset);
            }
            let position = tip + offset * radius - heading * (radius * 0.3 * self.random.next_f32());
            let facing = normalize(
                Vec3::Z * 0.7
                    + self.envelope.outward(position) * 0.6
                    + Vec3::new(self.random.jitter(0.9), self.random.jitter(0.9), self.random.jitter(0.5)),
            );
            let size = self.leaf_size * (0.75 + 0.5 * self.random.next_f32());
            self.leaf(position, facing, normalize(offset + heading * 0.5 + Vec3::new(0.0, 0.0, 0.0001)), size);
        }
    }

    /// An ovate leaf folded slightly along its midrib: six triangles, or four when it is too small to tell.
    fn leaf(&mut self, base: Vec3, normal: Vec3, mut along: Vec3, length: f32) {
        along -= normal * normal.dot(along);
        if along.length_squared() < 1e-8 {
            along = basis(normal).0;
        }
        let along = normalize(along);
        let side = normal.cross(along);
        let width = self.leaf_width;
        let p = |x: f32, y: f32, lift: f32| base + along * (x * length) + side * (y * length * width) + normal * (lift * length);
        let mesh = &mut *self.mesh;
        if length < 0.08 {
            // Small leaves and needle blades: a folded diamond is all a pixel can resolve.
            let (nb, nl, nt, nr, nm) = (p(0.0, 0.0, 0.0), p(0.45, 0.24, 0.06), p(1.0, 0.0, 0.02), p(0.45, -0.24, 0.06), p(0.45, 0.0, 0.0));
            mesh.add_triangle(nb, nl, nm, SurfaceKind::Leaf);
            mesh.add_triangle(nl, nt, nm, SurfaceKind::Leaf);
            mesh.add_triangle(nb, nm, nr, SurfaceKind::Leaf);
            mesh.add_triangle(nm, nt, nr, SurfaceKind::Leaf);
            return;
        }
        let b = p(0.0, 0.0, 0.0);
        let l1 = p(0.30, 0.22, 0.07);
        let l2 = p(0.68, 0.17, 0.06);
        let t = p(1.0, 0.0, 0.02);
        let r2 = p(0.68, -0.17, 0.06);
        let r1 = p(0.30, -0.22, 0.07);
        let m = p(0.5, 0.0, 0.0);
        mesh.add_triangle(b, l1, m, SurfaceKind::Leaf);
        mesh.add_triangle(l1, l2, m, SurfaceKind::Leaf);
        mesh.add_triangle(l2, t, m, SurfaceKind::Leaf);
        mesh.add_triangle(b, m, r1, SurfaceKind::Leaf);
        mesh.add_triangle(r1, m, r2, SurfaceKind::Leaf);
        mesh.add_triangle(r2, m, t, SurfaceKind::Leaf);
    }

    /// A spray of needles along a shoot. Needles are far thinner than a pixel; what reads is the spray they form,
    /// so each spray is a dense run of narrow leaf blades.
    fn needle_spray(&mut self, from: Vec3, to: Vec3) {
        let axis = to - from;
        let length = axis.length();
        if length < 1e-4 {
            return;
        }
        let steps = ((length / 0.14) as i32).max(1);
        for step in 0..=steps {
            self.leaf_cluster(lerp(from, to, step as f32 / steps as f32), axis / length, 0.32, Some(self.leaves_per_twig));
        }
    }

    /// A palm frond: an arching rachis with long pinnae on both sides forming a shallow V.
    fn frond(&mut self, crown: Vec3, outward: Vec3, rise: f32, length: f32) {
        let side = normalize(Vec3::Z.cross(outward));
        let spine = |t: f32| crown + outward * (length * t) + Vec3::new(0.0, 0.0, length * (rise * 0.5 * t - 0.55 * t * t));
        const STEPS: i32 = 12;
        for step in 0..STEPS {
            self.tube(
                spine(step as f32 / STEPS as f32),
                spine((step + 1) as f32 / STEPS as f32),
                0.025 * (1.0 - 0.8 * step as f32 / STEPS as f32),
                0.025 * (1.0 - 0.8 * (step + 1) as f32 / STEPS as f32),
                SurfaceKind::Bark,
                4,
            );
        }
        const PINNAE: i32 = 52;
        for index in 2..PINNAE {
            let t = index as f32 / PINNAE as f32;
            let at = spine(t);
            let forward = normalize(spine((t + 0.02).min(1.0)) - spine((t - 0.02).max(0.0)));
            let leaf_length = length * 0.42 * (PI * (t * 1.1 + 0.05).min(1.0)).sin() + 0.05;
            for sign in [-1.0f32, 1.0] {
                let direction = normalize(side * sign * 0.85 + forward * 0.45 + Vec3::new(0.0, 0.0, 0.18));
                let droop = Vec3::new(0.0, 0.0, -leaf_length * 0.35);
                let middle = at + direction * (leaf_length * 0.5) + droop * 0.35;
                let tip = at + direction * leaf_length + droop;
                let width = normalize(direction.cross(Vec3::Z)) * 0.022;
                self.mesh.add_triangle(at - width, at + width, middle + width, SurfaceKind::Leaf);
                self.mesh.add_triangle(at - width, middle + width, middle - width, SurfaceKind::Leaf);
                self.mesh.add_triangle(middle - width, middle + width, tip, SurfaceKind::Leaf);
            }
        }
    }

    /// A smooth-shaded tapered cylinder.
    fn tube(&mut self, from: Vec3, to: Vec3, start_radius: f32, end_radius: f32, kind: SurfaceKind, sides: i32) {
        let axis = to - from;
        if axis.length_squared() < 1e-10 || start_radius <= 0.0 {
            return;
        }
        let (u, v) = basis(normalize(axis));
        for side in 0..sides {
            let a0 = side as f32 * TAU / sides as f32;
            let a1 = (side + 1) as f32 * TAU / sides as f32;
            let n0 = u * a0.cos() + v * a0.sin();
            let n1 = u * a1.cos() + v * a1.sin();
            self.mesh.add_smooth_triangle([from + n0 * start_radius, from + n1 * start_radius, to + n1 * end_radius], [n0, n1, n1], kind);
            self.mesh.add_smooth_triangle([from + n0 * start_radius, to + n1 * end_radius, to + n0 * end_radius], [n0, n1, n0], kind);
        }
    }

    /// A small smooth sphere (a coconut).
    fn sphere(&mut self, center: Vec3, radius: f32, kind: SurfaceKind) {
        const RINGS: i32 = 6;
        const SEGMENTS: i32 = 10;
        let normal = |ring: i32, segment: i32| {
            let (polar, around) = (PI * ring as f32 / RINGS as f32, TAU * segment as f32 / SEGMENTS as f32);
            Vec3::new(polar.sin() * around.cos(), polar.sin() * around.sin(), polar.cos())
        };
        for ring in 0..RINGS {
            for segment in 0..SEGMENTS {
                let (a, b) = (normal(ring, segment), normal(ring + 1, segment));
                let (c, d) = (normal(ring + 1, segment + 1), normal(ring, segment + 1));
                self.mesh.add_smooth_triangle([center + a * radius, center + b * radius, center + c * radius], [a, b, c], kind);
                self.mesh.add_smooth_triangle([center + a * radius, center + c * radius, center + d * radius], [a, c, d], kind);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::plan::Point2;

    fn plant(style: i32, height: f64, width: f64) -> StructuralElement {
        StructuralElement {
            id: Id::parse("72f91165-936e-4b63-bc4c-715dd60cf860").expect("a GUID"),
            kind: 28,
            start: Point2::new(3.0, -2.0),
            width,
            height,
            style,
            ..Default::default()
        }
    }

    #[test]
    fn the_seed_combines_the_guid_as_dotnet_stores_it() {
        // BitConverter.ToInt32 over Guid.ToByteArray() of this identifier, words 0, 4, 8 and 12, XORed.
        let id = Id::parse("72f91165-936e-4b63-bc4c-715dd60cf860").expect("a GUID");
        let expected = 0x72f9_1165u32 ^ 0x4b63_936e ^ 0x5d71_4cbc ^ 0x60f8_0cd6;
        assert_eq!(seed(id), expected as i32);
    }

    #[test]
    fn every_species_fits_its_height_and_spread_and_stands_on_its_base() {
        for style in [element_style::DEFAULT, element_style::GUANACASTE, element_style::PALM, element_style::CONIFER, element_style::SHRUB] {
            let mut mesh = SceneMesh::default();
            add_plant(&mut mesh, &plant(style, 6.0, 4.0), 1.5);
            assert!(mesh.triangle_count() > 1000, "style {style} grew only {} triangles", mesh.triangle_count());
            let (min, max) = mesh.bounds();
            assert!((min.z - 1.5).abs() < 1e-4 && (max.z - 7.5).abs() < 1e-3, "style {style}: {min:?} to {max:?}");
            let half_width = (max.x - 3.0).max(3.0 - min.x).max((max.y + 2.0).max(-2.0 - min.y));
            assert!((half_width - 2.0).abs() < 1e-3, "style {style} is {half_width} m from its axis");
            assert!(mesh.positions.iter().all(|p| p.is_finite()));
            let normals = mesh.normals.as_ref().expect("trunks are smooth-shaded");
            assert_eq!(normals.len(), mesh.positions.len());
            assert!(normals.iter().all(|n| *n == Vec3::ZERO || (n.length() - 1.0).abs() < 1e-3));
        }
    }

    #[test]
    fn the_same_plant_always_grows_the_same_way() {
        let (mut first, mut second) = (SceneMesh::default(), SceneMesh::default());
        add_plant(&mut first, &plant(element_style::DEFAULT, 8.0, 6.0), 0.0);
        add_plant(&mut second, &plant(element_style::DEFAULT, 8.0, 6.0), 0.0);
        assert_eq!(first.positions, second.positions);
    }
}
