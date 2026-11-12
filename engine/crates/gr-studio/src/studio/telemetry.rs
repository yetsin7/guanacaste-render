//! What the studio can honestly report about the machine: the graphics adapter and its dedicated memory as Windows
//! records them, the processor's name, and the measured time of each interface frame. Temperatures and power draw
//! need vendor drivers' private interfaces and are not read.

use std::collections::VecDeque;
use std::sync::mpsc::{Receiver, channel};

/// Frames of history the frame-time chart shows.
pub const HISTORY: usize = 90;

/// A gap between frames longer than this is the interface waiting for input, not drawing slowly: the view only
/// redraws when something changes, so such gaps say nothing about how fast it can draw.
const IDLE_GAP_MS: f32 = 90.0;

#[derive(Clone, Debug, Default)]
pub struct Hardware {
    /// Adapters with their dedicated memory in bytes, as the display driver class in the registry lists them.
    pub adapters: Vec<(String, u64)>,
    pub processor: Option<String>,
}

impl Hardware {
    /// Dedicated memory of the adapter whose name contains `name`.
    pub fn memory_of(&self, name: &str) -> Option<u64> {
        let wanted = name.to_lowercase();
        self.adapters
            .iter()
            .find(|(adapter, _)| {
                adapter.to_lowercase().contains(&wanted) || wanted.contains(&adapter.to_lowercase())
            })
            .map(|(_, bytes)| *bytes)
            .filter(|bytes| *bytes > 0)
    }
}

pub struct Telemetry {
    pub frame_ms: VecDeque<f32>,
    /// When the last continuous frame was recorded.
    last_active: Option<std::time::Instant>,
    pub hardware: Hardware,
    pending: Option<Receiver<Hardware>>,
}

impl Default for Telemetry {
    fn default() -> Telemetry {
        let (sender, receiver) = channel();
        std::thread::spawn(move || {
            let _ = sender.send(read_hardware());
        });
        Telemetry {
            frame_ms: VecDeque::with_capacity(HISTORY),
            last_active: None,
            hardware: Hardware::default(),
            pending: Some(receiver),
        }
    }
}

impl Telemetry {
    /// Records the time since the previous interface frame; gaps of an idle interface are left out.
    pub fn record(&mut self, milliseconds: f32) {
        if milliseconds <= IDLE_GAP_MS {
            if self.frame_ms.len() == HISTORY {
                self.frame_ms.pop_front();
            }
            self.frame_ms.push_back(milliseconds);
            self.last_active = Some(std::time::Instant::now());
        }
        if let Some(receiver) = &self.pending
            && let Ok(hardware) = receiver.try_recv()
        {
            self.hardware = hardware;
            self.pending = None;
        }
    }

    /// Mean, minimum and maximum of the recorded frame times.
    pub fn stats(&self) -> Option<(f32, f32, f32)> {
        if self.frame_ms.is_empty() {
            return None;
        }
        let sum: f32 = self.frame_ms.iter().sum();
        let min = self.frame_ms.iter().copied().fold(f32::MAX, f32::min);
        let max = self.frame_ms.iter().copied().fold(0.0, f32::max);
        Some((sum / self.frame_ms.len() as f32, min, max))
    }

    /// Frames per second over the recorded history.
    pub fn fps(&self) -> f32 {
        self.stats()
            .map_or(0.0, |(mean, _, _)| 1000.0 / mean.max(0.01))
    }

    /// True when nothing has been drawn continuously for a moment: the view is waiting for input.
    pub fn is_idle(&self) -> bool {
        self.last_active
            .is_none_or(|time| time.elapsed().as_secs_f32() > 0.5)
    }

    /// "60 FPS" while the interface is drawing, "EN REPOSO" while it waits for input.
    pub fn fps_label(&self) -> String {
        if self.is_idle() {
            "EN REPOSO".into()
        } else {
            format!("{:.0} FPS", self.fps())
        }
    }
}

/// Reads the display adapters and the processor from the registry with the system's `reg` tool.
fn read_hardware() -> Hardware {
    let adapters = query(
        r"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}",
        true,
    )
    .map(|output| parse_adapters(&output))
    .unwrap_or_default();
    let processor = query(
        r"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
        false,
    )
    .and_then(|output| {
        output
            .lines()
            .find(|line| line.contains("ProcessorNameString"))
            .and_then(|line| line.split("REG_SZ").nth(1))
            .map(|name| name.split_whitespace().collect::<Vec<_>>().join(" "))
    });
    Hardware {
        adapters,
        processor,
    }
}

fn query(key: &str, recursive: bool) -> Option<String> {
    let mut command = std::process::Command::new("reg");
    command.arg("query").arg(key);
    if recursive {
        command.arg("/s");
    }
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        // No console window flashes up behind the studio.
        command.creation_flags(0x0800_0000);
    }
    let output = command.output().ok()?;
    output
        .status
        .success()
        .then(|| String::from_utf8_lossy(&output.stdout).into_owned())
}

/// Pairs each adapter's `DriverDesc` with its `HardwareInformation.qwMemorySize` within one registry key.
fn parse_adapters(output: &str) -> Vec<(String, u64)> {
    let mut adapters = Vec::new();
    let mut name: Option<String> = None;
    let mut memory = 0u64;
    let mut flush = |name: &mut Option<String>, memory: &mut u64| {
        if let Some(found) = name.take()
            && !found.contains("Basic Render")
            && !adapters
                .iter()
                .any(|(existing, _): &(String, u64)| *existing == found)
        {
            adapters.push((found, *memory));
        }
        *memory = 0;
    };
    for line in output.lines() {
        if line.starts_with("HKEY_") {
            flush(&mut name, &mut memory);
        } else if line.contains("DriverDesc") && line.contains("REG_SZ") {
            name = line
                .split("REG_SZ")
                .nth(1)
                .map(|value| value.trim().to_owned());
        } else if line.contains("HardwareInformation.qwMemorySize")
            && let Some(value) = line.split("REG_QWORD").nth(1)
        {
            memory = u64::from_str_radix(value.trim().trim_start_matches("0x"), 16).unwrap_or(0);
        }
    }
    flush(&mut name, &mut memory);
    adapters
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn adapters_are_read_with_their_memory() {
        let output = "HKEY_LOCAL_MACHINE\\...\\0000\n    DriverDesc    REG_SZ    Intel(R) UHD Graphics\n\nHKEY_LOCAL_MACHINE\\...\\0001\n    HardwareInformation.qwMemorySize    REG_QWORD    0x180000000\n    DriverDesc    REG_SZ    NVIDIA GeForce RTX 4050 Laptop GPU\n";
        let adapters = parse_adapters(output);
        assert_eq!(adapters.len(), 2);
        assert_eq!(
            adapters[1],
            ("NVIDIA GeForce RTX 4050 Laptop GPU".to_owned(), 6 << 30)
        );
        let hardware = Hardware {
            adapters,
            processor: None,
        };
        assert_eq!(
            hardware.memory_of("NVIDIA GeForce RTX 4050 Laptop GPU"),
            Some(6 << 30)
        );
        assert_eq!(hardware.memory_of("Intel(R) UHD Graphics"), None);
    }
}
