using Microsoft.EntityFrameworkCore;
using Platform.Application.Patients;
using Platform.Domain.PlatformAdmin;

namespace Platform.Infrastructure.Persistence.Patients;

/// <summary>
/// Adapter of <see cref="IPatientLocations"/> (change request 2026-10-19, A2): reads <c>saas.cities</c>, <c>saas.districts</c>
/// and <c>saas.areas</c> by id (platform rows, no tenant, readable from every tenant scope) through the earlier-wave
/// platform entity types, as <see cref="LaboratoryCountryLookup"/> reads the countries.
/// </summary>
internal sealed class PatientLocations(AppDbContext context) : IPatientLocations
{
    public const int MaxIds = 500;

    public async Task<IReadOnlyDictionary<Guid, LocationNode>> CitiesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = Wanted(ids);
        if (wanted.Count == 0)
        {
            return new Dictionary<Guid, LocationNode>();
        }

        return await context.Set<City>().AsNoTracking()
            .Where(city => wanted.Contains(city.Id))
            .Select(city => new LocationNode(city.Id, city.GovernorateId, city.Name.En, city.Name.Ar, city.IsActive))
            .ToDictionaryAsync(node => node.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, LocationNode>> DistrictsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = Wanted(ids);
        if (wanted.Count == 0)
        {
            return new Dictionary<Guid, LocationNode>();
        }

        return await context.Set<District>().AsNoTracking()
            .Where(district => wanted.Contains(district.Id))
            .Select(district => new LocationNode(district.Id, district.CityId, district.Name.En, district.Name.Ar, district.IsActive))
            .ToDictionaryAsync(node => node.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, LocationNode>> AreasAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = Wanted(ids);
        if (wanted.Count == 0)
        {
            return new Dictionary<Guid, LocationNode>();
        }

        return await context.Set<Area>().AsNoTracking()
            .Where(area => wanted.Contains(area.Id))
            .Select(area => new LocationNode(area.Id, area.DistrictId, area.Name.En, area.Name.Ar, area.IsActive))
            .ToDictionaryAsync(node => node.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<Guid> Wanted(IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.Distinct().ToList();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(wanted.Count, MaxIds, nameof(ids));
        return wanted;
    }
}
