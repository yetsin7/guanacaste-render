namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Identifies the professional workspace shown by a reusable ribbon instance.
/// </summary>
public enum RibbonWorkspace
{
    Structure,
    Concrete,
    Steel,
    Systems,
    Insert,
    Annotate,
    Analyze,
    MassAndSite,
    Collaborate,
    View,
    Manage,
    AddIns,
    Modify,
    Wall
}

/// <summary>
/// Determines how a command is presented inside its ribbon group.
/// </summary>
public enum RibbonCommandSize
{
    Large,
    Compact
}

/// <summary>
/// Describes one localized command without coupling the ribbon to application logic.
/// </summary>
public sealed record RibbonCommandDefinition(
    string CommandId,
    string LabelResourceKey,
    string IconResourceKey,
    RibbonCommandSize Size = RibbonCommandSize.Large);

/// <summary>
/// Describes a named group of commands in a professional workspace ribbon.
/// </summary>
public sealed record RibbonGroupDefinition(
    string LabelResourceKey,
    params RibbonCommandDefinition[] Commands);

/// <summary>
/// Carries a ribbon selection to the main application command router.
/// </summary>
public sealed class ProfessionalRibbonCommandEventArgs : EventArgs
{
    /// <summary>
    /// Creates an event payload for a command selected by the user.
    /// </summary>
    public ProfessionalRibbonCommandEventArgs(RibbonWorkspace workspace, string commandId, string displayName)
    {
        Workspace = workspace;
        CommandId = commandId;
        DisplayName = displayName;
    }

    public RibbonWorkspace Workspace { get; }

    public string CommandId { get; }

    public string DisplayName { get; }
}
