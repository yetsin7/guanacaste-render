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
| Abrir proyectos `.gr`/`.g3d` en el estudio | Hecho: con Rust si está cubierto, si no con el puente C# | `casa-render.gr` (2.6 M de triángulos) se convierte en 7.1 s y se traza a 144 muestras/s |
| Lector de `.gr`/`.g3d` en Rust | Hecho (esquema 15) | Contenedor con suma verificada; pruebas con archivos escritos por la aplicación. Esquemas anteriores necesitan las migraciones de C# |
| Mallado en Rust: muros, vanos, losas planas, columnas, suelo | Hecho, idéntico a la aplicación | `tests/reference.rs` y `gr-bench --bin mesh`: diferencia 0 en los elementos cubiertos de 5 proyectos |
| Mallado en Rust: vegetación procedural | Hecho, mismo árbol que la aplicación | Mismo número de triángulos y menos de 0.05 mm de diferencia en 50 plantas; prueba permanente con arbusto, palma y conífera |
| Mallado en Rust: resto de elementos | **No hecho** | Ver "Cobertura del mallado" |
| Perfiles de hardware | Hecho (sugerencia y presupuestos) | Pruebas en `hardware.rs`; falta calibración medida y lectura de VRAM |
| Estudio WPF | Sigue instalado y sin cambios | Renderiza con el motor C# en CPU; convive con el estudio Rust |

## Mapa de la migración

El código C# suma 127 499 líneas en 819 archivos (2026-11-11). No se migra de una vez; este es el reparto y el estado:

| Bloque C# | Líneas | Destino en Rust | Estado |
|---|---:|---|---|
| Motor de render (`Core/rendering`) | 13 280 | `gr-core`, `gr-gpu` | BVH y trazador GPU básico hechos; faltan texturas, cielo físico, luces, denoiser, efectos, película |
| Interfaz WPF (`Desktop`) | ~50 000 | `gr-studio` (rediseño, no port línea a línea) | Visor y controles básicos hechos; faltan materiales, objetos, cámaras guardadas, salidas |
| Geometría y mallado (`Core/geometry`) | 3 120 | `gr-model` | Empezado: muros, uniones, vanos, losas planas, columnas, suelo y vegetación procedural, verificados. Faltan componentes paramétricos, escaleras, cubiertas inclinadas y terreno |
| Modelo BIM y sesión (`Core` raíz, `architecture`, `families`, …) | ~25 000 | `gr-model` | Solo lectura de lo que usa el mallador. Sin edición, validación ni migraciones de esquema |
| Importadores (IFC, glTF, OBJ, Revit) | 6 803 | crate nuevo `gr-import` | No empezado |
| Pruebas | ~24 000 | Pruebas junto a cada crate | 48 pruebas Rust hoy |

Regla de trabajo: cada módulo portado se verifica contra el C# con los mismos datos antes de sustituirlo, como se
hizo con el BVH (impactos idénticos) y el trazador (primer impacto idéntico).

## Cobertura del mallado

Medido con `gr-bench --bin mesh` el 2026-11-11. "Elementos" son los rangos de la malla de la aplicación (un muro,
una puerta, una losa, un árbol…).

| Proyecto | Elementos coincidentes | Triángulos Rust / aplicación | Elementos sin cubrir | Leer y mallar en Rust | Diferencia máxima |
|---|---:|---:|---:|---:|---:|
| demo-house | 18 de 18 | 2 696 / 2 698 (el resto es el suelo, que el estudio sí añade) | 0 | 0.6 ms | 0 |
| garden | 3 de 3 | 43 794 / 43 796 | 0 | 5.0 ms | 0.0006 mm |
| showcase | 20 de 34 | 2 276 / 3 998 | 14 | 0.8 ms | 0 |
| modern-house | 30 de 36 | 2 200 430 / 2 201 200 (99.97 %) | 6 | 239 ms | 0.05 mm |
| cobogo | 136 de 184 | 1 489 362 / 1 502 688 (99.1 %) | 56 | 176 ms | 0.04 mm |

Cómo leerlo:

- **Muros, vanos, losas y columnas coinciden exactamente** (diferencia 0): solo usan aritmética de planta.
- **La vegetación coincide en número de triángulos y dentro de 0.05 mm.** Se genera con el mismo generador aleatorio
  con semilla que .NET, así que crece el mismo árbol; la diferencia residual es el último bit del seno y el coseno de
  cada entorno, acumulado a lo largo de las ramas. Se comprobó con dos conjuntos independientes de 25 árboles, porque
  los proyectos de muestra generan identificadores nuevos (y por tanto árboles nuevos) en cada ejecución.
- Por triángulos la cobertura ya supera el 99 % en los proyectos con vegetación, pero **un proyecto solo se abre sin
  C# si está cubierto del todo**, y a Cobogó le faltan 56 elementos pequeños.
- Tiempo de la aplicación C# sobre los mismos archivos, primera construcción (incluye compilación JIT, por lo que no
  es una comparación limpia): 353 ms en cobogo y 801 ms en modern-house.

Lo que falta, en el orden que más proyectos desbloquea:

1. **Componentes paramétricos** (`ElementGeometry.Parts`, `ResidentialComponentGeometry`): vehículos, mobiliario,
   aparatos. Son 41 de los 56 elementos que le faltan a Cobogó.
2. **Escaleras, barandas y muros cortina** (`StairGeometry` y afines): lo único que le falta a modern-house.
3. **Huecos de losa** (aberturas, ductos, ascensores), que hoy no se cortan.
4. **Instalaciones**: fontanería y dispositivos eléctricos, que dependen de sus análisis.
5. **Cubiertas inclinadas y hastiales** (`RoofGeometry`, 438 líneas).
6. **Terreno** (`TopographyGeometry`, 351 líneas); mientras falte, las plantas sobre terreno tampoco se generan.
7. **Familias de componentes con malla** (`FamilyMesh`) y **familias en sitio** (`FamilyGeometry`): ninguno de los
   proyectos de referencia las usa, así que no hay con qué verificarlas todavía.
8. **Materiales asignados en el proyecto** y texturas: el camino Rust usa el material de serie de cada tipo de
   superficie; el puente sí aplica los asignados.

## Siguientes pasos, en orden

1. **Componentes paramétricos, escaleras, barandas y muros cortina** en `gr-model`: con eso modern-house y casi todo Cobogó se abren sin el puente.
2. **Texturas y recorte alfa en `.grscene` y en el shader.** Sin esto la vegetación sale blanca.
3. **Huecos de losa, instalaciones, cubiertas inclinadas y terreno** en `gr-model` (ver la lista de cobertura).
4. **Denoiser en GPU** (À-trous guiado por normal y profundidad) para que 8–16 muestras ya sean presentables.
5. **Cielo físico y luces artificiales** portados del motor C# para poder comparar imágenes píxel a píxel.
6. **Estudio: materiales y selección** (picar una superficie y cambiar su material) y cámaras guardadas.
7. **Calibración de hardware** al primer arranque y lectura de VRAM para afinar el perfil sugerido.
8. **Reducir la memoria del BVH de CPU** (338 MB en Cobogó).
9. Llevar el mismo motor a Guanacaste 3D, sin dependencia entre aplicaciones.

## Bloqueos y avisos

- El checkout principal (`C:\Dev\Mau Launcher\guanacaste-render`) tiene trabajo sin confirmar de otra sesión
  (conflictos de actualización de modelo). Este trabajo se hizo en un worktree aparte y no toca esos archivos.
- Generar la geometría de Cobogó en C# tarda 6.1 s: será el cuello de botella de cualquier flujo interactivo hasta
  que el paso 4 avance.
- El contador de rayos del shader es de 32 bits; `gr-bench` mide en lotes cortos por ese motivo.
