using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Settings;
using Platform.Domain.Authorization;
using Platform.Domain.Settings;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Application.Settings.Queries;

/// <summary>
/// SRS CP-57 / SA-19 Parameters: every catalogued key with its effective value and its source. A key the laboratory never
/// set shows the platform value it inherits (D-16), and only then the code default (validation 2026-10-11, C-09).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Settings, PermissionAction.View)]
public sealed record GetTenantSettingsQuery : IQuery<TenantSettingsResponse>;

public sealed class GetTenantSettingsQueryHandler(ITenantSettingsStore store, ICurrentTenant currentTenant) : IQueryHandler<GetTenantSettingsQuery, TenantSettingsResponse>
{
    public Task<TenantSettingsResponse> Handle(GetTenantSettingsQuery query, CancellationToken cancellationToken) =>
        DescribeAllAsync(store, currentTenant, cancellationToken);

    /// <summary>The full, effective list for the current tenant (the query and the update command answer with it).</summary>
    public static async Task<TenantSettingsResponse> DescribeAllAsync(ITenantSettingsStore store, ICurrentTenant currentTenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(currentTenant);
        var own = (await store.ListAsync(cancellationToken)).ToDictionary(setting => setting.Key, StringComparer.Ordinal);
        var inherited = (await store.ListInheritedAsync(cancellationToken)).ToDictionary(setting => setting.Key, StringComparer.Ordinal);
        var isPlatform = currentTenant.TenantId is { } tenantId && TenantIds.IsPlatform(tenantId);
        return new TenantSettingsResponse(SettingKeys.All.Select(descriptor => Describe(descriptor, own, inherited, isPlatform)).ToList());
    }

    /// <summary>
    /// The tenant's own valid value, else the inherited platform value, else the code default, exactly as modules read it
    /// (<c>ITenantSettings</c>). A stored value that no longer validates (the catalogue tightened) is skipped at both levels.
    /// </summary>
    public static TenantSettingItem Describe(SettingDescriptor descriptor, IReadOnlyDictionary<string, StoredSetting> own, IReadOnlyDictionary<string, StoredSetting> inherited, bool isPlatformTenant)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(inherited);
        var row = own.GetValueOrDefault(descriptor.Key);
        var ownValid = row is not null && descriptor.IsValid(row.Value);
        var platformRow = inherited.GetValueOrDefault(descriptor.Key);
        var platformValid = platformRow is not null && descriptor.IsValid(platformRow.Value);
        var inheritedValue = platformValid ? platformRow!.Value.Trim() : descriptor.DefaultValue;

        var (effective, source) = ownValid
            ? (row!.Value.Trim(), isPlatformTenant ? TenantSettingSources.Platform : TenantSettingSources.Laboratory)
            : (inheritedValue, platformValid ? TenantSettingSources.Platform : TenantSettingSources.Default);
        return new TenantSettingItem(
            descriptor.Key,
            descriptor.Type.ToString(),
            effective,
            descriptor.DefaultValue,
            descriptor.Description,
            !ownValid,
            row?.UpdatedAtUtc,
            row?.UpdatedByUserId,
            source,
            inheritedValue);
    }
}
