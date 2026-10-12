using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Account.Commands;
using Platform.Domain.Account;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Account.Queries;

/// <summary>The laboratory logo's bytes for the top bar (the controller streams and disposes them).</summary>
public sealed record LaboratoryLogoContent(Stream Content, string ContentType, int Version);

/// <summary>
/// The current laboratory's logo, for every signed-in member of the laboratory (it is shown in everyone's top bar). Own
/// laboratory only: the tenant comes from the session, the read is filtered by tenant and RLS. ACC-045 when none.
/// </summary>
[RequireAuthenticatedActor]
public sealed record GetLaboratoryLogoQuery : IQuery<LaboratoryLogoContent>;

public sealed class GetLaboratoryLogoHandler(ILaboratoryLogoRepository logos, IFileStore files, ICurrentTenant tenant)
    : IQueryHandler<GetLaboratoryLogoQuery, LaboratoryLogoContent>
{
    public async Task<LaboratoryLogoContent> Handle(GetLaboratoryLogoQuery query, CancellationToken cancellationToken)
    {
        LaboratoryScope.Require(tenant);
        var logo = await logos.FindActiveAsync(cancellationToken) ?? throw NotFound();
        var content = await files.OpenReadAsync(logo.StorageKey, cancellationToken) ?? throw NotFound();
        return new LaboratoryLogoContent(content, logo.ContentType, logo.Version);
    }

    private static DomainException NotFound() => new(AccountCodes.LogoNotFound, "The laboratory has no logo.", DomainFault.NotFound);
}
