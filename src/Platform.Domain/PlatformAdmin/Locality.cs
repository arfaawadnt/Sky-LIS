using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// A row of the location hierarchy as a chain check reads it: its id and the id of its parent (a city's governorate, a
/// district's city, an area's district).
/// </summary>
public readonly record struct LocalityNode(Guid Id, Guid ParentId);

/// <summary>The rows the picked ids name, as read from <c>saas.cities / districts / areas</c>; a null node is an id no row has.</summary>
public sealed record LocalityRows(LocalityNode? City, LocalityNode? District, LocalityNode? Area)
{
    public static readonly LocalityRows None = new(null, null, null);
}

/// <summary>
/// LIS-CR-008 C-04: where a laboratory (<see cref="Tenant"/>, <see cref="SubscriptionRequest"/>) or a branch sits below its
/// governorate — city (markaz) → district → area (village), each optional. A locality is only obtained through
/// <see cref="Resolve"/>, which checks the chain on the rows read by the handler: the city belongs to the governorate
/// (GEO-020), the district to the city (GEO-021), the area to the district (GEO-022); a lower level without its parent, and an
/// id no row has, are refused the same way. Inactive rows are accepted (the pick lists offer active ones; a record keeps its
/// row after a deactivation), like the patient file (PAT-014…016). <see cref="GovernorateId"/> records the governorate the
/// chain was checked against, so the owner refuses a locality of another governorate (<see cref="EnsureIn"/>).
/// </summary>
public sealed record Locality
{
    /// <summary>No locality: every level empty (always valid).</summary>
    public static readonly Locality None = new(null, null, null, null);

    private Locality(Guid? governorateId, Guid? cityId, Guid? districtId, Guid? areaId)
    {
        GovernorateId = governorateId;
        CityId = cityId;
        DistrictId = districtId;
        AreaId = areaId;
    }

    /// <summary>The governorate the chain was checked against; null for <see cref="None"/>.</summary>
    public Guid? GovernorateId { get; }

    public Guid? CityId { get; }

    public Guid? DistrictId { get; }

    public Guid? AreaId { get; }

    public bool IsEmpty => CityId is null && DistrictId is null && AreaId is null;

    /// <summary>The ids a request names, <see cref="Guid.Empty"/> read as "none".</summary>
    public static (Guid? CityId, Guid? DistrictId, Guid? AreaId) Clean(Guid? cityId, Guid? districtId, Guid? areaId) =>
        (Value(cityId), Value(districtId), Value(areaId));

    /// <summary>
    /// Checks the picked ids against the rows read for them and the governorate of the owner. Nothing picked is
    /// <see cref="None"/> whatever the governorate.
    /// </summary>
    public static Locality Resolve(Guid? governorateId, Guid? cityId, Guid? districtId, Guid? areaId, LocalityRows rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var (city, district, area) = Clean(cityId, districtId, areaId);
        var governorate = Value(governorateId);
        if (city is null)
        {
            Require(district is null, PlatformAdminCodes.LocalityDistrictNotInCity, "A district needs its city.");
            Require(area is null, PlatformAdminCodes.LocalityAreaNotInDistrict, "An area needs its district.");
            return None;
        }

        Require(
            governorate is not null && rows.City is { } cityRow && cityRow.Id == city && cityRow.ParentId == governorate,
            PlatformAdminCodes.LocalityCityNotInGovernorate,
            "The city does not belong to the chosen governorate.");

        if (district is null)
        {
            Require(area is null, PlatformAdminCodes.LocalityAreaNotInDistrict, "An area needs its district.");
            return new Locality(governorate, city, null, null);
        }

        Require(
            rows.District is { } districtRow && districtRow.Id == district && districtRow.ParentId == city,
            PlatformAdminCodes.LocalityDistrictNotInCity,
            "The district does not belong to the chosen city.");

        if (area is not null)
        {
            Require(
                rows.Area is { } areaRow && areaRow.Id == area && areaRow.ParentId == district,
                PlatformAdminCodes.LocalityAreaNotInDistrict,
                "The area does not belong to the chosen district.");
        }

        return new Locality(governorate, city, district, area);
    }

    /// <summary>GEO-020: a locality checked against another governorate than the owner's is refused (the owner's governorate changed, or it has none).</summary>
    public void EnsureIn(Guid? governorateId)
    {
        if (IsEmpty)
        {
            return;
        }

        Require(Value(governorateId) is { } governorate && governorate == GovernorateId, PlatformAdminCodes.LocalityCityNotInGovernorate, "The city does not belong to the chosen governorate.");
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

/// <summary>
/// LIS-CR-008 C-04: the one-line address of a locality, finest level first — "Area, District, City, Governorate" — for the
/// public laboratory card and the clients register. Blank parts are skipped; nothing (null) when no city is picked, since the
/// governorate alone is already shown beside the country. Arabic joins with the Arabic comma.
/// </summary>
public static class AddressLine
{
    public const string EnglishSeparator = ", ";
    public const string ArabicSeparator = "، ";

    /// <summary>One language: the parts from the finest (area) to the widest (governorate).</summary>
    public static string? Of(string separator, string? area, string? district, string? city, string? governorate)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return null;
        }

        var parts = new[] { area, district, city, governorate }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim());
        return string.Join(separator, parts);
    }

    /// <summary>Both languages from the bilingual names (null names skipped).</summary>
    public static (string? En, string? Ar) Of(LocalityNames names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return (
            Of(EnglishSeparator, names.AreaEn, names.DistrictEn, names.CityEn, names.GovernorateEn),
            Of(ArabicSeparator, names.AreaAr, names.DistrictAr, names.CityAr, names.GovernorateAr));
    }
}

/// <summary>The bilingual names of a locality and its governorate as a register reads them (any may be null).</summary>
public sealed record LocalityNames(
    string? AreaEn,
    string? AreaAr,
    string? DistrictEn,
    string? DistrictAr,
    string? CityEn,
    string? CityAr,
    string? GovernorateEn,
    string? GovernorateAr);
