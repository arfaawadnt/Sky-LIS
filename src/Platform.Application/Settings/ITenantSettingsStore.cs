namespace Platform.Application.Settings;

/// <summary>One stored row of <c>app.tenant_settings</c> as the Parameters page shows it.</summary>
public sealed record StoredSetting(string Key, string Value, DateTimeOffset UpdatedAtUtc, Guid? UpdatedByUserId);

/// <summary>
/// Write side of the tenant parameters (SRS CP-57): the current tenant's stored rows and an upsert. The store stages
/// changes; the handler commits once through <c>IUnitOfWork</c>. Runtime reads go through <c>ITenantSettings</c>.
/// </summary>
public interface ITenantSettingsStore
{
    Task<IReadOnlyList<StoredSetting>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Stages a value for the key: a new row or an update of the existing one, stamped with the actor and the time.</summary>
    Task UpsertAsync(string key, string value, Guid? actorUserId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
