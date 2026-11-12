//! Pipe runs, fixture traps, and vent stacks for the supported plumbing systems.

use super::plumbing_routing::{PipeRun, PlumbingNode, SystemLayout, VentRun};
use super::plumbing_specifications::{DRAIN_SIZES, SUPPLY_SIZES, drain_size, peak_flow, supply_size, vent_size};
use super::storm_runoff::{STORM_SIZES, storm_size};
use crate::document::{PlumbingPoint, plumbing_point_kind, plumbing_system_kind as kind};
use crate::plan::Point2;

/// Builds supply mains and fixture risers, sizing each branch from its downstream demand.
pub(super) fn supply(nodes: &[PlumbingNode], system_kind: i32) -> SystemLayout {
    let hot = system_kind == kind::HOT_WATER;
    let root = nodes[0];
    let run_z = root.level - if hot { 0.14 } else { 0.10 };
    let (parents, order) = tree(nodes);
    let units = subtree(nodes, &parents, &order);
    let mut runs = vec![PipeRun {
        from: root.point,
        from_z: root.inlet_z,
        to: root.point,
        to_z: run_z,
        outside: SUPPLY_SIZES[0].1,
    }];
    let mut main_size = 0;
    for &index in order.iter().skip(1) {
        let parent = parents[index];
        let mut size = supply_size(peak_flow(units[index]), hot);
        if parent == 0 && !hot {
            size = size.max(1);
        }
        if parent == 0 {
            main_size = main_size.max(size);
        }
        add_square_run(
            &mut runs,
            nodes[parent].point,
            run_z,
            nodes[index].point,
            run_z,
            SUPPLY_SIZES[size].1,
        );
    }
    if main_size == 0 && nodes.len() > 1 && !hot {
        main_size = 1;
    }
    runs[0].outside = SUPPLY_SIZES[main_size].1;
    for node in nodes.iter().skip(1) {
        let size = supply_size(peak_flow(node.units), hot);
        runs.push(PipeRun {
            from: node.point,
            from_z: run_z,
            to: node.point,
            to_z: node.inlet_z,
            outside: SUPPLY_SIZES[size].1,
        });
    }
    SystemLayout {
        kind: system_kind,
        runs,
        vents: Vec::new(),
    }
}

/// Builds one-percent-fall storm branches and the boots connecting each downspout to the buried pipe.
pub(super) fn storm(nodes: &[PlumbingNode]) -> SystemLayout {
    let (parents, order) = tree(nodes);
    let flows = subtree(nodes, &parents, &order);
    let mut runs = Vec::new();
    let mut invert = vec![0.0; nodes.len()];
    invert[0] = nodes[0].outlet_z;
    for &index in order.iter().skip(1) {
        let parent = parents[index];
        invert[index] = invert[parent] + 0.01 * manhattan(nodes[parent].point, nodes[index].point);
        let size = storm_size(flows[index]);
        add_square_run(&mut runs, nodes[parent].point, invert[parent], nodes[index].point, invert[index], STORM_SIZES[size].1);
    }
    for index in 1..nodes.len() {
        let node = nodes[index];
        let size = storm_size(node.units);
        runs.push(PipeRun { from: node.point, from_z: node.outlet_z, to: node.point, to_z: invert[index], outside: STORM_SIZES[size].1 });
    }
    SystemLayout { kind: kind::STORM_WATER, runs, vents: Vec::new() }
}

/// Builds sloped sanitary branches, fixture traps, a cleanout, and the stacks serving those fixtures.
pub(super) fn drain(nodes: &[PlumbingNode], points: &[PlumbingPoint], roof_top: f64) -> SystemLayout {
    let root = nodes[0];
    let (parents, order) = tree(nodes);
    let units = subtree(nodes, &parents, &order);
    let mut minimum = vec![0; nodes.len()];
    for index in 1..nodes.len() {
        minimum[index] = nodes[index].spec.expect("fixture specification").minimum_drain;
    }
    for &index in order.iter().rev().filter(|&&index| index != 0) {
        minimum[parents[index]] = minimum[parents[index]].max(minimum[index]);
    }
    let mut runs = Vec::new();
    let mut invert = vec![0.0; nodes.len()];
    invert[0] = root.outlet_z;
    let mut main_size = 0;
    for &index in order.iter().skip(1) {
        let parent = parents[index];
        invert[index] = invert[parent] + 0.02 * manhattan(nodes[parent].point, nodes[index].point);
        let size = drain_size(units[index], minimum[index]);
        if parent == 0 {
            main_size = main_size.max(size);
        }
        add_square_run(
            &mut runs,
            nodes[parent].point,
            invert[parent],
            nodes[index].point,
            invert[index],
            DRAIN_SIZES[size].0,
        );
    }
    runs.push(PipeRun {
        from: root.point,
        from_z: root.inlet_z,
        to: root.point,
        to_z: root.outlet_z,
        outside: DRAIN_SIZES[main_size].0,
    });
    let vent_points: Vec<_> = points
        .iter()
        .filter(|point| point.kind == plumbing_point_kind::VENT_STACK)
        .collect();
    let mut groups: Vec<(usize, f64, f64)> = Vec::new();
    for index in 1..nodes.len() {
        let node = nodes[index];
        let spec = node.spec.expect("fixture specification");
        let size = drain_size(node.units, spec.minimum_drain);
        runs.push(PipeRun {
            from: node.point,
            from_z: node.outlet_z,
            to: node.point,
            to_z: invert[index],
            outside: DRAIN_SIZES[size].0,
        });
        if spec.trap_height.is_some() {
            add_trap(&mut runs, node, DRAIN_SIZES[spec.minimum_drain].0);
        }
        if !vent_points.is_empty() {
            let mut nearest = 0;
            for candidate in 1..vent_points.len() {
                if manhattan(vent_points[candidate].position, node.point) < manhattan(vent_points[nearest].position, node.point) {
                    nearest = candidate;
                }
            }
            if let Some(group) = groups.iter_mut().find(|group| group.0 == nearest) {
                group.1 = group.1.min(invert[index]);
                group.2 += node.units;
            } else {
                groups.push((nearest, invert[index], node.units));
            }
        }
    }
    let vents = groups
        .into_iter()
        .map(|(index, bottom, units)| VentRun {
            point: vent_points[index].position,
            bottom,
            top: roof_top,
            outside: DRAIN_SIZES[vent_size(units)].0,
        })
        .collect();
    SystemLayout {
        kind: kind::SANITARY,
        runs,
        vents,
    }
}

/// Adds a four-piece P-trap between the fixture's tailpiece and its wall connection.
fn add_trap(runs: &mut Vec<PipeRun>, node: PlumbingNode, outside: f64) {
    let spec = node.spec.expect("fixture specification");
    let (dx, dy) = (node.center.x - node.point.x, node.center.y - node.point.y);
    let length = (dx * dx + dy * dy).sqrt();
    let (fx, fy) = if length > 1e-6 { (dx / length, dy / length) } else { (0.0, -1.0) };
    let out = |distance| Point2::new(node.point.x + fx * distance, node.point.y + fy * distance);
    let arm = node.outlet_z;
    let bottom = arm - 0.08;
    let (tail, weir) = (out(0.14), out(0.05));
    runs.push(PipeRun {
        from: tail,
        from_z: arm + spec.tailpiece,
        to: tail,
        to_z: bottom,
        outside,
    });
    runs.push(PipeRun {
        from: tail,
        from_z: bottom,
        to: weir,
        to_z: bottom,
        outside,
    });
    runs.push(PipeRun {
        from: weir,
        from_z: bottom,
        to: weir,
        to_z: arm,
        outside,
    });
    runs.push(PipeRun {
        from: weir,
        from_z: arm,
        to: node.point,
        to_z: arm,
        outside,
    });
}

/// Splits an orthogonal route at its X-then-Y corner, interpolating the drain elevation.
fn add_square_run(runs: &mut Vec<PipeRun>, from: Point2, from_z: f64, to: Point2, to_z: f64, outside: f64) {
    let corner = Point2::new(to.x, from.y);
    let total = manhattan(from, to);
    let corner_z = if total < 1e-9 {
        to_z
    } else {
        from_z + (to_z - from_z) * (to.x - from.x).abs() / total
    };
    if from.distance_to(corner) > 1e-6 {
        runs.push(PipeRun {
            from,
            from_z,
            to: corner,
            to_z: corner_z,
            outside,
        });
    }
    if corner.distance_to(to) > 1e-6 {
        runs.push(PipeRun {
            from: corner,
            from_z: corner_z,
            to,
            to_z,
            outside,
        });
    }
}

/// Builds Prim's minimum spanning tree with the source application's penalty for changing levels.
fn tree(nodes: &[PlumbingNode]) -> (Vec<usize>, Vec<usize>) {
    let count = nodes.len();
    let mut parents = vec![0; count];
    let mut best = vec![f64::MAX; count];
    let mut done = vec![false; count];
    let mut order = Vec::with_capacity(count);
    best[0] = 0.0;
    for _ in 0..count {
        let mut next = None;
        for index in 0..count {
            if !done[index] && next.is_none_or(|selected| best[index] < best[selected]) {
                next = Some(index);
            }
        }
        let next = next.expect("an unvisited plumbing node");
        done[next] = true;
        order.push(next);
        for index in 0..count {
            if done[index] {
                continue;
            }
            let distance = manhattan(nodes[next].point, nodes[index].point) + (nodes[next].level - nodes[index].level).abs() * 2.0;
            if distance < best[index] {
                best[index] = distance;
                parents[index] = next;
            }
        }
    }
    (parents, order)
}

/// Accumulates fixture units from each tree node through all of its descendants.
fn subtree(nodes: &[PlumbingNode], parents: &[usize], order: &[usize]) -> Vec<f64> {
    let mut units: Vec<f64> = nodes.iter().map(|node| node.units).collect();
    for &index in order.iter().rev().filter(|&&index| index != 0) {
        units[parents[index]] += units[index];
    }
    units
}

/// Returns the orthogonal plan distance used by the routing algorithm.
fn manhattan(a: Point2, b: Point2) -> f64 {
    (a.x - b.x).abs() + (a.y - b.y).abs()
}
