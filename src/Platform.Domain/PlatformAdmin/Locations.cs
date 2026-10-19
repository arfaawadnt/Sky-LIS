using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

// The location hierarchy under a governorate (change request 2026-10-19, A2 — SA-11b "Locations"): governorate → city →
// district → area. Platform reference data (saas.cities, saas.districts, saas.areas, no tenant), edited from the
// Governorates page and offered to the patient form as pick lists. Each level: bilingual name ≤ 100, English name unique
// within its parent (case-insensitive index in PlatformAdmin.migration.sql), never deleted (deactivated instead), never
// moved to another parent. A child is created or reactivated only under an active parent (GEO-015); a parent with active
// children is not deactivated (GEO-112).

/// <summary>A city of a governorate.</summary>
public sealed class City : AggregateRoot
{
    public const int MaxNameLength = 100;

    private City()
    {
        Name = null!;
    }

    private City(Guid id, Guid governorateId, LocalizedText name)
        : base(id)
    {
        GovernorateId = governorateId;
        Name = name;
        IsActive = true;
    }

    public Guid GovernorateId { get; private set; }

    public LocalizedText Name { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static City Create(Governorate governorate, string nameEn, string nameAr)
    {
        ArgumentNullException.ThrowIfNull(governorate);
        LocationRules.RequireActiveParent(governorate.IsActive, "governorate");
        return new City(Guid.CreateVersion7(), governorate.Id, LocationRules.Name(nameEn, nameAr, "The city name"));
    }

    /// <summary>Renames the city; the governorate is fixed once created (a city never moves).</summary>
    public void Rename(string nameEn, string nameAr) => Name = LocationRules.Name(nameEn, nameAr, "The city name");

    /// <summary>Refused while an active district belongs to the city (GEO-112): the handler tells.</summary>
    public void Deactivate(bool hasActiveDistricts)
    {
        LocationRules.RequireNoActiveChildren(hasActiveDistricts, "city", "districts");
        IsActive = false;
    }

    /// <summary>Back into use; only under an active governorate (GEO-015).</summary>
    public void Reactivate(Governorate governorate)
    {
        ArgumentNullException.ThrowIfNull(governorate);
        LocationRules.RequireActiveParent(governorate.IsActive, "governorate");
        IsActive = true;
    }
}

/// <summary>A district (quarter, markaz) of a city.</summary>
public sealed class District : AggregateRoot
{
    public const int MaxNameLength = 100;

    private District()
    {
        Name = null!;
    }

    private District(Guid id, Guid cityId, LocalizedText name)
        : base(id)
    {
        CityId = cityId;
        Name = name;
        IsActive = true;
    }

    public Guid CityId { get; private set; }

    public LocalizedText Name { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static District Create(City city, string nameEn, string nameAr)
    {
        ArgumentNullException.ThrowIfNull(city);
        LocationRules.RequireActiveParent(city.IsActive, "city");
        return new District(Guid.CreateVersion7(), city.Id, LocationRules.Name(nameEn, nameAr, "The district name"));
    }

    public void Rename(string nameEn, string nameAr) => Name = LocationRules.Name(nameEn, nameAr, "The district name");

    /// <summary>Refused while an active area belongs to the district (GEO-112).</summary>
    public void Deactivate(bool hasActiveAreas)
    {
        LocationRules.RequireNoActiveChildren(hasActiveAreas, "district", "areas");
        IsActive = false;
    }

    public void Reactivate(City city)
    {
        ArgumentNullException.ThrowIfNull(city);
        LocationRules.RequireActiveParent(city.IsActive, "city");
        IsActive = true;
    }
}

/// <summary>An area (neighbourhood) of a district; the finest level of the hierarchy.</summary>
public sealed class Area : AggregateRoot
{
    public const int MaxNameLength = 100;

    private Area()
    {
        Name = null!;
    }

    private Area(Guid id, Guid districtId, LocalizedText name)
        : base(id)
    {
        DistrictId = districtId;
        Name = name;
        IsActive = true;
    }

    public Guid DistrictId { get; private set; }

    public LocalizedText Name { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static Area Create(District district, string nameEn, string nameAr)
    {
        ArgumentNullException.ThrowIfNull(district);
        LocationRules.RequireActiveParent(district.IsActive, "district");
        return new Area(Guid.CreateVersion7(), district.Id, LocationRules.Name(nameEn, nameAr, "The area name"));
    }

    public void Rename(string nameEn, string nameAr) => Name = LocationRules.Name(nameEn, nameAr, "The area name");

    public void Deactivate() => IsActive = false;

    public void Reactivate(District district)
    {
        ArgumentNullException.ThrowIfNull(district);
        LocationRules.RequireActiveParent(district.IsActive, "district");
        IsActive = true;
    }
}

/// <summary>The rules the three levels share.</summary>
public static class LocationRules
{
    public const int MaxNameLength = 100;

    public static LocalizedText Name(string? nameEn, string? nameAr, string field) =>
        DomainText.Localized(nameEn, nameAr, MaxNameLength, PlatformAdminCodes.GeographyTextInvalid, field);

    public static void RequireActiveParent(bool parentActive, string parent)
    {
        if (!parentActive)
        {
            throw new DomainException(PlatformAdminCodes.LocationParentInactive, $"The {parent} is inactive; reactivate it first.", DomainFault.Conflict);
        }
    }

    public static void RequireNoActiveChildren(bool hasActiveChildren, string level, string children)
    {
        if (hasActiveChildren)
        {
            throw new DomainException(PlatformAdminCodes.LocationHasActiveChildren, $"The {level} still has active {children}; deactivate them first.", DomainFault.Conflict);
        }
    }
}
