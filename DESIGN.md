# Studio interface direction

Guanacaste Render Studio is a desktop architectural visualization workspace. Its visual reference is the supplied
Stitch workstation: a dark canvas, narrow tool rail, compact inspectors, and a live scene at the center of the work.

## Visual language

- Use charcoal surfaces (`#121316`, `#1a1b1e`, and `#292a2d`) separated by quiet one-pixel borders.
- Reserve pale blue (`#adc6ff`) for primary actions and the active tool; use teal (`#5cdbc0`) for live status.
- Use a proportional system font for headings and descriptions and a monospace font for measurements and status.
- Keep the left rail near 48 pixels and the inspector near 320 pixels; let the real render occupy the remaining area.
- Show actual project names, rendering progress, hardware, and scene counts. Do not fabricate project thumbnails,
  hardware telemetry, performance figures, or editing features.

## Interaction boundaries

The project dashboard opens supported files, remembers recently opened projects locally, and previews the active
scene. Render and environment controls affect the actual renderer. A camera reset returns to the opening view.
The material inspector exposes the loaded scene's actual properties in read-only form. Material editing, object
placement, asset libraries, and movie timelines require their underlying engines before their reference controls can
be enabled. Unimplemented workspaces must state their availability clearly.

Runtime colors and typography are owned by `engine/crates/gr-studio/src/theme.rs`; interface text is owned by
`engine/crates/gr-studio/locales/spanish.properties`.
