# Third-party 3D models

The detailed mesh families in `src/Guanacaste3D.Desktop/resources/families/` were converted from public-domain
(CC0 1.0) models. CC0 needs no attribution; the sources are listed so the conversion can be repeated.

| Family file | Family name | Source | License |
|---|---|---|---|
| `jacaranda-tree.g3df` | Jacaranda Tree (detailed) | [Jacaranda Tree](https://polyhaven.com/a/jacaranda_tree), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `sedan-car.g3df` | Sedan Car (detailed) | [Sedan](https://grab3d.com/models/vehicles/sedan/), Grab3D, GLB | [CC0 1.0](https://grab3d.com/license/) |
| `coupe-car-silver.g3df` | Coupe Car Silver (detailed) | [Sports Car](https://grab3d.com/models/vehicles/sports-car/), Grab3D, GLB | [CC0 1.0](https://grab3d.com/license/) |
| `island-tree-a.g3df` | Island Tree A (detailed) | [Island Tree 01](https://polyhaven.com/a/island_tree_01), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `island-tree-b.g3df` | Island Tree B (detailed) | [Island Tree 02](https://polyhaven.com/a/island_tree_02), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `small-tree.g3df` | Small Tree (detailed) | [Tree Small 02](https://polyhaven.com/a/tree_small_02), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `searsia.g3df` | Searsia Shrub Tree (detailed) | [Searsia Lucida](https://polyhaven.com/a/searsia_lucida), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `shrub-dense-a.g3df` | Dense Shrub A (detailed) | [Shrub 02 (variant 1)](https://polyhaven.com/a/shrub_02), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `shrub-dense-b.g3df` | Dense Shrub B (detailed) | [Shrub 02 (variant 3)](https://polyhaven.com/a/shrub_02), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `fern.g3df` | Fern (detailed) | [Fern 02](https://polyhaven.com/a/fern_02), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `grass-tuft.g3df` | Grass Tuft (detailed) | [Grass Medium 01](https://polyhaven.com/a/grass_medium_01), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `pachira.g3df` | Money Tree Pachira (detailed) | [Pachira Aquatica 01](https://polyhaven.com/a/pachira_aquatica_01), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `calathea.g3df` | Calathea (detailed) | [Calathea Orbifolia 01](https://polyhaven.com/a/calathea_orbifolia_01), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |
| `anthurium.g3df` | Anthurium (detailed) | [Anthurium Botany 01](https://polyhaven.com/a/anthurium_botany_01), Poly Haven, glTF 1k | [CC0 1.0](https://polyhaven.com/license) |

The originals are not stored in the repository. Each family keeps its source and license in its metadata.

## What the conversion changes

With `--textures`, the vegetation keeps its own images. Each material's base colour image is read from the glTF and,
for cut-out materials (alpha mode MASK or BLEND), combined with the alpha map Poly Haven ships beside it
(`*_alpha_1k.png`). The converter stores color and normal images at the requested texture size, up to 2048 pixels per
side, along with their texture coordinates. The shipped jacaranda uses the Poly Haven 2K trunk color and OpenGL normal
maps; its branches and leaves use 1K color maps and 512-pixel normal maps. The renderer reads them by coordinate.
Where the mask is transparent a leaf or grass card is a hole: camera rays and shadows pass through it, so foliage casts
dappled shadows instead of squares. Simplified bark retains texture coordinates in the current conversion.
`--variant k` keeps one of the variants an asset lays side by side, and `--thin =name=1` keeps one material whole.

`tools/mesh-family-converter` reads the model with the application's importer and writes a `.g3df` mesh family:

- Materials map onto render surfaces (`--map`), so the families use the project's materials (leaf, bark, car paint,
  tinted glass) rather than the source textures. The cars are repainted: navy for the sedan, silver for the coupe.
- The jacaranda (3.9 million triangles) is reduced to about 540,000. Trunk and branches are simplified by vertex
  clustering, and the leaves are thinned (`--thin`) to one in five whole leaves, each enlarged to keep the crown's coverage.
- Every family also stores a light preview (about 25,000 triangles) that the interactive 3D view draws; the renderer
  always uses the full mesh.

```bash
dotnet run -c Release --project tools/mesh-family-converter -- convert jacaranda_tree_1k.gltf jacaranda-tree.g3df --name "Jacaranda Tree (detailed)" --category Planting --triangles 90000 --map leaves=Leaf --map branches=Bark --map trunk=Bark --default Bark --thin leaves=0.2 --preview 25000 --meta License=CC0-1.0
```

```bash
dotnet run -c Release --project tools/mesh-family-converter -- convert sedan.glb sedan-car.g3df --name "Sedan Car (detailed)" --category Entourage --plan-shape Car --map body=CarPaint --map tire=Rubber --map glass=TintedGlass --map trim=Metal --map hubcap=Metal --default Metal --meta License=CC0-1.0
```

```bash
dotnet run -c Release --project tools/mesh-family-converter -- convert sports-car.glb coupe-car-silver.g3df --name "Coupe Car Silver (detailed)" --category Entourage --plan-shape SilverCar --map body=CarPaintSilver --map tire=Rubber --map glass=TintedGlass --map rim=Rubber --map trim=Metal --map hubcap=Metal --default Metal --meta License=CC0-1.0
```

After converting, regenerate the sample project that embeds the families:

```bash
dotnet run --project tools/sample-projects
```
