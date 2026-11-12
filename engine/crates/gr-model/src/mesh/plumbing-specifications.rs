//! Fixture units and pipe sizes used by the original plumbing layout.

use crate::document::component_shape as catalog;
use crate::document::{StructuralElement, element_kind};

/// The fixture properties that influence routing, pipe diameter, and trap geometry.
#[derive(Clone, Copy)]
pub(super) struct FixtureSpec {
    pub cold: f64,
    pub hot: f64,
    pub drain: f64,
    pub minimum_drain: usize,
    pub inlet_height: f64,
    pub trap_height: Option<f64>,
    pub tailpiece: f64,
}

/// Supply pipe inside and outside diameters in millimetres.
pub(super) const SUPPLY_SIZES: [(f64, f64); 6] = [(13.8, 15.9), (19.9, 22.2), (26.0, 28.6), (32.1, 34.9), (38.2, 41.3), (50.4, 54.0)];
/// Drain outside diameter and fixture-unit capacity.
pub(super) const DRAIN_SIZES: [(f64, f64); 5] = [(48.0, 3.0), (60.0, 6.0), (89.0, 42.0), (114.0, 216.0), (168.0, 840.0)];

/// Returns the source catalog's plumbing specification for a supported residential fixture.
pub(super) fn fixture_spec(element: &StructuralElement) -> Option<FixtureSpec> {
    if element.kind != element_kind::COMPONENT {
        return None;
    }
    let (cold, hot, drain, minimum_drain, inlet_height, trap_height, tailpiece) = match element.component_shape {
        catalog::TOILET => (2.2, 0.0, 3.0, 2, 0.20, None, 0.15),
        catalog::VANITY if element.width >= 1.3 => (1.0, 1.0, 2.0, 0, 0.50, Some(0.45), 0.15),
        catalog::VANITY | catalog::PEDESTAL_SINK => (0.5, 0.5, 1.0, 0, 0.50, Some(0.45), 0.15),
        catalog::KITCHEN_SINK => (1.0, 1.0, 2.0, 0, 0.50, Some(0.45), 0.15),
        catalog::BATHTUB => (1.0, 1.0, 2.0, 0, 0.60, Some(-0.15), 0.15),
        catalog::SHOWER => (1.0, 1.0, 2.0, 1, 1.00, Some(-0.15), 0.15),
        catalog::DISHWASHER => (0.0, 1.4, 2.0, 0, 0.15, Some(0.30), 0.15),
        catalog::WASHER_DRYER => (1.0, 1.0, 3.0, 1, 1.10, Some(0.30), 0.60),
        catalog::BIDET => (0.5, 0.5, 1.0, 0, 0.20, Some(0.10), 0.15),
        catalog::URINAL => (2.2, 0.0, 2.0, 1, 1.10, None, 0.15),
        catalog::WATER_HEATER => (0.0, 0.0, 0.0, 0, 0.3_f64.max(element.height - 0.1), None, 0.15),
        _ => return None,
    };
    Some(FixtureSpec {
        cold,
        hot,
        drain,
        minimum_drain,
        inlet_height,
        trap_height,
        tailpiece,
    })
}

/// Interpolates the original Hunter fixture-unit curve to obtain peak flow in litres per second.
pub(super) fn peak_flow(units: f64) -> f64 {
    const WSFU: [f64; 26] = [
        1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 12.0, 14.0, 16.0, 18.0, 20.0, 25.0, 30.0, 35.0, 40.0, 45.0, 50.0, 60.0, 70.0,
        80.0, 90.0, 100.0,
    ];
    const GPM: [f64; 26] = [
        3.0, 5.0, 6.5, 8.0, 9.4, 10.7, 11.8, 12.8, 13.7, 14.6, 16.0, 17.0, 18.0, 18.8, 19.6, 21.5, 23.3, 24.9, 26.3, 27.7, 29.1, 32.0,
        35.0, 38.0, 41.0, 43.5,
    ];
    if units <= 0.0 {
        return 0.0;
    }
    let flow = if units <= 1.0 {
        3.0 * units
    } else if units >= 100.0 {
        43.5 + (units - 100.0) * 0.25
    } else {
        let index = WSFU.iter().position(|&value| value >= units).expect("fixture-unit interval");
        let fraction = (units - WSFU[index - 1]) / (WSFU[index] - WSFU[index - 1]);
        GPM[index - 1] + (GPM[index] - GPM[index - 1]) * fraction
    };
    flow * 0.0630902
}

/// Finds the smallest supply pipe whose flow velocity is below the applicable limit.
pub(super) fn supply_size(flow: f64, hot: bool) -> usize {
    let limit = if hot { 1.5 } else { 2.4 };
    SUPPLY_SIZES
        .iter()
        .position(|&(inside, _)| {
            let diameter = inside / 1000.0;
            flow / 1000.0 / (std::f64::consts::PI * diameter * diameter / 4.0) <= limit
        })
        .unwrap_or(SUPPLY_SIZES.len() - 1)
}

/// Finds the smallest drain at least as large as the fixture's trap and carrying the downstream load.
pub(super) fn drain_size(units: f64, minimum: usize) -> usize {
    (minimum..DRAIN_SIZES.len())
        .find(|&index| DRAIN_SIZES[index].1 >= units)
        .unwrap_or(DRAIN_SIZES.len() - 1)
}

/// Chooses the vent diameter from the drainage units connected to the stack.
pub(super) fn vent_size(units: f64) -> usize {
    if units <= 8.0 {
        0
    } else if units <= 24.0 {
        1
    } else {
        2
    }
}
