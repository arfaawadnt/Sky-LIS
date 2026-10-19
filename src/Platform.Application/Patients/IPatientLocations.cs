using Platform.Contracts.Patients;
using Platform.Domain.Patients;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Patients;

/// <summary>One row of the platform's location hierarchy as the Patients module reads it: its parent and its names.</summary>
public sealed record LocationNode(Guid Id, Guid ParentId, string NameEn, string NameAr, bool IsActive);

/// <summary>
/// Read port of the Patients module over the platform's location hierarchy (change request 2026-10-19, A2; the public
/// lookups <c>/api/v1/public/{cities,districts,areas}</c>): cities by id, districts by id, areas by id, with their parent
/// ids and bilingual names, so a handler checks that a file's city / district / area chain to its governorate and the
/// registers show the names. Platform rows (no tenant). Unknown ids are absent from the answers. The adapter is
/// <c>Platform.Infrastructure.Persistence.Patients.PatientLocations</c>.
/// </summary>
public interface IPatientLocations
{
    Task<IReadOnlyDictionary<Guid, LocationNode>> CitiesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, LocationNode>> DistrictsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, LocationNode>> AreasAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>
/// The location rules of T1/T2 (change request 2026-10-19, A2): a picked city must belong to the chosen governorate, a
/// district to the city and an area to the district (PAT-014/015/016, 422); a lower level without its parent is refused the
/// same way. A picked city's English name is written into the free-text <c>City</c> so lists, exports and labels keep
/// working. Inactive rows are accepted (the pick lists offer active ones; a file keeps its row after a deactivation).
/// </summary>
public static class PatientLocationChain
{
    /// <summary>Checks the chain and returns the demographics with <see cref="PatientDemographics.City"/> set to the picked city's English name (unchanged when no city is picked).</summary>
    public static async Task<PatientDemographics> ResolveAsync(IPatientLocations locations, PatientDemographics demographics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(demographics);
        var governorateId = Value(demographics.GovernorateId);
        var cityId = Value(demographics.CityId);
        var districtId = Value(demographics.DistrictId);
        var areaId = Value(demographics.AreaId);

        if (cityId is null)
        {
            Require(districtId is null, PatientCodes.DistrictNotInCity, "A district needs its city.");
            Require(areaId is null, PatientCodes.AreaNotInDistrict, "An area needs its district.");
            return demographics;
        }

        var city = (await locations.CitiesAsync([cityId.Value], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(cityId.Value);
        Require(city is not null && city.ParentId == governorateId, PatientCodes.CityNotInGovernorate, "The city does not belong to the chosen governorate.");

        if (districtId is null)
        {
            Require(areaId is null, PatientCodes.AreaNotInDistrict, "An area needs its district.");
            return demographics with { City = city!.NameEn };
        }

        var district = (await locations.DistrictsAsync([districtId.Value], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(districtId.Value);
        Require(district is not null && district.ParentId == cityId, PatientCodes.DistrictNotInCity, "The district does not belong to the chosen city.");

        if (areaId is not null)
        {
            var area = (await locations.AreasAsync([areaId.Value], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(areaId.Value);
            Require(area is not null && area.ParentId == districtId, PatientCodes.AreaNotInDistrict, "The area does not belong to the chosen district.");
        }

        return demographics with { City = city!.NameEn };
    }

    /// <summary>The ids and names of the rows' locations, keyed by patient id; absent for a row without a city, district or area.</summary>
    public static async Task<IReadOnlyDictionary<Guid, PatientLocation>> NamesAsync(IPatientLocations locations, IReadOnlyCollection<PatientRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(rows);
        var cities = await Lookup(locations.CitiesAsync, rows.Select(row => row.CityId), cancellationToken).ConfigureAwait(false);
        var districts = await Lookup(locations.DistrictsAsync, rows.Select(row => row.DistrictId), cancellationToken).ConfigureAwait(false);
        var areas = await Lookup(locations.AreasAsync, rows.Select(row => row.AreaId), cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<Guid, PatientLocation>();
        foreach (var row in rows)
        {
            if (row.CityId is null && row.DistrictId is null && row.AreaId is null)
            {
                continue;
            }

            var city = row.CityId is { } cityId ? cities.GetValueOrDefault(cityId) : null;
            var district = row.DistrictId is { } districtId ? districts.GetValueOrDefault(districtId) : null;
            var area = row.AreaId is { } areaId ? areas.GetValueOrDefault(areaId) : null;
            result[row.Id] = new PatientLocation(row.CityId, city?.NameEn, city?.NameAr, row.DistrictId, district?.NameEn, district?.NameAr, row.AreaId, area?.NameEn, area?.NameAr);
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<Guid, LocationNode>> Lookup(
        Func<IReadOnlyCollection<Guid>, CancellationToken, Task<IReadOnlyDictionary<Guid, LocationNode>>> read,
        IEnumerable<Guid?> ids,
        CancellationToken cancellationToken)
    {
        var wanted = ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        return wanted.Count == 0 ? new Dictionary<Guid, LocationNode>() : await read(wanted, cancellationToken).ConfigureAwait(false);
    }

    private static Guid? Value(Guid? id) => id == Guid.Empty ? null : id;

    private static void Require(bool condition, string code, string message)
    {
        if (!condition)
        {
            throw new DomainException(code, message, DomainFault.Validation);
        }
    }
}
