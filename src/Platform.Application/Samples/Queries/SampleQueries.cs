using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.Common;
using Platform.Contracts.Samples;
using Platform.Domain.Authorization;
using Platform.Domain.Samples;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Samples.Queries;

// Registers, tracking and documents of the module (intake §14). Queries carry a policy like commands (D-04) and read projections only.

/// <summary><paramref name="Status"/> is the status name; <paramref name="From"/>/<paramref name="To"/> bound the time the sample was ordered.</summary>
[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.View)]
public sealed record ListSamplesQuery(
    Guid? VisitId,
    string? Accession,
    string? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? BranchId,
    Guid? ClinicalGroupId,
    string? Search,
    int? Page,
    int? PageSize,
    string? Sort = null,
    string? Direction = null) : IQuery<Paged<SampleListItem>>;

public sealed class ListSamplesValidator : AbstractValidator<ListSamplesQuery>
{
    public ListSamplesValidator()
    {
        RuleFor(query => query.Status).IsEnumName(typeof(SampleStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.Accession).MaximumLength(Sample.MaxAccessionLength);
        RuleFor(query => query.Search).MaximumLength(Sample.MaxAccessionLength);
        RuleFor(query => query.To).GreaterThanOrEqualTo(query => query.From).When(query => query.From is not null && query.To is not null);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(SamplesSortKeys.Samples);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListSamplesHandler(ISampleRepository samples) : IQueryHandler<ListSamplesQuery, Paged<SampleListItem>>
{
    public Task<Paged<SampleListItem>> Handle(ListSamplesQuery query, CancellationToken cancellationToken)
    {
        SampleStatus? status = string.IsNullOrEmpty(query.Status) ? null : Enum.Parse<SampleStatus>(query.Status, ignoreCase: true);
        var filter = new SampleFilter(
            query.VisitId,
            string.IsNullOrWhiteSpace(query.Accession) ? null : query.Accession.Trim(),
            status,
            query.From,
            query.To,
            query.BranchId,
            query.ClinicalGroupId,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim().ToUpperInvariant(),
            ListSort.Of(query.Sort, query.Direction));
        return samples.ListAsync(filter, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.View)]
public sealed record GetSampleQuery(Guid SampleId) : IQuery<SampleDetail>;

public sealed class GetSampleHandler(ISampleRepository samples) : IQueryHandler<GetSampleQuery, SampleDetail>
{
    public async Task<SampleDetail> Handle(GetSampleQuery query, CancellationToken cancellationToken) =>
        await samples.GetDetailAsync(query.SampleId, cancellationToken) ?? throw SamplesNotFound.Sample();
}

/// <summary>Lookup by barcode (scan or typed), any generation.</summary>
[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.View)]
public sealed record GetSampleByNumberQuery(string SampleNumber) : IQuery<SampleDetail>;

public sealed class GetSampleByNumberValidator : AbstractValidator<GetSampleByNumberQuery>
{
    public GetSampleByNumberValidator()
    {
        RuleFor(query => query.SampleNumber).NotEmpty().MaximumLength(Sample.MaxSampleNumberLength);
    }
}

public sealed class GetSampleByNumberHandler(ISampleRepository samples) : IQueryHandler<GetSampleByNumberQuery, SampleDetail>
{
    public async Task<SampleDetail> Handle(GetSampleByNumberQuery query, CancellationToken cancellationToken)
    {
        var id = await samples.FindIdByNumberAsync(Sample.CleanNumber(query.SampleNumber), cancellationToken) ?? throw SamplesNotFound.Sample();
        return await samples.GetDetailAsync(id, cancellationToken) ?? throw SamplesNotFound.Sample();
    }
}

/// <summary>CP-17: the sample, its steps with the time since the previous one, and the stage durations.</summary>
[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.View)]
public sealed record GetSampleTimelineQuery(Guid SampleId) : IQuery<SampleTimeline>;

public sealed class GetSampleTimelineHandler(ISampleRepository samples) : IQueryHandler<GetSampleTimelineQuery, SampleTimeline>
{
    public async Task<SampleTimeline> Handle(GetSampleTimelineQuery query, CancellationToken cancellationToken)
    {
        var detail = await samples.GetDetailAsync(query.SampleId, cancellationToken) ?? throw SamplesNotFound.Sample();
        var steps = await samples.GetTimelineStepsAsync(query.SampleId, cancellationToken);
        return SampleTimelineCalculator.Build(detail, steps);
    }
}

/// <summary>The label document of one sample (CP-15 "print one label").</summary>
[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.View)]
public sealed record GetSampleLabelQuery(Guid SampleId) : IQuery<SampleLabelDocument>;

public sealed class GetSampleLabelHandler(ISampleRepository samples, ITenantSettings settings) : IQueryHandler<GetSampleLabelQuery, SampleLabelDocument>
{
    public async Task<SampleLabelDocument> Handle(GetSampleLabelQuery query, CancellationToken cancellationToken)
    {
        var copies = await settings.GetAsync(SettingKeys.LabelCopies, cancellationToken);
        return await samples.GetLabelAsync(query.SampleId, copies, cancellationToken) ?? throw SamplesNotFound.Sample();
    }
}

/// <summary>The labels of every live sample of a visit (CP-15 "print all labels").</summary>
[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.View)]
public sealed record GetVisitLabelsQuery(Guid VisitId) : IQuery<VisitLabelsDocument>;

public sealed class GetVisitLabelsHandler(ISampleRepository samples, ITenantSettings settings) : IQueryHandler<GetVisitLabelsQuery, VisitLabelsDocument>
{
    public async Task<VisitLabelsDocument> Handle(GetVisitLabelsQuery query, CancellationToken cancellationToken)
    {
        var copies = await settings.GetAsync(SettingKeys.LabelCopies, cancellationToken);
        var labels = await samples.GetVisitLabelsAsync(query.VisitId, copies, cancellationToken);
        if (labels.Count == 0)
        {
            throw SamplesNotFound.Visit();
        }

        return new VisitLabelsDocument(query.VisitId, labels[0].Accession, labels);
    }
}

/// <summary>
/// The dashboard's "rejected samples" (CP-05, CP-16 "notifies reception"): samples still Rejected whose rejection
/// happened on the caller's local day; <paramref name="UtcOffsetMinutes"/> is the caller's offset (east positive).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.View)]
public sealed record ListRejectedTodayQuery(Guid? BranchId, int? UtcOffsetMinutes) : IQuery<IReadOnlyList<SampleListItem>>;

public sealed class ListRejectedTodayValidator : AbstractValidator<ListRejectedTodayQuery>
{
    public ListRejectedTodayValidator()
    {
        RuleFor(query => query.UtcOffsetMinutes).InclusiveBetween(-LocalDay.MaxOffsetMinutes, LocalDay.MaxOffsetMinutes).When(query => query.UtcOffsetMinutes is not null);
    }
}

public sealed class ListRejectedTodayHandler(ISampleRepository samples, IClock clock) : IQueryHandler<ListRejectedTodayQuery, IReadOnlyList<SampleListItem>>
{
    public Task<IReadOnlyList<SampleListItem>> Handle(ListRejectedTodayQuery query, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = LocalDay.Of(clock.UtcNow, query.UtcOffsetMinutes ?? 0);
        return samples.ListRejectedAsync(fromUtc, toUtc, query.BranchId, cancellationToken);
    }
}

/// <summary>The tenant parameters the sample screens follow (which actions to offer; the server still decides).</summary>
[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.View)]
public sealed record GetSampleSettingsQuery : IQuery<SampleSettingsView>;

public sealed class GetSampleSettingsHandler(ITenantSettings settings) : IQueryHandler<GetSampleSettingsQuery, SampleSettingsView>
{
    public async Task<SampleSettingsView> Handle(GetSampleSettingsQuery query, CancellationToken cancellationToken) =>
        new(
            await settings.GetAsync(SettingKeys.AutoCollect, cancellationToken),
            await settings.GetAsync(SettingKeys.AutoReceive, cancellationToken),
            await settings.GetAsync(SettingKeys.NewBarcodeOnReject, cancellationToken),
            await settings.GetAsync(SettingKeys.LabelCopies, cancellationToken));
}
