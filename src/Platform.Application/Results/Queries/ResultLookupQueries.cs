using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Results;
using Platform.Domain.Authorization;

namespace Platform.Application.Results.Queries;

// Lists the result screens need while entering (CP-19 "insert comment from library", "culture entry") and filtering
// (analysers). Served under results.view so that a technician needs no catalogue or integration permission.

/// <summary>The comment library (CP-47) for the <c>%</c> search: short name or text contains <paramref name="Search"/>; comments of the group or of every group.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetCommentTemplatesQuery(string? Search, Guid? ClinicalGroupId) : IQuery<IReadOnlyList<CommentTemplateOption>>;

public sealed class GetCommentTemplatesValidator : AbstractValidator<GetCommentTemplatesQuery>
{
    public GetCommentTemplatesValidator() => RuleFor(query => query.Search).MaximumLength(100);
}

public sealed class GetCommentTemplatesHandler(IResultReferenceLookup references) : IQueryHandler<GetCommentTemplatesQuery, IReadOnlyList<CommentTemplateOption>>
{
    public const int MaxRows = 50;

    public Task<IReadOnlyList<CommentTemplateOption>> Handle(GetCommentTemplatesQuery query, CancellationToken cancellationToken)
    {
        var search = (query.Search ?? string.Empty).Trim().TrimStart('%').Trim();
        return references.CommentTemplatesAsync(search.Length == 0 ? null : search, query.ClinicalGroupId, MaxRows, cancellationToken);
    }
}

/// <summary>The active organisms, antibiotics and sensitivity values (CP-56) for culture entry.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetMicrobiologyLookupsQuery : IQuery<MicrobiologyLookups>;

public sealed class GetMicrobiologyLookupsHandler(IResultReferenceLookup references) : IQueryHandler<GetMicrobiologyLookupsQuery, MicrobiologyLookups>
{
    public Task<MicrobiologyLookups> Handle(GetMicrobiologyLookupsQuery query, CancellationToken cancellationToken) => references.MicrobiologyAsync(cancellationToken);
}

/// <summary>The analysers of the laboratory, for the worklist and work-queue analyser filters.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetResultAnalysersQuery : IQuery<IReadOnlyList<AnalyserOption>>;

public sealed class GetResultAnalysersHandler(IResultAnalyserDirectory analysers) : IQueryHandler<GetResultAnalysersQuery, IReadOnlyList<AnalyserOption>>
{
    public Task<IReadOnlyList<AnalyserOption>> Handle(GetResultAnalysersQuery query, CancellationToken cancellationToken) => analysers.ListAsync(cancellationToken);
}

/// <summary>Active clinical groups for the worklist filter.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetResultClinicalGroupsQuery : IQuery<IReadOnlyList<LookupOption>>;

public sealed class GetResultClinicalGroupsHandler(IResultReferenceLookup references) : IQueryHandler<GetResultClinicalGroupsQuery, IReadOnlyList<LookupOption>>
{
    public Task<IReadOnlyList<LookupOption>> Handle(GetResultClinicalGroupsQuery query, CancellationToken cancellationToken) => references.ClinicalGroupsAsync(cancellationToken);
}

/// <summary>Active tests (of a group) for the worklist filter, by code or name.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetResultTestsQuery(Guid? ClinicalGroupId, string? Search) : IQuery<IReadOnlyList<LookupOption>>;

public sealed class GetResultTestsValidator : AbstractValidator<GetResultTestsQuery>
{
    public GetResultTestsValidator() => RuleFor(query => query.Search).MaximumLength(100);
}

public sealed class GetResultTestsHandler(IResultReferenceLookup references) : IQueryHandler<GetResultTestsQuery, IReadOnlyList<LookupOption>>
{
    public const int MaxRows = 200;

    public Task<IReadOnlyList<LookupOption>> Handle(GetResultTestsQuery query, CancellationToken cancellationToken) =>
        references.TestsAsync(query.ClinicalGroupId, string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), MaxRows, cancellationToken);
}

/// <summary>Active payers for the worklist filter.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetResultPayersQuery : IQuery<IReadOnlyList<LookupOption>>;

public sealed class GetResultPayersHandler(IResultReferenceLookup references) : IQueryHandler<GetResultPayersQuery, IReadOnlyList<LookupOption>>
{
    public Task<IReadOnlyList<LookupOption>> Handle(GetResultPayersQuery query, CancellationToken cancellationToken) => references.ActivePayersAsync(cancellationToken);
}
