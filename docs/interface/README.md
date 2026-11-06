# Interfaz de Guanacaste Render

Capturas generadas por los controles WPF reales durante las pruebas del estudio. La escena es la casa de ejemplo incluida en el proyecto.

## Inspector

Selección y visibilidad por objeto, búsqueda y filtro por categoría sobre el documento nativo.

![Inspector](inspector.png)

## Cielos

Biblioteca de cielos procedurales con miniaturas, búsqueda y aplicación de iluminación.

![Cielos](cielos.png)

## Película en ventana compacta

Ventana de 1100 × 760: herramientas desplazables, línea de tiempo y exportación accesibles.

![Película](pelicula-compacta.png)

Para regenerar las capturas: `$env:G3D_ONLY='studio'; dotnet run --project tests/desktop-interaction-checks -c Release`. Los resultados nuevos se guardan en `artifacts/studio-checks`.
