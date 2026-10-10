namespace Platform.Contracts.Settings;

/// <summary>One tenant parameter (SRS CP-57): the stored text value (or the default when the tenant never set it) and its descriptor.</summary>
public sealed record TenantSettingItem(
    string Key,
    string Type,
    string Value,
    string DefaultValue,
    string Description,
    bool IsDefault,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId);

public sealed record TenantSettingsResponse(IReadOnlyList<TenantSettingItem> Settings);

/// <summary>A value to store; the text form is validated against the key's descriptor (<c>SET-001</c> unknown key, <c>SET-002</c> invalid value).</summary>
public sealed record SettingValueItem(string Key, string Value);

public sealed record UpdateTenantSettingsRequest(IReadOnlyList<SettingValueItem> Settings);
