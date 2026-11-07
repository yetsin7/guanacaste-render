# Interfaz de Guanacaste Render

Capturas generadas por los controles WPF reales durante las pruebas del estudio. La escena es la casa de ejemplo incluida en el proyecto.

## Inicio

Acceso directo a proyectos y a la escena de ejemplo, con una introducción al flujo de trabajo.

![Inicio](inicio.png)

## Inspector

Selección y visibilidad por objeto, búsqueda y filtro por categoría sobre el documento nativo.

![Inspector](inspector.png)

## Cielos

Biblioteca de cielos procedurales con miniaturas, búsqueda y aplicación de iluminación.

![Cielos](cielos.png)

## Película en ventana compacta

Ventana de 1100 × 760: herramientas con nombre agrupadas por tarea, línea de tiempo y exportación accesibles.

![Película](pelicula-compacta.png)

## Herramientas de escena

Selección visible, deshacer/rehacer y acciones habilitadas según el tipo de objeto. Las 18 herramientas se reparten en Crear, Editar y Vista: seis por grupo, sin desplazamiento horizontal. La barra superior unifica Escena, Foto, Película y 360°. El panel lateral mide 340 píxeles inicialmente y la ayuda se muestra bajo demanda.

![Herramientas](herramientas.png)

Validación de esta revisión: 55 comprobaciones del estudio, incluyendo comandos de edición, protección del muro anfitrión, biblioteca de efectos, modo ampliado, distribución compacta, bloqueo durante render y exportación de PNG.

Para regenerar las capturas: `$env:G3D_ONLY='studio'; dotnet run --project tests/desktop-interaction-checks -c Release`. Los resultados nuevos se guardan en `artifacts/studio-checks`.
