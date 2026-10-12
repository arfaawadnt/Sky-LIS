using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration.Queries;

// PCR kits (CP-62): register, detail and the simulation (paste a run file, see the verdicts; nothing is stored).

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListPcrKitsQuery(string? Search, bool? IsActive, Guid? AnalyserId, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<PcrKitListItem>>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record GetPcrKitQuery(Guid KitId) : IQuery<PcrKitDetail>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record SimulatePcrRunQuery(Guid KitId, string? InstrumentSerial, string Content) : IQuery<PcrRunView>;

public sealed class ListPcrKitsValidator : AbstractValidator<ListPcrKitsQuery>
{
    public ListPcrKitsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(IntegrationSortKeys.PcrKits);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class SimulatePcrRunValidator : AbstractValidator<SimulatePcrRunQuery>
{
    public SimulatePcrRunValidator()
    {
        RuleFor(query => query.KitId).NotEmpty();
        RuleFor(query => query.InstrumentSerial).MaximumLength(PcrKit.MaxSerialLength);
        RuleFor(query => query.Content).NotEmpty().MaximumLength(PcrInterpreter.MaxContentLength);
    }
}

public sealed class ListPcrKitsHandler(IPcrKitRepository kits) : IQueryHandler<ListPcrKitsQuery, Paged<PcrKitListItem>>
{
    public Task<Paged<PcrKitListItem>> Handle(ListPcrKitsQuery query, CancellationToken cancellationToken) =>
        kits.ListAsync(new PcrKitFilter(string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), query.IsActive, query.AnalyserId, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

public sealed class GetPcrKitHandler(IPcrKitRepository kits) : IQueryHandler<GetPcrKitQuery, PcrKitDetail>
{
    public async Task<PcrKitDetail> Handle(GetPcrKitQuery query, CancellationToken cancellationToken)
    {
        var kit = await kits.GetForUpdateAsync(query.KitId, cancellationToken)
            ?? throw new DomainException(IntegrationCodes.KitNotFound, "PCR kit not found.", DomainFault.NotFound);
        return IntegrationMapping.ToDetail(kit);
    }
}

public sealed class SimulatePcrRunHandler(IPcrKitRepository kits) : IQueryHandler<SimulatePcrRunQuery, PcrRunView>
{
    public async Task<PcrRunView> Handle(SimulatePcrRunQuery query, CancellationToken cancellationToken)
    {
        var kit = await kits.GetForUpdateAsync(query.KitId, cancellationToken)
            ?? throw new DomainException(IntegrationCodes.KitNotFound, "PCR kit not found.", DomainFault.NotFound);
        return IntegrationMapping.ToView(kit.Interpret(query.InstrumentSerial, query.Content));
    }
}
