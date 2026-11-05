namespace Guanacaste3D.Desktop.Customization;

/// <summary>
/// Represents one editable row in the command customization experience.
/// </summary>
public sealed class CommandCustomizationItem
{
    public required string CommandId { get; init; }

    public required string Label { get; init; }

    public required string Workspace { get; init; }

    public required string IconResourceKey { get; init; }

    public bool IsQuickAccess { get; set; }

    public string Shortcut { get; set; } = string.Empty;
}
