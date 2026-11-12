# Electrical lighting in the native mesher

The mesher calculates light output from branch breakers, panel status, connected loads, voltage compatibility,
switches, three-way switching, and dimmers. It carries per-triangle dimming into the render scene using the same
1/64 emission steps as the C# renderer. Lighting components controlled by switches use the same calculation.

`tests/electrical-simulation.rs` compares triangle positions, surface categories, mesh emission scales, and rendered
emission against a project exported by `tests/GuanacasteRender.RenderBench/electrical-simulation-reference.cs`. The
fixture covers live and disconnected circuits, overloaded breakers and panels, wrong voltage, inverter generation,
single-pole and three-way switches, a dimmer, and a controlled lighting component. It also checks that smooth and
combined geometry keep per-triangle emission scales aligned.

This implements the lighting state needed for native previews. Full electrical engineering reports, editing, and
the rest of the desktop application's project workflows are separate migration work.
