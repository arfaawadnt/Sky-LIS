using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Settings;
using Platform.Domain.Authorization;
using Platform.Domain.Settings;

namespace Platform.Application.Settings.Queries;

/// <summary>SRS CP-57 Parameters: every catalogued key with its effective value; keys the tenant never set show the code default.</summary>
[RequirePermissionPolicy(PermissionCatalog.Settings, PermissionAction.View)]
public sealed record GetTenantSettingsQuery : IQuery<TenantSettingsResponse>;

public sealed class GetTenantSettingsQueryHandler(ITenantSettingsStore store) : IQueryHandler<GetTenantSettingsQuery, TenantSettingsResponse>
{
    public async Task<TenantSettingsResponse> Handle(GetTenantSettingsQuery query, CancellationToken cancellationToken)
    {
        var stored = (await store.ListAsync(cancellationToken)).ToDictionary(setting => setting.Key, StringComparer.Ordinal);
        return new TenantSettingsResponse(SettingKeys.All.Select(descriptor => Describe(descriptor, stored)).ToList());
    }

    /// <summary>A stored value that no longer validates (the catalogue tightened) is shown as the default, exactly as modules read it.</summary>
    public static TenantSettingItem Describe(SettingDescriptor descriptor, IReadOnlyDictionary<string, StoredSetting> stored)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(stored);
        var row = stored.GetValueOrDefault(descriptor.Key);
        var effective = row is not null && descriptor.IsValid(row.Value) ? row.Value.Trim() : descriptor.DefaultValue;
        return new TenantSettingItem(
            descriptor.Key,
            descriptor.Type.ToString(),
            effective,
            descriptor.DefaultValue,
            descriptor.Description,
            row is null || !descriptor.IsValid(row.Value),
            row?.UpdatedAtUtc,
            row?.UpdatedByUserId);
    }
}
