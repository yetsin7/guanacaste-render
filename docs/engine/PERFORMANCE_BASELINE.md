# Línea base de rendimiento

Medido el 2026-11-11 sobre el commit `e18e7e7` (versión 1.0.6), compilación Release.

**Equipo:** Intel Core i7-12650H (10 núcleos, 16 hilos), 31.7 GB de RAM, NVIDIA GeForce RTX 4050 Laptop (6 GB) e Intel
UHD integrada, Windows 11, .NET 10.0.203, Rust 1.98.1.

## Cómo reproducirla

```text
dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --baseline --out artifacts/engine-bench
```

Escribe `artifacts/engine-bench/baseline-csharp.json` y, por cada escena, un `.grscene` y un `.grrays` con los
400 000 rayos exactos que se cronometraron (la mitad salen de la cámara, la mitad de puntos al azar dentro de la
escena). Cada cifra es la mejor de varias repeticiones, para que la carga de otros programas no la contamine.

## Motor C# (CPU), antes de cualquier cambio

| Escena | Triángulos | Generar geometría | Construir BVH | Rayos crudos, 16 hilos | Rayos crudos, 1 hilo | Path tracer 480×270 |
|---|---:|---:|---:|---:|---:|---|
| house-exterior | 2 698 | 220 ms | 11.3 ms | 33.9 Mray/s | 11.5 Mray/s | 33.5 ms/pasada · 13.2 Mray/s |
| daylit-room | 122 | 1 ms | 0.3 ms | 26.9 Mray/s | 3.5 Mray/s | 67.2 ms/pasada · 15.1 Mray/s |
| villa | 20 382 | 94 ms | 19.2 ms | 62.5 Mray/s | 8.8 Mray/s | 38.0 ms/pasada · 9.3 Mray/s |
| cobogo (Casa del Cobogó) | 2 617 926 | 6 115 ms | 1 368 ms | 15.0 Mray/s | 2.2 Mray/s | 172.4 ms/pasada · 3.3 Mray/s |
| soup-600k (estrés) | 600 000 | 141 ms | 253 ms | 2.8 Mray/s | 0.34 Mray/s | no aplica |

Lecturas:

- El trazador completo rinde entre un tercio y la mitad que la intersección cruda: el sombreado (materiales, patrones
  procedurales, muestreo de luces) cuesta tanto como recorrer el BVH.
- En Casa del Cobogó una sola muestra por píxel a 480×270 tarda 172 ms. A 1920×1080 son unos 2.8 s por muestra, es
  decir, minutos para una imagen limpia.
- Generar la geometría de Cobogó (6.1 s) cuesta más que construir su BVH (1.4 s).
- El visor interactivo es un `Viewport3D` de WPF rasterizado, sin relación con el trazador (ver
  `docs/capability-matrix.md`).

## Métricas que todavía no se miden

No se inventan cifras; estas faltan y por qué:

| Métrica | Estado |
|---|---|
| Tiempo por fotograma y latencia del visor interactivo | Requiere instrumentar la aplicación WPF en ejecución. Pendiente. |
| Uso de GPU (%) y VRAM total del proceso | Hay contadores de Windows (`GPU Engine`, `GPU Process Memory`); falta integrarlos. La VRAM que reserva el motor Rust sí se informa, porque el motor conoce sus propios búferes. |
| Consumo energético | No hay contador fiable por proceso en este equipo. No se informa. |
| Tiempo del denoiser | Existe `--denoise-speed` en RenderBench; falta incorporarlo al JSON de la línea base. |
| Memoria nativa y pico sostenido de RAM | El JSON guarda el pico del conjunto de trabajo del proceso completo, no por escena. |
