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
