namespace Platform.Domain.Authorization;

/// <summary>A module in the catalogue: its key, a display name for the privilege screen and the actions it exposes.</summary>
public sealed record PermissionModule
{
    public PermissionModule(string key, string displayName, params PermissionAction[] actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(actions);

        if (actions.Length == 0)
        {
            throw new ArgumentException("A module exposes at least one action.", nameof(actions));
        }

        Key = key;
        DisplayName = displayName;
        Actions = actions.Distinct().ToArray();
    }

    public string Key { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PermissionAction> Actions { get; }

    /// <summary>The permission keys of this module, <c>module.action</c>.</summary>
    public IEnumerable<string> Keys => Actions.Select(action => PermissionCatalog.Key(Key, action));
}
