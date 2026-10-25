using Microsoft.EntityFrameworkCore;
using Platform.Application.PlatformAdmin;
using Platform.Domain.PlatformAdmin;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

/// <summary>
/// LIS-CR-008 C-04: reads of the location hierarchy shared by the platform and organization adapters — the rows a picked
/// city / district / area names (for the chain check) and the bilingual names of a locality (for the address lines). Platform
/// reference data (<c>saas</c>, no tenant, no RLS), active or not.
/// </summary>
internal static class LocalityReads
{
    public static async Task<LocalityRows> RowsAsync(AppDbContext context, Guid? cityId, Guid? districtId, Guid? areaId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var city = cityId is { } c
            ? await context.Set<City>().AsNoTracking().Where(row => row.Id == c).Select(row => new { row.Id, Parent = row.GovernorateId }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var district = districtId is { } d
            ? await context.Set<District>().AsNoTracking().Where(row => row.Id == d).Select(row => new { row.Id, Parent = row.CityId }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var area = areaId is { } a
            ? await context.Set<Area>().AsNoTracking().Where(row => row.Id == a).Select(row => new { row.Id, Parent = row.DistrictId }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        return new LocalityRows(
            city is null ? null : new LocalityNode(city.Id, city.Parent),
            district is null ? null : new LocalityNode(district.Id, district.Parent),
            area is null ? null : new LocalityNode(area.Id, area.Parent));
    }

    /// <summary>The names of the cities, districts and areas the ids name (missing ids are absent), keyed by id.</summary>
    public static async Task<IReadOnlyDictionary<Guid, (string En, string Ar)>> NamesAsync(AppDbContext context, IEnumerable<Guid?> cityIds, IEnumerable<Guid?> districtIds, IEnumerable<Guid?> areaIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cities = Ids(cityIds);
        var districts = Ids(districtIds);
        var areas = Ids(areaIds);
        var names = new Dictionary<Guid, (string En, string Ar)>();
        if (cities.Count > 0)
        {
            foreach (var row in await context.Set<City>().AsNoTracking().Where(row => cities.Contains(row.Id)).Select(row => new { row.Id, row.Name.En, row.Name.Ar }).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                names[row.Id] = (row.En, row.Ar);
            }
        }

        if (districts.Count > 0)
        {
            foreach (var row in await context.Set<District>().AsNoTracking().Where(row => districts.Contains(row.Id)).Select(row => new { row.Id, row.Name.En, row.Name.Ar }).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                names[row.Id] = (row.En, row.Ar);
            }
        }

        if (areas.Count > 0)
        {
            foreach (var row in await context.Set<Area>().AsNoTracking().Where(row => areas.Contains(row.Id)).Select(row => new { row.Id, row.Name.En, row.Name.Ar }).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                names[row.Id] = (row.En, row.Ar);
            }
        }

        return names;
    }

    /// <summary>"Area, District, City, Governorate" in both languages from the looked-up names (null when no city).</summary>
    public static (string? En, string? Ar) Line(IReadOnlyDictionary<Guid, (string En, string Ar)> names, Guid? cityId, Guid? districtId, Guid? areaId, string? governorateEn, string? governorateAr)
    {
        ArgumentNullException.ThrowIfNull(names);
        (string En, string Ar)? Name(Guid? id) => id is { } value && names.TryGetValue(value, out var name) ? name : null;
        var city = Name(cityId);
        var district = Name(districtId);
        var area = Name(areaId);
        return AddressLine.Of(new LocalityNames(area?.En, area?.Ar, district?.En, district?.Ar, city?.En, city?.Ar, governorateEn, governorateAr));
    }

    private static List<Guid> Ids(IEnumerable<Guid?> ids) => ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
}

/// <summary><see cref="ILocalityLookup"/> over <c>saas.cities / districts / areas</c>.</summary>
internal sealed class LocalityLookup(AppDbContext context) : ILocalityLookup
{
    public Task<LocalityRows> ReadAsync(Guid? cityId, Guid? districtId, Guid? areaId, CancellationToken cancellationToken) =>
        LocalityReads.RowsAsync(context, cityId, districtId, areaId, cancellationToken);
}
