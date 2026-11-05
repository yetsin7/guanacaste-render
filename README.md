# Guanacaste Render

Guanacaste Render is a Windows desktop application for the rendering workspace originally developed in Guanacaste 3D. The rendering interface, engine, navigation, scene model, resources and currently required desktop dependencies are copied into this repository. Guanacaste 3D remains a separate project.

## Build

Install the .NET 10 SDK and run `dotnet build guanacaste-render.csproj`. No adjacent Guanacaste 3D checkout is required. The local model and desktop projects are retained during the gradual separation of rendering from BIM editing; this initial copy therefore contains more than rendering code.

## Project exchange

Open a `.g3d` snapshot exported from Guanacaste 3D. Linked models remain in a sidecar, and external textures and other referenced files must remain accessible. The snapshot is not a self-contained asset package. Save rendering changes to the snapshot rather than the original design project.

The copied code and assets retain their existing licenses and third-party attribution from the Guanacaste 3D project.
