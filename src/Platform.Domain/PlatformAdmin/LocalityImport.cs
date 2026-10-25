using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>One line of a locality file (SA-11 import): the city, district and area names, English and Arabic, as read.</summary>
public sealed record LocalityImportRow(int Line, string? CityEn, string? CityAr, string? DistrictEn, string? DistrictAr, string? AreaEn, string? AreaAr);

/// <summary>The hierarchy level a refusal or a count belongs to.</summary>
public enum LocalityLevel
{
    City = 0,
    District = 1,
    Area = 2,
}

/// <summary>A refused line: the level it failed at, the refusal code (GEO-025, GEO-015, GEO-006) and the reason.</summary>
public sealed record LocalityImportRefusal(int Line, LocalityLevel Level, string Code, string Message);

/// <summary>Created / existing (distinct rows matched) / refused (lines) of one level.</summary>
public sealed record LocalityImportCount(int Created, int Existing, int Refused);

/// <summary>
/// The outcome of planning an import: the new rows to stage (only when <see cref="Refusals"/> is empty — all-or-nothing) and
/// the counts per level.
/// </summary>
public sealed class LocalityImportPlan
{
    internal LocalityImportPlan(
        IReadOnlyList<City> newCities,
        IReadOnlyList<District> newDistricts,
        IReadOnlyList<Area> newAreas,
        LocalityImportCount cities,
        LocalityImportCount districts,
        LocalityImportCount areas,
        IReadOnlyList<LocalityImportRefusal> refusals)
    {
        NewCities = newCities;
        NewDistricts = newDistricts;
        NewAreas = newAreas;
        Cities = cities;
        Districts = districts;
        Areas = areas;
        Refusals = refusals;
    }

    public IReadOnlyList<City> NewCities { get; }

    public IReadOnlyList<District> NewDistricts { get; }

    public IReadOnlyList<Area> NewAreas { get; }

    public LocalityImportCount Cities { get; }

    public LocalityImportCount Districts { get; }

    public LocalityImportCount Areas { get; }

    public IReadOnlyList<LocalityImportRefusal> Refusals { get; }

    /// <summary>Nothing refused: the plan may be applied.</summary>
    public bool IsClean => Refusals.Count == 0;
}

/// <summary>
/// LIS-CR-008 C-04 (decision D-2): the locality import of one governorate (SA-11 "Import CSV"). Each line names a city and,
/// optionally, a district of that city and an area of that district. A name is matched case-insensitively on its English name
/// within its parent — among the stored rows (active or not) and the rows created earlier in the same file — so a file can be
/// imported again without duplicates (idempotent); an existing row is never renamed or reactivated. A blank English name
/// takes the Arabic one and the reverse (the official lists may come in one language). Refusals: no city, an area without its
/// district, a name over 100 characters (GEO-025); a new row under an inactive governorate, city or district (GEO-015). The
/// caller stages <see cref="LocalityImportPlan.NewCities"/>… only when the plan is clean (all-or-nothing).
/// </summary>
public static class LocalityImport
{
    public static LocalityImportPlan Plan(
        Governorate governorate,
        IReadOnlyCollection<City> cities,
        IReadOnlyCollection<District> districts,
        IReadOnlyCollection<Area> areas,
        IReadOnlyList<LocalityImportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(governorate);
        ArgumentNullException.ThrowIfNull(cities);
        ArgumentNullException.ThrowIfNull(districts);
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(rows);

        var cityByName = Index(cities.Where(city => city.GovernorateId == governorate.Id), city => Key(city.Name.En));
        var districtByName = Index(districts, district => (district.CityId, Key(district.Name.En)));
        var areaByName = Index(areas, area => (area.DistrictId, Key(area.Name.En)));
        var newCities = new List<City>();
        var newDistricts = new List<District>();
        var newAreas = new List<Area>();
        var matched = new HashSet<Guid>();
        var created = new HashSet<Guid>();
        var refusals = new List<LocalityImportRefusal>();
        int existingCities = 0, existingDistricts = 0, existingAreas = 0;

        foreach (var row in rows)
        {
            var level = LocalityLevel.City;
            try
            {
                var cityName = Names(row.CityEn, row.CityAr);
                var districtName = Names(row.DistrictEn, row.DistrictAr);
                var areaName = Names(row.AreaEn, row.AreaAr);
                if (cityName is null)
                {
                    refusals.Add(new LocalityImportRefusal(row.Line, LocalityLevel.City, PlatformAdminCodes.LocalityImportRowInvalid, "The line has no city."));
                    continue;
                }

                if (districtName is null && areaName is not null)
                {
                    refusals.Add(new LocalityImportRefusal(row.Line, LocalityLevel.Area, PlatformAdminCodes.LocalityImportRowInvalid, "An area needs its district."));
                    continue;
                }

                var tooLong = TooLong(cityName, LocalityLevel.City) ?? TooLong(districtName, LocalityLevel.District) ?? TooLong(areaName, LocalityLevel.Area);
                if (tooLong is { } longLevel)
                {
                    refusals.Add(new LocalityImportRefusal(row.Line, longLevel, PlatformAdminCodes.LocalityImportRowInvalid, $"A name must not exceed {LocationRules.MaxNameLength} characters."));
                    continue;
                }

                if (!cityByName.TryGetValue(Key(cityName.Value.En), out var city))
                {
                    city = City.Create(governorate, cityName.Value.En, cityName.Value.Ar);
                    cityByName.Add(Key(city.Name.En), city);
                    newCities.Add(city);
                    created.Add(city.Id);
                }
                else if (!created.Contains(city.Id) && matched.Add(city.Id))
                {
                    existingCities++;
                }

                if (districtName is null)
                {
                    continue;
                }

                level = LocalityLevel.District;
                if (!districtByName.TryGetValue((city.Id, Key(districtName.Value.En)), out var district))
                {
                    district = District.Create(city, districtName.Value.En, districtName.Value.Ar);
                    districtByName.Add((city.Id, Key(district.Name.En)), district);
                    newDistricts.Add(district);
                    created.Add(district.Id);
                }
                else if (!created.Contains(district.Id) && matched.Add(district.Id))
                {
                    existingDistricts++;
                }

                if (areaName is null)
                {
                    continue;
                }

                level = LocalityLevel.Area;
                if (!areaByName.TryGetValue((district.Id, Key(areaName.Value.En)), out var area))
                {
                    area = Area.Create(district, areaName.Value.En, areaName.Value.Ar);
                    areaByName.Add((district.Id, Key(area.Name.En)), area);
                    newAreas.Add(area);
                    created.Add(area.Id);
                }
                else if (!created.Contains(area.Id) && matched.Add(area.Id))
                {
                    existingAreas++;
                }
            }
            catch (DomainException exception)
            {
                refusals.Add(new LocalityImportRefusal(row.Line, level, exception.Code, exception.Message));
            }
        }

        int Refused(LocalityLevel level) => refusals.Count(refusal => refusal.Level == level);
        return new LocalityImportPlan(
            newCities,
            newDistricts,
            newAreas,
            new LocalityImportCount(newCities.Count, existingCities, Refused(LocalityLevel.City)),
            new LocalityImportCount(newDistricts.Count, existingDistricts, Refused(LocalityLevel.District)),
            new LocalityImportCount(newAreas.Count, existingAreas, Refused(LocalityLevel.Area)),
            refusals);
    }

    /// <summary>The comparison key of a name within its parent: trimmed, lower-case (the unique indexes use <c>lower(name_en)</c>).</summary>
    public static string Key(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Both names trimmed, a blank one taking the other; null when both are blank.</summary>
    private static (string En, string Ar)? Names(string? en, string? ar)
    {
        var english = (en ?? string.Empty).Trim();
        var arabic = (ar ?? string.Empty).Trim();
        if (english.Length == 0 && arabic.Length == 0)
        {
            return null;
        }

        return (english.Length == 0 ? arabic : english, arabic.Length == 0 ? english : arabic);
    }

    /// <summary>The rows by key; the first wins should the stored data hold a duplicate the unique index would have refused.</summary>
    private static Dictionary<TKey, T> Index<TKey, T>(IEnumerable<T> rows, Func<T, TKey> key)
        where TKey : notnull
    {
        var index = new Dictionary<TKey, T>();
        foreach (var row in rows)
        {
            index.TryAdd(key(row), row);
        }

        return index;
    }

    private static LocalityLevel? TooLong((string En, string Ar)? name, LocalityLevel level) =>
        name is { } value && (value.En.Length > LocationRules.MaxNameLength || value.Ar.Length > LocationRules.MaxNameLength) ? level : null;
}
