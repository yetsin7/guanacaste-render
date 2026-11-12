# Rust migration status

Updated 2026-11-12. The migration is in progress; the Rust studio is not yet a replacement for the C# application.
The installed C# application remains the source of truth for project features and rendering that have not been ported.

## Implemented and checked

- Native reading of schema 15 project containers and meshing of walls, openings, flat and pitched slabs, columns,
  structural elements, slab openings, procedural vegetation, and the complete residential component catalog.
- Shed, gable, and hip roofs, including overhangs and gable walls; surveyed terrain with smooth normals and plants
  positioned on the terrain.
- Water meters, sewer and storm outlets, cisterns, elevated tanks, and septic systems with drain fields. Cold-water,
  hot-water, sanitary, and storm drainage routing reproduces the supported source and fixture geometry, including
  fixture sizes, traps, vent stacks, household sizing, and rainfall-based downspout sizing.
- The geometry of all 25 electrical catalog device types, including solar arrays, appliances, fire equipment, and
  standalone lights. Projects with circuit-connected lights or switches controlling elements still require the bridge.
- A Rust studio with a progressive GPU path-traced viewport. Projects requiring unported geometry, saved render
  settings, material assignments, saved scenes, render effects, or linked-model sidecars use the installed
  application's temporary scene-export bridge. If no bridge is available, the studio identifies the partial view.
  Converted views also disclose the bridge's rendering limits, including external models and effects it cannot export.
- Independent bridge workspaces prevent simultaneous loads from overwriting one another's temporary scenes.
- The studio interface follows the Stitch prototype screen by screen: Projects, Build, Nature library, Asset
  inspector, Material editor, Movie studio, Render studio, Settings, and Help. Every figure shown is measured or read
  from the project; features the engine does not have yet (wind, levels of detail, seasons, depth of field, textures,
  batch rendering, cloud sync, people and vehicle assets, move/rotate/scale gizmos) are shown disabled with the reason.
- Navigation is rasterized and redraws only on change; ray tracing runs only for "Vista previa" and "Render".
- Vegetation placed in Build exists only for the session; it is not yet written back to the `.gr` project.

## Evidence and limits

The permanent tests compare source-application reference meshes with Rust, including all 44 non-box residential
shapes in two orientations, twelve roofs, surveyed terrain and shading normals, and representative plumbing systems,
storm drainage, water-storage sizing, a septic tank with a multi-trench field, and every electrical device in two
orientations. Separate electrical fixtures check standalone lights on and unconnected lights with a panel off.
The full workspace test suite and C# reference generator compile on the development machine.

The following measurements compare all non-ground triangles in generated reference projects. Reference meshes also
contain the two triangles of the ground plane. The recorded distance is the largest absolute coordinate difference;
every compared surface category matched.

| Project | Matched element ranges | Rust triangles | Reference triangles | Maximum difference |
|---|---:|---:|---:|---:|
| Cobogo | 184 / 184 | 1,502,686 | 1,502,688 | 0.000044 m |
| Demonstration house | 18 / 18 | 2,696 | 2,698 | 0 m |
| Garden | 3 / 3 | 43,794 | 43,796 | 0.00000061 m |
| Modern house | 36 / 36 | 2,201,198 | 2,201,200 | 0.000051 m |
| Architectural showcase | 34 / 34 | 3,996 | 3,998 | 0 m |

These numbers establish geometry parity for these inputs only. The Cobogo project contains saved render settings,
so its studio preview still uses the bridge even though its measured geometry is covered in Rust.

## Still required

- Port the remaining plumbing design reports and engineering analyses, including pump power and electrical status;
  validate complex real-world systems beyond the reference fixtures.
- Port electrical circuit and controlled-lighting analysis, including dimmed emission; family meshes, in-place
  families, elevators, structural-member cuts, and any remaining unsupported project elements.
- Support older project-schema migrations, project editing, validation, persistence, and the remaining BIM services.
- Port material assignments, textures and alpha cutouts, saved render settings, linked models, lighting, environment,
  denoising, and the other render features that the existing scene bridge does not carry.
- Rebuild the remainder of the desktop application and import/export workflows in Rust. The present studio covers
  navigation, preview and final rendering, vegetation placement, material edits, camera animation with PNG-sequence
  export, and settings; it does not save edits into projects and is not full WPF feature parity.
- Validate real user projects, visual fidelity, installations, and interoperability before retiring the C# application.

The separate `guanacaste-render` checkout contains unrelated uncommitted work. Do not reset or overwrite that checkout,
the user's projects, or personal renders while continuing the migration.

## Reproducing checks

From the repository root, with Rust available on `PATH`:

```text
cargo test --manifest-path engine/Cargo.toml --workspace
cargo run --release --manifest-path engine/Cargo.toml -p gr-bench --bin mesh -- artifacts/mesh-reference
cargo build --release --manifest-path engine/Cargo.toml -p gr-studio --bin GuanacasteStudio
```

`artifacts/mesh-reference` is generated by the existing C# `--mesh-reference` benchmark. The smaller checked-in
fixtures can be regenerated with `dotnet run -c Release --project tests/GuanacasteRender.RenderBench --` followed by
`--residential-reference`, `--roof-reference`, `--terrain-reference`, `--plumbing-point-reference`, or
`--plumbing-system-reference`, `--storm-reference`, `--water-storage-reference`, `--septic-reference`, or
`--electrical-reference`, and `--out engine/crates/gr-model/tests/fixtures`. The residential-components fixture is a
compact extraction of the generated Cobogo sample and its original element ranges.
