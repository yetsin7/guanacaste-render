# Resultados medidos del motor Rust

Medido el 2026-11-11 en el equipo descrito en `PERFORMANCE_BASELINE.md`. Todas las cifras salen de
`artifacts/engine-bench/` (`baseline-csharp.json`, `baseline-rust.json`, `baseline-gpu-*.json`) y se reproducen con
los comandos de `engine/README.md`.

## 1. BVH en CPU: C# frente a Rust

Mismos triángulos y mismos 400 000 rayos por escena. Antes de cronometrar, el número de impactos y la suma de
distancias de Rust deben coincidir con los de C#: coinciden exactamente en las cinco escenas.

| Escena | Triángulos | Construir BVH C# → Rust | Rayos, 16 hilos C# → Rust | Rayos, 1 hilo C# → Rust |
|---|---:|---|---|---|
| house-exterior | 2 698 | 11.3 → 3.2 ms | 33.9 → 194.5 Mray/s | 11.5 → 23.5 Mray/s (2.0×) |
| daylit-room | 122 | 0.3 → 0.2 ms | 26.9 → 99.0 | 3.5 → 9.7 (2.8×) |
| villa | 20 382 | 19.2 → 13.7 ms | 62.5 → 157.5 | 8.8 → 19.4 (2.2×) |
| cobogo | 2 617 926 | 1 368 → 838 ms | 15.0 → 72.1 (4.8×) | 2.2 → 5.9 (2.7×) |
| soup-600k | 600 000 | 253 → 184 ms | 2.8 → 10.2 (3.6×) | 0.34 → 0.75 (2.2×) |

Cómo leerlo:

- La comparación limpia es la de **un hilo: 2.0–2.8×**. Mide solo el algoritmo: jerarquía de cuatro vías con SIMD
  frente a jerarquía binaria escalar.
- En 16 hilos la ventaja sube porque `rayon` reparte mejor que el `Parallel.For` del banco de pruebas de C#. En las
  escenas pequeñas parte de esa diferencia es del banco de pruebas y no del motor.
- **Coste en memoria:** la jerarquía de Rust ocupa 338 MB en Cobogó, aproximadamente el doble que la de C#, porque
  cada hoja reserva cuatro carriles aunque contenga dos triángulos. Es deuda abierta (ver `RENDER_NEXT_STEPS.md`).

Esta comparación es de la estructura de aceleración, no del trazador completo: el sombreado del motor C# todavía no
está portado a Rust en CPU.

## 2. Trazador en GPU

Compute shader WGSL sobre `wgpu`, sin extensiones de trazado de rayos. 1280×720, tres rebotes.
Verificación previa a cada medición: la distancia al primer impacto de cada píxel debe coincidir con la jerarquía de
CPU sobre los mismos rayos de cámara.

| Escena | RTX 4050 (Vulkan) | Intel UHD integrada | Motor C# en CPU | Píxeles que difieren del BVH de CPU |
|---|---|---|---|---|
| house-exterior | 2.40 ms/muestra · 1 213 Mray/s | 13.7 ms · 214 Mray/s | 13.2 Mray/s | 0 de 921 600 |
| daylit-room | 4.38 ms · 969 Mray/s | 31.1 ms · 137 Mray/s | 15.1 Mray/s | 0 |
| villa | 2.41 ms · 841 Mray/s | 14.4 ms · 141 Mray/s | 9.3 Mray/s | 0 |
| cobogo | 16.7 ms · 172 Mray/s | 109 ms · 26.3 Mray/s | 3.3 Mray/s | 1 (RTX), 6 (Intel) |
| soup-600k | 32.8 ms · 78 Mray/s | 209 ms · 12.2 Mray/s | no aplica | 13 |

VRAM que reserva el motor en Cobogó: 173 MB de escena más 14 MB de imagen.

Cómo leerlo:

- En rayos por segundo la RTX 4050 rinde entre 50× y 90× el trazador C#, y la gráfica integrada entre 8× y 16×.
- **No es una comparación de imagen equivalente.** El trazador GPU sombrea menos que el de C#: colores planos PBR,
  vidrio fino, sol con sombra suave y un cielo analítico. No lee todavía texturas, recorte alfa, patrones
  procedurales, normales suaves, luces artificiales, HDRI, portales de cielo ni el modelo de cielo del motor C#.
  Portar eso costará parte de la ventaja; cuánto, se medirá al hacerlo.
- Consecuencia visible: en `cobogo-gpu.png` el árbol sale blanco y macizo, porque sus hojas son tarjetas con textura
  y transparencia.
- Las imágenes `house-exterior-gpu.png` y `cobogo-gpu.png` fueron inspeccionadas: geometría, sombras del sol, luz a
  través del vidrio e iluminación indirecta del cielo son correctas. No se ha comparado todavía píxel a píxel con el
  render de C#, porque los modelos de cielo difieren.
- Sin denoiser en GPU: el grano a 64–128 muestras es el esperado.

## 3. Estado de integración

El motor Rust se ejecuta hoy desde la línea de comandos sobre escenas exportadas. **La aplicación instalada sigue
renderizando con el motor C# en CPU**; nada de lo anterior es visible aún desde la interfaz.

## 4. Normales suaves y eliminación de ruido en GPU (2026-11-12)

**Normales suaves.** El trazador interpola ahora las normales por vértice que traen troncos, ramas, palmas y
terreno, en vez de sombrearlos facetados. Se reutilizan las coordenadas baricéntricas que ya calcula la prueba de
intersección, así que no cuesta tiempo medible: RTX 4050 2.37 ms/muestra en la casa de ejemplo (antes 2.40),
Intel UHD 13.60 ms (antes 13.62). El primer impacto sigue coincidiendo con el BVH de CPU (0 y 1 píxel de 921 600).

**Eliminación de ruido** (`shaders/denoise.wgsl`): filtro À-trous guiado por la normal y la distancia del primer
impacto, cuatro pasadas (61 × 61 píxeles). Desactivado por defecto; se activa con `GpuTracer::set_denoise`.
No altera las muestras acumuladas, así que puede encenderse y apagarse en cualquier momento.

Medido con `gr-bench --bin denoise`, 1280 × 720, RTX 4050. PSNR de la imagen de 8 bits mostrada frente a una
referencia de 1024 muestras (más alto es más cercano):

| Escena | Muestras | Sin filtrar | Filtrada | Ganancia | Coste |
|---|---:|---:|---:|---:|---:|
| house-exterior | 1 | 19.8 dB | 31.4 dB | +11.6 dB | 2.1 ms |
| house-exterior | 4 | 26.0 dB | 34.4 dB | +8.4 dB | 2.1 ms |
| house-exterior | 16 | 32.2 dB | 38.4 dB | +6.2 dB | 2.1 ms |
| house-exterior | 64 | 38.4 dB | 42.3 dB | +3.9 dB | 2.1 ms |
| garden | 4 | 30.1 dB | 37.0 dB | +6.9 dB | 2.1 ms |
| garden | 64 | 42.2 dB | 41.9 dB | −0.3 dB | 2.1 ms |
| modern-house | 4 | 28.8 dB | 33.3 dB | +4.5 dB | 2.1 ms |
| modern-house | 64 | 41.1 dB | 41.0 dB | −0.1 dB | 2.0 ms |

Cómo leerlo:

- 4 muestras filtradas se parecen a la referencia más que 16 sin filtrar: es lo que hace usable la navegación.
- En escenas con vegetación deja de ayudar hacia 64 muestras (−0.3 dB): suaviza el detalle fino de las hojas.
  **Uso recomendado:** encenderlo mientras la cámara se mueve y durante las primeras ~32 muestras, y apagarlo después.
- Inspección visual (`artifacts/denoise/`): bordes de muros, marcos y sombras nítidos; el interior de una palma
  pierde algo de definición a 4 muestras. No hay halos ni manchas de color.
- Coste fijo de unos 2 ms por imagen mostrada, independiente del número de triángulos. Memoria de vídeo: tres
  búferes más del tamaño de la imagen (42 MB a 1280 × 720).
- **Aún no está conectado al estudio**: `gr-studio` lo lleva Codex; la llamada es una línea en `viewport.rs`.
