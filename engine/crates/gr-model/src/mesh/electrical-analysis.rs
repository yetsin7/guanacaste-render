//! Electrical supply and switch behavior that determine render-time light output.

use crate::document::{Document, ElectricalCircuit, ElectricalDevice, Id, electrical_device_kind as kind};
use std::collections::HashMap;

/// The circuit and switch state needed to evaluate a luminaire or lighting component.
pub(super) struct ElectricalAnalysis<'a> {
    devices: HashMap<Id, &'a ElectricalDevice>,
    controls: HashMap<Id, Vec<&'a ElectricalDevice>>,
    energized: HashMap<Id, bool>,
    has_panel: bool,
}

impl<'a> ElectricalAnalysis<'a> {
    /// Resolves live breakers and panel trips once per project rather than once per light.
    pub(super) fn new(document: &'a Document) -> Self {
        let mut controls: HashMap<Id, Vec<&ElectricalDevice>> = HashMap::new();
        let mut loads: HashMap<Id, Vec<&ElectricalDevice>> = HashMap::new();
        for device in &document.electrical_devices {
            if matches!(device.kind, kind::SWITCH | kind::THREE_WAY_SWITCH | kind::DIMMER) {
                for target in &device.controls {
                    controls.entry(*target).or_default().push(device);
                }
            }
            if let Some(circuit) = device.circuit_id {
                loads.entry(circuit).or_default().push(device);
            }
        }
        let mut circuits_by_panel: HashMap<Id, Vec<&ElectricalCircuit>> = HashMap::new();
        for circuit in &document.electrical_circuits {
            circuits_by_panel.entry(circuit.panel_id).or_default().push(circuit);
        }
        let panels: Vec<_> = document
            .electrical_devices
            .iter()
            .filter(|device| device.kind == kind::PANEL)
            .collect();
        let mut energized = HashMap::new();
        for panel in &panels {
            let mut circuits = circuits_by_panel.remove(&panel.id).unwrap_or_default();
            circuits.sort_by_key(|circuit| circuit.number);
            let mut panel_load = 0.0;
            let mut states = Vec::new();
            for circuit in circuits {
                let devices = loads.get(&circuit.id).map(Vec::as_slice).unwrap_or_default();
                let load: f64 = devices.iter().map(|device| device.watts.max(0.0)).sum();
                let generation: f64 = devices
                    .iter()
                    .filter(|device| device.kind == kind::INVERTER)
                    .map(|device| device.watts.max(0.0))
                    .sum();
                panel_load += load - generation;
                let two_pole = circuit.voltage >= 200.0;
                let wrong_voltage = devices
                    .iter()
                    .any(|device| matches!(device.kind, kind::OUTLET_240 | kind::MINI_SPLIT | kind::INVERTER) != two_pole);
                let utilization = load / circuit.voltage.max(1.0) / circuit.breaker_amps.max(1.0);
                states.push((
                    circuit.id,
                    !devices.is_empty() && circuit.breaker_on && !wrong_voltage && utilization <= 1.0,
                ));
            }
            let main = if panel.main_breaker_amps > 0.0 {
                panel.main_breaker_amps
            } else {
                100.0
            };
            let live = panel.is_on && panel_load / 240.0 / main <= 1.0;
            for (circuit, state) in states {
                energized.insert(circuit, state && live);
            }
        }
        Self {
            devices: document.electrical_devices.iter().map(|device| (device.id, device)).collect(),
            controls,
            energized,
            has_panel: !panels.is_empty(),
        }
    }

    /// Returns the source application's light output, including three-way parity and chained dimmers.
    pub(super) fn brightness(&self, target: Id) -> f64 {
        if let Some(device) = self.devices.get(&target) {
            let powered = device
                .circuit_id
                .map_or(!self.has_panel, |circuit| self.energized.get(&circuit).copied().unwrap_or(false));
            if !powered {
                return 0.0;
            }
        }
        let mut level = 1.0;
        let mut three_way_off = 0;
        if let Some(switches) = self.controls.get(&target) {
            for switch in switches {
                if switch.kind == kind::THREE_WAY_SWITCH {
                    if !switch.is_on {
                        three_way_off += 1;
                    }
                } else if !switch.is_on {
                    return 0.0;
                } else if switch.kind == kind::DIMMER {
                    level *= switch.dim_level.clamp(0.1, 1.0);
                }
            }
        }
        if three_way_off % 2 == 0 { level } else { 0.0 }
    }
}
