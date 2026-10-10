using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
internal sealed class TenantSettingsStore(AppDbContext context) : ITenantSettingsStore
{
    public async Task<IReadOnlyList<StoredSetting>> ListAsync(CancellationToken cancellationToken) =>
        await context.Set<TenantSetting>().AsNoTracking()
            .OrderBy(setting => setting.Key)
            .Select(setting => new StoredSetting(setting.Key, setting.Value, setting.UpdatedAtUtc, setting.UpdatedByUserId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
/// Runtime reads (MODULES.md §1): the tenant's rows are loaded once per scope on the first read and parsed through
/// their descriptors; a key the tenant never set, or set to something the catalogue no longer accepts, reads as the default.
/// </summary>
internal sealed class TenantSettings(AppDbContext context) : ITenantSettings
{
    private Dictionary<string, string>? _stored;

    public async ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var stored = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return descriptor.Parse(stored.GetValueOrDefault(descriptor.Key));
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken)
    {
        var stored = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return SettingKeys.All.ToDictionary(
            descriptor => descriptor.Key,
            descriptor => stored.TryGetValue(descriptor.Key, out var value) && descriptor.IsValid(value) ? value.Trim() : descriptor.DefaultValue,
            StringComparer.Ordinal);
    }

    private async ValueTask<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken) =>
        _stored ??= await context.Set<TenantSetting>().AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
}
