using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// A governorate (province) of a country, with an optional district (SA-11). Platform reference data
/// (<c>saas.governorates</c>); the English name is unique within its country (INV-PLT-012). A client's governorate
/// must belong to the client's country (<c>GEO-013</c>).
/// </summary>
public sealed class Governorate : AggregateRoot
{
    public const int MaxNameLength = 100;
    public const int MaxDistrictLength = 100;
    public const int MaxRemarksLength = 500;

    private Governorate()
    {
        Name = null!;
    }

    private Governorate(Guid id, Guid countryId, LocalizedText name, string? district, string? remarks)
        : base(id)
    {
        CountryId = countryId;
        Name = name;
        District = district;
        Remarks = remarks;
        IsActive = true;
    }

    public Guid CountryId { get; private set; }

    public LocalizedText Name { get; private set; }

    public string? District { get; private set; }

    public string? Remarks { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static Governorate Create(Country country, string nameEn, string nameAr, string? district, string? remarks)
    {
        ArgumentNullException.ThrowIfNull(country);
        Require(country.IsActive, PlatformAdminCodes.CountryInactive, "A governorate cannot be added to an inactive country.", DomainFault.Conflict);
        return new Governorate(
            Guid.CreateVersion7(),
            country.Id,
            DomainText.Localized(nameEn, nameAr, MaxNameLength, PlatformAdminCodes.GeographyTextInvalid, "The governorate name"),
            DomainText.Optional(district, MaxDistrictLength, PlatformAdminCodes.GeographyTextInvalid, "The district"),
            DomainText.Optional(remarks, MaxRemarksLength, PlatformAdminCodes.GeographyTextInvalid, "The remarks"));
    }

    /// <summary>Edits the names, district and remarks; the country is fixed once created (a governorate never moves).</summary>
    public void Update(string nameEn, string nameAr, string? district, string? remarks)
    {
        Name = DomainText.Localized(nameEn, nameAr, MaxNameLength, PlatformAdminCodes.GeographyTextInvalid, "The governorate name");
        District = DomainText.Optional(district, MaxDistrictLength, PlatformAdminCodes.GeographyTextInvalid, "The district");
        Remarks = DomainText.Optional(remarks, MaxRemarksLength, PlatformAdminCodes.GeographyTextInvalid, "The remarks");
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
