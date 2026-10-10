namespace Platform.Domain.Authorization;

/// <summary>
/// The closed action set permissions are built from (03 §9). Adding an action is an architectural change;
/// adding a module is a change request handled in <see cref="PermissionCatalog"/>.
/// </summary>
public enum PermissionAction
{
    View = 0,
    Create = 1,
    Edit = 2,
    Approve = 3,
    Cancel = 4,
    Export = 5,
    Manage = 6,
    ViewConfidential = 7,
}

public static class PermissionActionExtensions
{
    /// <summary>The key segment: lower camel case (<c>view</c>, <c>viewConfidential</c>).</summary>
    public static string ToKey(this PermissionAction action) => action switch
    {
        PermissionAction.View => "view",
        PermissionAction.Create => "create",
        PermissionAction.Edit => "edit",
        PermissionAction.Approve => "approve",
        PermissionAction.Cancel => "cancel",
        PermissionAction.Export => "export",
        PermissionAction.Manage => "manage",
        PermissionAction.ViewConfidential => "viewConfidential",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown permission action."),
    };
}
