# Guanacaste Render

Guanacaste Render is a Windows desktop application for the rendering workspace originally developed in Guanacaste 3D. The rendering interface, engine, navigation, scene model, resources and currently required desktop dependencies are copied into this repository. Guanacaste 3D remains a separate project.

## Build

Install the .NET 10 SDK and run `dotnet build guanacaste-render.csproj`. No adjacent Guanacaste 3D checkout is required. The local model and desktop projects are retained during the gradual separation of rendering from BIM editing; this initial copy therefore contains more than rendering code.

## Project exchange

Open a `.g3d` snapshot exported from Guanacaste 3D. Linked models remain in a sidecar, and external textures and other referenced files must remain accessible. The snapshot is not a self-contained asset package. Save rendering changes to the snapshot rather than the original design project.

The copied code and assets retain their existing licenses and third-party attribution from the Guanacaste 3D project.

## Estudio de visualización

La interfaz sigue la organización de las referencias de Lumion: una barra superior para Escena, Foto, Película y Panorama; paneles oscuros; bibliotecas visuales y herramientas sobre la vista 3D. Las capturas de referencia no se distribuyen como recursos de la aplicación.

- **Proyectos:** abrir, guardar, guardar como y casa de ejemplo desde la barra superior.
- **Materiales:** biblioteca paginada con búsqueda y categorías, pestañas Biblioteca/Ajustes y asignación a la superficie seleccionada. Los controles PBR modifican los materiales del documento.
- **Entorno:** biblioteca de cielos con miniaturas y búsqueda, carga de HDRI, orientación e intensidad. Las miniaturas incluidas representan cielos procedurales del motor.
- **Escena:** inspector por nivel y categoría, búsqueda y filtro combinables, selección y visibilidad persistente con deshacer. El catálogo enumera objetos nativos; no descompone modelos vinculados en objetos editables.
- **Objetos:** 18 herramientas con nombres agrupadas en Crear, Editar y Vista, objeto seleccionado visible y acciones habilitadas según la selección. Movimiento, elevación de componentes, rotación, duplicación, eliminación, deshacer y rehacer. Seleccionar una puerta o ventana no permite girar o duplicar accidentalmente su muro anfitrión. La colocación masiva permite grupos circulares o líneas con separación y variación de tamaño.
- **Salidas:** cámaras con miniaturas, exportación de fotos, línea de tiempo de película y panorama. Cada grupo muestra seis herramientas en una sola fila sin desplazamiento horizontal. El panel lateral más estrecho deja más espacio para la escena; los ajustes tienen títulos y la ayuda de navegación se abre con el botón «?». Durante el render se bloquean las ediciones incompatibles, y los controles se restauran al terminar. Ampliar oculta los paneles; Esc permite recuperarlos.

Esta adaptación utiliza las funciones reales del motor de Guanacaste. No incorpora el motor, el catálogo comercial ni todos los efectos de Lumion. La duplicación y la elevación se limitan a los tipos de objeto compatibles.

## Verificación

Ejecuta `./scripts/verify.ps1` en PowerShell para compilar Release y comprobar el motor y el estudio. Añade `-IncludeVideo` para los flujos ampliados de foto y vídeo. Las pruebas usan proyectos temporales y generan capturas en `artifacts/studio-checks/`, incluyendo tamaños de 1440 × 900 y 1100 × 760. Comprueban selección, filtros, visibilidad, movimiento, deshacer, materiales, cámaras, persistencia y exportación de un PNG real.
