namespace Guanacaste3D.Core;

/// <summary>Builds the reduced model used by structural section views without changing the shared building document.</summary>
public static class StructuralSectionDocument
{
    /// <summary>
    /// Keeps construction that communicates the load path: walls and their openings, slabs, columns, framing,
    /// foundations, connections, and slab edges. Architectural equipment and site decoration are omitted.
    /// </summary>
    public static BuildingDocument Create(BuildingDocument source)
    {
        var document = source.Clone();
        document.Rooms.Clear();
        document.ElectricalDevices.Clear();
        document.ElectricalCircuits.Clear();
        document.PlumbingPoints.Clear();
        document.PlumbingSystems.Clear();
        document.InPlaceFamilies.Clear();
        document.Floors = document.Floors.Where(floor => floor.Kind is FloorKind.Floor or FloorKind.Roof).ToList();
        document.StructuralElements = document.StructuralElements.Where(element => element.Kind is
            StructuralElementKind.Beam or StructuralElementKind.Brace or StructuralElementKind.Truss or
            StructuralElementKind.WallFoundation or StructuralElementKind.IsolatedFoundation or
            StructuralElementKind.Connection or StructuralElementKind.SlabEdge).ToList();
        return document;
    }
}
