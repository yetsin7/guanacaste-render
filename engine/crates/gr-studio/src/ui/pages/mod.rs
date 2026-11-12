//! One module per screen of the studio, plus the pieces they share.

mod build;
mod common;
mod help;
mod inspector;
mod library;
mod materials;
mod movie;
mod projects;
mod render;
mod settings;

use crate::app::{Page, Studio};
use eframe::egui::Ui;
use eframe::egui_wgpu::RenderState;

/// What the pages remember between frames that is about the interface only, not about the project.
#[derive(Default)]
pub struct Local {
    build: build::BuildUi,
    projects: projects::ProjectsUi,
    materials: materials::MaterialsUi,
}

/// Draws the current page into the central area.
pub fn show(ui: &mut Ui, studio: &mut Studio, state: Option<&RenderState>) {
    let mut local = std::mem::take(&mut studio.local);
    match studio.page {
        Page::Projects => projects::show(ui, studio, state, &mut local.projects),
        Page::Build => build::show(ui, studio, state, &mut local.build),
        Page::Library => library::show(ui, studio, state),
        Page::Inspector => inspector::show(ui, studio, state),
        Page::Materials => materials::show(ui, studio, state, &mut local.materials),
        Page::Movie => movie::show(ui, studio, state),
        Page::Render => render::show(ui, studio, state),
        Page::Settings => settings::show(ui, studio, state),
        Page::Help => help::show(ui, studio, state),
    }
    studio.local = local;
}
