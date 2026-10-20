namespace Platform.Contracts.PlatformAdmin;

/// <summary>
/// LIS-CR-005: what the sign-in page of a laboratory address shows before anyone signs in (anonymous,
/// <c>GET /api/v1/public/laboratories/{slug}</c>): the address, the laboratory's names and the version of its logo (null when it
/// has none; the logo is <c>GET /api/v1/public/laboratories/{slug}/logo</c>). Nothing else about a tenant is public.
/// </summary>
public sealed record PublicLaboratory(string Slug, string NameEn, string NameAr, int? LogoVersion);
