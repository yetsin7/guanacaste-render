using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Isolated grid row whose edits are committed only when the user applies the sheet composition.</summary>
public sealed class SheetViewportRow
{
    public Guid Id { get; set; }
    public Guid ViewId { get; set; }
    public string Number { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Scale { get; set; }

    /// <summary>Creates a staging row without retaining mutable collections from the model.</summary>
    public static SheetViewportRow From(SheetViewport viewport) => new() { Id = viewport.Id, ViewId = viewport.ViewId,
        Number = viewport.Number, X = viewport.X, Y = viewport.Y, Width = viewport.Width, Height = viewport.Height, Scale = viewport.Scale };

    /// <summary>Builds an immutable placement for atomic model validation.</summary>
    public SheetViewport ToModel() => new(Id, ViewId, X, Y, Width, Height, Scale, Number);
}

/// <summary>Editable paper-space family graphic with stable identity across repeated edits.</summary>
public sealed class SheetGraphicRow
{
    public Guid Id { get; set; }
    public SheetGraphicKind Kind { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public double Weight { get; set; }
    public bool Bold { get; set; }

    /// <summary>Copies one family graphic into independent editor state.</summary>
    public static SheetGraphicRow From(SheetGraphic graphic) => new() { Id = graphic.Id, Kind = graphic.Kind,
        StartX = graphic.Start.X, StartY = graphic.Start.Y, EndX = graphic.End.X, EndY = graphic.End.Y,
        Text = graphic.Text, FontSize = graphic.FontSize, Weight = graphic.Weight, Bold = graphic.Bold };

    /// <summary>Returns an immutable graphic to be validated with the complete family layout.</summary>
    public SheetGraphic ToModel() => new(Id, Kind, new(StartX, StartY), new(EndX, EndY), Text, FontSize, Weight, Bold);
}
