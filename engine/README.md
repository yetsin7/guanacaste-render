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
│   ├── gr-model/         Proyectos `.gr`/`.g3d` y mallado del edificio, sin la aplicación C#. Sin `unsafe`.
│   │   ├── data/
│   │   │   └── stock-materials.tsv     Material de cada tipo de superficie, exportado de la aplicación
│   │   ├── src/
│   │   │   ├── container.rs        Contenedor `G3DNATV1`: cabecera, gzip y suma SHA-256
│   │   │   ├── document.rs         El modelo que lee el mallador (niveles, muros, vanos, losas, columnas)
│   │   │   ├── scene.rs            De la malla a la escena del motor: materiales, cámara y luz
│   │   │   ├── plan/               Geometría en planta (metros, `f64`)
│   │   │   │   ├── polygon.rs          Puntos, orientación y triangulación por orejas
│   │   │   │   ├── wall_solids.rs      Tramos macizos de un muro alrededor de sus vanos
│   │   │   │   ├── wall_joins.rs       Encuentros de muros: inglete, tope y en T
│   │   │   │   └── columns.rs          Contornos de columnas
│   │   │   └── mesh/               De proyecto a triángulos
│   │   │       ├── scene_mesh.rs       La malla y los tipos de superficie
│   │   │       ├── prism.rs            Prisma vertical y caja en coordenadas de muro
│   │   │       ├── openings.rs         Puertas, ventanas, herrajes y rejas
│   │   │       ├── plants.rs           Vegetación procedural: árboles, palmas, coníferas y arbustos
│   │   │       ├── dotnet.rs           Generador aleatorio y redondeos de .NET que la vegetación necesita
│   │   │       └── builder.rs          Recorre el proyecto y cuenta lo que aún no cubre
│   │   └── tests/
│   │       ├── reference.rs        Comparación triángulo a triángulo con la aplicación
│   │       └── fixtures/           Proyectos de muestra y su malla de referencia
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
│   │       ├── project.rs          Abrir `.grscene` y proyectos `.gr`/`.g3d` (con `gr-model` o el puente)
│   │       └── theme.rs            Colores y espaciado de la interfaz
│   └── gr-bench/         Mediciones reproducibles contra el motor C#
│       └── src/
│           ├── main.rs             CPU: construcción del BVH y rayos por segundo
│           ├── gpu.rs              GPU: verificación del primer impacto, render y rayos por segundo
│           └── mesh.rs             Mallado: cobertura, tiempos y diferencia con la aplicación
└── target/               Salida de compilación (no se versiona)
```

## Cómo abre proyectos el estudio

1. Una escena `.grscene` se lee directamente.
2. Un proyecto `.gr` o `.g3d` se lee y se malla con `gr-model`. Si ese mallado cubre todo el proyecto, se muestra tal
   cual: no interviene nada de C#.
3. Si el proyecto tiene elementos que `gr-model` aún no genera, el estudio ejecuta sin ventana la aplicación C#
   instalada (`GuanacasteRender.exe --export-scene <proyecto> <salida.grscene>`), que lo convierte completo.
4. Si esa aplicación no está instalada, se muestra el mallado parcial con un aviso de cuántos elementos faltan.

El puente del paso 3 se encoge a medida que `gr-model` cubre más tipos de elemento y desaparece cuando los cubra
todos. El formato `.grscene` está especificado en la cabecera de `crates/gr-core/src/scene_file.rs`.

## Comandos

Desde la raíz del repositorio:

```text
cargo test  --release --manifest-path engine/Cargo.toml
dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --baseline --out artifacts/engine-bench
cargo run  --release --manifest-path engine/Cargo.toml -p gr-bench -- artifacts/engine-bench
cargo run  --release --manifest-path engine/Cargo.toml -p gr-bench --bin gpu -- artifacts/engine-bench
dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --mesh-reference --out artifacts/mesh-reference
cargo run  --release --manifest-path engine/Cargo.toml -p gr-bench --bin mesh -- artifacts/mesh-reference
cargo run  --release --manifest-path engine/Cargo.toml -p gr-studio -- artifacts/engine-bench/cobogo.grscene
```

Los resultados (JSON, tabla comparativa e imágenes) quedan en `artifacts/engine-bench/`, que no se versiona. El estado
medido y los siguientes pasos están en `docs/engine/`.
