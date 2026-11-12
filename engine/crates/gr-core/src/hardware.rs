//! Quality tiers: what the engine asks of a machine, and which tier a machine should start in.
//!
//! The tier is only a starting suggestion. The user can always choose another one, and the engine must still
//! degrade in a controlled way when a scene does not fit the budgets of the chosen tier.

/// Where the rays are traced.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AdapterKind {
    /// No usable graphics adapter: the CPU hierarchy does the work.
    Cpu,
    /// Shares system memory (Intel UHD, Radeon Graphics).
    Integrated,
    /// Has its own video memory.
    Discrete,
}

/// What was detected and measured on this machine.
#[derive(Clone, Copy, Debug)]
pub struct Capabilities {
    pub adapter: AdapterKind,
    /// Dedicated video memory in bytes; `None` when the system does not report it.
    pub video_memory_bytes: Option<u64>,
    /// `None` when the system does not report it.
    pub system_memory_bytes: Option<u64>,
    /// Path-tracing throughput measured on the reference house scene, in millions of rays per second.
    /// `None` until the calibration has run. Reference points measured on 2026-11-11: 1 213 on an RTX 4050 Laptop,
    /// 214 on Intel UHD (12th gen), 13 on the C# CPU tracer with 16 threads.
    pub measured_mega_rays: Option<f32>,
    pub on_battery: bool,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub enum QualityTier {
    Economico,
    Equilibrado,
    Alto,
    Ultra,
}

/// The budgets a tier grants. Everything here is a ceiling, not a target.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct TierBudget {
    /// Fraction of the window resolution the interactive view traces at (the rest is upscaled).
    pub viewport_scale: f32,
    pub max_bounces: u32,
    /// Samples per pixel accumulated per displayed frame while the camera moves.
    pub samples_while_moving: u32,
    pub texture_memory_megabytes: u32,
    pub geometry_memory_megabytes: u32,
}

impl QualityTier {
    pub fn budget(self) -> TierBudget {
        match self {
            QualityTier::Economico => TierBudget {
                viewport_scale: 0.5,
                max_bounces: 2,
                samples_while_moving: 1,
                texture_memory_megabytes: 512,
                geometry_memory_megabytes: 512,
            },
            QualityTier::Equilibrado => TierBudget {
                viewport_scale: 0.75,
                max_bounces: 3,
                samples_while_moving: 1,
                texture_memory_megabytes: 1536,
                geometry_memory_megabytes: 1024,
            },
            QualityTier::Alto => TierBudget {
                viewport_scale: 1.0,
                max_bounces: 4,
                samples_while_moving: 2,
                texture_memory_megabytes: 3072,
                geometry_memory_megabytes: 2048,
            },
            QualityTier::Ultra => TierBudget {
                viewport_scale: 1.0,
                max_bounces: 6,
                samples_while_moving: 4,
                texture_memory_megabytes: 6144,
                geometry_memory_megabytes: 4096,
            },
        }
    }
}

/// The tier a machine should start in: the lower of what its speed and its memory allow.
///
/// Measured speed decides when the calibration has run; otherwise the kind of adapter gives a cautious guess. Video
/// memory then caps the result, because a fast card with little memory still cannot hold a large scene. A machine
/// on battery starts one tier lower, and one known to have less than 8 GB of system memory starts in the lowest.
pub fn suggest_tier(capabilities: &Capabilities) -> QualityTier {
    const GIGABYTE: u64 = 1 << 30;
    let by_speed = match capabilities.measured_mega_rays {
        Some(rate) if rate >= 2500.0 => QualityTier::Ultra,
        Some(rate) if rate >= 700.0 => QualityTier::Alto,
        Some(rate) if rate >= 120.0 => QualityTier::Equilibrado,
        Some(_) => QualityTier::Economico,
        None if capabilities.adapter == AdapterKind::Discrete => QualityTier::Equilibrado,
        None => QualityTier::Economico,
    };
    let by_memory = match capabilities.adapter {
        AdapterKind::Cpu => QualityTier::Economico,
        AdapterKind::Integrated => QualityTier::Equilibrado,
        AdapterKind::Discrete => match capabilities.video_memory_bytes {
            Some(bytes) if bytes >= 10 * GIGABYTE => QualityTier::Ultra,
            Some(bytes) if bytes >= 5 * GIGABYTE => QualityTier::Alto,
            Some(bytes) if bytes >= 3 * GIGABYTE => QualityTier::Equilibrado,
            Some(_) => QualityTier::Economico,
            // Unknown memory: assume a modest card rather than risk filling it.
            None => QualityTier::Equilibrado,
        },
    };
    if capabilities
        .system_memory_bytes
        .is_some_and(|bytes| bytes < 8 * GIGABYTE)
    {
        return QualityTier::Economico;
    }
    let tier = by_speed.min(by_memory);
    match (capabilities.on_battery, tier) {
        (true, QualityTier::Ultra) => QualityTier::Alto,
        (true, QualityTier::Alto) => QualityTier::Equilibrado,
        (true, _) => QualityTier::Economico,
        (false, tier) => tier,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn budgets_never_shrink_as_the_tier_rises() {
        let tiers = [
            QualityTier::Economico,
            QualityTier::Equilibrado,
            QualityTier::Alto,
            QualityTier::Ultra,
        ];
        for pair in tiers.windows(2) {
            let (lower, higher) = (pair[0].budget(), pair[1].budget());
            assert!(higher.viewport_scale >= lower.viewport_scale);
            assert!(higher.max_bounces >= lower.max_bounces);
            assert!(higher.samples_while_moving >= lower.samples_while_moving);
            assert!(higher.texture_memory_megabytes >= lower.texture_memory_megabytes);
            assert!(higher.geometry_memory_megabytes >= lower.geometry_memory_megabytes);
        }
    }

    #[test]
    fn a_machine_without_a_graphics_adapter_starts_in_the_lowest_tier() {
        let capabilities = Capabilities {
            adapter: AdapterKind::Cpu,
            video_memory_bytes: None,
            system_memory_bytes: Some(8 << 30),
            measured_mega_rays: None,
            on_battery: false,
        };
        assert_eq!(suggest_tier(&capabilities), QualityTier::Economico);
    }

    #[test]
    fn the_measured_machines_land_in_the_expected_tiers() {
        const GIGABYTE: u64 = 1 << 30;
        let laptop_rtx = Capabilities {
            adapter: AdapterKind::Discrete,
            video_memory_bytes: Some(6 * GIGABYTE),
            system_memory_bytes: Some(32 * GIGABYTE),
            measured_mega_rays: Some(1213.0),
            on_battery: false,
        };
        assert_eq!(suggest_tier(&laptop_rtx), QualityTier::Alto);
        assert_eq!(
            suggest_tier(&Capabilities {
                on_battery: true,
                ..laptop_rtx
            }),
            QualityTier::Equilibrado
        );
        // Fast but short of video memory: memory wins.
        assert_eq!(
            suggest_tier(&Capabilities {
                video_memory_bytes: Some(2 * GIGABYTE),
                ..laptop_rtx
            }),
            QualityTier::Economico
        );
        // Not calibrated yet: a discrete card starts in the middle, never at the top.
        assert_eq!(
            suggest_tier(&Capabilities {
                measured_mega_rays: None,
                ..laptop_rtx
            }),
            QualityTier::Equilibrado
        );
        let integrated = Capabilities {
            adapter: AdapterKind::Integrated,
            video_memory_bytes: None,
            system_memory_bytes: Some(16 * GIGABYTE),
            measured_mega_rays: Some(214.0),
            on_battery: false,
        };
        assert_eq!(suggest_tier(&integrated), QualityTier::Equilibrado);
        assert_eq!(
            suggest_tier(&Capabilities {
                system_memory_bytes: Some(4 * GIGABYTE),
                ..integrated
            }),
            QualityTier::Economico
        );
    }
}
