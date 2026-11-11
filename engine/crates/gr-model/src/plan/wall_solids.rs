//! Splits a wall into the solid boxes and wedges left around its hosted openings.
//!
//! Ported from `WallGeometry.Solids` in `WallOpenings.cs`.

use crate::document::{Wall, WallOpening};

/// A portion of solid wall between two stations along its centreline. Heights are metres above the wall's level.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WallSolid {
    pub from: f64,
    pub to: f64,
    pub bottom: f64,
    pub top: f64,
    top_at_from: Option<f64>,
    top_at_to: Option<f64>,
}

impl WallSolid {
    fn flat(from: f64, to: f64, bottom: f64, top: f64) -> WallSolid {
        WallSolid { from, to, bottom, top, top_at_from: None, top_at_to: None }
    }

    pub fn top_start(&self) -> f64 {
        self.top_at_from.unwrap_or(self.top)
    }

    pub fn top_end(&self) -> f64 {
        self.top_at_to.unwrap_or(self.top)
    }

    /// Whether the top slopes, which makes the solid a wedge rather than a box.
    pub fn sloped(&self) -> bool {
        (self.top_start() - self.top_end()).abs() > 1e-9
    }

    /// Height of the top at a distance along the wall, continuing the slope past either end.
    pub fn top_height_at(&self, along: f64) -> f64 {
        if self.to - self.from < 1e-9 {
            self.top
        } else {
            self.top_start() + (self.top_end() - self.top_start()) * (along - self.from) / (self.to - self.from)
        }
    }
}

/// The solids of `wall` around `openings`, which must all be hosted by it and must not overlap.
pub fn wall_solids(wall: &Wall, openings: &[&WallOpening]) -> Vec<WallSolid> {
    let mut hosted: Vec<&WallOpening> = openings.to_vec();
    // A stable sort, as in the application: openings that begin at the same station keep their document order.
    hosted.sort_by(|a, b| a.begin().total_cmp(&b.begin()));

    let length = wall.length();
    let mut parts = Vec::new();
    let mut cursor = 0.0;
    for opening in hosted {
        if opening.begin() - cursor >= 0.000001 {
            parts.push(WallSolid::flat(cursor, opening.begin(), 0.0, wall.height));
        }
        if opening.sill > 0.0 {
            parts.push(WallSolid::flat(opening.begin(), opening.end(), 0.0, opening.sill));
        }
        let top = opening.sill + opening.height;
        if top < wall.height {
            parts.push(WallSolid::flat(opening.begin(), opening.end(), top, wall.height));
        }
        cursor = opening.end();
    }
    if length - cursor >= 0.000001 {
        parts.push(WallSolid::flat(cursor, length, 0.0, wall.height));
    }

    let shaped = if wall.shaping_profile().is_some() { follow_profile(wall, parts) } else { parts };
    if wall.base_offset == 0.0 { shaped } else { reshape_base(wall, shaped) }
}

/// Cuts every solid that reaches the top of the wall at each point of the profile, so the top of each piece is one
/// straight slope. Solids below an opening stay as they are.
fn follow_profile(wall: &Wall, parts: Vec<WallSolid>) -> Vec<WallSolid> {
    let profile = wall.shaping_profile().unwrap_or_default();
    let mut result = Vec::new();
    for part in parts {
        if part.top < wall.height - 1e-9 {
            result.push(part);
            continue;
        }
        let mut stations: Vec<f64> =
            profile.iter().map(|point| point.x).filter(|&x| x > part.from + 1e-6 && x < part.to - 1e-6).collect();
        stations.push(part.from);
        stations.push(part.to);
        stations.sort_by(f64::total_cmp);
        for pair in stations.windows(2) {
            let (from, to) = (wall.top_height_at(pair[0]), wall.top_height_at(pair[1]));
            result.push(WallSolid {
                from: pair[0],
                to: pair[1],
                bottom: part.bottom,
                top: from.max(to),
                top_at_from: Some(from),
                top_at_to: Some(to),
            });
        }
    }
    result
}

/// Moves the base of the solids to the wall's base: a raised base trims what lies below it, and a base below the
/// level lengthens every piece that started at the level. Openings keep their place, measured from the level.
fn reshape_base(wall: &Wall, parts: Vec<WallSolid>) -> Vec<WallSolid> {
    let mut result = Vec::new();
    for part in parts {
        if wall.base_offset > 0.0 {
            let highest = part.top.max(part.top_start().max(part.top_end()));
            if highest <= wall.base_offset + 1e-9 {
                continue;
            }
            result.push(if part.bottom < wall.base_offset { WallSolid { bottom: wall.base_offset, ..part } } else { part });
        } else {
            result.push(if part.bottom <= 1e-9 { WallSolid { bottom: wall.base_offset, ..part } } else { part });
        }
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::document::Id;
    use crate::plan::Point2;

    fn wall(height: f64) -> Wall {
        Wall {
            id: Id(1),
            level_id: Id(2),
            start: Point2::new(0.0, 0.0),
            end: Point2::new(6.0, 0.0),
            thickness: 0.2,
            height,
            wall_type_id: Id(3),
            base_offset: 0.0,
            start_join: 0,
            end_join: 0,
            top_profile: None,
        }
    }

    fn opening(offset: f64, width: f64, sill: f64, height: f64) -> WallOpening {
        WallOpening { id: Id(9), wall_id: Id(1), kind: 1, offset, width, sill, height, opening_type_id: Id(4), flip_hand: false }
    }

    fn volume(parts: &[WallSolid]) -> f64 {
        parts.iter().map(|p| (p.to - p.from) * ((p.top_start() + p.top_end()) / 2.0 - p.bottom)).sum()
    }

    #[test]
    fn a_window_leaves_the_wall_minus_its_own_area() {
        let wall = wall(3.0);
        let window = opening(3.0, 2.0, 0.9, 1.2);
        let parts = wall_solids(&wall, &[&window]);
        // Left of it, the sill piece, the lintel piece, right of it.
        assert_eq!(parts.len(), 4);
        assert!((volume(&parts) - (6.0 * 3.0 - 2.0 * 1.2)).abs() < 1e-12);
    }

    #[test]
    fn a_door_leaves_no_piece_under_it_and_openings_are_taken_in_order() {
        let wall = wall(3.0);
        let (door, window) = (opening(1.0, 0.9, 0.0, 2.1), opening(4.0, 1.0, 1.0, 1.0));
        let parts = wall_solids(&wall, &[&window, &door]);
        assert_eq!(parts.len(), 6);
        assert!(parts.windows(2).all(|pair| pair[0].from <= pair[1].from));
        assert!((volume(&parts) - (18.0 - 0.9 * 2.1 - 1.0)).abs() < 1e-12);
    }

    #[test]
    fn a_gable_profile_makes_wedges_and_a_raised_base_trims_them() {
        let mut gable = wall(4.0);
        gable.top_profile = Some(vec![Point2::new(0.0, 2.0), Point2::new(3.0, 4.0), Point2::new(6.0, 2.0)]);
        let parts = wall_solids(&gable, &[]);
        assert_eq!(parts.len(), 2);
        assert!(parts.iter().all(WallSolid::sloped));
        assert!((volume(&parts) - 18.0).abs() < 1e-12);
        assert!((parts[0].top_height_at(1.5) - 3.0).abs() < 1e-12);

        gable.base_offset = 1.0;
        assert!((volume(&wall_solids(&gable, &[])) - 12.0).abs() < 1e-12);
    }
}
