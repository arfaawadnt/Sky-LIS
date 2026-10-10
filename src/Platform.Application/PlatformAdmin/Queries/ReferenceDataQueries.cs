using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Queries;

// Registers of the reference data: countries (SA-10), governorates (SA-11), notification templates (SA-20) and the
// integration library (SA-15…SA-18). Every list takes a search text, the active filter and a page.

public sealed class ReferenceListValidator<TQuery> : AbstractValidator<TQuery>
    where TQuery : IReferenceListQuery
{
    public ReferenceListValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

/// <summary>The common shape of the reference-data lists.</summary>
public interface IReferenceListQuery
{
    string? Search { get; }

    int? Page { get; }

    int? PageSize { get; }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record ListCountriesQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<CountryListItem>>, IPlatformAdministrationRequest, IReferenceListQuery;

public sealed class ListCountriesValidator : AbstractValidator<ListCountriesQuery>
{
    public ListCountriesValidator() => Include(new ReferenceListValidator<ListCountriesQuery>());
}

public sealed class ListCountriesHandler(ICountryRepository countries, ICurrentTenant currentTenant) : IQueryHandler<ListCountriesQuery, Paged<CountryListItem>>
{
    public Task<Paged<CountryListItem>> Handle(ListCountriesQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return countries.ListAsync(Trim(query.Search), query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }

    private static string? Trim(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record ListGovernoratesQuery(Guid? CountryId, string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<GovernorateListItem>>, IPlatformAdministrationRequest, IReferenceListQuery;

public sealed class ListGovernoratesValidator : AbstractValidator<ListGovernoratesQuery>
{
    public ListGovernoratesValidator() => Include(new ReferenceListValidator<ListGovernoratesQuery>());
}

public sealed class ListGovernoratesHandler(IGovernorateRepository governorates, ICurrentTenant currentTenant) : IQueryHandler<ListGovernoratesQuery, Paged<GovernorateListItem>>
{
    public Task<Paged<GovernorateListItem>> Handle(ListGovernoratesQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return governorates.ListAsync(query.CountryId, SearchText.Of(query.Search), query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Notifications, PermissionAction.View)]
public sealed record ListNotificationTemplatesQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<NotificationTemplateListItem>>, IPlatformAdministrationRequest, IReferenceListQuery;

public sealed class ListNotificationTemplatesValidator : AbstractValidator<ListNotificationTemplatesQuery>
{
    public ListNotificationTemplatesValidator() => Include(new ReferenceListValidator<ListNotificationTemplatesQuery>());
}

public sealed class ListNotificationTemplatesHandler(INotificationTemplateRepository templates, ICurrentTenant currentTenant) : IQueryHandler<ListNotificationTemplatesQuery, Paged<NotificationTemplateListItem>>
{
    public Task<Paged<NotificationTemplateListItem>> Handle(ListNotificationTemplatesQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return templates.ListAsync(SearchText.Of(query.Search), query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

/// <summary>The placeholder help of SA-20.</summary>
[RequirePermissionPolicy(PermissionCatalog.Notifications, PermissionAction.View)]
public sealed record GetTemplatePlaceholdersQuery : IQuery<TemplatePlaceholders>, IPlatformAdministrationRequest;

public sealed class GetTemplatePlaceholdersHandler(ICurrentTenant currentTenant) : IQueryHandler<GetTemplatePlaceholdersQuery, TemplatePlaceholders>
{
    public Task<TemplatePlaceholders> Handle(GetTemplatePlaceholdersQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return Task.FromResult(new TemplatePlaceholders(NotificationTemplate.Placeholders.Canonical, NotificationTemplate.Placeholders.Aliases));
    }
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListAnalyserModelsQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<AnalyserModelListItem>>, IPlatformAdministrationRequest, IReferenceListQuery;

public sealed class ListAnalyserModelsValidator : AbstractValidator<ListAnalyserModelsQuery>
{
    public ListAnalyserModelsValidator() => Include(new ReferenceListValidator<ListAnalyserModelsQuery>());
}

public sealed class ListAnalyserModelsHandler(IAnalyserModelRepository models, ICurrentTenant currentTenant) : IQueryHandler<ListAnalyserModelsQuery, Paged<AnalyserModelListItem>>
{
    public Task<Paged<AnalyserModelListItem>> Handle(ListAnalyserModelsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return models.ListAsync(SearchText.Of(query.Search), query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListPcrKitsQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<PcrKitListItem>>, IPlatformAdministrationRequest, IReferenceListQuery;

public sealed class ListPcrKitsValidator : AbstractValidator<ListPcrKitsQuery>
{
    public ListPcrKitsValidator() => Include(new ReferenceListValidator<ListPcrKitsQuery>());
}

public sealed class ListPcrKitsHandler(IPcrKitTemplateRepository kits, ICurrentTenant currentTenant) : IQueryHandler<ListPcrKitsQuery, Paged<PcrKitListItem>>
{
    public Task<Paged<PcrKitListItem>> Handle(ListPcrKitsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return kits.ListAsync(SearchText.Of(query.Search), query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListCodeSystemsQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<CodeSystemListItem>>, IPlatformAdministrationRequest, IReferenceListQuery;

public sealed class ListCodeSystemsValidator : AbstractValidator<ListCodeSystemsQuery>
{
    public ListCodeSystemsValidator() => Include(new ReferenceListValidator<ListCodeSystemsQuery>());
}

public sealed class ListCodeSystemsHandler(ICodeSystemRepository codeSystems, ICurrentTenant currentTenant) : IQueryHandler<ListCodeSystemsQuery, Paged<CodeSystemListItem>>
{
    public Task<Paged<CodeSystemListItem>> Handle(ListCodeSystemsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return codeSystems.ListAsync(SearchText.Of(query.Search), query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListCodeSystemItemsQuery(Guid CodeSystemId, string? Search, int? Page, int? PageSize) : IQuery<Paged<CodeSystemItemView>>, IPlatformAdministrationRequest, IReferenceListQuery;

public sealed class ListCodeSystemItemsValidator : AbstractValidator<ListCodeSystemItemsQuery>
{
    public ListCodeSystemItemsValidator()
    {
        RuleFor(query => query.CodeSystemId).NotEmpty();
        Include(new ReferenceListValidator<ListCodeSystemItemsQuery>());
    }
}

public sealed class ListCodeSystemItemsHandler(ICodeSystemRepository codeSystems, ICurrentTenant currentTenant) : IQueryHandler<ListCodeSystemItemsQuery, Paged<CodeSystemItemView>>
{
    public async Task<Paged<CodeSystemItemView>> Handle(ListCodeSystemItemsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        _ = await codeSystems.GetForUpdateAsync(query.CodeSystemId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CodeSystemNotFound, "Code system not found.", DomainFault.NotFound);
        return await codeSystems.ListItemsAsync(query.CodeSystemId, SearchText.Of(query.Search), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListRuleTemplatesQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<RuleTemplateListItem>>, IPlatformAdministrationRequest, IReferenceListQuery;

public sealed class ListRuleTemplatesValidator : AbstractValidator<ListRuleTemplatesQuery>
{
    public ListRuleTemplatesValidator() => Include(new ReferenceListValidator<ListRuleTemplatesQuery>());
}

public sealed class ListRuleTemplatesHandler(IRuleTemplateRepository rules, ICurrentTenant currentTenant) : IQueryHandler<ListRuleTemplatesQuery, Paged<RuleTemplateListItem>>
{
    public Task<Paged<RuleTemplateListItem>> Handle(ListRuleTemplatesQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return rules.ListAsync(SearchText.Of(query.Search), query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

internal static class SearchText
{
    public static string? Of(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}
