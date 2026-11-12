//! Four-wide bounding-volume hierarchy for single rays.
//!
//! A binary tree is built with a binned surface-area heuristic (on all cores for large inputs) and then collapsed so
//! every interior node holds up to four children. A ray tests the four child boxes of a node, and the up to four
//! triangles of a leaf, with one SIMD operation each. `wide` provides the SIMD types, so this module has no `unsafe`
//! and runs on SSE2, NEON or a scalar fallback from the same source.

use glam::Vec3;
use rayon::prelude::*;
use wide::{CmpGe, CmpGt, CmpLe, CmpLt, f32x4};

/// Triangles per leaf: one SIMD block.
const LEAF_SIZE: usize = 4;
const BINS: usize = 16;
/// Subtrees with fewer triangles than this are built on the calling thread.
const PARALLEL_BUILD: usize = 8192;
/// Ranges larger than this are binned on all cores.
const PARALLEL_BINNING: usize = 1 << 17;
/// Beyond this depth splits are forced to the median, which bounds the depth of pathological inputs.
const MEDIAN_DEPTH: u32 = 40;
const LEAF_FLAG: u32 = 1 << 31;
const STACK_SIZE: usize = 256;
const DETERMINANT_EPSILON: f32 = 1e-12;

/// Closest intersection of a ray.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Hit {
    pub distance: f32,
    /// Triangle index in the caller's numbering.
    pub triangle: u32,
    /// Barycentric weights of the second and third corners.
    pub u: f32,
    pub v: f32,
}

struct Node4 {
    min_x: f32x4,
    min_y: f32x4,
    min_z: f32x4,
    max_x: f32x4,
    max_y: f32x4,
    max_z: f32x4,
    /// Interior children are node indices; leaves are `LEAF_FLAG | block index`.
    child: [u32; 4],
    /// Bit `k` is set when lane `k` holds a child.
    lanes: i32,
}

/// Up to four triangles as vertex and two edges, one triangle per SIMD lane. Unused lanes have zero edges and never hit.
struct Tri4 {
    v0: [f32x4; 3],
    e1: [f32x4; 3],
    e2: [f32x4; 3],
}

pub struct Bvh {
    nodes: Vec<Node4>,
    blocks: Vec<Tri4>,
    block_triangles: Vec<[u32; 4]>,
    root: u32,
    triangle_count: usize,
    bounds: (Vec3, Vec3),
}

struct Ray4 {
    origin: [f32x4; 3],
    direction: [f32x4; 3],
    inverse: [f32x4; 3],
}

impl Ray4 {
    fn new(origin: Vec3, direction: Vec3) -> Self {
        // A zero component would give 0 x infinity = NaN on a slab plane; a huge finite reciprocal avoids it.
        let reciprocal = |d: f32| {
            if d.abs() < 1e-20 {
                1e20f32.copysign(d)
            } else {
                1.0 / d
            }
        };
        Ray4 {
            origin: [
                f32x4::splat(origin.x),
                f32x4::splat(origin.y),
                f32x4::splat(origin.z),
            ],
            direction: [
                f32x4::splat(direction.x),
                f32x4::splat(direction.y),
                f32x4::splat(direction.z),
            ],
            inverse: [
                f32x4::splat(reciprocal(direction.x)),
                f32x4::splat(reciprocal(direction.y)),
                f32x4::splat(reciprocal(direction.z)),
            ],
        }
    }
}

impl Node4 {
    /// Entry distance into each child box and the lanes entered before `limit`.
    #[inline(always)]
    fn test(&self, ray: &Ray4, limit: f32) -> (i32, f32x4) {
        let t1x = (self.min_x - ray.origin[0]) * ray.inverse[0];
        let t2x = (self.max_x - ray.origin[0]) * ray.inverse[0];
        let t1y = (self.min_y - ray.origin[1]) * ray.inverse[1];
        let t2y = (self.max_y - ray.origin[1]) * ray.inverse[1];
        let t1z = (self.min_z - ray.origin[2]) * ray.inverse[2];
        let t2z = (self.max_z - ray.origin[2]) * ray.inverse[2];
        let entry = t1x
            .fast_min(t2x)
            .fast_max(t1y.fast_min(t2y))
            .fast_max(t1z.fast_min(t2z))
            .fast_max(f32x4::ZERO);
        let exit = t1x
            .fast_max(t2x)
            .fast_min(t1y.fast_max(t2y))
            .fast_min(t1z.fast_max(t2z))
            .fast_min(f32x4::splat(limit));
        (entry.cmp_le(exit).move_mask() & self.lanes, entry)
    }
}

#[inline(always)]
fn cross(a: &[f32x4; 3], b: &[f32x4; 3]) -> [f32x4; 3] {
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ]
}

#[inline(always)]
fn dot(a: &[f32x4; 3], b: &[f32x4; 3]) -> f32x4 {
    a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
}

impl Tri4 {
    /// Möller–Trumbore on four triangles, two-sided. Returns the lanes hit inside (`t_min`, `t_max`) with their
    /// distance and barycentric weights.
    #[inline(always)]
    fn test(&self, ray: &Ray4, t_min: f32, t_max: f32) -> (i32, f32x4, f32x4, f32x4) {
        let p = cross(&ray.direction, &self.e2);
        let determinant = dot(&self.e1, &p);
        let inverse = f32x4::ONE / determinant;
        let t = [
            ray.origin[0] - self.v0[0],
            ray.origin[1] - self.v0[1],
            ray.origin[2] - self.v0[2],
        ];
        let u = dot(&t, &p) * inverse;
        let q = cross(&t, &self.e1);
        let v = dot(&ray.direction, &q) * inverse;
        let distance = dot(&self.e2, &q) * inverse;
        let inside = determinant.abs().cmp_ge(f32x4::splat(DETERMINANT_EPSILON))
            & u.cmp_ge(f32x4::ZERO)
            & v.cmp_ge(f32x4::ZERO)
            & (u + v).cmp_le(f32x4::ONE)
            & distance.cmp_gt(f32x4::splat(t_min))
            & distance.cmp_lt(f32x4::splat(t_max));
        (inside.move_mask(), distance, u, v)
    }
}

impl Bvh {
    /// Builds the hierarchy over indexed triangles. Hits report the position of the triangle in `indices`.
    pub fn build(positions: &[Vec3], indices: &[[u32; 3]]) -> Bvh {
        let count = indices.len();
        assert!(
            count < LEAF_FLAG as usize,
            "too many triangles for 31-bit references"
        );
        if count == 0 {
            return Bvh {
                nodes: Vec::new(),
                blocks: Vec::new(),
                block_triangles: Vec::new(),
                root: LEAF_FLAG,
                triangle_count: 0,
                bounds: (Vec3::ZERO, Vec3::ZERO),
            };
        }
        let corners = |triangle: usize| {
            let [a, b, c] = indices[triangle];
            (
                positions[a as usize],
                positions[b as usize],
                positions[c as usize],
            )
        };
        let (binary, order) = build_binary(positions, indices);

        let mut collapse = Collapse {
            binary: &binary,
            order: &order,
            corners: &corners,
            nodes: Vec::with_capacity(binary.len() / 3 + 1),
            blocks: Vec::with_capacity(count / 3 + 1),
            block_triangles: Vec::with_capacity(count / 3 + 1),
        };
        let root = collapse.emit(0);
        Bvh {
            nodes: collapse.nodes,
            blocks: collapse.blocks,
            block_triangles: collapse.block_triangles,
            root,
            triangle_count: count,
            bounds: (binary[0].min, binary[0].max),
        }
    }

    pub fn triangle_count(&self) -> usize {
        self.triangle_count
    }

    /// Bounds of everything in the hierarchy; a degenerate box when it is empty.
    pub fn bounds(&self) -> (Vec3, Vec3) {
        self.bounds
    }

    /// Heap bytes held by the hierarchy, including the packed triangle copies.
    pub fn memory_bytes(&self) -> usize {
        self.nodes.capacity() * size_of::<Node4>()
            + self.blocks.capacity() * size_of::<Tri4>()
            + self.block_triangles.capacity() * size_of::<[u32; 4]>()
    }

    /// Closest intersection with a distance inside (`t_min`, `t_max`).
    pub fn intersect(&self, origin: Vec3, direction: Vec3, t_min: f32, t_max: f32) -> Option<Hit> {
        if self.triangle_count == 0 {
            return None;
        }
        let ray = Ray4::new(origin, direction);
        let mut best = t_max;
        let mut hit = None;
        let mut stack = [(0f32, 0u32); STACK_SIZE];
        let mut top = 0usize;
        let mut current = self.root;
        loop {
            if current & LEAF_FLAG != 0 {
                let block = (current & !LEAF_FLAG) as usize;
                let (mask, distance, u, v) = self.blocks[block].test(&ray, t_min, best);
                if mask != 0 {
                    let (distance, u, v) = (distance.to_array(), u.to_array(), v.to_array());
                    for lane in 0..4 {
                        if mask & (1 << lane) != 0 && distance[lane] < best {
                            best = distance[lane];
                            hit = Some(Hit {
                                distance: best,
                                triangle: self.block_triangles[block][lane],
                                u: u[lane],
                                v: v[lane],
                            });
                        }
                    }
                }
            } else {
                let node = &self.nodes[current as usize];
                let (mask, entry) = node.test(&ray, best);
                if mask != 0 {
                    if mask & (mask - 1) == 0 {
                        current = node.child[mask.trailing_zeros() as usize];
                        continue;
                    }
                    // Several children: visit the nearest now and keep the others, farthest at the bottom.
                    let entry = entry.to_array();
                    let base = top;
                    let mut bits = mask;
                    while bits != 0 {
                        let lane = bits.trailing_zeros() as usize;
                        bits &= bits - 1;
                        let mut slot = top;
                        while slot > base && stack[slot - 1].0 < entry[lane] {
                            stack[slot] = stack[slot - 1];
                            slot -= 1;
                        }
                        stack[slot] = (entry[lane], node.child[lane]);
                        top += 1;
                    }
                    top -= 1;
                    current = stack[top].1;
                    continue;
                }
            }
            loop {
                if top == 0 {
                    return hit;
                }
                top -= 1;
                if stack[top].0 < best {
                    current = stack[top].1;
                    break;
                }
            }
        }
    }

    /// True as soon as any triangle blocks the segment (`t_min`, `t_max`); cheaper than a closest-hit query.
    pub fn occluded(&self, origin: Vec3, direction: Vec3, t_min: f32, t_max: f32) -> bool {
        if self.triangle_count == 0 {
            return false;
        }
        let ray = Ray4::new(origin, direction);
        let mut stack = [0u32; STACK_SIZE];
        let mut top = 0usize;
        let mut current = self.root;
        loop {
            if current & LEAF_FLAG != 0 {
                if self.blocks[(current & !LEAF_FLAG) as usize]
                    .test(&ray, t_min, t_max)
                    .0
                    != 0
                {
                    return true;
                }
            } else {
                let node = &self.nodes[current as usize];
                let mut bits = node.test(&ray, t_max).0;
                while bits != 0 {
                    stack[top] = node.child[bits.trailing_zeros() as usize];
                    top += 1;
                    bits &= bits - 1;
                }
            }
            if top == 0 {
                return false;
            }
            top -= 1;
            current = stack[top];
        }
    }
}

/// Node of a [`FlatBvh`], laid out for a GPU storage buffer (32 bytes).
#[derive(Clone, Copy, Debug, Default, bytemuck::Pod, bytemuck::Zeroable)]
#[repr(C)]
pub struct FlatNode {
    pub min: [f32; 3],
    /// Interior: index of the right child (the left child is the next node). Leaf: first slot in `triangle_order`.
    pub right_or_first: u32,
    pub max: [f32; 3],
    /// Triangles in the leaf; zero for an interior node.
    pub count: u32,
}

/// Binary hierarchy in depth-first order with at most four triangles per leaf: the form a shader can walk with a
/// small stack. It comes from the same builder as [`Bvh`].
pub struct FlatBvh {
    pub nodes: Vec<FlatNode>,
    /// Triangle numbers in leaf order; a leaf covers `triangle_order[first..first + count]`.
    pub triangle_order: Vec<u32>,
}

impl FlatBvh {
    pub fn build(positions: &[Vec3], indices: &[[u32; 3]]) -> FlatBvh {
        if indices.is_empty() {
            return FlatBvh {
                nodes: Vec::new(),
                triangle_order: Vec::new(),
            };
        }
        let (binary, triangle_order) = build_binary(positions, indices);
        let nodes = binary
            .par_iter()
            .map(|node| FlatNode {
                min: node.min.to_array(),
                right_or_first: if node.count > 0 {
                    node.first
                } else {
                    node.right
                },
                max: node.max.to_array(),
                count: node.count,
            })
            .collect();
        FlatBvh {
            nodes,
            triangle_order,
        }
    }
}

/// Builds the binary tree over all triangles. Node 0 is the root and the left child of node `i` is node `i + 1`.
fn build_binary(positions: &[Vec3], indices: &[[u32; 3]]) -> (Vec<BinaryNode>, Vec<u32>) {
    let boxes: Vec<PrimBox> = indices
        .par_iter()
        .map(|&[a, b, c]| {
            let (a, b, c) = (
                positions[a as usize],
                positions[b as usize],
                positions[c as usize],
            );
            PrimBox {
                min: a.min(b).min(c),
                max: a.max(b).max(c),
            }
        })
        .collect();
    let mut order: Vec<u32> = (0..indices.len() as u32).collect();
    let binary = build_parallel(&boxes, &mut order, 0, 0);
    (binary, order)
}

#[derive(Clone, Copy)]
struct PrimBox {
    min: Vec3,
    max: Vec3,
}

impl PrimBox {
    #[inline(always)]
    fn centroid2(&self) -> Vec3 {
        self.min + self.max
    }
}

/// Node of the intermediate binary tree. `count > 0` marks a leaf over `order[first..first + count]`.
#[derive(Clone, Copy)]
struct BinaryNode {
    min: Vec3,
    max: Vec3,
    left: u32,
    right: u32,
    first: u32,
    count: u32,
}

#[derive(Clone, Copy)]
struct Extent {
    min: Vec3,
    max: Vec3,
    centroid_min: Vec3,
    centroid_max: Vec3,
}

impl Extent {
    const EMPTY: Extent = Extent {
        min: Vec3::INFINITY,
        max: Vec3::NEG_INFINITY,
        centroid_min: Vec3::INFINITY,
        centroid_max: Vec3::NEG_INFINITY,
    };

    #[inline(always)]
    fn add(mut self, prim: &PrimBox) -> Extent {
        let centroid = prim.centroid2();
        self.min = self.min.min(prim.min);
        self.max = self.max.max(prim.max);
        self.centroid_min = self.centroid_min.min(centroid);
        self.centroid_max = self.centroid_max.max(centroid);
        self
    }

    fn merge(self, other: Extent) -> Extent {
        Extent {
            min: self.min.min(other.min),
            max: self.max.max(other.max),
            centroid_min: self.centroid_min.min(other.centroid_min),
            centroid_max: self.centroid_max.max(other.centroid_max),
        }
    }

    fn of(boxes: &[PrimBox], order: &[u32]) -> Extent {
        if order.len() >= PARALLEL_BINNING {
            order
                .par_chunks(1 << 14)
                .map(|chunk| {
                    chunk.iter().fold(Extent::EMPTY, |extent, &prim| {
                        extent.add(&boxes[prim as usize])
                    })
                })
                .reduce(|| Extent::EMPTY, Extent::merge)
        } else {
            order.iter().fold(Extent::EMPTY, |extent, &prim| {
                extent.add(&boxes[prim as usize])
            })
        }
    }
}

#[derive(Clone, Copy)]
struct Bin {
    min: Vec3,
    max: Vec3,
    count: u32,
}

type Bins = [[Bin; BINS]; 3];
const EMPTY_BINS: Bins = [[Bin {
    min: Vec3::INFINITY,
    max: Vec3::NEG_INFINITY,
    count: 0,
}; BINS]; 3];

fn half_area(min: Vec3, max: Vec3) -> f32 {
    let size = max - min;
    size.x * size.y + size.y * size.z + size.z * size.x
}

#[inline(always)]
fn bin_of(prim: &PrimBox, origin: Vec3, scale: Vec3) -> [usize; 3] {
    let position = (prim.centroid2() - origin) * scale;
    [
        (position.x as usize).min(BINS - 1),
        (position.y as usize).min(BINS - 1),
        (position.z as usize).min(BINS - 1),
    ]
}

fn fill_bins(boxes: &[PrimBox], order: &[u32], origin: Vec3, scale: Vec3) -> Bins {
    let add = |mut bins: Bins, &prim: &u32| {
        let prim = &boxes[prim as usize];
        let slots = bin_of(prim, origin, scale);
        for axis in 0..3 {
            let bin = &mut bins[axis][slots[axis]];
            bin.min = bin.min.min(prim.min);
            bin.max = bin.max.max(prim.max);
            bin.count += 1;
        }
        bins
    };
    if order.len() < PARALLEL_BINNING {
        return order.iter().fold(EMPTY_BINS, add);
    }
    order
        .par_chunks(1 << 14)
        .map(|chunk| chunk.iter().fold(EMPTY_BINS, add))
        .reduce(
            || EMPTY_BINS,
            |mut a, b| {
                for axis in 0..3 {
                    for slot in 0..BINS {
                        a[axis][slot].min = a[axis][slot].min.min(b[axis][slot].min);
                        a[axis][slot].max = a[axis][slot].max.max(b[axis][slot].max);
                        a[axis][slot].count += b[axis][slot].count;
                    }
                }
                a
            },
        )
}

/// Reorders `order` around the cheapest binned SAH plane and returns the size of the left part, always strictly
/// inside the range. Falls back to the median of the widest axis when no plane separates the triangles.
fn split(boxes: &[PrimBox], order: &mut [u32], extent: &Extent, depth: u32) -> usize {
    let size = extent.centroid_max - extent.centroid_min;
    let widest = if size.x >= size.y && size.x >= size.z {
        0
    } else if size.y >= size.z {
        1
    } else {
        2
    };
    let median = |order: &mut [u32]| {
        let middle = order.len() / 2;
        order.select_nth_unstable_by(middle, |&a, &b| {
            boxes[a as usize].centroid2()[widest].total_cmp(&boxes[b as usize].centroid2()[widest])
        });
        middle
    };
    if depth >= MEDIAN_DEPTH || size.max_element() <= 0.0 {
        return median(order);
    }

    let scale = Vec3::select(
        size.cmpgt(Vec3::splat(1e-12)),
        Vec3::splat(BINS as f32) / size,
        Vec3::ZERO,
    );
    let bins = fill_bins(boxes, order, extent.centroid_min, scale);
    let mut best: Option<(f32, usize, usize)> = None;
    for (axis, axis_bins) in bins.iter().enumerate() {
        // Sweep from the right once, then from the left while evaluating each of the BINS - 1 planes.
        let mut right_area = [0f32; BINS];
        let mut right_count = [0u32; BINS];
        let (mut min, mut max, mut count) = (Vec3::INFINITY, Vec3::NEG_INFINITY, 0u32);
        for slot in (1..BINS).rev() {
            let bin = &axis_bins[slot];
            min = min.min(bin.min);
            max = max.max(bin.max);
            count += bin.count;
            right_count[slot] = count;
            right_area[slot] = if count == 0 { 0.0 } else { half_area(min, max) };
        }
        let (mut min, mut max, mut count) = (Vec3::INFINITY, Vec3::NEG_INFINITY, 0u32);
        for slot in 0..BINS - 1 {
            let bin = &axis_bins[slot];
            min = min.min(bin.min);
            max = max.max(bin.max);
            count += bin.count;
            if count == 0 || right_count[slot + 1] == 0 {
                continue;
            }
            let cost = count as f32 * half_area(min, max)
                + right_count[slot + 1] as f32 * right_area[slot + 1];
            if best.is_none_or(|(best_cost, _, _)| cost < best_cost) {
                best = Some((cost, axis, slot));
            }
        }
    }
    let Some((_, axis, plane)) = best else {
        return median(order);
    };

    // In-place partition by bin, the same classification the costs were computed from.
    let (mut left, mut right) = (0usize, order.len());
    while left < right {
        if bin_of(&boxes[order[left] as usize], extent.centroid_min, scale)[axis] <= plane {
            left += 1;
        } else {
            right -= 1;
            order.swap(left, right);
        }
    }
    if left == 0 || left == order.len() {
        median(order)
    } else {
        left
    }
}

/// Builds the subtree over `order`, whose first element sits at absolute offset `first`. The root is node 0 and child
/// links are indices into the returned vector. Large ranges split their two halves across threads.
fn build_parallel(boxes: &[PrimBox], order: &mut [u32], first: u32, depth: u32) -> Vec<BinaryNode> {
    if order.len() <= PARALLEL_BUILD {
        let mut nodes = Vec::with_capacity(order.len() / 2 + 1);
        build_sequential(boxes, order, first, depth, &mut nodes);
        return nodes;
    }
    let extent = Extent::of(boxes, order);
    let middle = split(boxes, order, &extent, depth);
    let (left_order, right_order) = order.split_at_mut(middle);
    let (left, right) = rayon::join(
        || build_parallel(boxes, left_order, first, depth + 1),
        || build_parallel(boxes, right_order, first + middle as u32, depth + 1),
    );
    let mut nodes = Vec::with_capacity(1 + left.len() + right.len());
    let right_root = 1 + left.len() as u32;
    nodes.push(BinaryNode {
        min: extent.min,
        max: extent.max,
        left: 1,
        right: right_root,
        first: 0,
        count: 0,
    });
    for (subtree, offset) in [(left, 1), (right, right_root)] {
        nodes.extend(subtree.into_iter().map(|mut node| {
            if node.count == 0 {
                node.left += offset;
                node.right += offset;
            }
            node
        }));
    }
    nodes
}

fn build_sequential(
    boxes: &[PrimBox],
    order: &mut [u32],
    first: u32,
    depth: u32,
    nodes: &mut Vec<BinaryNode>,
) -> u32 {
    let extent = Extent::of(boxes, order);
    let index = nodes.len();
    nodes.push(BinaryNode {
        min: extent.min,
        max: extent.max,
        left: 0,
        right: 0,
        first,
        count: order.len() as u32,
    });
    if order.len() <= LEAF_SIZE {
        return index as u32;
    }
    let middle = split(boxes, order, &extent, depth);
    let (left_order, right_order) = order.split_at_mut(middle);
    let left = build_sequential(boxes, left_order, first, depth + 1, nodes);
    let right = build_sequential(boxes, right_order, first + middle as u32, depth + 1, nodes);
    let node = &mut nodes[index];
    node.left = left;
    node.right = right;
    node.count = 0;
    index as u32
}

/// Turns the binary tree into four-wide nodes and SIMD triangle blocks.
struct Collapse<'a, F: Fn(usize) -> (Vec3, Vec3, Vec3)> {
    binary: &'a [BinaryNode],
    order: &'a [u32],
    corners: &'a F,
    nodes: Vec<Node4>,
    blocks: Vec<Tri4>,
    block_triangles: Vec<[u32; 4]>,
}

impl<F: Fn(usize) -> (Vec3, Vec3, Vec3)> Collapse<'_, F> {
    /// Emits the subtree under binary node `index` and returns its reference.
    fn emit(&mut self, index: u32) -> u32 {
        let source = self.binary[index as usize];
        if source.count > 0 {
            return self.emit_leaf(&source);
        }
        // Pull grandchildren up, always opening the child with the largest surface, until four lanes are used.
        let mut children = [source.left, source.right, 0, 0];
        let mut used = 2;
        while used < 4 {
            let widest = (0..used)
                .filter(|&lane| self.binary[children[lane] as usize].count == 0)
                .max_by(|&a, &b| self.area(children[a]).total_cmp(&self.area(children[b])));
            let Some(lane) = widest else { break };
            let opened = self.binary[children[lane] as usize];
            children[lane] = opened.left;
            children[used] = opened.right;
            used += 1;
        }

        let slot = self.nodes.len();
        let mut min = [[0f32; 4]; 3];
        let mut max = [[0f32; 4]; 3];
        for lane in 0..used {
            let child = &self.binary[children[lane] as usize];
            for axis in 0..3 {
                min[axis][lane] = child.min[axis];
                max[axis][lane] = child.max[axis];
            }
        }
        self.nodes.push(Node4 {
            min_x: min[0].into(),
            min_y: min[1].into(),
            min_z: min[2].into(),
            max_x: max[0].into(),
            max_y: max[1].into(),
            max_z: max[2].into(),
            child: [0; 4],
            lanes: (1 << used) - 1,
        });
        for (lane, &child) in children.iter().enumerate().take(used) {
            let reference = self.emit(child);
            self.nodes[slot].child[lane] = reference;
        }
        slot as u32
    }

    fn area(&self, index: u32) -> f32 {
        let node = &self.binary[index as usize];
        half_area(node.min, node.max)
    }

    fn emit_leaf(&mut self, leaf: &BinaryNode) -> u32 {
        let mut v0 = [[0f32; 4]; 3];
        let mut e1 = [[0f32; 4]; 3];
        let mut e2 = [[0f32; 4]; 3];
        let mut triangles = [u32::MAX; 4];
        for lane in 0..leaf.count as usize {
            let triangle = self.order[leaf.first as usize + lane];
            let (a, b, c) = (self.corners)(triangle as usize);
            triangles[lane] = triangle;
            for axis in 0..3 {
                v0[axis][lane] = a[axis];
                e1[axis][lane] = b[axis] - a[axis];
                e2[axis][lane] = c[axis] - a[axis];
            }
        }
        let block = self.blocks.len() as u32;
        self.blocks.push(Tri4 {
            v0: [v0[0].into(), v0[1].into(), v0[2].into()],
            e1: [e1[0].into(), e1[1].into(), e1[2].into()],
            e2: [e2[0].into(), e2[1].into(), e2[2].into()],
        });
        self.block_triangles.push(triangles);
        LEAF_FLAG | block
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Deterministic generator, so a failure reproduces.
    struct Random(u32);

    impl Random {
        fn next(&mut self) -> f32 {
            self.0 ^= self.0 << 13;
            self.0 ^= self.0 >> 17;
            self.0 ^= self.0 << 5;
            (self.0 >> 8) as f32 / 16_777_216.0
        }

        fn vec3(&mut self) -> Vec3 {
            Vec3::new(self.next(), self.next(), self.next())
        }
    }

    fn brute_force(
        positions: &[Vec3],
        indices: &[[u32; 3]],
        origin: Vec3,
        direction: Vec3,
        t_max: f32,
    ) -> Option<Hit> {
        let mut best: Option<Hit> = None;
        for (triangle, [a, b, c]) in indices.iter().enumerate() {
            let v0 = positions[*a as usize];
            let (e1, e2) = (positions[*b as usize] - v0, positions[*c as usize] - v0);
            let p = direction.cross(e2);
            let determinant = e1.dot(p);
            if determinant.abs() < DETERMINANT_EPSILON {
                continue;
            }
            let t = origin - v0;
            let u = t.dot(p) / determinant;
            let q = t.cross(e1);
            let v = direction.dot(q) / determinant;
            let distance = e2.dot(q) / determinant;
            if u < 0.0
                || v < 0.0
                || u + v > 1.0
                || distance <= 1e-4
                || distance >= best.map_or(t_max, |h| h.distance)
            {
                continue;
            }
            best = Some(Hit {
                distance,
                triangle: triangle as u32,
                u,
                v,
            });
        }
        best
    }

    fn soup(count: usize, seed: u32) -> (Vec<Vec3>, Vec<[u32; 3]>) {
        let mut random = Random(seed);
        let mut positions = Vec::new();
        let mut indices = Vec::new();
        for triangle in 0..count as u32 {
            let centre = random.vec3() * 20.0;
            for _ in 0..3 {
                positions.push(centre + (random.vec3() - 0.5) * 1.5);
            }
            indices.push([triangle * 3, triangle * 3 + 1, triangle * 3 + 2]);
        }
        (positions, indices)
    }

    #[test]
    fn empty_hierarchy_never_hits() {
        let bvh = Bvh::build(&[], &[]);
        assert!(bvh.intersect(Vec3::ZERO, Vec3::X, 0.0, f32::MAX).is_none());
        assert!(!bvh.occluded(Vec3::ZERO, Vec3::X, 0.0, f32::MAX));
    }

    #[test]
    fn single_triangle_reports_barycentrics() {
        let positions = [
            Vec3::new(0.0, 0.0, 5.0),
            Vec3::new(4.0, 0.0, 5.0),
            Vec3::new(0.0, 4.0, 5.0),
        ];
        let bvh = Bvh::build(&positions, &[[0, 1, 2]]);
        let hit = bvh
            .intersect(Vec3::new(1.0, 2.0, 0.0), Vec3::Z, 1e-4, f32::MAX)
            .expect("the ray faces the triangle");
        assert!(
            (hit.distance - 5.0).abs() < 1e-5
                && (hit.u - 0.25).abs() < 1e-5
                && (hit.v - 0.5).abs() < 1e-5
        );
        assert!(
            bvh.intersect(Vec3::new(3.0, 3.0, 0.0), Vec3::Z, 1e-4, f32::MAX)
                .is_none()
        );
        assert!(
            bvh.intersect(Vec3::new(1.0, 2.0, 0.0), Vec3::Z, 1e-4, 4.0)
                .is_none()
        );
    }

    #[test]
    fn matches_brute_force_on_random_triangles() {
        for (count, seed) in [(3usize, 7u32), (37, 11), (5000, 19)] {
            let (positions, indices) = soup(count, seed);
            let bvh = Bvh::build(&positions, &indices);
            let mut random = Random(seed + 1);
            for _ in 0..4000 {
                let origin = random.vec3() * 20.0;
                let direction = (random.vec3() - 0.5).normalize();
                let expected = brute_force(&positions, &indices, origin, direction, f32::MAX);
                let actual = bvh.intersect(origin, direction, 1e-4, f32::MAX);
                match (expected, actual) {
                    (None, None) => {}
                    (Some(e), Some(a)) => assert!(
                        (e.distance - a.distance).abs() <= 1e-3 * e.distance.max(1.0),
                        "distance {} vs {} with {count} triangles",
                        e.distance,
                        a.distance
                    ),
                    other => panic!("hit disagreement with {count} triangles: {other:?}"),
                }
                assert_eq!(
                    expected.is_some(),
                    bvh.occluded(origin, direction, 1e-4, f32::MAX)
                );
            }
        }
    }

    #[test]
    fn flat_hierarchy_is_depth_first_and_covers_every_triangle() {
        let (positions, indices) = soup(20_000, 5);
        let flat = FlatBvh::build(&positions, &indices);
        let mut seen = vec![false; indices.len()];
        let mut stack = vec![0usize];
        while let Some(index) = stack.pop() {
            let node = flat.nodes[index];
            if node.count == 0 {
                assert!(
                    node.right_or_first as usize > index + 1
                        && (node.right_or_first as usize) < flat.nodes.len()
                );
                stack.extend([index + 1, node.right_or_first as usize]);
                continue;
            }
            assert!(node.count as usize <= LEAF_SIZE);
            for slot in node.right_or_first..node.right_or_first + node.count {
                let triangle = flat.triangle_order[slot as usize] as usize;
                assert!(!seen[triangle], "triangle {triangle} is in two leaves");
                seen[triangle] = true;
                for corner in indices[triangle] {
                    let p = positions[corner as usize];
                    assert!(
                        p.cmpge(Vec3::from(node.min)).all() && p.cmple(Vec3::from(node.max)).all()
                    );
                }
            }
        }
        assert!(seen.iter().all(|&covered| covered));
    }

    #[test]
    fn coincident_triangles_still_build_small_leaves() {
        let positions = [Vec3::ZERO, Vec3::X, Vec3::Y];
        let indices = vec![[0u32, 1, 2]; 1000];
        let bvh = Bvh::build(&positions, &indices);
        let hit = bvh.intersect(Vec3::new(0.2, 0.2, -1.0), Vec3::Z, 1e-4, f32::MAX);
        assert!(hit.is_some_and(|h| (h.distance - 1.0).abs() < 1e-5));
    }

    #[test]
    fn axis_aligned_rays_cross_flat_boxes() {
        // A ground quad in the plane z = 0: its box has no thickness and the ray has two zero components.
        let positions = [
            Vec3::new(-1., -1., 0.),
            Vec3::new(1., -1., 0.),
            Vec3::new(1., 1., 0.),
            Vec3::new(-1., 1., 0.),
        ];
        let bvh = Bvh::build(&positions, &[[0, 1, 2], [0, 2, 3]]);
        assert!(
            bvh.intersect(Vec3::new(0.3, -0.2, 3.0), Vec3::NEG_Z, 1e-4, f32::MAX)
                .is_some()
        );
        assert!(bvh.occluded(Vec3::new(0.3, -0.2, 3.0), Vec3::NEG_Z, 1e-4, 10.0));
    }
}
