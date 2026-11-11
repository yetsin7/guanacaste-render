//! The triangle soup the mesher produces: positions, one surface kind per triangle, and which element each run of
//! triangles belongs to.

use crate::document::Id;
use glam::Vec3;

/// What a surface is made of; renderers map it to a material. The numbers are those of `SurfaceKind` in the C#
/// application (`geometry/building-mesher.cs`) and must stay in step with it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[repr(u8)]
pub enum SurfaceKind {
    Wall = 0,
    Floor = 1,
    Glass = 2,
    Ground = 3,
    DoorLeaf = 4,
    OpeningFrame = 5,
    Structure = 6,
    Foliage = 7,
    Bark = 8,
    Earth = 9,
    Roof = 10,
    Leaf = 11,
    InteriorWall = 12,
    Metal = 13,
    LightEmitter = 14,
    Upholstery = 15,
    Bedding = 16,
    Ceramic = 17,
    LightOff = 18,
    SpotEmitter = 19,
    WallBrick = 20,
    WallBlock = 21,
    WallConcrete = 22,
    WallStone = 23,
    WallWood = 24,
    WallMetal = 25,
    Mirror = 26,
    PipeCold = 27,
    PipeHot = 28,
    PipeDrain = 29,
    PipeStorm = 30,
    SolarCell = 31,
    FireRed = 32,
    Water = 33,
    CarPaint = 34,
    CarPaintSilver = 35,
    Rubber = 36,
    Asphalt = 37,
    TintedGlass = 38,
    Ocean = 39,
    Rock = 40,
}

impl SurfaceKind {
    pub const COUNT: usize = 41;
}

/// A run of triangles `[first, end)` that belongs to one element of the project.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ElementRange {
    pub first: usize,
    pub end: usize,
    pub element: Id,
}

/// Triangles in Z-up model coordinates, in metres. Every triangle has its own three vertices.
#[derive(Debug, Default)]
pub struct SceneMesh {
    pub positions: Vec<Vec3>,
    pub kinds: Vec<SurfaceKind>,
    /// Sorted, non-overlapping; triangles outside every range belong to no element (the ground).
    pub element_ranges: Vec<ElementRange>,
}

impl SceneMesh {
    pub fn triangle_count(&self) -> usize {
        self.kinds.len()
    }

    /// The three corners of a triangle.
    pub fn triangle(&self, index: usize) -> [Vec3; 3] {
        [self.positions[index * 3], self.positions[index * 3 + 1], self.positions[index * 3 + 2]]
    }

    pub fn add_triangle(&mut self, a: Vec3, b: Vec3, c: Vec3, kind: SurfaceKind) {
        self.positions.extend([a, b, c]);
        self.kinds.push(kind);
    }

    /// A planar quad given counter-clockwise as seen from its front.
    pub fn add_quad(&mut self, a: Vec3, b: Vec3, c: Vec3, d: Vec3, kind: SurfaceKind) {
        self.add_triangle(a, b, c, kind);
        self.add_triangle(a, c, d, kind);
    }

    /// Attributes every triangle added since `first` to one element.
    pub fn mark_element(&mut self, element: Id, first: usize) {
        if first < self.triangle_count() {
            self.element_ranges.push(ElementRange { first, end: self.triangle_count(), element });
        }
    }

    /// Bounds of every vertex; a unit box when the mesh is empty.
    pub fn bounds(&self) -> (Vec3, Vec3) {
        if self.positions.is_empty() {
            return (Vec3::ZERO, Vec3::ONE);
        }
        self.positions.iter().fold((Vec3::MAX, Vec3::MIN), |(min, max), &p| (min.min(p), max.max(p)))
    }
}
