namespace Platform.Contracts.Settings;

/// <summary>
/// One tenant parameter (SRS CP-57) with its descriptor. <see cref="Value"/> is the effective value, exactly as modules read
/// it: the tenant's own valid value, else the platform's (SA-19, D-16), else the code default; <see cref="Source"/> says
/// which (<see cref="TenantSettingSources"/>). <see cref="InheritedValue"/> is what the tenant reads without a value of its
/// own; <see cref="IsDefault"/> is true when the tenant holds no valid value of its own (validation 2026-10-11, C-09).
/// </summary>
public sealed record TenantSettingItem(
    string Key,
    string Type,
    string Value,
    string DefaultValue,
    string Description,
    bool IsDefault,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    string Source,
    string InheritedValue);

/// <summary>Where an effective parameter value comes from.</summary>
public static class TenantSettingSources
{
    /// <summary>The laboratory stored it.</summary>
    public const string Laboratory = "Laboratory";

    /// <summary>The platform parameters (SA-19) store it: the platform's own row, or the value a laboratory inherits.</summary>
    public const string Platform = "Platform";

    /// <summary>Nobody stored a valid value: the code default of the catalogue.</summary>
    public const string Default = "Default";
}

public sealed record TenantSettingsResponse(IReadOnlyList<TenantSettingItem> Settings);

/// <summary>
/// A value to store; the text form is validated against the key's descriptor (<c>SET-001</c> unknown key, <c>SET-002</c> invalid
/// value, <c>SET-003</c> unknown time zone). A <c>null</c> <see cref="Value"/> removes the tenant's own value ("Use default",
/// validation F-13): the row is deleted and the key reads the inherited value again (<c>IsDefault</c> true, no update stamp).
/// </summary>
public sealed record SettingValueItem(string Key, string? Value);

public sealed record UpdateTenantSettingsRequest(IReadOnlyList<SettingValueItem> Settings);
