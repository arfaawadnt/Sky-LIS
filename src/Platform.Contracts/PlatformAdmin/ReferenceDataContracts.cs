namespace Platform.Contracts.PlatformAdmin;

// Countries (SA-10), governorates (SA-11), notification templates (SA-20) and the integration library (SA-15…SA-18).

public sealed record CountryListItem(
    Guid Id,
    string NameEn,
    string NameAr,
    string IsoCode,
    string DialCode,
    string Currency,
    string CurrencyCode,
    string Language,
    string TimeZone,
    int MobileLength,
    string? Flag,
    string? Remarks,
    bool IsActive,
    int GovernorateCount);

/// <summary>Create and edit a country; the ISO code is ignored on edit (natural key).</summary>
public sealed record CountryRequest(
    string NameEn,
    string NameAr,
    string IsoCode,
    string DialCode,
    string Currency,
    string CurrencyCode,
    string Language,
    string TimeZone,
    int MobileLength,
    string? Flag,
    string? Remarks);

public sealed record GovernorateListItem(
    Guid Id,
    Guid CountryId,
    string CountryNameEn,
    string CountryNameAr,
    string NameEn,
    string NameAr,
    string? District,
    string? Remarks,
    bool IsActive);

/// <summary>Create and edit a governorate; the country is ignored on edit (a governorate never moves).</summary>
public sealed record GovernorateRequest(Guid CountryId, string NameEn, string NameAr, string? District, string? Remarks);

/// <summary>Channels: Sms, Email.</summary>
public sealed record NotificationTemplateListItem(
    Guid Id,
    string Code,
    string Channel,
    string? SubjectEn,
    string? SubjectAr,
    string TextEn,
    string TextAr,
    bool IsActive);

/// <summary>Create and edit a template; the code is ignored on edit (natural key).</summary>
public sealed record NotificationTemplateRequest(string Code, string Channel, string TextEn, string TextAr, string? SubjectEn, string? SubjectAr);

/// <summary>The placeholders a template may use and the SRS short spellings accepted as aliases (<c>lab</c> → <c>labName</c>).</summary>
public sealed record TemplatePlaceholders(IReadOnlyList<string> Placeholders, IReadOnlyDictionary<string, string> Aliases);

/// <summary>Protocols: Astm, Hl7, Proprietary. Physical links: Serial, Tcp, File.</summary>
public sealed record AnalyserModelListItem(
    Guid Id,
    string Vendor,
    string Model,
    string Discipline,
    string Protocol,
    string PhysicalLink,
    IReadOnlyList<string> Modes,
    string? DriverVersion,
    bool IsActive);

public sealed record AnalyserModelRequest(string Vendor, string Model, string Discipline, string Protocol, string PhysicalLink, IReadOnlyList<string>? Modes, string? DriverVersion);

public sealed record PcrKitListItem(
    Guid Id,
    string Name,
    Guid? AnalyserModelId,
    string? AnalyserModelName,
    string LayoutJson,
    string ThresholdsJson,
    string VerdictRulesJson,
    bool IsActive);

public sealed record PcrKitRequest(string Name, Guid? AnalyserModelId, string LayoutJson, string ThresholdsJson, string VerdictRulesJson);

/// <summary>Names: Loinc, Cpt, Icd10, Snomed.</summary>
public sealed record CodeSystemListItem(
    Guid Id,
    string Name,
    string Version,
    string? Use,
    int ItemCount,
    DateTimeOffset? LastImportedAtUtc,
    bool IsActive);

/// <summary>Create and edit a code system; the name is ignored on edit.</summary>
public sealed record CodeSystemRequest(string Name, string Version, string? Use);

public sealed record CodeSystemItemView(string Code, string Display);

public sealed record CodeSystemImportResult(int Inserted, int Updated, int Unchanged, int Total);

public sealed record RuleTemplateListItem(Guid Id, string Name, string? ClinicalGroupName, string Formula, string? Description, bool IsActive);

public sealed record RuleTemplateRequest(string Name, string? ClinicalGroupName, string Formula, string? Description);

// Location hierarchy under a governorate (change request 2026-10-19, A2 — SA-11b "Locations"): cities, districts, areas.

/// <summary>A city, district or area row of the Locations drawer; <paramref name="ParentId"/> is the governorate, the city or the district.</summary>
public sealed record LocationListItem(Guid Id, Guid ParentId, string NameEn, string NameAr, bool IsActive);

/// <summary>Create and rename a city, district or area; the parent is fixed once created.</summary>
public sealed record LocationRequest(string NameEn, string NameAr);

/// <summary>Active cities of a governorate (<c>GET /api/v1/public/cities?governorateId=</c>; every governorate when unfiltered).</summary>
public sealed record PublicCity(Guid Id, Guid GovernorateId, string NameEn, string NameAr);

/// <summary>Active districts of a city (<c>GET /api/v1/public/districts?cityId=</c>).</summary>
public sealed record PublicDistrict(Guid Id, Guid CityId, string NameEn, string NameAr);

/// <summary>Active areas of a district (<c>GET /api/v1/public/areas?districtId=</c>).</summary>
public sealed record PublicArea(Guid Id, Guid DistrictId, string NameEn, string NameAr);
