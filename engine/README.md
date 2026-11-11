# Motor de Guanacaste Render (Rust)

Todo el código Rust del producto vive en esta carpeta. Cada crate tiene una sola responsabilidad y depende solo de los
que están por encima de él en esta lista.

```text
engine/
├── Cargo.toml            Workspace: versiones de dependencias y perfil de compilación
├── crates/
│   ├── gr-core/          Núcleo sin GPU ni ventana. Sin `unsafe`.
│   │   └── src/
│   │       ├── scene_file.rs   Lector validado de escenas `.grscene`
│   │       ├── bvh.rs          Jerarquía de 4 vías con SIMD (CPU) y jerarquía plana (GPU)
│   │       └── hardware.rs     Perfiles de calidad y sugerencia según el equipo
│   ├── gr-gpu/           Trazador de rayos en la tarjeta gráfica (wgpu: DirectX 12 y Vulkan)
│   │   ├── shaders/
│   │   │   ├── path_tracer.wgsl    El trazador: recorrido del BVH, materiales, sol y cielo
│   │   │   └── resolve.wgsl        De radiancia acumulada a imagen en pantalla (exposición y tono)
│   │   └── src/
│   │       ├── tracer.rs           Sube la escena, lanza pasadas, redimensiona y lee la imagen
│   │       └── camera.rs           Marco de cámara compartido entre shader y CPU
│   ├── gr-studio/        La aplicación de escritorio: GuanacasteStudio.exe
│   │   └── src/
│   │       ├── main.rs             Arranque de la ventana y del dispositivo gráfico
│   │       ├── app.rs              Barra superior, panel lateral, barra de estado, abrir y guardar
│   │       ├── viewport.rs         Visor 3D: entrada, ritmo de muestreo y presentación
│   │       ├── orbit_camera.rs     Órbita, desplazamiento, zoom y caminar
│   │       ├── project.rs          Abrir `.grscene` y proyectos `.gr`/`.g3d` (puente temporal)
│   │       └── theme.rs            Colores y espaciado de la interfaz
│   └── gr-bench/         Mediciones reproducibles contra el motor C#
│       └── src/
│           ├── main.rs             CPU: construcción del BVH y rayos por segundo
│           └── gpu.rs              GPU: verificación del primer impacto, render y rayos por segundo
└── target/               Salida de compilación (no se versiona)
```

## Cómo abre proyectos el estudio

El estudio lee escenas `.grscene` directamente. Para un proyecto `.gr` o `.g3d` ejecuta, sin ventana, la aplicación
C# instalada (`GuanacasteRender.exe --export-scene <proyecto> <salida.grscene>`), que triangula el modelo y escribe la
escena (`SceneExportBridge` y `SceneExchange`). Ese puente es temporal: desaparece cuando el motor lea y triangule
proyectos por sí mismo. El formato está especificado en la cabecera de `crates/gr-core/src/scene_file.rs`.

## Comandos

Desde la raíz del repositorio:

```text
cargo test  --release --manifest-path engine/Cargo.toml
dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --baseline --out artifacts/engine-bench
cargo run  --release --manifest-path engine/Cargo.toml -p gr-bench -- artifacts/engine-bench
cargo run  --release --manifest-path engine/Cargo.toml -p gr-bench --bin gpu -- artifacts/engine-bench
cargo run  --release --manifest-path engine/Cargo.toml -p gr-studio -- artifacts/engine-bench/cobogo.grscene
```

Los resultados (JSON, tabla comparativa e imágenes) quedan en `artifacts/engine-bench/`, que no se versiona. El estado
medido y los siguientes pasos están en `docs/engine/`.
