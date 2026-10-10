using Platform.Domain.Settings;

namespace Platform.Application.Abstractions.Settings;

/// <summary>
/// Tenant parameters as a module reads them at runtime (MODULES.md §1, SRS CP-57 "read by every module"): a value is
/// the tenant's stored text parsed by its descriptor, or the code default. The adapter loads the tenant's rows once per
/// scope (one query per request at most); the first read pays it.
/// </summary>
public interface ITenantSettings
{
    ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken);

    /// <summary>Every catalogued key with its effective text value (stored or default).</summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken);
}
