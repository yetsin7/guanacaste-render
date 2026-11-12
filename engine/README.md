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
│   │   │   ├── edit.rs             Guardar vegetación en el proyecto sin tocar el resto del documento
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
│   │   │   ├── denoise.wgsl        Filtro À-trous guiado por normal y distancia
│   │   │   ├── raster.wgsl         Navegación sin trazado: rasterizado con el mismo sol, cielo y tono
│   │   │   └── resolve.wgsl        De radiancia acumulada a imagen en pantalla (exposición y tono)
│   │   └── src/
│   │       ├── tracer.rs           Sube la escena, lanza pasadas, redimensiona y lee la imagen
│   │       ├── raster.rs           Pipelines del rasterizado (MSAA ×4, Z invertida, vidrio con mezcla)
│   │       └── camera.rs           Marco de cámara compartido entre shader y CPU
│   ├── gr-studio/        La aplicación de escritorio: GuanacasteStudio.exe
│   │   ├── assets/fonts/           Inter, JetBrains Mono y Material Icons (licencias en NOTICE.md)
│   │   └── src/
│   │       ├── main.rs             Ventana sin marco del sistema y dispositivo gráfico
│   │       ├── app.rs              Estado del estudio y trabajo entre pantallas (abrir, plantar, perfiles)
│   │       ├── viewport.rs         Visor 3D: navegación rasterizada, vista previa y render trazados
│   │       ├── orbit_camera.rs     Órbita, desplazamiento, zoom y caminar
│   │       ├── project.rs          Abrir `.grscene` y proyectos `.gr`/`.g3d` (con `gr-model` o el puente)
│   │       ├── theme.rs            Sistema de diseño «Obsidian Precision CAD»: colores, fuentes y marcos
│   │       ├── studio/             Funciones del estudio que no son interfaz
│   │       │   ├── vegetation.rs       Biblioteca de especies y sus ejemplares trazados en GPU
│   │       │   ├── timeline.rs         Fotogramas clave, curva Catmull-Rom y exportación de película
│   │       │   ├── telemetry.rs        Tiempos de fotograma y datos del equipo (registro de Windows)
│   │       │   ├── preferences.rs      Ajustes guardados entre sesiones
│   │       │   └── thumbnails.rs       Vistas de los proyectos recientes
│   │       └── ui/                 La interfaz
│   │           ├── shell.rs            Barra de título propia y barra de herramientas lateral
│   │           ├── widgets.rs          Controles del sistema de diseño
│   │           ├── icons.rs            Símbolos de Material Icons
│   │           └── pages/              Una pantalla por archivo
│   │               ├── projects.rs         Proyectos
│   │               ├── build.rs            Construcción (colocar vegetación)
│   │               ├── library.rs          Biblioteca de naturaleza
│   │               ├── inspector.rs        Inspector de recursos
│   │               ├── materials.rs        Editor de materiales
│   │               ├── movie.rs            Estudio de película
│   │               ├── render.rs           Estudio de render
│   │               ├── settings.rs         Ajustes y telemetría
│   │               ├── help.rs             Ayuda y diagnóstico
│   │               └── common.rs           Piezas compartidas por las pantallas
│   └── gr-bench/         Mediciones reproducibles contra el motor C#
│       └── src/
│           ├── main.rs             CPU: construcción del BVH y rayos por segundo
│           ├── gpu.rs              GPU: verificación del primer impacto, render y rayos por segundo
│           ├── denoise.rs          PSNR del filtro de ruido frente a una referencia
│           ├── raster.rs           Navegación rasterizada frente a una muestra trazada
│           └── mesh.rs             Mallado: cobertura, tiempos y diferencia con la aplicación
└── target/               Salida de compilación (no se versiona)
```

## Calidad de la vista

El visor tiene tres modos, para no gastar la tarjeta gráfica en trazado mientras se navega:

1. **Navegación** (por defecto): rasterizado con MSAA ×4, el mismo sol, cielo, exposición y tono que el trazador,
   sin sombras ni luz indirecta. Solo redibuja cuando cambia la cámara, la luz o la escena; en reposo no consume GPU.
2. **Vista previa** (botón «Vista previa» o tecla V): una pasada corta de trazado de rayos a resolución reducida
   (64 muestras y 60 % por defecto) con eliminación de ruido, para ver cómo quedará la luz. Se detiene al llegar.
3. **Render** (botón «Render» o tecla R): el trazado final a la resolución de salida elegida (512 muestras por
   defecto). Mover la cámara vuelve a la navegación.

## Guardar vegetación

Las plantas de un proyecto `.gr`/`.g3d` se abren como ejemplares editables en Construcción, también las colocadas
en Guanacaste 3D. «Guardar» (Ctrl+S) las escribe en el archivo como elementos `Planting` (tipo 28), con los mismos
campos que escribe la aplicación C# y sin cambiar la versión del esquema:

- Solo se editan las plantas; el resto del JSON se reescribe igual y en el mismo orden de claves.
- La versión anterior queda como `<proyecto>.bak`; el archivo nuevo se lee y verifica antes de darlo por guardado.
- La altura de cada planta se guarda como `Offset` sobre el nivel y el terreno, de modo que el mallador la hace
  crecer exactamente donde se colocó (comprobado vértice a vértice en `gr-model/src/edit.rs`).
- Cerrar o abrir otro proyecto con cambios sin guardar avisa una vez; repetir la acción los descarta.

Las escenas `.grscene` son de solo lectura.

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
