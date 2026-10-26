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

// Registers of catalogue half A (intake LIS-INT-CAT-001 §14): clinical groups, sample types, units, patient
// conditions. Lists are paged; exports return the whole filtered list (≤ CatalogueALimits.MaxExportRows) under
// catalogue.export; the SPA writes the CSV.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListClinicalGroupsQuery(string? Search, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<ClinicalGroupListItem>>;

public sealed class ListClinicalGroupsValidator : AbstractValidator<ListClinicalGroupsQuery>
{
    public ListClinicalGroupsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(CatalogueSortKeys.ClinicalGroups);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListClinicalGroupsHandler(IClinicalGroupRepository groups) : IQueryHandler<ListClinicalGroupsQuery, Paged<ClinicalGroupListItem>>
{
    public Task<Paged<ClinicalGroupListItem>> Handle(ListClinicalGroupsQuery query, CancellationToken cancellationToken) =>
        groups.ListAsync(new CatalogueAListFilter(query.Search, query.IsActive, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportClinicalGroupsQuery(string? Search, bool? IsActive) : IQuery<IReadOnlyList<ClinicalGroupListItem>>;

public sealed class ExportClinicalGroupsHandler(IClinicalGroupRepository groups) : IQueryHandler<ExportClinicalGroupsQuery, IReadOnlyList<ClinicalGroupListItem>>
{
    public Task<IReadOnlyList<ClinicalGroupListItem>> Handle(ExportClinicalGroupsQuery query, CancellationToken cancellationToken) =>
        groups.ExportAsync(new CatalogueAListFilter(query.Search, query.IsActive), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
/// <summary><paramref name="ForCulture"/> (LIS-CR-007): true = the culture specimens only (Microbiology tab), false = the others, null = all.</summary>
public sealed record ListSampleTypesQuery(string? Search, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null, bool? ForCulture = null) : IQuery<Paged<SampleTypeListItem>>;

public sealed class ListSampleTypesValidator : AbstractValidator<ListSampleTypesQuery>
{
    public ListSampleTypesValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(CatalogueSortKeys.SampleTypes);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListSampleTypesHandler(ISampleTypeRepository sampleTypes) : IQueryHandler<ListSampleTypesQuery, Paged<SampleTypeListItem>>
{
    public Task<Paged<SampleTypeListItem>> Handle(ListSampleTypesQuery query, CancellationToken cancellationToken) =>
        sampleTypes.ListAsync(new SampleTypeListFilter(query.Search, query.IsActive, query.ForCulture, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportSampleTypesQuery(string? Search, bool? IsActive, bool? ForCulture = null) : IQuery<IReadOnlyList<SampleTypeListItem>>;

public sealed class ExportSampleTypesHandler(ISampleTypeRepository sampleTypes) : IQueryHandler<ExportSampleTypesQuery, IReadOnlyList<SampleTypeListItem>>
{
    public Task<IReadOnlyList<SampleTypeListItem>> Handle(ExportSampleTypesQuery query, CancellationToken cancellationToken) =>
        sampleTypes.ExportAsync(new SampleTypeListFilter(query.Search, query.IsActive, query.ForCulture), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetSampleTypeQuery(Guid SampleTypeId) : IQuery<SampleTypeDetail>;

public sealed class GetSampleTypeHandler(ISampleTypeRepository sampleTypes) : IQueryHandler<GetSampleTypeQuery, SampleTypeDetail>
{
    public async Task<SampleTypeDetail> Handle(GetSampleTypeQuery query, CancellationToken cancellationToken) =>
        await sampleTypes.GetDetailAsync(query.SampleTypeId, cancellationToken)
        ?? throw new DomainException(SampleTypeCodes.NotFound, "Sample type not found.", DomainFault.NotFound);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListMeasurementUnitsQuery(string? Search, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<MeasurementUnitListItem>>;

public sealed class ListMeasurementUnitsValidator : AbstractValidator<ListMeasurementUnitsQuery>
{
    public ListMeasurementUnitsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(CatalogueSortKeys.Units);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListMeasurementUnitsHandler(IMeasurementUnitRepository units) : IQueryHandler<ListMeasurementUnitsQuery, Paged<MeasurementUnitListItem>>
{
    public Task<Paged<MeasurementUnitListItem>> Handle(ListMeasurementUnitsQuery query, CancellationToken cancellationToken) =>
        units.ListAsync(new CatalogueAListFilter(query.Search, query.IsActive, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportMeasurementUnitsQuery(string? Search, bool? IsActive) : IQuery<IReadOnlyList<MeasurementUnitListItem>>;

public sealed class ExportMeasurementUnitsHandler(IMeasurementUnitRepository units) : IQueryHandler<ExportMeasurementUnitsQuery, IReadOnlyList<MeasurementUnitListItem>>
{
    public Task<IReadOnlyList<MeasurementUnitListItem>> Handle(ExportMeasurementUnitsQuery query, CancellationToken cancellationToken) =>
        units.ExportAsync(new CatalogueAListFilter(query.Search, query.IsActive), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListPatientConditionsQuery(string? Search, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<PatientConditionListItem>>;

public sealed class ListPatientConditionsValidator : AbstractValidator<ListPatientConditionsQuery>
{
    public ListPatientConditionsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(CatalogueSortKeys.PatientConditions);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListPatientConditionsHandler(IPatientConditionRepository conditions) : IQueryHandler<ListPatientConditionsQuery, Paged<PatientConditionListItem>>
{
    public Task<Paged<PatientConditionListItem>> Handle(ListPatientConditionsQuery query, CancellationToken cancellationToken) =>
        conditions.ListAsync(new CatalogueAListFilter(query.Search, query.IsActive, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportPatientConditionsQuery(string? Search, bool? IsActive) : IQuery<IReadOnlyList<PatientConditionListItem>>;

public sealed class ExportPatientConditionsHandler(IPatientConditionRepository conditions) : IQueryHandler<ExportPatientConditionsQuery, IReadOnlyList<PatientConditionListItem>>
{
    public Task<IReadOnlyList<PatientConditionListItem>> Handle(ExportPatientConditionsQuery query, CancellationToken cancellationToken) =>
        conditions.ExportAsync(new CatalogueAListFilter(query.Search, query.IsActive), cancellationToken);
}

/// <summary>Search bounds of the catalogue-A registers.</summary>
public static class CatalogueAPaging
{
    public const int MaxSearchLength = 100;
}
