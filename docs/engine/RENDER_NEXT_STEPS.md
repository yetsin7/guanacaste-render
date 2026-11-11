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
| Integración en la aplicación | **No hecho** | La app instalada usa el motor C# |
| Estudio Rust (ventana e interfaz) | **No empezado** | |

## Siguientes pasos, en orden

1. **Texturas y recorte alfa en `.grscene` y en el shader.** Sin esto la vegetación sale blanca; es el defecto más
   visible. Requiere versión 2 del formato (coordenadas UV ya viajan; faltan las imágenes).
2. **Estudio Rust mínimo:** ventana con el trazador GPU progresivo, órbita/pan/zoom y reinicio de la acumulación al
   mover la cámara. Es el primer visor interactivo con iluminación real.
3. **Denoiser en GPU** (À-trous guiado por normal y profundidad) para que 8–16 muestras ya sean presentables.
4. **Cielo físico y luces artificiales** portados del motor C# para poder comparar imágenes píxel a píxel.
5. **Reducir la memoria del BVH de CPU** (338 MB en Cobogó): compactar hojas parcialmente llenas o cuantizar nodos.
6. **Perfiles de hardware** (Económico, Equilibrado, Alto, Ultra) a partir de las capacidades reales del adaptador.
7. **Lector de `.gr` en Rust** (contenedor `G3DNATV1`: cabecera, JSON en gzip, SHA-256) como inicio del paso 4.
8. Llevar el mismo motor a Guanacaste 3D mediante el mismo `.grscene`, sin dependencia entre aplicaciones.

## Bloqueos y avisos

- El checkout principal (`C:\Dev\Mau Launcher\guanacaste-render`) tiene trabajo sin confirmar de otra sesión
  (conflictos de actualización de modelo). Este trabajo se hizo en un worktree aparte y no toca esos archivos.
- Generar la geometría de Cobogó en C# tarda 6.1 s: será el cuello de botella de cualquier flujo interactivo hasta
  que el paso 4 avance.
- El contador de rayos del shader es de 32 bits; `gr-bench` mide en lotes cortos por ese motivo.
