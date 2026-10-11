using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Settings;
using Platform.Domain.Settings;
using Platform.Infrastructure.Persistence.Audit;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Infrastructure.Persistence.Settings;

/// <summary><c>app.tenant_settings</c> (SRS CP-57): one text value per tenant and key, keyed <c>(tenant_id, key)</c>, under RLS; its changes are field-audited.</summary>
public sealed class TenantSetting : ITenantScoped, IFieldChangeTracked
{
    private TenantSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    private TenantSetting(string key, string value, Guid? updatedByUserId, DateTimeOffset nowUtc)
    {
        Key = key;
        Value = value;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Stamped by the tenant interceptor from the current scope (never passed in: pitfall P-30).</summary>
    public Guid TenantId { get; private set; }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid? UpdatedByUserId { get; private set; }

    public static TenantSetting Create(string key, string value, Guid? updatedByUserId, DateTimeOffset nowUtc) => new(key, value, updatedByUserId, nowUtc);

    public void Set(string value, Guid? updatedByUserId, DateTimeOffset nowUtc)
    {
        Value = value;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = nowUtc;
    }
}

internal sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> builder)
    {
        builder.ToTable("tenant_settings", AppDbContext.AppSchema);
        builder.HasKey(setting => new { setting.TenantId, setting.Key });
        builder.Property(setting => setting.Key).HasMaxLength(SettingDescriptor.MaxKeyLength);
        builder.Property(setting => setting.Value).HasMaxLength(SettingDescriptor.MaxValueLength).IsRequired();
        builder.Property(setting => setting.UpdatedAtUtc).IsRequired();
    }
}

/// <summary>Write side of the Parameters page; the handler commits (InfrastructureHardeningTests: adapters never save).</summary>
internal sealed class TenantSettingsStore(AppDbContext context, ITenantContextAccessor tenant, ICurrentTenantSetter tenantScope) : ITenantSettingsStore
{
    public async Task<IReadOnlyList<StoredSetting>> ListAsync(CancellationToken cancellationToken) =>
        await context.Set<TenantSetting>().AsNoTracking()
            .OrderBy(setting => setting.Key)
            .Select(setting => new StoredSetting(setting.Key, setting.Value, setting.UpdatedAtUtc, setting.UpdatedByUserId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Read as <see cref="TenantSettings"/> reads them: under <c>ForTenant(platform)</c>, without elevation.</summary>
    public async Task<IReadOnlyList<StoredSetting>> ListInheritedAsync(CancellationToken cancellationToken)
    {
        if (tenant.CurrentTenantId is not { } current || TenantIds.IsPlatform(current))
        {
            return [];
        }

        using (tenantScope.ForTenant(TenantIds.Platform))
        {
            return await ListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UpsertAsync(string key, string value, Guid? actorUserId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var existing = await context.Set<TenantSetting>().FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            context.Set<TenantSetting>().Add(TenantSetting.Create(key, value, actorUserId, nowUtc));
        }
        else
        {
            existing.Set(value, actorUserId, nowUtc);
        }
    }
}

/// <summary>
/// Runtime reads (MODULES.md §1): the tenant's rows are loaded once per scope and tenant on the first read and parsed
/// through their descriptors. A key the tenant never set (or set to something the catalogue no longer accepts) reads
/// as the <b>platform value</b> — the row the platform parameters page (SA-19) stores for the platform tenant — and
/// only then as the descriptor's default (intake LIS-INT-FND-001 §6; UAT dry run D-16: a laboratory that never
/// changed the password length kept accepting 8 characters after the platform raised it to 10).
/// </summary>
/// <remarks>
/// The platform rows are read under <c>ForTenant(TenantIds.Platform)</c>, without elevation: the next connection
/// open stamps the platform tenant and its own RLS policy admits the rows. Inside an open transaction the stamp of
/// the caller's tenant stays and the platform read yields nothing, which falls back to the descriptor default — the
/// behaviour every read had before.
/// </remarks>
internal sealed class TenantSettings(AppDbContext context, ITenantContextAccessor tenant, ICurrentTenantSetter tenantScope) : ITenantSettings
{
    private readonly Dictionary<Guid, Dictionary<string, string>> _loaded = [];

    public async ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var (own, platform) = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return descriptor.Parse(Resolve(descriptor, own, platform));
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken)
    {
        var (own, platform) = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return SettingKeys.All.ToDictionary(
            descriptor => descriptor.Key,
            descriptor => Resolve(descriptor, own, platform) ?? descriptor.DefaultValue,
            StringComparer.Ordinal);
    }

    /// <summary>The tenant's valid value, else the platform's valid value, else <c>null</c> (the descriptor decides the default).</summary>
    private static string? Resolve(SettingDescriptor descriptor, Dictionary<string, string> own, Dictionary<string, string> platform) =>
        own.TryGetValue(descriptor.Key, out var value) && descriptor.IsValid(value) ? value.Trim()
        : platform.TryGetValue(descriptor.Key, out var inherited) && descriptor.IsValid(inherited) ? inherited.Trim()
        : null;

    private async ValueTask<(Dictionary<string, string> Own, Dictionary<string, string> Platform)> LoadAsync(CancellationToken cancellationToken)
    {
        var current = tenant.CurrentTenantId ?? Guid.Empty;
        var own = await RowsAsync(current, cancellationToken).ConfigureAwait(false);
        if (TenantIds.IsPlatform(current))
        {
            return (own, own);
        }

        if (!_loaded.TryGetValue(TenantIds.Platform, out var platform))
        {
            using (tenantScope.ForTenant(TenantIds.Platform))
            {
                platform = await RowsAsync(TenantIds.Platform, cancellationToken).ConfigureAwait(false);
            }
        }

        return (own, platform);
    }

    private async ValueTask<Dictionary<string, string>> RowsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_loaded.TryGetValue(tenantId, out var rows))
        {
            rows = await context.Set<TenantSetting>().AsNoTracking()
                .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false);
            _loaded[tenantId] = rows;
        }

        return rows;
    }
}
