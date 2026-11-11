//! Doors and windows: the lining, leaves, sashes, glass, sills, ironmongery and security bars of a hosted opening.
//!
//! Ported from `AddOpeningAssembly` and its helpers in `geometry/building-mesher.cs`. Every member is a box in
//! wall coordinates, added in the same order as the original.

use super::prism::WallFrame;
use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::{OpeningType, Wall, WallOpening, leaf_style, opening_kind, operation};
use crate::plan::Point2;

/// Adds boxes in the coordinates of one wall.
struct Joinery<'a> {
    mesh: &'a mut SceneMesh,
    frame: WallFrame,
}

impl Joinery<'_> {
    /// `from`..`to` along the wall, `side_a`..`side_b` across it, `bottom`..`top` in elevation.
    #[allow(clippy::too_many_arguments)]
    fn block(&mut self, from: f64, to: f64, side_a: f64, side_b: f64, bottom: f64, top: f64, kind: SurfaceKind) {
        self.frame.block(self.mesh, from, to, side_a, side_b, bottom, top, kind);
    }
}

/// The leaf thickness an opening carries inside its host wall: never thicker than the wall; zero leaves a hole.
fn leaf_depth(kind: &OpeningType, wall: &Wall) -> f64 {
    if kind.depth > 0.0 { kind.depth.min(wall.thickness) } else { 0.0 }
}

/// Builds a hosted opening in layers: a lining recessed inside the rough opening, then a door leaf or a window
/// sash with its glass and sill. `elevation` is that of the wall's level.
pub fn add_opening_assembly(mesh: &mut SceneMesh, wall: &Wall, opening: &WallOpening, kind: &OpeningType, elevation: f64) {
    // A wall opening is a plain hole: no lining, sash, leaf or sill.
    if kind.operation == operation::VOID {
        return;
    }
    let mut joinery = Joinery { mesh, frame: WallFrame::new(wall) };
    let j = &mut joinery;
    let bottom = elevation + opening.sill;
    let top = bottom + opening.height;
    let lining = kind.frame_width.min(opening.width.min(opening.height) / 6.0);
    // The lining sits 1 cm inside both wall faces, so the reveal reads as a step rather than a flush cut.
    let lining_half = wall.thickness / 2.0 - 0.01f64.min(wall.thickness / 4.0);
    let (begin, end) = (opening.begin(), opening.end());
    j.block(begin, begin + lining, -lining_half, lining_half, bottom, top, SurfaceKind::OpeningFrame);
    j.block(end - lining, end, -lining_half, lining_half, bottom, top, SurfaceKind::OpeningFrame);
    j.block(begin + lining, end - lining, -lining_half, lining_half, top - lining, top, SurfaceKind::OpeningFrame);

    if opening.kind == opening_kind::WINDOW {
        let swings = matches!(kind.operation, operation::CASEMENT | operation::AWNING);
        if kind.operation == operation::STANDARD || swings {
            window_sash(j, opening, kind, lining, bottom, top);
        } else {
            operated_window(j, opening, kind, lining, bottom, top);
        }
        if swings {
            window_handles(j, opening, kind, lining, bottom, top);
        }
        // A projecting sill that overhangs both wall faces and the opening by 3 cm.
        let projection = wall.thickness / 2.0 + 0.02;
        j.block(begin - 0.03, end + 0.03, -projection, projection, bottom - 0.03, bottom, SurfaceKind::OpeningFrame);
        return;
    }

    let depth = if kind.operation == operation::REVOLVING { kind.depth } else { leaf_depth(kind, wall) };
    if depth <= 0.0 {
        return;
    }
    if kind.operation == operation::STANDARD {
        door_leaves(j, opening, depth, lining, bottom, top, kind.glazed_leaf, kind.leaf_style);
        door_hardware(j, opening, depth, lining, bottom);
    } else {
        operated_door(j, wall, opening, kind, depth, lining, bottom, top);
    }
}

/// The sash frame and glass inside the lining, one light per division of the window.
fn window_sash(j: &mut Joinery<'_>, opening: &WallOpening, kind: &OpeningType, lining: f64, bottom: f64, top: f64) {
    let member = kind.frame_width.min(0.07);
    const SASH_HALF: f64 = 0.025;
    const GLASS_HALF: f64 = 0.003;
    let (from, to) = (opening.begin() + lining, opening.end() - lining);
    let (sash_bottom, sash_top) = (bottom + lining, top - lining);
    let lights = light_count(opening, kind);
    let light_width = (to - from) / lights as f64;
    for light in 0..lights {
        let light_from = from + light as f64 * light_width;
        let light_to = light_from + light_width;
        j.block(light_from, light_from + member, -SASH_HALF, SASH_HALF, sash_bottom, sash_top, SurfaceKind::OpeningFrame);
        j.block(light_to - member, light_to, -SASH_HALF, SASH_HALF, sash_bottom, sash_top, SurfaceKind::OpeningFrame);
        j.block(light_from + member, light_to - member, -SASH_HALF, SASH_HALF, sash_top - member, sash_top, SurfaceKind::OpeningFrame);
        j.block(light_from + member, light_to - member, -SASH_HALF, SASH_HALF, sash_bottom, sash_bottom + member, SurfaceKind::OpeningFrame);
        j.block(light_from + member, light_to - member, -GLASS_HALF, GLASS_HALF, sash_bottom + member, sash_top - member, SurfaceKind::Glass);
    }
}

/// Lights a window is divided into: its type's count, or two when it is wider than a metre.
fn light_count(opening: &WallOpening, kind: &OpeningType) -> i32 {
    if kind.glazing_light_count > 0 {
        kind.glazing_light_count
    } else if opening.width > 1.0 {
        2
    } else {
        1
    }
}

/// The sashes of sliding, hung and louvred windows inside the lining.
fn operated_window(j: &mut Joinery<'_>, opening: &WallOpening, kind: &OpeningType, lining: f64, bottom: f64, top: f64) {
    let member = kind.frame_width.min(0.07);
    const SASH_HALF: f64 = 0.02;
    const GLASS_HALF: f64 = 0.003;
    const PLANE: f64 = 0.0225;
    let (from, to) = (opening.begin() + lining, opening.end() - lining);
    let (sash_bottom, sash_top) = (bottom + lining, top - lining);
    let columns = if kind.operation == operation::SLIDING { kind.glazing_light_count.max(2) } else { 1 };
    let rows = if kind.operation == operation::HUNG { 2 } else { 1 };
    let cell_width = (to - from) / columns as f64;
    let cell_height = (sash_top - sash_bottom) / rows as f64;
    let louvred = kind.operation == operation::LOUVERED;
    for row in 0..rows {
        for column in 0..columns {
            let x0 = from + column as f64 * cell_width;
            let x1 = x0 + cell_width;
            let y0 = sash_bottom + row as f64 * cell_height;
            let y1 = y0 + cell_height;
            // Neighbouring sashes sit on alternate tracks so they can pass one another.
            let shift = if louvred {
                0.0
            } else if (row + column) % 2 == 0 {
                -PLANE
            } else {
                PLANE
            };
            j.block(x0, x0 + member, shift - SASH_HALF, shift + SASH_HALF, y0, y1, SurfaceKind::OpeningFrame);
            j.block(x1 - member, x1, shift - SASH_HALF, shift + SASH_HALF, y0, y1, SurfaceKind::OpeningFrame);
            j.block(x0 + member, x1 - member, shift - SASH_HALF, shift + SASH_HALF, y1 - member, y1, SurfaceKind::OpeningFrame);
            j.block(x0 + member, x1 - member, shift - SASH_HALF, shift + SASH_HALF, y0, y0 + member, SurfaceKind::OpeningFrame);
            if louvred {
                // Overlapping glass blades, each a thin horizontal slat.
                const PITCH: f64 = 0.07;
                const BLADE: f64 = 0.045;
                let mut level = y0 + member;
                while level + BLADE <= y1 - member + 1e-9 {
                    j.block(x0 + member, x1 - member, -0.035, 0.035, level, level + 0.006, SurfaceKind::Glass);
                    level += PITCH;
                }
            } else {
                j.block(x0 + member, x1 - member, shift - GLASS_HALF, shift + GLASS_HALF, y0 + member, y1 - member, SurfaceKind::Glass);
            }
        }
    }
}

/// An operating handle on the free stile of each casement light, or at the base of an awning sash.
fn window_handles(j: &mut Joinery<'_>, opening: &WallOpening, kind: &OpeningType, lining: f64, bottom: f64, top: f64) {
    let member = kind.frame_width.min(0.07);
    let (from, to) = (opening.begin() + lining, opening.end() - lining);
    let lights = light_count(opening, kind);
    let light_width = (to - from) / lights as f64;
    if kind.operation == operation::AWNING {
        let center = (from + to) / 2.0;
        let middle = bottom + lining + member / 2.0;
        j.block(center - 0.04, center + 0.04, 0.02, 0.04, middle - 0.01, middle + 0.01, SurfaceKind::OpeningFrame);
        return;
    }
    let handle_height = bottom + (top - bottom) / 2.0;
    for light in 0..lights {
        // Handles alternate sides so adjacent casements meet at their handles.
        let edge = if light % 2 == 0 {
            from + (light + 1) as f64 * light_width - member
        } else {
            from + light as f64 * light_width + member
        };
        j.block(edge - 0.01, edge + 0.01, 0.02, 0.04, handle_height - 0.06, handle_height + 0.06, SurfaceKind::OpeningFrame);
    }
}

/// The door leaves, one per half of a wide opening, each clear of the lining by a few millimetres.
#[allow(clippy::too_many_arguments)]
fn door_leaves(j: &mut Joinery<'_>, opening: &WallOpening, depth: f64, lining: f64, bottom: f64, top: f64, glazed: bool, style: i32) {
    const CLEARANCE: f64 = 0.004;
    const THRESHOLD_GAP: f64 = 0.012;
    let leaf_count = if opening.width > 1.2 { 2 } else { 1 };
    let left = opening.begin() + lining + CLEARANCE;
    let right = opening.end() - lining - CLEARANCE;
    let leaf_width = (right - left) / leaf_count as f64;
    for leaf in 0..leaf_count {
        let from = left + leaf as f64 * leaf_width;
        door_leaf(j, from, from + leaf_width, depth, bottom + THRESHOLD_GAP, top - CLEARANCE, glazed, style);
    }
}

/// One door leaf in the style of its type.
#[allow(clippy::too_many_arguments)]
fn door_leaf(j: &mut Joinery<'_>, from: f64, to: f64, depth: f64, bottom: f64, top: f64, glazed: bool, style: i32) {
    use SurfaceKind::{DoorLeaf, Glass, Metal, OpeningFrame};
    let half = depth / 2.0;
    match style {
        leaf_style::FLUSH => {
            // A hollow-core flush leaf: one smooth slab with hardwood lipping at its edges.
            j.block(from + 0.012, to - 0.012, -half, half, bottom, top - 0.012, DoorLeaf);
            j.block(from, from + 0.012, -half, half, bottom, top, DoorLeaf);
            j.block(to - 0.012, to, -half, half, bottom, top, DoorLeaf);
            j.block(from + 0.012, to - 0.012, -half, half, top - 0.012, top, DoorLeaf);
        }
        leaf_style::SIX_PANEL => {
            const STILE: f64 = 0.11;
            j.block(from, from + STILE, -half, half, bottom, top, DoorLeaf);
            j.block(to - STILE, to, -half, half, bottom, top, DoorLeaf);
            let middle = (from + to) / 2.0;
            j.block(middle - 0.05, middle + 0.05, -half, half, bottom, top, DoorLeaf);
            // Rows: tall bottom pair, middle pair, short top pair, as on a colonial door.
            let heights = [bottom, bottom + 0.20, bottom + 0.95, bottom + 1.12, top - 0.55, top - 0.40, top - 0.10, top];
            for rail in heights.chunks_exact(2) {
                j.block(from + STILE, to - STILE, -half, half, rail[0], rail[1], DoorLeaf);
            }
            for row in (1..heights.len() - 1).step_by(2) {
                for (left, right) in [(from + STILE, middle - 0.05), (middle + 0.05, to - STILE)] {
                    j.block(left, right, -half * 0.35, half * 0.35, heights[row], heights[row + 1], DoorLeaf);
                    if right - left > 0.1 && heights[row + 1] - heights[row] > 0.1 {
                        j.block(left + 0.035, right - 0.035, -half * 0.7, half * 0.7, heights[row] + 0.035, heights[row + 1] - 0.035, DoorLeaf);
                    }
                }
            }
        }
        leaf_style::LOUVERED => {
            const STILE: f64 = 0.07;
            j.block(from, from + STILE, -half, half, bottom, top, DoorLeaf);
            j.block(to - STILE, to, -half, half, bottom, top, DoorLeaf);
            j.block(from + STILE, to - STILE, -half, half, top - 0.08, top, DoorLeaf);
            j.block(from + STILE, to - STILE, -half, half, bottom, bottom + 0.10, DoorLeaf);
            // Slats at 45 degrees, drawn as two overlapping strips stepped through the leaf's depth.
            let mut z = bottom + 0.12;
            while z < top - 0.10 {
                j.block(from + STILE, to - STILE, -half * 0.9, 0.0, z, z + 0.022, DoorLeaf);
                j.block(from + STILE, to - STILE, 0.0, half * 0.9, z + 0.016, z + 0.038, DoorLeaf);
                z += 0.045;
            }
        }
        leaf_style::FULL_GLASS => {
            j.block(from, to, -0.006, 0.006, bottom + 0.01, top - 0.01, Glass);
            // Patch fittings at the pivot corners.
            for z in [bottom, top - 0.07] {
                j.block(from, from + 0.16, -0.012, 0.012, z, z + 0.07, Metal);
            }
        }
        leaf_style::SECURITY => {
            const MEMBER: f64 = 0.05;
            j.block(from, from + MEMBER, -half, half, bottom, top, OpeningFrame);
            j.block(to - MEMBER, to, -half, half, bottom, top, OpeningFrame);
            j.block(from + MEMBER, to - MEMBER, -half, half, top - MEMBER, top, OpeningFrame);
            j.block(from + MEMBER, to - MEMBER, -half, half, bottom, bottom + 0.35, OpeningFrame);
            j.block(from + MEMBER, to - MEMBER, -0.003, 0.003, bottom + 0.35, top - MEMBER, Glass);
            // A grille of square bars over the glass, on the outside face.
            let mut x = from + MEMBER + 0.1;
            while x < to - MEMBER - 0.05 {
                j.block(x - 0.008, x + 0.008, half - 0.016, half, bottom + 0.35, top - MEMBER, OpeningFrame);
                x += 0.1;
            }
            for z in [bottom + 0.9, bottom + 1.5] {
                j.block(from + MEMBER, to - MEMBER, half - 0.02, half, z, z + 0.03, OpeningFrame);
            }
        }
        _ => door_panel(j, from, to, depth, bottom, top, glazed),
    }
}

/// A panelled leaf as a joiner builds it: full-depth stiles, a top rail, a lock rail at handle height and a deep
/// bottom rail, framing two raised-and-fielded panels, or glass above the lock rail for a glazed leaf.
fn door_panel(j: &mut Joinery<'_>, from: f64, to: f64, depth: f64, bottom: f64, top: f64, glazed: bool) {
    const STILE: f64 = 0.11;
    const TOP_RAIL: f64 = 0.11;
    const LOCK_RAIL: f64 = 0.16;
    const BOTTOM_RAIL: f64 = 0.22;
    const BEVEL: f64 = 0.04;
    let half = depth / 2.0;
    let kind = SurfaceKind::DoorLeaf;
    let lock_bottom = (bottom + 0.88).min(top - TOP_RAIL - 0.4);
    j.block(from, from + STILE, -half, half, bottom, top, kind);
    j.block(to - STILE, to, -half, half, bottom, top, kind);
    j.block(from + STILE, to - STILE, -half, half, top - TOP_RAIL, top, kind);
    j.block(from + STILE, to - STILE, -half, half, lock_bottom, lock_bottom + LOCK_RAIL, kind);
    j.block(from + STILE, to - STILE, -half, half, bottom, bottom + BOTTOM_RAIL, kind);
    for (low, high, is_upper) in [(bottom + BOTTOM_RAIL, lock_bottom, false), (lock_bottom + LOCK_RAIL, top - TOP_RAIL, true)] {
        if high - low < 0.05 {
            continue;
        }
        if glazed && is_upper {
            j.block(from + STILE, to - STILE, -0.004, 0.004, low, high, SurfaceKind::Glass);
            continue;
        }
        // Field: thin, set back in the frame. Raised centre: thicker, inset by the bevel all round.
        j.block(from + STILE, to - STILE, -half * 0.35, half * 0.35, low, high, kind);
        if to - from - 2.0 * (STILE + BEVEL) > 0.05 && high - low - 2.0 * BEVEL > 0.05 {
            j.block(from + STILE + BEVEL, to - STILE - BEVEL, -half * 0.7, half * 0.7, low + BEVEL, high - BEVEL, kind);
        }
    }
}

/// Ironmongery of a swing door: a lever handle on a rose on both faces at 95 cm near the latch edge, and three
/// butt hinges on the hinge edge. Double doors get a handle on each meeting stile and no visible hinges.
fn door_hardware(j: &mut Joinery<'_>, opening: &WallOpening, depth: f64, lining: f64, bottom: f64) {
    let (left, right) = (opening.begin() + lining, opening.end() - lining);
    let half = depth / 2.0;
    let handle_height = bottom + 0.95;
    let double = opening.width > 1.2;
    let middle = (left + right) / 2.0;
    let latches: &[(f64, f64)] = if double {
        &[(middle - 0.07, -1.0), (middle + 0.07, 1.0)]
    } else if opening.flip_hand {
        &[(left + 0.065, 1.0)]
    } else {
        &[(right - 0.065, -1.0)]
    };
    for &(at, toward) in latches {
        for face in [-1.0f64, 1.0] {
            let plate = face * half;
            let across = |reach: f64| (plate.min(plate + face * reach), plate.max(plate + face * reach));
            // Rose: a short boss, then the spindle, then the lever running toward the hinge side.
            let (near, far) = across(0.01);
            j.block(at - 0.026, at + 0.026, near, far, handle_height - 0.026, handle_height + 0.026, SurfaceKind::Metal);
            let (near, far) = across(0.055);
            j.block(at - 0.009, at + 0.009, near, far, handle_height - 0.009, handle_height + 0.009, SurfaceKind::Metal);
            let (lever_from, lever_to) = (at.min(at + toward * 0.13), at.max(at + toward * 0.13));
            let (lever_near, lever_far) = (plate + face * 0.045, plate + face * 0.065);
            j.block(lever_from, lever_to, lever_near.min(lever_far), lever_near.max(lever_far), handle_height - 0.01, handle_height + 0.01, SurfaceKind::Metal);
        }
    }
    if double {
        return;
    }
    let hinge = if opening.flip_hand { right - 0.004 } else { left + 0.004 };
    for height in [bottom + 0.25, bottom + 1.05, bottom + opening.height - 0.25] {
        j.block(hinge - 0.006, hinge + 0.006, -half * 0.9, half * 0.9, height - 0.05, height + 0.05, SurfaceKind::Metal);
    }
}

/// The leaves of a door that does not swing: sliding panels on two tracks, a closed pocket leaf, narrow folding
/// leaves, overhead sections, or the glass wings of a revolving door.
#[allow(clippy::too_many_arguments)]
fn operated_door(j: &mut Joinery<'_>, wall: &Wall, opening: &WallOpening, kind: &OpeningType, depth: f64, lining: f64, bottom: f64, top: f64) {
    const CLEARANCE: f64 = 0.004;
    const THRESHOLD_GAP: f64 = 0.012;
    let left = opening.begin() + lining + CLEARANCE;
    let right = opening.end() - lining - CLEARANCE;
    let low = bottom + THRESHOLD_GAP;
    let high = top - lining - CLEARANCE;
    let panel_kind = if kind.glazed_leaf { SurfaceKind::Glass } else { SurfaceKind::DoorLeaf };
    match kind.operation {
        operation::SLIDING => {
            // Two panels overlap by one stile and run on separate tracks, one in front of the other.
            const OVERLAP: f64 = 0.05;
            let panel_width = (right - left + OVERLAP) / 2.0;
            let half = depth.min(0.06) / 2.0;
            j.block(left, right, -half * 2.0, half * 2.0, high, high + lining, SurfaceKind::OpeningFrame);
            for panel in 0..2 {
                let from = if panel == 0 { left } else { right - panel_width };
                let shift = if panel == 0 { -half } else { half };
                sliding_panel(j, from, from + panel_width, shift - half, shift + half, low, high, panel_kind);
            }
        }
        operation::POCKET => door_panel(j, left, right, depth.min(0.035), low, high, false),
        operation::FOLDING => {
            // An even number of leaves about 45 cm wide; halves round to the nearest even number, as in .NET.
            let leaves = (((right - left) / 0.45 / 2.0).round_ties_even() as i32 * 2).max(2);
            let leaf_width = (right - left) / leaves as f64;
            for leaf in 0..leaves {
                let from = left + leaf as f64 * leaf_width;
                let shift = if leaf % 2 == 0 { -0.012 } else { 0.012 };
                j.block(from + 0.002, from + leaf_width - 0.002, shift - 0.015, shift + 0.015, low, high, SurfaceKind::DoorLeaf);
            }
        }
        operation::OVERHEAD => {
            let sections = (((high - low) / 0.5).round_ties_even() as i32).max(4);
            let section_height = (high - low) / sections as f64;
            let half = depth.min(wall.thickness) / 2.0;
            for section in 0..sections {
                let from = low + section as f64 * section_height;
                j.block(left, right, -half, half, from + 0.005, from + section_height - 0.005, SurfaceKind::DoorLeaf);
            }
        }
        operation::REVOLVING => {
            let center = (opening.begin() + opening.end()) / 2.0;
            let radius = ((right - left) / 2.0).min(depth / 2.0);
            j.block(center - radius, center + radius, -0.02, 0.02, low, high, SurfaceKind::Glass);
            j.block(center - 0.02, center + 0.02, -radius, radius, low, high, SurfaceKind::Glass);
            j.block(center - 0.05, center + 0.05, -0.05, 0.05, low, high, SurfaceKind::OpeningFrame);
        }
        _ => {}
    }
}

/// One sliding panel: a frame of four members around a pane that is glass or timber.
#[allow(clippy::too_many_arguments)]
fn sliding_panel(j: &mut Joinery<'_>, from: f64, to: f64, side_a: f64, side_b: f64, bottom: f64, top: f64, pane: SurfaceKind) {
    const MEMBER: f64 = 0.05;
    j.block(from, from + MEMBER, side_a, side_b, bottom, top, SurfaceKind::OpeningFrame);
    j.block(to - MEMBER, to, side_a, side_b, bottom, top, SurfaceKind::OpeningFrame);
    j.block(from + MEMBER, to - MEMBER, side_a, side_b, top - MEMBER, top, SurfaceKind::OpeningFrame);
    j.block(from + MEMBER, to - MEMBER, side_a, side_b, bottom, bottom + MEMBER, SurfaceKind::OpeningFrame);
    let middle = (side_a + side_b) / 2.0;
    j.block(from + MEMBER, to - MEMBER, middle - 0.003, middle + 0.003, bottom + MEMBER, top - MEMBER, pane);
}

/// A window grille on the outside face: a flat-bar frame and square bars about every 12 cm. The outside is the
/// side away from the building's interior.
pub fn add_security_bars(mesh: &mut SceneMesh, wall: &Wall, opening: &WallOpening, elevation: f64, inside: &dyn Fn(Point2) -> bool) {
    let length = wall.length().max(1e-9);
    let (normal_x, normal_y) = (-(wall.end.y - wall.start.y) / length, (wall.end.x - wall.start.x) / length);
    let (begin, end) = (opening.begin(), opening.end());
    let middle = (begin + end) / 2.0;
    let center = Point2::new(
        wall.start.x + (wall.end.x - wall.start.x) / length * middle,
        wall.start.y + (wall.end.y - wall.start.y) / length * middle,
    );
    let side = if inside(Point2::new(center.x + normal_x * 0.5, center.y + normal_y * 0.5)) { -1.0 } else { 1.0 };
    let face = side * (wall.thickness / 2.0 + 0.03);
    let (near, far) = if side > 0.0 { (face - 0.016, face) } else { (face, face + 0.016) };
    let bottom = elevation + opening.sill;
    let top = bottom + opening.height;
    let mut j = Joinery { mesh, frame: WallFrame::new(wall) };
    j.block(begin, end, near, far, bottom, bottom + 0.03, SurfaceKind::OpeningFrame);
    j.block(begin, end, near, far, top - 0.03, top, SurfaceKind::OpeningFrame);
    j.block(begin, end, near, far, (bottom + top) / 2.0 - 0.015, (bottom + top) / 2.0 + 0.015, SurfaceKind::OpeningFrame);
    let spacing = 0.1f64.max((end - begin) / ((end - begin) / 0.12).round_ties_even());
    let mut at = begin;
    while at <= end + 1e-9 {
        j.block(begin.max(at - 0.008), end.min(at + 0.008), near, far, bottom, top, SurfaceKind::OpeningFrame);
        at += spacing;
    }
}
