using Platform.Domain.PlatformAdmin;

namespace Platform.Application.PlatformAdmin;

/// <summary>
/// LIS-CR-008 C-04: the rows a picked city / district / area names, with their parents, read from <c>saas.cities / districts /
/// areas</c> (platform reference data, no tenant, active or not). An id no row has is absent (null). The adapter is
/// <c>Platform.Infrastructure.Persistence.PlatformAdmin.LocalityLookup</c>.
/// </summary>
public interface ILocalityLookup
{
    Task<LocalityRows> ReadAsync(Guid? cityId, Guid? districtId, Guid? areaId, CancellationToken cancellationToken);
}

/// <summary>The chain check a handler runs before it places a laboratory or a wizard request (<see cref="Locality.Resolve"/>).</summary>
public static class LocalityChecks
{
    /// <summary>Reads the picked rows (nothing when none is picked) and checks the chain under <paramref name="governorateId"/>.</summary>
    public static async Task<Locality> ResolveAsync(ILocalityLookup lookup, Guid? governorateId, Guid? cityId, Guid? districtId, Guid? areaId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var (city, district, area) = Locality.Clean(cityId, districtId, areaId);
        var rows = city is null && district is null && area is null
            ? LocalityRows.None
            : await lookup.ReadAsync(city, district, area, cancellationToken).ConfigureAwait(false);
        return Locality.Resolve(governorateId, city, district, area, rows);
    }
}
