# Rumbo del motor y siguientes pasos

Documento de relevo: otra sesión debe poder continuar desde aquí. Actualizado el 2026-11-11.

## Decisión de rumbo

El 2026-11-11 el propietario del producto decidió que **Rust sea el lenguaje de prácticamente todo el software** y que
la interfaz puede rehacerse. Esto sustituye la regla anterior de mantener C#/WPF como base permanente.

La migración se hace de dentro hacia fuera, sin dejar nunca la aplicación instalada sin funcionar:

1. **Motor** (hecho en parte): aceleración, trazado en CPU y GPU. Rust, en `engine/`.
2. **Puente**: la aplicación C# exporta la escena triangulada a `.grscene`. Es temporal; desaparece cuando el paso 4
   termine.
3. **Estudio Rust**: aplicación nueva con visor GPU e interfaz propia (`wgpu` + `winit` + `egui`), que crece hasta
   igualar al estudio WPF.
4. **Modelo y geometría**: leer `.gr`/`.g3d` y triangular el modelo BIM en Rust. Es la parte más grande (el mallado
   está repartido en miles de líneas de C#) y la que permite retirar C#.

Tecnología GPU elegida: `wgpu` con compute shaders. Motivo: una sola base de código para DirectX 12 y Vulkan, funciona
en gráficas integradas (medido en Intel UHD) y no exige hardware RTX. El trazado por hardware (DXR / Vulkan ray query)
existe en `wgpu` como característica experimental y queda como aceleración opcional posterior, no como requisito.

## Estado verificado

| Pieza | Estado | Evidencia |
|---|---|---|
| Línea base C# reproducible | Hecho | `RenderBench --baseline`, `PERFORMANCE_BASELINE.md` |
| Exportación `.grscene` | Hecho (geometría, materiales básicos, cámara, sol) | Lectura validada en `scene_file.rs`, pruebas |
| BVH de 4 vías en CPU | Hecho | 8 pruebas en `gr-core`; coincide con C# en 5 escenas |
| Trazador GPU, prototipo | Hecho, por línea de comandos | Primer impacto verificado contra CPU; imágenes inspeccionadas |
| Estudio Rust (`GuanacasteStudio.exe`) | Hecho, versión preliminar | Ventana con visor GPU progresivo, órbita, desplazamiento, zoom y caminar, sol, exposición, rebotes, perfiles y guardar PNG. Capturas inspeccionadas con Cobogó y con un proyecto `.gr` real |
| Abrir proyectos `.gr`/`.g3d` en el estudio | Hecho mediante puente C# | `casa-render.gr` (2.6 M de triángulos) se convierte en 7.1 s y se traza a 144 muestras/s |
| Perfiles de hardware | Hecho (sugerencia y presupuestos) | Pruebas en `hardware.rs`; falta calibración medida y lectura de VRAM |
| Estudio WPF | Sigue instalado y sin cambios | Renderiza con el motor C# en CPU; convive con el estudio Rust |

## Mapa de la migración

El código C# suma 127 499 líneas en 819 archivos (2026-11-11). No se migra de una vez; este es el reparto y el estado:

| Bloque C# | Líneas | Destino en Rust | Estado |
|---|---:|---|---|
| Motor de render (`Core/rendering`) | 13 280 | `gr-core`, `gr-gpu` | BVH y trazador GPU básico hechos; faltan texturas, cielo físico, luces, denoiser, efectos, película |
| Interfaz WPF (`Desktop`) | ~50 000 | `gr-studio` (rediseño, no port línea a línea) | Visor y controles básicos hechos; faltan materiales, objetos, cámaras guardadas, salidas |
| Geometría y mallado (`Core/geometry`) | 3 120 | crate nuevo `gr-model` | No empezado. Es lo que permite retirar el puente |
| Modelo BIM y sesión (`Core` raíz, `architecture`, `families`, …) | ~25 000 | `gr-model` | No empezado |
| Importadores (IFC, glTF, OBJ, Revit) | 6 803 | crate nuevo `gr-import` | No empezado |
| Pruebas | ~24 000 | Pruebas junto a cada crate | 15 pruebas Rust hoy |

Regla de trabajo: cada módulo portado se verifica contra el C# con los mismos datos antes de sustituirlo, como se
hizo con el BVH (impactos idénticos) y el trazador (primer impacto idéntico).

## Siguientes pasos, en orden

1. **Texturas y recorte alfa en `.grscene` y en el shader.** Sin esto la vegetación sale blanca; es el defecto más
   visible. Requiere versión 2 del formato (las coordenadas UV ya viajan; faltan las imágenes).
2. **Denoiser en GPU** (À-trous guiado por normal y profundidad) para que 8–16 muestras ya sean presentables.
3. **Lector de `.gr` y mallado en Rust** (`gr-model`): contenedor `G3DNATV1`, modelo, muros, losas, vanos. Elimina
   los 7 s del puente y la dependencia de la aplicación C#.
4. **Cielo físico y luces artificiales** portados del motor C# para poder comparar imágenes píxel a píxel.
5. **Estudio: materiales y selección** (picar una superficie y cambiar su material) y cámaras guardadas.
6. **Calibración de hardware** al primer arranque y lectura de VRAM para afinar el perfil sugerido.
7. **Reducir la memoria del BVH de CPU** (338 MB en Cobogó).
8. Llevar el mismo motor a Guanacaste 3D mediante el mismo `.grscene`, sin dependencia entre aplicaciones.

## Bloqueos y avisos

- El checkout principal (`C:\Dev\Mau Launcher\guanacaste-render`) tiene trabajo sin confirmar de otra sesión
  (conflictos de actualización de modelo). Este trabajo se hizo en un worktree aparte y no toca esos archivos.
- Generar la geometría de Cobogó en C# tarda 6.1 s: será el cuello de botella de cualquier flujo interactivo hasta
  que el paso 4 avance.
- El contador de rayos del shader es de 32 bits; `gr-bench` mide en lotes cortos por ese motivo.
