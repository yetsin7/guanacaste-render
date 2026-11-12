# Engine direction and historical handoff

This document records the state before the latest migration work on 2026-11-11. For current implementation details,
validation, and remaining work, see [Rust migration status](rust-migration-status.md).

## Product direction

The product owner chose Rust for practically all of Guanacaste Render and allowed the user interface to be rebuilt.
The migration proceeds from the inside out while keeping the installed application usable:

1. Build the Rust CPU and GPU rendering engine.
2. Use a temporary `.grscene` export bridge from the C# application.
3. Develop a new Rust studio with its own user interface and progressively replace the WPF workflows.
4. Read and mesh native BIM projects in Rust, eventually removing the bridge.

The GPU backend uses wgpu compute shaders on DirectX 12 or Vulkan. Hardware ray tracing remains an optional future
acceleration, not a requirement.

## Historical baseline

At this handoff, the repository contained approximately 127,499 lines of C# across 819 files. The Rust code had a
four-way CPU BVH, a preliminary GPU path tracer, and an early studio with navigation, rendering controls, and PNG
export. The native project reader handled schema 15; older schemas still required C# migrations.

The measured C# rendering block was about 13,280 lines, the WPF desktop block about 50,000, BIM and session code about
25,000, importers about 6,803, and tests about 24,000. The Rust renderer still lacked textures, physical sky,
artificial lights, denoising, effects, and film features. The Rust studio had not replaced the material, object,
saved-camera, and output workflows. Editing, validation, schema migration, and importers were not yet ported.

The earlier benchmark matched all covered wall, opening, flat-slab, column, and procedural-plant elements against the
C# mesher. Plants matched within 0.05 mm, with small residual differences from trigonometric rounding. The comparison
then reported these figures:

| Project | Matched element ranges | Rust / reference triangles | Uncovered items | Maximum difference |
|---|---:|---:|---:|---:|
| Demonstration house | 18 / 18 | 2,696 / 2,698 | 0 | 0 m |
| Garden | 3 / 3 | 43,794 / 43,796 | 0 | 0.0000006 m |
| Architectural showcase | 20 / 34 | 2,276 / 3,998 | 14 | 0 m |
| Modern house | 30 / 36 | 2,200,430 / 2,201,200 | 6 | 0.00005 m |
| Cobogo | 136 / 184 | 1,489,362 / 1,502,688 | 56 | 0.00004 m |

The missing two triangles in the Rust counts were the ground plane, which the studio added separately. Coverage by
triangle count did not imply that a project could open completely without the bridge: small missing components still
mattered. The current status document supersedes these historical coverage figures.

The original plan prioritized residential components, stairs, railings, curtain walls, slab openings, services,
pitched roofs, terrain, mesh families, project materials, and textures. Each migrated module was to be compared with
the C# application using identical inputs before replacing it.

## Workspace considerations

The `guanacaste-render` checkout contained unrelated uncommitted model-update work, so engine work took place in the
separate `guanacaste-render-nextgen` worktree. Preserve that work and all user projects and renders. The earlier
performance notes also recorded the CPU BVH memory use and a 32-bit shader ray counter; see the other documents in
this directory for the benchmark details.
