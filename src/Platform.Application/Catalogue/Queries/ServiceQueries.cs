using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue.Services;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue.Queries;

// Registers of services (CP-40, CP-42, CP-55) and tests (CP-41), the formula check (T20) and the lookups other
// modules use. Enum filters travel as names and are validated here (VALIDATION-400 on an unknown name).

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListServicesQuery(string? Search, string? Type, Guid? ClinicalGroupId, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<ServiceListItem>>;

public sealed class ListServicesValidator : AbstractValidator<ListServicesQuery>
{
    public ListServicesValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Type).IsEnumName(typeof(ServiceType), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Type));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListServicesHandler(IServiceRepository services) : IQueryHandler<ListServicesQuery, Paged<ServiceListItem>>
{
    public Task<Paged<ServiceListItem>> Handle(ListServicesQuery query, CancellationToken cancellationToken) =>
        services.ListAsync(new ServiceListFilter(query.Search, CatalogueAEnums.ServiceTypeOrNull(query.Type), query.ClinicalGroupId, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportServicesQuery(string? Search, string? Type, Guid? ClinicalGroupId, bool? IsActive) : IQuery<IReadOnlyList<ServiceListItem>>;

public sealed class ExportServicesValidator : AbstractValidator<ExportServicesQuery>
{
    public ExportServicesValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Type).IsEnumName(typeof(ServiceType), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Type));
    }
}

public sealed class ExportServicesHandler(IServiceRepository services) : IQueryHandler<ExportServicesQuery, IReadOnlyList<ServiceListItem>>
{
    public Task<IReadOnlyList<ServiceListItem>> Handle(ExportServicesQuery query, CancellationToken cancellationToken) =>
        services.ExportAsync(new ServiceListFilter(query.Search, CatalogueAEnums.ServiceTypeOrNull(query.Type), query.ClinicalGroupId, query.IsActive), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetServiceQuery(Guid ServiceId) : IQuery<ServiceDetail>;

public sealed class GetServiceHandler(IServiceRepository services) : IQueryHandler<GetServiceQuery, ServiceDetail>
{
    public async Task<ServiceDetail> Handle(GetServiceQuery query, CancellationToken cancellationToken) =>
        await services.GetDetailAsync(query.ServiceId, cancellationToken)
        ?? throw new DomainException(ServiceCodes.NotFound, "Service not found.", DomainFault.NotFound);
}

/// <summary>CP-42: every service (filtered) in sequence order, for the inline editor.</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListServiceSequenceQuery(string? Search, string? Type) : IQuery<IReadOnlyList<ServiceSequenceRow>>;

public sealed class ListServiceSequenceValidator : AbstractValidator<ListServiceSequenceQuery>
{
    public ListServiceSequenceValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Type).IsEnumName(typeof(ServiceType), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Type));
    }
}

public sealed class ListServiceSequenceHandler(IServiceRepository services) : IQueryHandler<ListServiceSequenceQuery, IReadOnlyList<ServiceSequenceRow>>
{
    public Task<IReadOnlyList<ServiceSequenceRow>> Handle(ListServiceSequenceQuery query, CancellationToken cancellationToken) =>
        services.SequenceRowsAsync(query.Search, CatalogueAEnums.ServiceTypeOrNull(query.Type), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListLabTestsQuery(string? Search, string? Type, Guid? ClinicalGroupId, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<LabTestListItem>>;

public sealed class ListLabTestsValidator : AbstractValidator<ListLabTestsQuery>
{
    public ListLabTestsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Type).IsEnumName(typeof(LabTestType), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Type));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListLabTestsHandler(ILabTestRepository tests) : IQueryHandler<ListLabTestsQuery, Paged<LabTestListItem>>
{
    public Task<Paged<LabTestListItem>> Handle(ListLabTestsQuery query, CancellationToken cancellationToken) =>
        tests.ListAsync(new LabTestListFilter(query.Search, CatalogueAEnums.TestTypeOrNull(query.Type), query.ClinicalGroupId, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportLabTestsQuery(string? Search, string? Type, Guid? ClinicalGroupId, bool? IsActive) : IQuery<IReadOnlyList<LabTestListItem>>;

public sealed class ExportLabTestsValidator : AbstractValidator<ExportLabTestsQuery>
{
    public ExportLabTestsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Type).IsEnumName(typeof(LabTestType), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Type));
    }
}

public sealed class ExportLabTestsHandler(ILabTestRepository tests) : IQueryHandler<ExportLabTestsQuery, IReadOnlyList<LabTestListItem>>
{
    public Task<IReadOnlyList<LabTestListItem>> Handle(ExportLabTestsQuery query, CancellationToken cancellationToken) =>
        tests.ExportAsync(new LabTestListFilter(query.Search, CatalogueAEnums.TestTypeOrNull(query.Type), query.ClinicalGroupId, query.IsActive), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetLabTestQuery(Guid TestId) : IQuery<LabTestDetail>;

public sealed class GetLabTestHandler(ILabTestRepository tests) : IQueryHandler<GetLabTestQuery, LabTestDetail>
{
    public async Task<LabTestDetail> Handle(GetLabTestQuery query, CancellationToken cancellationToken) =>
        await tests.GetDetailAsync(query.TestId, cancellationToken)
        ?? throw new DomainException(LabTestCodes.NotFound, "Test not found.", DomainFault.NotFound);
}

/// <summary>T20 "Check formula": the verdict the save would give, never a refusal.</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record CheckFormulaQuery(string Expression, Guid? TestId) : IQuery<FormulaCheckResult>;

public sealed class CheckFormulaValidator : AbstractValidator<CheckFormulaQuery>
{
    public CheckFormulaValidator() => RuleFor(query => query.Expression).NotNull().MaximumLength(LabTest.MaxFormulaLength);
}

public sealed class CheckFormulaHandler(ILabTestRepository tests, IFormulaEvaluator evaluator) : IQueryHandler<CheckFormulaQuery, FormulaCheckResult>
{
    public async Task<FormulaCheckResult> Handle(CheckFormulaQuery query, CancellationToken cancellationToken)
    {
        var verdict = await LabTestFormulaRules.CheckAsync(evaluator, tests, query.Expression, query.TestId, cancellationToken);
        return new FormulaCheckResult(verdict.Valid, verdict.Normalized, verdict.References, verdict.Code, verdict.Message, verdict.Position);
    }
}

/// <summary>The service picker of every later module (registration, pricing, rules): active services, ≤ 50 per page.</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record LookupServicesQuery(string? Search, string? Type, bool? Orderable, int? Page, int? PageSize) : IQuery<Paged<ServiceLookupItem>>;

public sealed class LookupServicesValidator : AbstractValidator<LookupServicesQuery>
{
    public LookupServicesValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Type).IsEnumName(typeof(ServiceType), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Type));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, CatalogueALimits.MaxLookupPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class LookupServicesHandler(IServiceRepository services) : IQueryHandler<LookupServicesQuery, Paged<ServiceLookupItem>>
{
    public Task<Paged<ServiceLookupItem>> Handle(LookupServicesQuery query, CancellationToken cancellationToken) =>
        services.LookupAsync(query.Search, CatalogueAEnums.ServiceTypeOrNull(query.Type), query.Orderable, CatalogueAEnums.LookupPage(query.Page, query.PageSize), cancellationToken);
}

/// <summary>The test picker of every later module: active tests, ≤ 50 per page.</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record LookupTestsQuery(string? Search, string? Type, int? Page, int? PageSize) : IQuery<Paged<TestLookupItem>>;

public sealed class LookupTestsValidator : AbstractValidator<LookupTestsQuery>
{
    public LookupTestsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(CatalogueAPaging.MaxSearchLength);
        RuleFor(query => query.Type).IsEnumName(typeof(LabTestType), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Type));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, CatalogueALimits.MaxLookupPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class LookupTestsHandler(ILabTestRepository tests) : IQueryHandler<LookupTestsQuery, Paged<TestLookupItem>>
{
    public Task<Paged<TestLookupItem>> Handle(LookupTestsQuery query, CancellationToken cancellationToken) =>
        tests.LookupAsync(query.Search, CatalogueAEnums.TestTypeOrNull(query.Type), CatalogueAEnums.LookupPage(query.Page, query.PageSize), cancellationToken);
}

internal static class CatalogueAEnums
{
    public static ServiceType? ServiceTypeOrNull(string? value) => string.IsNullOrEmpty(value) ? null : Enum.Parse<ServiceType>(value, ignoreCase: true);

    public static LabTestType? TestTypeOrNull(string? value) => string.IsNullOrEmpty(value) ? null : Enum.Parse<LabTestType>(value, ignoreCase: true);

    /// <summary>Lookups default to 20 rows and never exceed <see cref="CatalogueALimits.MaxLookupPageSize"/>.</summary>
    public static PageRequest LookupPage(int? page, int? pageSize) =>
        PageRequest.Normalized(page, Math.Min(pageSize ?? 20, CatalogueALimits.MaxLookupPageSize));
}
