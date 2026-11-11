//! The studio window: top bar, side panel, status bar and the 3D view, and the flow of opening a project.

use crate::project::{self, Loaded};
use crate::theme;
use crate::viewport::Viewport;
use eframe::egui::{self, RichText};
use gr_core::hardware::{AdapterKind, Capabilities, QualityTier, suggest_tier};
use gr_gpu::wgpu;
use std::path::PathBuf;
use std::sync::mpsc::Receiver;

const TIERS: [(QualityTier, &str, &str); 4] = [
    (QualityTier::Economico, "Económico", "Gráfica integrada o equipo modesto: media resolución, 2 rebotes."),
    (QualityTier::Equilibrado, "Equilibrado", "Gráfica dedicada de gama media: 75 % de resolución, 3 rebotes."),
    (QualityTier::Alto, "Alto", "Gráfica potente: resolución completa, 4 rebotes."),
    (QualityTier::Ultra, "Ultra", "Equipo de alto rendimiento: resolución completa, 6 rebotes."),
];

pub struct StudioApp {
    viewport: Viewport,
    tier: QualityTier,
    suggested: QualityTier,
    loading: Option<(Receiver<Loaded>, String)>,
    project: Option<PathBuf>,
    /// Last thing worth telling the user: an error, or a confirmation.
    message: Option<(String, bool)>,
}

impl StudioApp {
    pub fn new(creation: &eframe::CreationContext<'_>, open: Option<PathBuf>) -> StudioApp {
        theme::apply(&creation.egui_ctx);
        let adapter = creation.wgpu_render_state.as_ref().map(|state| state.adapter.get_info().device_type);
        let suggested = suggest_tier(&Capabilities {
            adapter: match adapter {
                Some(wgpu::DeviceType::DiscreteGpu) => AdapterKind::Discrete,
                Some(wgpu::DeviceType::IntegratedGpu) => AdapterKind::Integrated,
                _ => AdapterKind::Cpu,
            },
            // Not reported by the graphics API; the tier stays a cautious suggestion until calibration exists.
            video_memory_bytes: None,
            system_memory_bytes: None,
            measured_mega_rays: None,
            on_battery: false,
        });
        let mut app = StudioApp {
            viewport: Viewport::new(),
            tier: suggested,
            suggested,
            loading: None,
            project: None,
            message: None,
        };
        if let Some(path) = open {
            app.open(path, &creation.egui_ctx);
        }
        app
    }

    fn open(&mut self, path: PathBuf, context: &egui::Context) {
        let name = path.file_name().map(|name| name.to_string_lossy().into_owned()).unwrap_or_default();
        let wake = context.clone();
        self.loading = Some((project::load_in_background(path, move || wake.request_repaint()), name));
        self.message = None;
    }

    fn receive_loaded(&mut self, frame: &eframe::Frame) {
        let Some((receiver, _)) = &self.loading else { return };
        let Ok(result) = receiver.try_recv() else { return };
        self.loading = None;
        let Some(state) = frame.wgpu_render_state() else {
            self.message = Some(("Esta ventana no tiene acceso a la tarjeta gráfica.".into(), true));
            return;
        };
        match result.and_then(|opened| {
            self.viewport.set_scene(state, &opened.scene, self.tier.budget().max_bounces).map(|()| (opened.path, opened.notice))
        }) {
            Ok((path, notice)) => {
                self.project = Some(path);
                self.message = notice.map(|text| (text, true));
            }
            Err(reason) => self.message = Some((reason, true)),
        }
    }

    fn top_bar(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            ui.label(RichText::new("Guanacaste Render").strong().size(15.0));
            ui.label(RichText::new("Studio").color(theme::ACCENT).size(15.0));
            ui.separator();
            if ui.button("Abrir…").on_hover_text("Proyecto .gr / .g3d o escena .grscene").clicked()
                && let Some(path) = rfd::FileDialog::new()
                    .add_filter("Proyectos y escenas de Guanacaste", &project::PROJECT_EXTENSIONS)
                    .pick_file()
            {
                self.open(path, ui.ctx());
            }
            if ui.add_enabled(self.viewport.has_scene(), egui::Button::new("Guardar imagen…")).clicked()
                && let Some(path) =
                    rfd::FileDialog::new().add_filter("Imagen PNG", &["png"]).set_file_name("render.png").save_file()
            {
                self.message = Some(match self.viewport.save_picture(&path) {
                    Ok(()) => (format!("Imagen guardada en {}", path.display()), false),
                    Err(reason) => (reason, true),
                });
            }
            if let Some(path) = &self.project {
                ui.separator();
                let name = path.file_name().map(|name| name.to_string_lossy().into_owned()).unwrap_or_default();
                ui.label(RichText::new(name).color(theme::MUTED)).on_hover_text(path.display().to_string());
            }
        });
    }

    fn side_panel(&mut self, ui: &mut egui::Ui) {
        egui::ScrollArea::vertical().show(ui, |ui| {
            section(ui, "Cámara");
            ui.add(egui::Slider::new(self.viewport.field_of_view(), 15.0..=110.0).fixed_decimals(0).suffix("°").text("Campo de visión"));
            if ui.button("Volver a la vista inicial").clicked() {
                self.viewport.go_home();
            }
            ui.label(
                RichText::new("Arrastrar: orbitar · Botón derecho: desplazar\nRueda: acercar · W A S D Q E: caminar")
                    .color(theme::MUTED)
                    .small(),
            );

            section(ui, "Luz del día");
            let settings = &mut self.viewport.settings;
            ui.add(egui::Slider::new(&mut settings.sun_azimuth_degrees, 0.0..=360.0).fixed_decimals(0).suffix("°").text("Orientación del sol"));
            ui.add(egui::Slider::new(&mut settings.sun_elevation_degrees, 1.0..=90.0).fixed_decimals(0).suffix("°").text("Altura del sol"));
            ui.add(egui::Slider::new(&mut settings.sun_intensity, 0.0..=6.0).fixed_decimals(1).text("Intensidad del sol"));
            ui.add(egui::Slider::new(&mut settings.sky_intensity, 0.0..=4.0).fixed_decimals(1).text("Intensidad del cielo"));

            section(ui, "Imagen");
            ui.add(egui::Slider::new(&mut settings.exposure, 0.05..=4.0).logarithmic(true).fixed_decimals(2).text("Exposición"));
            ui.add(egui::Slider::new(&mut settings.max_bounces, 0..=8).text("Rebotes de luz"));
            ui.add(egui::Slider::new(&mut settings.target_samples, 16..=4096).logarithmic(true).text("Muestras"));

            section(ui, "Calidad");
            for (tier, name, hint) in TIERS {
                let label = if tier == self.suggested { format!("{name}  (sugerido)") } else { name.to_string() };
                if ui.radio(self.tier == tier, label).on_hover_text(hint).clicked() && self.tier != tier {
                    self.tier = tier;
                    self.viewport.settings.max_bounces = tier.budget().max_bounces;
                    self.viewport.restart();
                }
            }

            section(ui, "Escena");
            let facts = &self.viewport.facts;
            fact(ui, "Triángulos", &group_thousands(facts.triangles));
            fact(ui, "Materiales", &facts.materials.to_string());
            fact(ui, "Memoria de vídeo", &format!("{:.0} MB", facts.scene_megabytes + self.viewport.image_megabytes()));
            ui.label(RichText::new(&facts.adapter).color(theme::MUTED).small());
            ui.add_space(6.0);
            ui.label(
                RichText::new("Versión preliminar: aún sin texturas, vegetación recortada ni luces artificiales.")
                    .color(theme::WARNING)
                    .small(),
            );
        });
    }

    fn status_bar(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            if let Some((_, name)) = &self.loading {
                ui.spinner();
                ui.label(format!("Abriendo {name}…"));
                return;
            }
            if self.viewport.has_scene() {
                let (width, height) = self.viewport.resolution();
                let (samples, target) = (self.viewport.samples(), self.viewport.settings.target_samples);
                ui.label(format!("{} / {target} muestras", samples.min(target)));
                ui.separator();
                ui.label(format!("{width} × {height}"));
                ui.separator();
                let rate = self.viewport.samples_per_second();
                ui.label(if rate > 0.0 { format!("{rate:.0} muestras/s") } else { "Imagen terminada".to_string() });
            }
            if let Some((text, is_error)) = &self.message {
                ui.separator();
                ui.label(RichText::new(text).color(if *is_error { theme::WARNING } else { theme::MUTED }));
            }
        });
    }

    fn empty_state(&mut self, ui: &mut egui::Ui) {
        ui.vertical_centered(|ui| {
            ui.add_space(ui.available_height() * 0.36);
            ui.label(RichText::new("Abre un proyecto para verlo con luz real").size(20.0));
            ui.add_space(4.0);
            ui.label(
                RichText::new("Proyectos de Guanacaste Render (.gr), de Guanacaste 3D (.g3d) o escenas (.grscene)")
                    .color(theme::MUTED),
            );
            ui.add_space(14.0);
            if ui.button(RichText::new("Abrir proyecto…").size(15.0)).clicked()
                && let Some(path) = rfd::FileDialog::new()
                    .add_filter("Proyectos y escenas de Guanacaste", &project::PROJECT_EXTENSIONS)
                    .pick_file()
            {
                self.open(path, ui.ctx());
            }
        });
    }
}

impl eframe::App for StudioApp {
    fn ui(&mut self, ui: &mut egui::Ui, frame: &mut eframe::Frame) {
        self.receive_loaded(frame);
        let bar = egui::Frame::NONE.fill(theme::PANEL).inner_margin(egui::Margin::symmetric(12, 7));
        egui::Panel::top("top-bar").frame(bar).show(ui, |ui| self.top_bar(ui));
        egui::Panel::bottom("status-bar").frame(bar).show(ui, |ui| self.status_bar(ui));
        if self.viewport.has_scene() {
            egui::Panel::right("side-panel")
                .frame(egui::Frame::NONE.fill(theme::PANEL).inner_margin(egui::Margin::same(12)))
                .default_size(300.0)
                .resizable(false)
                .show(ui, |ui| self.side_panel(ui));
        }
        egui::CentralPanel::default().frame(egui::Frame::NONE.fill(theme::BACKGROUND)).show(ui, |ui| {
            match (self.viewport.has_scene(), frame.wgpu_render_state()) {
                (true, Some(state)) => self.viewport.show(ui, state, &self.tier.budget()),
                _ if self.loading.is_none() => self.empty_state(ui),
                _ => {}
            }
        });
    }
}

fn section(ui: &mut egui::Ui, title: &str) {
    ui.add_space(10.0);
    ui.label(RichText::new(title.to_uppercase()).color(theme::MUTED).small().strong());
    ui.separator();
}

fn fact(ui: &mut egui::Ui, name: &str, value: &str) {
    ui.horizontal(|ui| {
        ui.label(RichText::new(name).color(theme::MUTED));
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| ui.label(value));
    });
}

fn group_thousands(value: usize) -> String {
    let digits = value.to_string();
    let mut grouped = String::with_capacity(digits.len() + digits.len() / 3);
    for (index, digit) in digits.chars().enumerate() {
        if index > 0 && (digits.len() - index).is_multiple_of(3) {
            grouped.push(' ');
        }
        grouped.push(digit);
    }
    grouped
}

#[cfg(test)]
mod tests {
    #[test]
    fn thousands_are_grouped_with_spaces() {
        assert_eq!(super::group_thousands(0), "0");
        assert_eq!(super::group_thousands(999), "999");
        assert_eq!(super::group_thousands(2_617_926), "2 617 926");
    }
}
