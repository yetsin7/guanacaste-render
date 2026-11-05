namespace Guanacaste3D.Core;

/// <summary>Professional discipline used to group categories in the category picker.</summary>
public enum BimDiscipline
{
    Architectural,
    Structural,
    Mep,
    Other
}

/// <summary>
/// A family category. <see cref="Id"/> is a stable identifier stored in projects, while
/// <see cref="NameKey"/> and <see cref="DefaultName"/> are presentation data that may change.
/// </summary>
public sealed record BimCategory(
    string Id,
    string NameKey,
    string DefaultName,
    BimDiscipline Discipline,
    bool SupportsInPlace);

/// <summary>
/// The registered family categories. Pickers read this list, so new categories are added
/// here without changing any window. Only Generic Models accepts in-place families for now.
/// </summary>
public static class BimCategoryCatalog
{
    public const string GenericModelsId = "generic-models";

    public static IReadOnlyList<BimCategory> All { get; } =
    [
        new("duct-fittings", "BimCategory.DuctFittings", "Accesorios de conductos", BimDiscipline.Mep, false),
        new("pipe-fittings", "BimCategory.PipeFittings", "Accesorios de tuberías", BimDiscipline.Mep, false),
        new("vibration-management", "BimCategory.VibrationManagement", "Administración de vibración", BimDiscipline.Mep, false),
        new("electrical-equipment", "BimCategory.ElectricalEquipment", "Aparatos eléctricos", BimDiscipline.Mep, false),
        new("plumbing-fixtures", "BimCategory.PlumbingFixtures", "Aparatos sanitarios", BimDiscipline.Mep, false),
        new("parking", "BimCategory.Parking", "Aparcamiento", BimDiscipline.Architectural, false),
        new("mep-auxiliary-frame", "BimCategory.MepAuxiliaryFrame", "Armazón auxiliar MEP", BimDiscipline.Mep, false),
        new("structural-framing", "BimCategory.StructuralFraming", "Armazón estructural", BimDiscipline.Structural, false),
        new("railings", "BimCategory.Railings", "Barandillas", BimDiscipline.Architectural, false),
        new("structural-cables", "BimCategory.StructuralCables", "Cables de acero estructurales", BimDiscipline.Structural, false),
        new("bridge-cables", "BimCategory.BridgeCables", "Cables de puente", BimDiscipline.Structural, false),
        new("roads", "BimCategory.Roads", "Carreteras", BimDiscipline.Other, false),
        new("structural-foundations", "BimCategory.StructuralFoundations", "Cimentación estructural", BimDiscipline.Structural, false),
        new("vertical-circulation", "BimCategory.VerticalCirculation", "Circulación vertical", BimDiscipline.Architectural, false),
        new("structural-connections", "BimCategory.StructuralConnections", "Conexiones estructurales", BimDiscipline.Structural, false),
        new("buttresses", "BimCategory.Buttresses", "Contrafuertes", BimDiscipline.Structural, false),
        new("roofs", "BimCategory.Roofs", "Cubiertas", BimDiscipline.Architectural, false),
        new("audiovisual-devices", "BimCategory.AudiovisualDevices", "Dispositivos audiovisuales", BimDiscipline.Mep, false),
        new("fire-alarm-devices", "BimCategory.FireAlarmDevices", "Dispositivos de alarma de incendios", BimDiscipline.Mep, false),
        new("communication-devices", "BimCategory.CommunicationDevices", "Dispositivos de comunicación", BimDiscipline.Mep, false),
        new("mechanical-control-devices", "BimCategory.MechanicalControlDevices", "Dispositivos de control mecánico", BimDiscipline.Mep, false),
        new("data-devices", "BimCategory.DataDevices", "Dispositivos de datos", BimDiscipline.Mep, false),
        new("expansion-joints", "BimCategory.ExpansionJoints", "Juntas de expansión", BimDiscipline.Structural, false),
        new("lighting-fixtures", "BimCategory.LightingFixtures", "Luminarias", BimDiscipline.Mep, false),
        new("mass", "BimCategory.Mass", "Masa", BimDiscipline.Architectural, false),
        new("furniture", "BimCategory.Furniture", "Mobiliario", BimDiscipline.Architectural, false),
        new(GenericModelsId, "BimCategory.GenericModels", "Modelos genéricos", BimDiscipline.Other, true),
        new("casework", "BimCategory.Casework", "Muebles de obra", BimDiscipline.Architectural, false)
    ];

    /// <summary>Returns the category with the given stable identifier, or null when it is not registered.</summary>
    public static BimCategory? Find(string id) => All.FirstOrDefault(category => category.Id == id);
}
