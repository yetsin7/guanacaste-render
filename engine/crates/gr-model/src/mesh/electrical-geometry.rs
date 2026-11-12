//! Solid geometry for wall-mounted electrical and life-safety equipment.

use super::electrical_solar::add_solar_array;
use super::plumbing_geometry::add_tube_sides;
use super::scene_mesh::{SceneMesh, SurfaceKind};
use crate::document::{ElectricalDevice, electrical_device_kind as kind};
use glam::Vec3;

/// Placement axes for a device facing away from its host wall.
#[derive(Clone, Copy)]
struct DeviceFrame {
    origin: Vec3,
    along: Vec3,
    outward: Vec3,
}

impl DeviceFrame {
    /// Moves the origin along the wall without changing device orientation.
    fn offset(self, distance: f32) -> Self {
        Self {
            origin: self.origin + self.along * distance,
            ..self
        }
    }

    /// Emits a vertical box using the original renderer's cap and side triangle order.
    fn block(self, mesh: &mut SceneMesh, dimensions: [f32; 5], surface: SurfaceKind) {
        let [half_along, half_out, offset_out, bottom, top] = dimensions;
        let center = self.origin + self.outward * offset_out;
        let corner = |along, outward| center + self.along * along + self.outward * outward;
        let mut outline = [
            corner(-half_along, -half_out),
            corner(half_along, -half_out),
            corner(half_along, half_out),
            corner(-half_along, half_out),
        ];
        add_prism(mesh, &mut outline, bottom, top, surface);
    }
}

/// Triangulates an electrical catalog prism with the source application's ordering.
fn add_prism(mesh: &mut SceneMesh, outline: &mut [Vec3], bottom: f32, top: f32, surface: SurfaceKind) {
    let area: f32 = (0..outline.len())
        .map(|index| {
            let a = outline[index];
            let b = outline[(index + 1) % outline.len()];
            a.x * b.y - b.x * a.y
        })
        .sum();
    if area < 0.0 {
        outline.reverse();
    }
    let up = Vec3::Z * top;
    let down = Vec3::Z * bottom;
    for index in 1..outline.len() - 1 {
        mesh.add_triangle(outline[0] + up, outline[index] + up, outline[index + 1] + up, surface);
        mesh.add_triangle(outline[index + 1] + down, outline[index] + down, outline[0] + down, surface);
    }
    for index in 0..outline.len() {
        let a = outline[index];
        let b = outline[(index + 1) % outline.len()];
        mesh.add_quad(a + down, b + down, b + up, a + up, surface);
    }
}

/// Adds a sixteen-sided vertical cylinder with heights relative to its center.
fn add_disc(mesh: &mut SceneMesh, center: Vec3, radius: f32, bottom: f32, top: f32, surface: SurfaceKind) {
    let mut outline: Vec<_> = (0..16)
        .map(|index| {
            let angle = index as f32 * std::f32::consts::TAU / 16.0;
            center + Vec3::new(radius * angle.cos(), radius * angle.sin(), 0.0)
        })
        .collect();
    add_prism(mesh, &mut outline, bottom, top, surface);
}

/// Adds supported device solids, returning false when device or lighting behavior still requires the source application.
pub(super) fn add_electrical_device(mesh: &mut SceneMesh, device: &ElectricalDevice, datum: f64, lit: bool) -> bool {
    let facing = device.facing as f32;
    let outward = Vec3::new(facing.cos(), facing.sin(), 0.0);
    let frame = DeviceFrame {
        origin: Vec3::new(
            device.position.x as f32,
            device.position.y as f32,
            (datum + device.elevation) as f32,
        ),
        along: Vec3::new(-outward.y, outward.x, 0.0),
        outward,
    };
    let glow = if lit { SurfaceKind::LightEmitter } else { SurfaceKind::LightOff };
    match device.kind {
        kind::OUTLET | kind::GFCI_OUTLET | kind::USB_OUTLET => {
            frame.block(mesh, [0.0375, 0.004, 0.004, -0.06, 0.06], SurfaceKind::Ceramic);
            for row in [-0.025, 0.025] {
                for slot in [-0.007, 0.007] {
                    frame
                        .offset(slot)
                        .block(mesh, [0.0012, 0.0015, 0.009, row - 0.006, row + 0.006], SurfaceKind::OpeningFrame);
                }
                frame.block(mesh, [0.002, 0.0015, 0.009, row - 0.016, row - 0.012], SurfaceKind::OpeningFrame);
            }
            if device.kind == kind::GFCI_OUTLET {
                frame.block(mesh, [0.012, 0.002, 0.0095, -0.004, 0.004], SurfaceKind::Metal);
            }
        }
        kind::OUTLET_240 => {
            frame.block(mesh, [0.06, 0.005, 0.005, -0.06, 0.06], SurfaceKind::Ceramic);
            frame.block(mesh, [0.025, 0.0015, 0.011, -0.025, 0.025], SurfaceKind::OpeningFrame);
        }
        kind::SWITCH | kind::THREE_WAY_SWITCH | kind::DIMMER => {
            frame.block(mesh, [0.0375, 0.004, 0.004, -0.06, 0.06], SurfaceKind::Ceramic);
            let tilt = if device.is_on { 0.004 } else { -0.004 };
            frame.block(mesh, [0.016, 0.008, 0.012 + tilt, 0.0, 0.03], SurfaceKind::Ceramic);
            frame.block(mesh, [0.016, 0.008, 0.012 - tilt, -0.03, 0.0], SurfaceKind::Ceramic);
            if device.kind == kind::DIMMER {
                frame.block(mesh, [0.004, 0.012, 0.014, -0.045, -0.035], SurfaceKind::Metal);
            }
        }
        kind::CEILING_FIXTURE => {
            add_disc(mesh, frame.origin, 0.20, -0.025, 0.0, SurfaceKind::Metal);
            add_disc(mesh, frame.origin, 0.17, -0.09, -0.025, glow);
        }
        kind::DOWNLIGHT => {
            add_disc(mesh, frame.origin, 0.075, -0.008, 0.004, SurfaceKind::Metal);
            add_disc(
                mesh,
                frame.origin,
                0.055,
                -0.010,
                -0.006,
                if lit { SurfaceKind::SpotEmitter } else { SurfaceKind::LightOff },
            );
        }
        kind::WALL_SCONCE => {
            frame.block(mesh, [0.05, 0.012, 0.012, -0.08, 0.08], SurfaceKind::Metal);
            DeviceFrame {
                origin: frame.origin + frame.outward * 0.06,
                ..frame
            }
            .block(mesh, [0.07, 0.05, 0.05, -0.12, 0.12], glow);
        }
        kind::PENDANT => {
            add_disc(mesh, frame.origin, 0.05, -0.02, 0.0, SurfaceKind::Metal);
            add_disc(mesh, frame.origin, 0.004, -0.70, -0.02, SurfaceKind::OpeningFrame);
            add_disc(mesh, frame.origin, 0.17, -0.95, -0.70, SurfaceKind::OpeningFrame);
            add_disc(mesh, frame.origin, 0.15, -0.96, -0.93, glow);
        }
        kind::PANEL => {
            frame.block(mesh, [0.21, 0.05, 0.05, -0.30, 0.30], SurfaceKind::Metal);
            frame.block(mesh, [0.16, 0.004, 0.103, -0.24, 0.20], SurfaceKind::OpeningFrame);
            frame
                .offset(0.15)
                .block(mesh, [0.01, 0.006, 0.106, -0.05, 0.05], SurfaceKind::Metal);
        }
        kind::CEILING_FAN => add_ceiling_fan(mesh, frame, glow),
        kind::MINI_SPLIT => add_mini_split(mesh, frame, device, datum),
        kind::WATER_PUMP => add_water_pump(mesh, frame),
        kind::SOLAR_ARRAY => {
            if !(0..=500).contains(&device.panel_count) || !(0.0..=70.0).contains(&device.tilt) {
                return false;
            }
            add_solar_array(mesh, device, datum);
        }
        kind::INVERTER => {
            frame.block(mesh, [0.22, 0.09, 0.09, -0.30, 0.30], SurfaceKind::Ceramic);
            frame
                .offset(0.10)
                .block(mesh, [0.03, 0.004, 0.182, 0.18, 0.22], SurfaceKind::LightEmitter);
            frame.offset(-0.38).block(mesh, [0.09, 0.06, 0.06, -0.20, 0.05], SurfaceKind::Metal);
        }
        kind::SMOKE_DETECTOR => {
            add_disc(mesh, frame.origin, 0.065, -0.035, 0.0, SurfaceKind::Ceramic);
            add_disc(mesh, frame.origin, 0.05, -0.045, -0.035, SurfaceKind::Ceramic);
            add_disc(
                mesh,
                frame.origin + Vec3::new(0.03, 0.0, 0.0),
                0.004,
                -0.047,
                -0.044,
                SurfaceKind::OpeningFrame,
            );
        }
        kind::EMERGENCY_LIGHT => {
            frame.block(mesh, [0.17, 0.04, 0.04, -0.06, 0.06], SurfaceKind::Ceramic);
            for side in [-0.10, 0.10] {
                frame
                    .offset(side)
                    .block(mesh, [0.035, 0.035, 0.07, 0.06, 0.11], SurfaceKind::LightOff);
            }
        }
        kind::EXIT_SIGN => {
            frame.block(mesh, [0.17, 0.012, 0.012, -0.10, 0.10], SurfaceKind::Metal);
            frame.block(mesh, [0.15, 0.004, 0.026, -0.08, 0.08], SurfaceKind::LightEmitter);
        }
        kind::FIRE_ALARM_PULL_STATION => {
            frame.block(mesh, [0.065, 0.03, 0.03, -0.085, 0.085], SurfaceKind::FireRed);
            frame.block(mesh, [0.03, 0.006, 0.064, -0.02, 0.02], SurfaceKind::Ceramic);
        }
        kind::HORN_STROBE => {
            frame.block(mesh, [0.07, 0.03, 0.03, -0.07, 0.07], SurfaceKind::FireRed);
            frame.block(mesh, [0.03, 0.012, 0.07, 0.01, 0.05], SurfaceKind::LightOff);
        }
        kind::SPRINKLER => {
            add_disc(mesh, frame.origin, 0.012, 0.0, 0.25, SurfaceKind::FireRed);
            add_disc(mesh, frame.origin, 0.04, -0.008, 0.0, SurfaceKind::Metal);
            add_disc(mesh, frame.origin, 0.01, -0.045, -0.008, SurfaceKind::Metal);
            add_disc(mesh, frame.origin, 0.028, -0.05, -0.045, SurfaceKind::Metal);
        }
        kind::FIRE_EXTINGUISHER => {
            let cylinder = frame.origin + frame.outward * 0.11;
            add_disc(mesh, cylinder, 0.08, -0.55, 0.0, SurfaceKind::FireRed);
            add_disc(mesh, cylinder, 0.025, 0.0, 0.09, SurfaceKind::OpeningFrame);
            frame.block(mesh, [0.03, 0.015, 0.015, -0.10, -0.02], SurfaceKind::Metal);
        }
        kind::FIRE_HOSE_CABINET => {
            frame.block(mesh, [0.40, 0.10, 0.08, 0.0, 0.80], SurfaceKind::FireRed);
            frame.block(mesh, [0.34, 0.004, 0.182, 0.06, 0.74], SurfaceKind::Glass);
            frame.block(mesh, [0.22, 0.06, 0.09, 0.18, 0.62], SurfaceKind::FireRed);
        }
        _ => return false,
    }
    device.controls.is_empty()
}

/// Builds the motor, five blades, and light kit of a ceiling fan.
fn add_ceiling_fan(mesh: &mut SceneMesh, frame: DeviceFrame, glow: SurfaceKind) {
    add_disc(mesh, frame.origin, 0.07, -0.05, 0.0, SurfaceKind::Metal);
    add_disc(mesh, frame.origin, 0.012, -0.32, -0.05, SurfaceKind::Metal);
    add_disc(mesh, frame.origin, 0.10, -0.44, -0.32, SurfaceKind::Metal);
    for blade in 0..5 {
        let angle = blade as f32 * std::f32::consts::TAU / 5.0 + 0.3;
        let radial = Vec3::new(angle.cos(), angle.sin(), 0.0);
        let blade_frame = DeviceFrame {
            origin: frame.origin,
            along: Vec3::new(-radial.y, radial.x, 0.0),
            outward: radial,
        };
        blade_frame.block(mesh, [0.012, 0.06, 0.14, -0.42, -0.41], SurfaceKind::Metal);
        blade_frame.block(mesh, [0.065, 0.27, 0.42, -0.425, -0.415], SurfaceKind::DoorLeaf);
    }
    add_disc(mesh, frame.origin, 0.09, -0.50, -0.44, glow);
}

/// Adds the indoor appliance, and its condenser and refrigerant lines when an outdoor position is stored.
fn add_mini_split(mesh: &mut SceneMesh, frame: DeviceFrame, device: &ElectricalDevice, datum: f64) {
    frame.block(mesh, [0.40, 0.095, 0.10, -0.14, 0.14], SurfaceKind::Ceramic);
    frame.block(mesh, [0.39, 0.08, 0.11, 0.14, 0.15], SurfaceKind::Ceramic);
    frame.block(mesh, [0.36, 0.012, 0.20, -0.13, -0.09], SurfaceKind::OpeningFrame);
    frame
        .offset(0.30)
        .block(mesh, [0.012, 0.004, 0.196, 0.02, 0.035], SurfaceKind::LightEmitter);
    let Some(outdoor_position) = device.outdoor_position else { return };
    let outdoor = Vec3::new(outdoor_position.x as f32, outdoor_position.y as f32, datum as f32);
    let line_z = frame.origin.z - 0.05;
    for (offset, radius) in [(-0.33, 0.011), (-0.29, 0.015)] {
        let mut start = frame.origin + frame.along * offset + frame.outward * 0.03;
        let mut face = outdoor + frame.outward * 0.41 + frame.along * offset;
        start.z = line_z;
        face.z = line_z;
        let mut low = face;
        low.z = datum as f32 + 0.40;
        let mut into = outdoor + frame.outward * 0.12 + frame.along * offset;
        into.z = datum as f32 + 0.40;
        add_tube_sides(mesh, start, face, radius, SurfaceKind::OpeningFrame, 8);
        add_tube_sides(mesh, face, low, radius, SurfaceKind::OpeningFrame, 8);
        add_tube_sides(mesh, low, into, radius, SurfaceKind::OpeningFrame, 8);
    }
    let outdoor_frame = DeviceFrame {
        origin: outdoor,
        outward: -frame.outward,
        ..frame
    };
    outdoor_frame.block(mesh, [0.46, 0.22, 0.02, 0.0, 0.08], SurfaceKind::Structure);
    outdoor_frame.block(mesh, [0.40, 0.15, 0.02, 0.10, 0.66], SurfaceKind::Ceramic);
    outdoor_frame
        .offset(-0.08)
        .block(mesh, [0.22, 0.006, 0.172, 0.16, 0.60], SurfaceKind::OpeningFrame);
    outdoor_frame
        .offset(0.30)
        .block(mesh, [0.07, 0.006, 0.172, 0.20, 0.45], SurfaceKind::Metal);
    for side in [-0.36, 0.36] {
        outdoor_frame
            .offset(side)
            .block(mesh, [0.02, 0.17, 0.02, 0.08, 0.10], SurfaceKind::Metal);
    }
}

/// Builds the pressure tank, motor, and equipment pad of a water pump.
fn add_water_pump(mesh: &mut SceneMesh, frame: DeviceFrame) {
    frame.block(mesh, [0.45, 0.30, 0.0, 0.0, 0.08], SurfaceKind::Structure);
    let tank = frame.origin + frame.along * 0.18;
    add_disc(mesh, tank, 0.15, 0.08, 0.62, SurfaceKind::PipeCold);
    add_disc(mesh, tank, 0.03, 0.62, 0.66, SurfaceKind::Metal);
    let motor = frame.origin - frame.along * 0.18 + Vec3::Z * 0.20;
    add_tube_sides(
        mesh,
        motor - frame.outward * 0.17,
        motor + frame.outward * 0.17,
        0.09,
        SurfaceKind::Metal,
        8,
    );
    add_tube_sides(
        mesh,
        motor + frame.outward * 0.17,
        motor + frame.outward * 0.26,
        0.07,
        SurfaceKind::PipeCold,
        8,
    );
    frame.offset(-0.18).block(mesh, [0.09, 0.12, 0.0, 0.08, 0.12], SurfaceKind::Metal);
}
