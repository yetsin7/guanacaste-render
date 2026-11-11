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
│   │       └── bvh.rs          Jerarquía de 4 vías con SIMD (CPU) y jerarquía plana (GPU)
│   ├── gr-gpu/           Trazador de rayos en la tarjeta gráfica (wgpu: DirectX 12 y Vulkan)
│   │   ├── shaders/
│   │   │   └── path_tracer.wgsl    El trazador: recorrido del BVH, materiales, sol y cielo
│   │   └── src/lib.rs              Carga la escena en la GPU, lanza pasadas y lee la imagen
│   └── gr-bench/         Mediciones reproducibles contra el motor C#
│       └── src/
│           ├── main.rs             CPU: construcción del BVH y rayos por segundo
│           └── bin/gpu.rs          GPU: verificación del primer impacto, render y rayos por segundo
└── target/               Salida de compilación (no se versiona)
```

## Cómo se conecta con la aplicación

La aplicación C# triangula el proyecto y escribe un archivo `.grscene` (`SceneExchange` en
`src/GuanacasteRender.Core/rendering/scene-exchange.cs`). El motor Rust solo lee ese archivo: no conoce el modelo BIM.
El formato está especificado en la cabecera de `crates/gr-core/src/scene_file.rs`.

## Comandos

Desde la raíz del repositorio:

```text
cargo test  --release --manifest-path engine/Cargo.toml
dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --baseline --out artifacts/engine-bench
cargo run  --release --manifest-path engine/Cargo.toml -p gr-bench -- artifacts/engine-bench
cargo run  --release --manifest-path engine/Cargo.toml -p gr-bench --bin gpu -- artifacts/engine-bench
```

Los resultados (JSON, tabla comparativa e imágenes) quedan en `artifacts/engine-bench/`, que no se versiona. El estado
medido y los siguientes pasos están en `docs/engine/`.
