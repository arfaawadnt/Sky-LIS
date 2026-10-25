using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Queries;

// LIS-CR-005: the anonymous card and logo of a laboratory's sign-in address. A malformed address is not a 400 here: every
// miss (malformed, unknown, inactive, platform) is the same 404 PLT-404, so the card never tells them apart.

/// <summary>The card of a laboratory address: names and logo version; <c>PLT-404</c> unless a usable laboratory has the address.</summary>
[AllowUnauthenticated]
public sealed record GetPublicLaboratoryQuery(string Slug) : IQuery<PublicLaboratory>;

public sealed class GetPublicLaboratoryHandler(IPublicLaboratoryDirectory laboratories) : IQueryHandler<GetPublicLaboratoryQuery, PublicLaboratory>
{
    public async Task<PublicLaboratory> Handle(GetPublicLaboratoryQuery query, CancellationToken cancellationToken)
    {
        var record = await PublicLaboratories.FindAsync(laboratories, query.Slug, cancellationToken);
        return new PublicLaboratory(record.Slug, record.NameEn, record.NameAr, record.Logo?.Version, record.AddressEn, record.AddressAr);
    }
}

/// <summary>The bytes of the laboratory's active logo for its sign-in page (the controller streams and disposes them).</summary>
public sealed record PublicLaboratoryLogoContent(Stream Content, string ContentType, int Version);

/// <summary>The logo of a laboratory address; <c>PLT-404</c> when no usable laboratory has the address or it has no logo.</summary>
[AllowUnauthenticated]
public sealed record GetPublicLaboratoryLogoQuery(string Slug) : IQuery<PublicLaboratoryLogoContent>;

public sealed class GetPublicLaboratoryLogoHandler(IPublicLaboratoryDirectory laboratories, IFileStore files, ICurrentTenantSetter tenantScope)
    : IQueryHandler<GetPublicLaboratoryLogoQuery, PublicLaboratoryLogoContent>
{
    public async Task<PublicLaboratoryLogoContent> Handle(GetPublicLaboratoryLogoQuery query, CancellationToken cancellationToken)
    {
        var record = await PublicLaboratories.FindAsync(laboratories, query.Slug, cancellationToken);
        if (record.Logo is not { } logo)
        {
            throw PublicLaboratories.NotFound();
        }

        Stream? content;
        using (tenantScope.ForTenant(record.TenantId))   // the file store opens only the current tenant's keys
        {
            content = await files.OpenReadAsync(logo.StorageKey, cancellationToken);
        }

        return content is null ? throw PublicLaboratories.NotFound() : new PublicLaboratoryLogoContent(content, logo.ContentType, logo.Version);
    }
}

internal static class PublicLaboratories
{
    public static async Task<PublicLaboratoryRecord> FindAsync(IPublicLaboratoryDirectory laboratories, string? slug, CancellationToken cancellationToken)
    {
        if (!Tenant.IsAddressShape(slug))
        {
            throw NotFound();
        }

        return await laboratories.FindUsableAsync(Tenant.NormaliseAddress(slug), cancellationToken) ?? throw NotFound();
    }

    public static DomainException NotFound() =>
        new(PlatformAdminCodes.LaboratoryAddressNotFound, "No laboratory has this address.", DomainFault.NotFound);
}
