# Guanacaste Render

Guanacaste Render is a Windows desktop application for the rendering workspace originally developed in Guanacaste 3D. The rendering interface, engine, navigation, scene model, resources and currently required desktop dependencies are copied into this repository. Guanacaste 3D remains a separate project.

## Build

Install the .NET 10 SDK and run `dotnet build guanacaste-render.csproj`. No adjacent Guanacaste 3D checkout is required. The local model and desktop projects are retained during the gradual separation of rendering from BIM editing; this initial copy therefore contains more than rendering code.

## Project exchange

Guanacaste Render saves native projects as `.gr`. It also opens and saves the same native project container as `.g3d` for compatibility with Guanacaste 3D, along with `.g3` and `.g3d.json` projects from older versions. The Revit add-in exports Revit exchange data using either `.g3d` or `.gr`; Guanacaste converts that data into an editable project when opened. Older `.g3dx` exchange files remain importable. Linked models remain in a sidecar, and external textures and other referenced files must remain accessible, so a project is not a self-contained asset package.

### Model updates

A render project stays connected to the model it came from. Guanacaste 3D writes a `.model-link` file beside every render export, and Guanacaste Render writes one beside each project it saves. The link records where the model lives and the model both applications last agreed on.

- When the model file is saved again, the studio bar highlights **Actualizar modelo**. Pressing it merges the new model into the open project as one undoable step.
- The building follows the model: added, changed and removed elements arrive as they are in the model.
- Render work stays: the camera and image settings, photos, render materials, `render.*` feature data and scene components placed in the studio. A render material assigned to an element that left the model is removed and reported.
- When both applications edited the same element, scene components keep the render version and building elements follow the model; the summary reports each such conflict.
- A project without a link (older projects) can still be updated by choosing the model file. Without the recorded common model the building is replaced by the incoming model, and only photos, settings, materials and studio-placed scene components are kept.
- Exporting again from Guanacaste 3D over an export that holds render work merges the new model into it instead of replacing it.

Limits: the link is a sidecar file, so it must travel with the project; a model is only noticed after it is saved; a linked model file reached through its relative path must stay in the same relative location; geometry inside linked external models (OBJ, IFC and similar) is reloaded, not merged. See `docs/capability-matrix.md` for the verified state of every product area.

The copied code and assets retain their existing licenses and third-party attribution from the Guanacaste 3D project.

## Estudio de visualización

La interfaz sigue la organización de las referencias de Lumion: una barra vertical para Escena, Foto, Película y Panorama; paneles oscuros; bibliotecas visuales y herramientas sobre la vista 3D. Las capturas de referencia no se distribuyen como recursos de la aplicación.

- **Proyectos:** abrir, guardar, guardar como y casa de ejemplo desde la barra superior.
- **Materiales:** cuadrícula compacta paginada con búsqueda y categorías por iconos, pestañas Biblioteca/Ajustes y asignación a la superficie seleccionada. Los controles PBR modifican los materiales del documento.
- **Entorno:** biblioteca de cielos con miniaturas, búsqueda y filtros de día, atardecer, nubes y noche, carga de HDRI, orientación e intensidad. Las miniaturas incluidas representan cielos procedurales del motor.
- **Escena:** inspector por nivel y categoría, búsqueda y filtro combinables, selección y visibilidad persistente con deshacer. El catálogo enumera objetos nativos; no descompone modelos vinculados en objetos editables.
- **Objetos:** 18 herramientas con nombres agrupadas en Crear, Editar y Vista, objeto seleccionado visible y acciones habilitadas según la selección. Movimiento, elevación de componentes, rotación mediante anillo en el plano, duplicación, eliminación, deshacer y rehacer. Seleccionar una puerta o ventana no permite girar o duplicar accidentalmente su muro anfitrión. La colocación masiva permite grupos circulares o líneas con separación y variación de tamaño.
- **Altura de ojo:** escribe la altura en metros en la barra de la vista y la cámara se mueve al instante, o pulsa **1,70 m** para utilizar esa altura. Se mide desde la superficie detectada justo debajo de la cámara; si todavía no hay una superficie detectable, se usa el origen vertical del proyecto. El control se actualiza al navegar y queda bloqueado mientras se renderiza.
- **Salidas:** cámaras con miniaturas, exportación de fotos, línea de tiempo de película y panorama. Cada grupo muestra seis herramientas en una sola fila sin desplazamiento horizontal. El panel lateral más estrecho deja más espacio para la escena; los ajustes tienen títulos y la ayuda de navegación se abre con el botón «?». Durante el render se bloquean las ediciones incompatibles, y los controles se restauran al terminar. Ampliar oculta los paneles; Esc permite recuperarlos.

Esta adaptación utiliza las funciones reales del motor de Guanacaste. No incorpora el motor, el catálogo comercial ni todos los efectos de Lumion. La duplicación y la elevación se limitan a los tipos de objeto compatibles.

## Verificación

Ejecuta `./scripts/verify.ps1` en PowerShell para compilar Release y comprobar el motor y el estudio. Añade `-IncludeVideo` para los flujos ampliados de foto y vídeo. Las pruebas usan proyectos temporales y generan capturas en `artifacts/studio-checks/`, incluyendo tamaños de 1440 × 900 y 1100 × 760. La suite `model-update` recorre el intercambio completo con la casa de ejemplo (guardar modelo, aviso, actualizar, deshacer, guardar, reabrir y volver a actualizar) y deja capturas en `artifacts/model-update/`. La suite `render-recovery` comprueba que una sesión interrumpida se ofrece al volver a abrir Guanacaste Render. Comprueban selección, filtros, visibilidad, movimiento, deshacer, materiales, cámaras, persistencia y exportación de un PNG real.

## Instalar en Windows

Publica con `dotnet publish guanacaste-render.csproj -c Release -r win-x64 --self-contained true -o release/win-x64` y ejecuta `./scripts/install.ps1`. El instalador coloca cada versión en LocalAppData/Programs/Guanacaste Render, verifica el ejecutable y las bibliotecas principales con SHA256, crea el acceso del menú Inicio y registra `.gr` para abrir proyectos con Guanacaste Render. Los proyectos se conservan fuera de la instalación.
