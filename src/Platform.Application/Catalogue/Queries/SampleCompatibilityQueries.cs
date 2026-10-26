using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue.Queries;

// Register of the compatibility groups of samples (change request LIS-CR-015, note M4). The search matches the group's code
// and names and the codes and names of its members' sample types and conditions.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListSampleCompatibilityGroupsQuery(string? Search, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<SampleCompatibilityGroupView>>;

public sealed class ListSampleCompatibilityGroupsValidator : AbstractValidator<ListSampleCompatibilityGroupsQuery>
{
    public ListSampleCompatibilityGroupsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(CatalogueSortKeys.SampleCompatibilityGroups);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListSampleCompatibilityGroupsHandler(ISampleCompatibilityGroupRepository groups) : IQueryHandler<ListSampleCompatibilityGroupsQuery, Paged<SampleCompatibilityGroupView>>
{
    public Task<Paged<SampleCompatibilityGroupView>> Handle(ListSampleCompatibilityGroupsQuery query, CancellationToken cancellationToken) =>
        groups.ListAsync(new CatalogueAListFilter(query.Search, query.IsActive, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportSampleCompatibilityGroupsQuery(string? Search, bool? IsActive) : IQuery<IReadOnlyList<SampleCompatibilityGroupView>>;

public sealed class ExportSampleCompatibilityGroupsValidator : AbstractValidator<ExportSampleCompatibilityGroupsQuery>
{
    public ExportSampleCompatibilityGroupsValidator() => RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
}

public sealed class ExportSampleCompatibilityGroupsHandler(ISampleCompatibilityGroupRepository groups) : IQueryHandler<ExportSampleCompatibilityGroupsQuery, IReadOnlyList<SampleCompatibilityGroupView>>
{
    public Task<IReadOnlyList<SampleCompatibilityGroupView>> Handle(ExportSampleCompatibilityGroupsQuery query, CancellationToken cancellationToken) =>
        groups.ExportAsync(new CatalogueAListFilter(query.Search, query.IsActive), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetSampleCompatibilityGroupQuery(Guid GroupId) : IQuery<SampleCompatibilityGroupView>;

public sealed class GetSampleCompatibilityGroupHandler(ISampleCompatibilityGroupRepository groups) : IQueryHandler<GetSampleCompatibilityGroupQuery, SampleCompatibilityGroupView>
{
    public async Task<SampleCompatibilityGroupView> Handle(GetSampleCompatibilityGroupQuery query, CancellationToken cancellationToken) =>
        await groups.GetViewAsync(query.GroupId, cancellationToken)
        ?? throw new DomainException(SampleCompatibilityCodes.NotFound, "Compatibility group not found.", DomainFault.NotFound);
}
