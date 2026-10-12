using Platform.Application.Abstractions.Persistence;
using Platform.Domain.Account;

namespace Platform.Application.Account;

/// <summary>What a reader needs of the current laboratory's active logo.</summary>
public sealed record LaboratoryLogoInfo(string StorageKey, string ContentType, int Version);

/// <summary>
/// Write side and reads of <see cref="LaboratoryLogo"/> (<c>app.laboratory_logos</c>, one row per tenant). Every read is
/// scoped to the current tenant by the query filter and the table's RLS policy; the tenant never comes from a request.
/// </summary>
public interface ILaboratoryLogoRepository : IRepository<LaboratoryLogo>
{
    /// <summary>The current laboratory's logo row, active or removed (tracked), or <see langword="null"/> before the first upload.</summary>
    Task<LaboratoryLogo?> GetCurrentForUpdateAsync(CancellationToken cancellationToken);

    /// <summary>The current laboratory's active logo, or <see langword="null"/> when it has none.</summary>
    Task<LaboratoryLogoInfo?> FindActiveAsync(CancellationToken cancellationToken);
}
