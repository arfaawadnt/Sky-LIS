using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration.Queries;

// Mappings (CP-59), the analyser code sheet document and the sample codes (CP-60).

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListAnalyserMappingsQuery(Guid AnalyserId, string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<MappingListItem>>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Export)]
public sealed record GetAnalyserCodeSheetQuery(Guid AnalyserId) : IQuery<AnalyserCodeSheetDocument>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListAnalyserSampleCodesQuery(Guid AnalyserId, bool? ActiveOnly) : IQuery<IReadOnlyList<SampleCodeItem>>;

public sealed class ListAnalyserMappingsValidator : AbstractValidator<ListAnalyserMappingsQuery>
{
    public ListAnalyserMappingsValidator()
    {
        RuleFor(query => query.AnalyserId).NotEmpty();
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

internal static class MappingNames
{
    /// <summary>Fills test code/name and unit code from the catalogue (an id the catalogue no longer knows stays blank).</summary>
    public static async Task<IReadOnlyList<MappingListItem>> FillAsync(IReadOnlyList<MappingListItem> items, IIntegrationReferenceLookup lookup, CancellationToken cancellationToken)
    {
        var tests = await IntegrationReferences.NamesAsync(lookup, ReferenceKind.Test, items.Select(item => (Guid?)item.TestId), cancellationToken);
        var units = await IntegrationReferences.NamesAsync(lookup, ReferenceKind.Unit, items.Select(item => item.UnitId), cancellationToken);
        return items.Select(item =>
        {
            tests.TryGetValue(item.TestId, out var test);
            ReferenceName? unit = null;
            if (item.UnitId is { } unitId)
            {
                units.TryGetValue(unitId, out unit);
            }

            return item with { TestCode = test?.Code, TestNameEn = test?.NameEn, TestNameAr = test?.NameAr, UnitCode = unit?.Code };
        }).ToList();
    }
}

public sealed class ListAnalyserMappingsHandler(IAnalyserRepository analysers, IAnalyserTestMappingRepository mappings, IIntegrationReferenceLookup lookup) : IQueryHandler<ListAnalyserMappingsQuery, Paged<MappingListItem>>
{
    public async Task<Paged<MappingListItem>> Handle(ListAnalyserMappingsQuery query, CancellationToken cancellationToken)
    {
        _ = await analysers.GetDetailAsync(query.AnalyserId, cancellationToken)
            ?? throw new DomainException(IntegrationCodes.AnalyserNotFound, "Analyser not found.", DomainFault.NotFound);
        var page = await mappings.ListAsync(query.AnalyserId, string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
        return page with { Items = await MappingNames.FillAsync(page.Items, lookup, cancellationToken) };
    }
}

/// <summary>The printable code sheet (SRS documents): the analyser and every active mapping and sample code; rendered by the SPA's print kit.</summary>
public sealed class GetAnalyserCodeSheetHandler(
    IAnalyserRepository analysers,
    IAnalyserTestMappingRepository mappings,
    IAnalyserSampleCodeRepository sampleCodes,
    ISiteAgentRepository agents,
    IIntegrationReferenceLookup lookup,
    ICurrentUser user,
    IClock clock) : IQueryHandler<GetAnalyserCodeSheetQuery, AnalyserCodeSheetDocument>
{
    public async Task<AnalyserCodeSheetDocument> Handle(GetAnalyserCodeSheetQuery query, CancellationToken cancellationToken)
    {
        var analyser = await analysers.GetDetailAsync(query.AnalyserId, cancellationToken)
            ?? throw new DomainException(IntegrationCodes.AnalyserNotFound, "Analyser not found.", DomainFault.NotFound);
        var lines = await MappingNames.FillAsync(await mappings.ListActiveAsync([analyser.Id], cancellationToken), lookup, cancellationToken);
        var codes = await SampleCodeNames.FillAsync(await sampleCodes.ListAsync([analyser.Id], activeOnly: true, cancellationToken), lookup, cancellationToken);
        string? agentName = null;
        if (analyser.SiteAgentId is { } agentId)
        {
            (await agents.NamesAsync([agentId], cancellationToken)).TryGetValue(agentId, out agentName);
        }

        return new AnalyserCodeSheetDocument(
            analyser.Id,
            analyser.Code,
            analyser.NameEn,
            analyser.NameAr,
            analyser.Mode,
            IntegrationMapping.Summary(IntegrationMapping.ToConnection(analyser.Connection)),
            agentName,
            lines.OrderBy(line => line.TestCode ?? line.AnalyserCode, StringComparer.OrdinalIgnoreCase)
                .Select(line => new CodeSheetLine(line.AnalyserCode, line.HostCode, line.TestCode, line.TestNameEn, line.TestNameAr, line.UnitCode, line.ResultSelection, line.Slope, line.Intercept, line.AutoSend))
                .ToList(),
            codes.OrderBy(code => code.SampleTypeCode ?? code.Code, StringComparer.OrdinalIgnoreCase)
                .Select(code => new CodeSheetSampleLine(code.Code, code.SampleTypeCode, code.SampleTypeNameEn, code.SampleTypeNameAr))
                .ToList(),
            clock.UtcNow,
            user.DisplayName);
    }
}

internal static class SampleCodeNames
{
    public static async Task<IReadOnlyList<SampleCodeItem>> FillAsync(IReadOnlyList<SampleCodeItem> items, IIntegrationReferenceLookup lookup, CancellationToken cancellationToken)
    {
        var types = await IntegrationReferences.NamesAsync(lookup, ReferenceKind.SampleType, items.Select(item => (Guid?)item.SampleTypeId), cancellationToken);
        return items.Select(item =>
        {
            types.TryGetValue(item.SampleTypeId, out var type);
            return item with { SampleTypeCode = type?.Code, SampleTypeNameEn = type?.NameEn, SampleTypeNameAr = type?.NameAr };
        }).ToList();
    }
}

public sealed class ListAnalyserSampleCodesHandler(IAnalyserRepository analysers, IAnalyserSampleCodeRepository codes, IIntegrationReferenceLookup lookup) : IQueryHandler<ListAnalyserSampleCodesQuery, IReadOnlyList<SampleCodeItem>>
{
    public async Task<IReadOnlyList<SampleCodeItem>> Handle(ListAnalyserSampleCodesQuery query, CancellationToken cancellationToken)
    {
        _ = await analysers.GetDetailAsync(query.AnalyserId, cancellationToken)
            ?? throw new DomainException(IntegrationCodes.AnalyserNotFound, "Analyser not found.", DomainFault.NotFound);
        return await SampleCodeNames.FillAsync(await codes.ListAsync([query.AnalyserId], query.ActiveOnly ?? false, cancellationToken), lookup, cancellationToken);
    }
}
