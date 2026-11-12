//! How wall ends meet: mitred corners, butt joints and tees, and the plan outline each wall section takes.
//!
//! Ported from `WallJoinIndex` (`wall-joins.cs`, `wall-join-styles.cs`). A corner shared by exactly two walls of a
//! level is mitred; an end resting on the body of another wall stops at its face; everything else stays square.

use crate::document::{Id, Wall, join_setting};
use crate::plan::Point2;
use std::collections::{HashMap, HashSet};

/// Distance in metres below which two wall end points count as the same corner.
pub const TOLERANCE: f64 = 1e-4;
/// Mitres longer than this many half-thicknesses (very acute corners) fall back to square ends.
const MAXIMUM_MITER_FACTOR: f64 = 4.0;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
enum End {
    Start,
    End,
}

type EndKey = (Id, End);

/// The part a wall end plays in a butt corner.
#[derive(Clone, Copy, PartialEq)]
enum Role {
    /// The end stops at the near face of the other wall.
    Abut,
    /// The end runs on to the far face of the other wall, covering the corner.
    Through,
}

pub struct WallJoins<'a> {
    /// The single other wall end that meets a wall end in a simple corner.
    joins: HashMap<EndKey, (&'a Wall, End)>,
    roles: HashMap<EndKey, Role>,
    /// Free ends that rest on the body of another wall, with that wall.
    tees: HashMap<EndKey, &'a Wall>,
}

fn corner_at(wall: &Wall, end: End) -> Point2 {
    if end == End::Start {
        wall.start
    } else {
        wall.end
    }
}

fn setting_at(wall: &Wall, end: End) -> i32 {
    if end == End::Start {
        wall.start_join
    } else {
        wall.end_join
    }
}

fn stays_apart(setting: i32) -> bool {
    setting == join_setting::SQUARE_OFF || setting == join_setting::DISALLOWED
}

fn square(corner: Point2, offset_x: f64, offset_y: f64) -> (Point2, Point2) {
    (
        Point2::new(corner.x + offset_x, corner.y + offset_y),
        Point2::new(corner.x - offset_x, corner.y - offset_y),
    )
}

/// The wall ends that fall in one grid cell of a level.
type CellEnds<'a> = Vec<(&'a Wall, End, Point2)>;

impl<'a> WallJoins<'a> {
    pub fn build(walls: &'a [Wall]) -> WallJoins<'a> {
        // End points by level and a tolerance-sized grid cell; floor (not round) matters for negative coordinates.
        let cell = |point: Point2| {
            (
                (point.x / TOLERANCE).floor() as i64,
                (point.y / TOLERANCE).floor() as i64,
            )
        };
        let mut cells: HashMap<(Id, i64, i64), CellEnds<'a>> = HashMap::new();
        for wall in walls {
            for end in [End::Start, End::End] {
                let corner = corner_at(wall, end);
                let (x, y) = cell(corner);
                cells
                    .entry((wall.level_id, x, y))
                    .or_default()
                    .push((wall, end, corner));
            }
        }

        let mut joins: HashMap<EndKey, (&Wall, End)> = HashMap::new();
        let mut touching: HashSet<EndKey> = HashSet::new();
        for wall in walls {
            for end in [End::Start, End::End] {
                let corner = corner_at(wall, end);
                let (x, y) = cell(corner);
                // A point within the tolerance lies in its own cell or one of the eight around it.
                let mut neighbours = (-1..=1)
                    .flat_map(|dx| (-1..=1).map(move |dy| (dx, dy)))
                    .filter_map(|(dx, dy)| cells.get(&(wall.level_id, x + dx, y + dy)))
                    .flatten()
                    .filter(|other| {
                        other.0.id != wall.id && corner.distance_to(other.2) <= TOLERANCE
                    });
                let Some(first) = neighbours.next() else {
                    continue;
                };
                touching.insert((wall.id, end));
                if neighbours.next().is_none() {
                    joins.insert((wall.id, end), (first.0, first.1));
                }
            }
        }

        // Nearness is not transitive: A can see both B and C while B sees only A. A join is kept only when the other
        // end independently chose this one, so a corner is never mitred on one side alone.
        let reciprocal: Vec<EndKey> = joins
            .iter()
            .filter(|(key, (other, other_end))| {
                joins
                    .get(&(other.id, *other_end))
                    .is_some_and(|back| back.0.id == key.0 && back.1 == key.1)
            })
            .map(|(key, _)| *key)
            .collect();
        let reciprocal: HashSet<EndKey> = reciprocal.into_iter().collect();
        joins.retain(|key, _| reciprocal.contains(key));

        let mut index = WallJoins {
            joins,
            roles: HashMap::new(),
            tees: HashMap::new(),
        };
        index.resolve_styles(walls, &touching);
        index
    }

    /// Applies the per-end settings to the corners found by end-point matching, then finds the tees.
    fn resolve_styles(&mut self, walls: &'a [Wall], touching: &HashSet<EndKey>) {
        let by_id: HashMap<Id, &Wall> = walls.iter().rev().map(|wall| (wall.id, wall)).collect();
        let corners: Vec<(EndKey, (&Wall, End))> =
            self.joins.iter().map(|(key, join)| (*key, *join)).collect();
        for (key, (other, other_end)) in corners {
            let own = setting_at(by_id[&key.0], key.1);
            let theirs = setting_at(other, other_end);
            if stays_apart(own) || stays_apart(theirs) {
                self.joins.remove(&key);
                continue;
            }
            // When both ends ask to butt, neither is the one that stops, so the corner stays mitred.
            if own == join_setting::BUTT && theirs != join_setting::BUTT {
                self.roles.insert(key, Role::Abut);
                self.roles.insert((other.id, other_end), Role::Through);
            }
        }

        let mut grid: Option<SegmentGrid> = None;
        for wall in walls {
            for end in [End::Start, End::End] {
                if wall.length() < TOLERANCE
                    || touching.contains(&(wall.id, end))
                    || stays_apart(setting_at(wall, end))
                {
                    continue;
                }
                let grid = grid.get_or_insert_with(|| SegmentGrid::build(walls));
                if let Some(host) = find_host(wall, end, grid) {
                    self.tees.insert((wall.id, end), host);
                }
            }
        }
    }

    /// Plan outline of the section between two stations along a wall: start-left, end-left, end-right,
    /// start-right. Only a section that starts or ends at a wall end takes that end's joint.
    pub fn section_outline(&self, wall: &Wall, from: f64, to: f64) -> [Point2; 4] {
        let (start_left, start_right) = self.end_faces(wall, End::Start);
        let (end_left, end_right) = self.end_faces(wall, End::End);
        let (direction_x, direction_y) = wall.direction();
        let (normal_x, normal_y) = wall.left_normal();
        let half = wall.thickness / 2.0;
        let station = |distance: f64, side: f64| {
            Point2::new(
                wall.start.x + direction_x * distance + side * normal_x * half,
                wall.start.y + direction_y * distance + side * normal_y * half,
            )
        };
        let at_start = from <= TOLERANCE;
        let at_end = to >= wall.length() - TOLERANCE;
        [
            if at_start {
                start_left
            } else {
                station(from, 1.0)
            },
            if at_end { end_left } else { station(to, 1.0) },
            if at_end { end_right } else { station(to, -1.0) },
            if at_start {
                start_right
            } else {
                station(from, -1.0)
            },
        ]
    }

    /// Left and right face points at one wall end.
    fn end_faces(&self, wall: &Wall, end: End) -> (Point2, Point2) {
        let corner = corner_at(wall, end);
        let (normal_x, normal_y) = wall.left_normal();
        let half = wall.thickness / 2.0;
        if let Some(styled) = self.styled_faces(wall, end) {
            return styled;
        }
        let Some(&(other, other_end)) = self.joins.get(&(wall.id, end)) else {
            return square(corner, normal_x * half, normal_y * half);
        };

        let (mut other_normal_x, mut other_normal_y) = other.left_normal();
        // Equal end roles reverse which face of the neighbour corresponds to which.
        if end == other_end {
            other_normal_x = -other_normal_x;
            other_normal_y = -other_normal_y;
        }
        let other_half = other.thickness / 2.0;
        let determinant = normal_x * other_normal_y - normal_y * other_normal_x;
        if determinant.abs() < 1e-9 {
            return square(corner, normal_x * half, normal_y * half);
        }
        // Solves n.x = half for both walls: x is the left-face corner relative to the shared corner.
        let offset_x = (half * other_normal_y - other_half * normal_y) / determinant;
        let offset_y = (normal_x * other_half - other_normal_x * half) / determinant;
        let limit = (MAXIMUM_MITER_FACTOR * half.max(other_half))
            .min(wall.length().min(other.length()) / 2.0);
        if (offset_x * offset_x + offset_y * offset_y).sqrt() > limit {
            return square(corner, normal_x * half, normal_y * half);
        }
        square(corner, offset_x, offset_y)
    }

    /// Faces of a butt or tee end, or `None` when the end is plain or mitred.
    fn styled_faces(&self, wall: &Wall, end: End) -> Option<(Point2, Point2)> {
        let key = (wall.id, end);
        let (target, near) = if let Some(&host) = self.tees.get(&key) {
            (host, true)
        } else {
            let role = self.roles.get(&key)?;
            (self.joins.get(&key)?.0, *role == Role::Abut)
        };
        Some(cut_against(wall, end, target, near).unwrap_or_else(|| {
            let (normal_x, normal_y) = wall.left_normal();
            square(
                corner_at(wall, end),
                normal_x * wall.thickness / 2.0,
                normal_y * wall.thickness / 2.0,
            )
        }))
    }
}

/// Ends a wall on one face of another: the face on the side of the wall's own body (near), or the far one, when
/// the wall runs on to cover the corner. `None` when the walls are almost parallel or the cut would reach far
/// along the wall.
fn cut_against(wall: &Wall, end: End, other: &Wall, near_face: bool) -> Option<(Point2, Point2)> {
    let corner = corner_at(wall, end);
    let (direction_x, direction_y) = wall.direction();
    let (other_x, other_y) = other.direction();
    let (other_normal_x, other_normal_y) = other.left_normal();
    // The wall's body lies on the side of `other` that its inward direction points to.
    let (inward_x, inward_y) = if end == End::Start {
        (direction_x, direction_y)
    } else {
        (-direction_x, -direction_y)
    };
    let projection = inward_x * other_normal_x + inward_y * other_normal_y;
    if projection == 0.0 || projection.is_nan() {
        return None;
    }
    let side = projection.signum();
    let face_side = if near_face { side } else { -side };
    let other_half = other.thickness / 2.0;
    let face = Point2::new(
        corner.x + other_normal_x * face_side * other_half,
        corner.y + other_normal_y * face_side * other_half,
    );
    let (normal_x, normal_y) = wall.left_normal();
    let half = wall.thickness / 2.0;
    let limit =
        (MAXIMUM_MITER_FACTOR * half.max(other_half)).min(wall.length().min(other.length()) / 2.0);
    let face_cut = |offset_x: f64, offset_y: f64| {
        let cross = direction_x * other_y - direction_y * other_x;
        if cross.abs() < 1e-6 {
            return None;
        }
        let (origin_x, origin_y) = (corner.x + offset_x, corner.y + offset_y);
        let t = ((face.x - origin_x) * other_y - (face.y - origin_y) * other_x) / cross;
        (t.abs() <= limit)
            .then(|| Point2::new(origin_x + direction_x * t, origin_y + direction_y * t))
    };
    Some((
        face_cut(normal_x * half, normal_y * half)?,
        face_cut(-normal_x * half, -normal_y * half)?,
    ))
}

/// The only wall whose body a free end rests on; `None` when there is none or more than one.
fn find_host<'a>(wall: &Wall, end: End, grid: &SegmentGrid<'a>) -> Option<&'a Wall> {
    let corner = corner_at(wall, end);
    let (direction_x, direction_y) = wall.direction();
    let mut found: Option<&Wall> = None;
    for other in grid.near(wall.level_id, corner) {
        if other.id == wall.id
            || other.length() < TOLERANCE
            || found.is_some_and(|host| host.id == other.id)
        {
            continue;
        }
        let (other_x, other_y) = other.direction();
        let along = (corner.x - other.start.x) * other_x + (corner.y - other.start.y) * other_y;
        if along <= TOLERANCE || along >= other.length() - TOLERANCE {
            continue;
        }
        let across = -(corner.x - other.start.x) * other_y + (corner.y - other.start.y) * other_x;
        if across.abs() > TOLERANCE || (direction_x * other_y - direction_y * other_x).abs() < 1e-3
        {
            continue;
        }
        if found.is_some() {
            return None;
        }
        found = Some(other);
    }
    found
}

/// A coarse grid of wall centrelines, so a tee search only looks at walls near the point.
struct SegmentGrid<'a> {
    cells: HashMap<(Id, i64, i64), Vec<&'a Wall>>,
    /// Walls too long to register cell by cell, by level.
    long_walls: HashMap<Id, Vec<&'a Wall>>,
}

impl<'a> SegmentGrid<'a> {
    const CELL_SIZE: f64 = 4.0;
    const MAXIMUM_SAMPLES: usize = 4096;

    fn cell_of(x: f64, y: f64) -> (i64, i64) {
        (
            (x / Self::CELL_SIZE).floor() as i64,
            (y / Self::CELL_SIZE).floor() as i64,
        )
    }

    fn build(walls: &'a [Wall]) -> SegmentGrid<'a> {
        let mut grid = SegmentGrid {
            cells: HashMap::new(),
            long_walls: HashMap::new(),
        };
        for wall in walls {
            let steps = (wall.length() / (Self::CELL_SIZE / 2.0)).ceil() as usize;
            if steps > Self::MAXIMUM_SAMPLES {
                grid.long_walls.entry(wall.level_id).or_default().push(wall);
                continue;
            }
            let mut last = None;
            for step in 0..=steps.max(1) {
                let fraction = if steps == 0 {
                    0.0
                } else {
                    step as f64 / steps as f64
                };
                let cell = Self::cell_of(
                    wall.start.x + (wall.end.x - wall.start.x) * fraction,
                    wall.start.y + (wall.end.y - wall.start.y) * fraction,
                );
                if last == Some(cell) {
                    continue;
                }
                last = Some(cell);
                grid.cells
                    .entry((wall.level_id, cell.0, cell.1))
                    .or_default()
                    .push(wall);
            }
        }
        grid
    }

    /// Walls registered around a point; a wall may appear more than once.
    fn near(&self, level: Id, point: Point2) -> impl Iterator<Item = &'a Wall> + '_ {
        let (x, y) = Self::cell_of(point.x, point.y);
        (-1..=1)
            .flat_map(move |dx| (-1..=1).map(move |dy| (dx, dy)))
            .filter_map(move |(dx, dy)| self.cells.get(&(level, x + dx, y + dy)))
            .flatten()
            .chain(self.long_walls.get(&level).into_iter().flatten())
            .copied()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn wall(id: u128, start: (f64, f64), end: (f64, f64), thickness: f64) -> Wall {
        Wall {
            id: Id(id),
            level_id: Id(100),
            start: Point2::new(start.0, start.1),
            end: Point2::new(end.0, end.1),
            thickness,
            height: 3.0,
            wall_type_id: Id(200),
            base_offset: 0.0,
            start_join: 0,
            end_join: 0,
            top_profile: None,
        }
    }

    fn close(a: Point2, x: f64, y: f64) -> bool {
        (a.x - x).abs() < 1e-9 && (a.y - y).abs() < 1e-9
    }

    #[test]
    fn a_right_angle_corner_is_mitred_on_the_diagonal() {
        let walls = [
            wall(1, (0., 0.), (4., 0.), 0.2),
            wall(2, (4., 0.), (4., 3.), 0.2),
        ];
        let joins = WallJoins::build(&walls);
        let outline = joins.section_outline(&walls[0], 0.0, 4.0);
        // The free start is square; the joined end meets the other wall's faces on the diagonal.
        assert!(close(outline[0], 0.0, 0.1) && close(outline[3], 0.0, -0.1));
        assert!(close(outline[1], 3.9, 0.1) && close(outline[2], 4.1, -0.1));
    }

    #[test]
    fn a_section_in_the_middle_of_a_wall_is_square() {
        let walls = [
            wall(1, (0., 0.), (4., 0.), 0.2),
            wall(2, (4., 0.), (4., 3.), 0.2),
        ];
        let joins = WallJoins::build(&walls);
        let outline = joins.section_outline(&walls[0], 1.0, 2.0);
        assert!(close(outline[0], 1.0, 0.1) && close(outline[1], 2.0, 0.1));
        assert!(close(outline[2], 2.0, -0.1) && close(outline[3], 1.0, -0.1));
    }

    #[test]
    fn three_walls_at_one_point_stay_square() {
        let walls = [
            wall(1, (0., 0.), (4., 0.), 0.2),
            wall(2, (4., 0.), (4., 3.), 0.2),
            wall(3, (4., 0.), (8., 0.), 0.2),
        ];
        let joins = WallJoins::build(&walls);
        let outline = joins.section_outline(&walls[0], 0.0, 4.0);
        assert!(close(outline[1], 4.0, 0.1) && close(outline[2], 4.0, -0.1));
    }

    #[test]
    fn an_end_resting_on_another_wall_stops_at_its_face() {
        // A partition running up into the middle of a 0.3 m wall along y = 3.
        let walls = [
            wall(1, (0., 3.), (8., 3.), 0.3),
            wall(2, (4., 0.), (4., 3.), 0.1),
        ];
        let joins = WallJoins::build(&walls);
        let outline = joins.section_outline(&walls[1], 0.0, 3.0);
        assert!(close(outline[1], 3.95, 2.85) && close(outline[2], 4.05, 2.85));
    }

    #[test]
    fn a_squared_off_end_does_not_mitre() {
        let mut walls = [
            wall(1, (0., 0.), (4., 0.), 0.2),
            wall(2, (4., 0.), (4., 3.), 0.2),
        ];
        walls[0].end_join = join_setting::SQUARE_OFF;
        let joins = WallJoins::build(&walls);
        let outline = joins.section_outline(&walls[0], 0.0, 4.0);
        assert!(close(outline[1], 4.0, 0.1) && close(outline[2], 4.0, -0.1));
    }
}
