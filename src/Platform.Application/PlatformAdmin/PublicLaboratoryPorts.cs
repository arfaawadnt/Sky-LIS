namespace Platform.Application.PlatformAdmin;

/// <summary>LIS-CR-005: a usable laboratory found by its address, with its active logo (if any).</summary>
public sealed record PublicLaboratoryRecord(Guid TenantId, string Slug, string NameEn, string NameAr, PublicLaboratoryLogo? Logo);

/// <summary>Where the laboratory's active logo is stored (the file store's key in the laboratory's own area) and its version.</summary>
public sealed record PublicLaboratoryLogo(string StorageKey, string ContentType, int Version);

/// <summary>
/// LIS-CR-005: the anonymous read behind a laboratory's sign-in page. <see cref="FindUsableAsync"/> answers only a client
/// laboratory whose users may sign in now (the statuses sign-in lets through: Active, or Trial before its end — INV-PLT-003);
/// <see langword="null"/> for an unknown address, an inactive laboratory and the platform tenant. The client record is a platform
/// table (no RLS); the logo row is read in the laboratory's own tenant scope (no elevation).
/// </summary>
public interface IPublicLaboratoryDirectory
{
    Task<PublicLaboratoryRecord?> FindUsableAsync(string slug, CancellationToken cancellationToken);
}
