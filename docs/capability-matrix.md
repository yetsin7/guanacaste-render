# Capability matrix

Last reviewed: 2026-11-10, version 1.0.6. This is an evidence record, not a feature list for marketing. A row is
only **Working** when an automated check or an inspected output backs it; the evidence column names it.

States:

- **Working**: implemented and backed by a check that passed on the review date, or by inspected output.
- **Partial**: implemented with a stated limit that a professional user will meet.
- **Missing**: not implemented.
- **Unverified**: code exists, but nothing run on the review date demonstrates it.

Checks named below live in `tests/GuanacasteRender.Checks` (core suite) and `tests/desktop-interaction-checks`
(`studio`, `model-update` and `render-recovery` suites). `scripts/verify.ps1` runs all of them.

## Rendering technology

The renderer is a progressive CPU path tracer (`src/GuanacasteRender.Core/rendering/path-tracer.cs`) with a BVH,
sky portals, light sampling, a radiance cache and an edge-aware denoiser. The interactive viewport is a WPF
`Viewport3D` rasterised preview, not the path tracer. No GPU backend exists: the hardware profile reports the GPU
and states that it is unused. Consequences that bound every row below:

- Final quality scales with CPU cores and time; there is no real-time ray-traced viewport.
- The interactive view shows flat-shaded materials and simplified vegetation; lighting, reflections, weather,
  grass and water are only visible in the preview render and the final image.
- Movie time is frame time multiplied by frame count; long sequences are hours on a laptop CPU.

## Matrix

| Area | State | Evidence and limits |
|---|---|---|
| Open and save `.gr`, `.g3d`, legacy `.g3`/`.g3d.json` | Working | `native-document-checks`, `project-save-checks`; atomic replace and `.bak` copy |
| Export from Guanacaste 3D | Working | `RenderModelExport` checks in `render-model-update-checks`; re-export merges render work |
| Model update from the modeling application | Working | `model-update` suite: notice, update, undo, save, reopen, second update, export path; screenshots in `artifacts/model-update/` |
| Model update of linked external models (OBJ, IFC, glTF) | Partial | New links are adopted and files reloaded; their geometry is not merged or diffed |
| Live link while the model is unsaved | Missing | Changes are noticed only after the model file is saved (2 s polling) |
| Revit exchange | Unverified | `revit-exchange-checks` pass on fixtures; no Revit run on the review date |
| Viewport navigation, eye height | Working | `studio` suite, `navigation-checks`, `orbit-pivot-checks` |
| Scene inspector, selection, visibility with undo | Working | `studio` suite; linked models are not broken into editable objects |
| Move, rotate, elevate, duplicate, delete | Partial | `studio` suite; duplicate and elevate only for supported object types; no scale gizmo |
| Mass placement (lines, circles, size variation) | Working | `mass-placement-checks`, `mass-placement-interaction-checks` |
| Surface scattering by painting or density maps | Missing | Only line and circle groups exist |
| PBR materials: base, normal, roughness, metallic, relief | Working | `pbr-material-audit-checks`, `material-tint-checks`, `studio` suite |
| Material per element | Working | `render-detail-checks`; per face of an element is not supported |
| Texture UV offset and rotation controls | Partial | Tile size is editable; offset and rotation are not exposed |
| Sun by date, time and latitude; sky model; clouds | Working | `solar-checks`, `render-sun-checks`, `render-look-checks` |
| HDRI environment lighting | Working | `render-infrastructure-checks`; Radiance `.hdr` only, no `.exr` |
| Artificial lights with IES profiles | Working | `lighting-checks`, `render-engine-checks` |
| Rain, snow, wet ground, fog | Partial | `render-weather-checks`, `render-wet-checks`; rain and snow streaks are image overlays, wetness is rendered |
| Terrain (toposolid from points) | Partial | `mass-and-site-checks`, `site-import-checks`; no sculpting brushes in the studio |
| Water and ocean | Partial | `render-water-checks`; one ocean plane per project, no rivers or pools as water volumes |
| Grass | Working | `render-grass-checks`; rendered only, not visible in the interactive view |
| Vegetation, people and vehicle library | Partial | 18 bundled families; people and cars follow routes (`render-people-checks`); no wind animation of trees |
| Cameras, saved photos, lens shift, vertical correction | Working | `render-scene-bookmark-checks`, `render-photo-checks`, `studio` suite |
| Depth of field (f-number and focus distance) | Working | `render-camera-effect-checks`; not shown in the interactive view |
| Final still render with pause, stop and memory watchdog | Working | `studio` suite renders and decodes a real PNG; `render-engine-checks` |
| Real-time ray-traced viewport | Missing | CPU-only renderer; see "Rendering technology" |
| GPU acceleration | Partial | A GPU path tracer exists in `engine/` (Rust, wgpu) and is verified from the command line (`docs/engine/OPTIMIZATION_RESULTS.md`). It is not connected to the application: renders made from the studio still run on the CPU, and the performance panel still reports the GPU as unused |
| Denoising | Working | `denoiser-checks`: 4-spp indirect error cut by over 35 % |
| Effects, looks, LUT colour grading | Working | `render-fx-checks`, `render-look-checks`; `.cube` LUT import |
| Panorama 360° (equirectangular) | Partial | `render-panorama-checks` renders the demonstration house at 768 × 384 and checks the seam, zenith and nadir; the picture in `artifacts/panorama-checks/` was inspected. The studio's 4096 × 2048 export button was not run on the review date |
| Stereo panorama, virtual tour export | Missing | |
| Movie: camera path, people, layers, MP4 (H.264) | Unverified | `render-motion-checks` and `render-movie-storage-checks` pass; the video suite (`verify.ps1 -IncludeVideo`) was not run on the review date |
| Autosave | Working | `auto-recovery-checks` |
| Recovery prompt after a crash | Working | `render-recovery` suite: an interrupted session is offered at the next start, reopens as unsaved work in the render workspace and keeps its model link |
| Asset management (collect textures into the project) | Missing | Textures, HDRI and LUT files are external references that must stay reachable |
| Accessibility | Partial | Tool buttons carry automation names; several studio labels are hard-coded and keyboard-only operation is untested |
| Localization | Partial | Spanish only; many studio strings are literals in XAML and code rather than resources |

## Known defects seen during the review

- The status bar can show "No hay geometría que mostrar" while a scene is displayed after a project is attached
  (`artifacts/model-update/01-before.png`).
- `README.md` still describes the interface by reference to another product's layout; the wording should describe
  Guanacaste Render on its own terms.

## Next milestones, in order of value

1. Collect external textures, HDRI and LUT files into a project folder so a project can move between computers.
2. Run the studio's full-size panorama export and a short movie in `verify.ps1`, then mark those rows from evidence.
3. Move hard-coded studio strings into resources and add an English dictionary.
4. Texture offset and rotation per material, and a scale gizmo.
5. Notice model changes before the model is saved (a live link between the two running applications).
6. Evaluate a GPU backend behind the existing `RenderSession` interface; until then keep performance claims CPU-based.
