using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Patients;
using Platform.Application.Results;
using Platform.Contracts.Common;
using Platform.Contracts.Tracker;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.Domain.Patients;
using Platform.Domain.Reporting;
using Platform.Domain.Settings;
using Platform.Domain.Tracker;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Tracker.Queries;

// Lab Tracker (change request LIS-CR-006 §0, intake LIS-INT-TRK-001): the visits of one laboratory day with their stage,
// next step and time left. Read-only; queries never commit.

/// <summary>
/// <paramref name="Day"/> = laboratory-calendar day (tenant time zone; default today) the visit was registered in;
/// <paramref name="BranchId"/> null = the session branch (TENANT-004 without one; any branch of the laboratory otherwise, as
/// the visits register reads them). <paramref name="Stage"/>, <paramref name="Attention"/> and <paramref name="View"/> filter
/// the rows (names of <see cref="TrackerStage"/>, <see cref="TrackerAttention"/>, <see cref="TrackerView"/>) but not the
/// counters; <paramref name="Search"/> narrows both. <paramref name="Sort"/> is a key of <see cref="TrackerSortKeys.Visits"/>.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record GetLabTrackerQuery(
    DateOnly? Day,
    Guid? BranchId,
    DateTime? From,
    DateTime? To,
    string? Stage,
    string? Attention,
    string? View,
    string? Search,
    int? Page,
    int? PageSize,
    string? Sort = null,
    string? Direction = null) : IQuery<LabTrackerPage>;

public sealed class GetLabTrackerValidator : AbstractValidator<GetLabTrackerQuery>
{
    public const int MaxSearchLength = 60;

    /// <summary>A from / to range spans at most this many days (the day is composed in memory).</summary>
    public const int MaxRangeDays = 31;

    public GetLabTrackerValidator()
    {
        RuleFor(query => query.To).NotNull().When(query => query.From is not null).WithMessage("'from' and 'to' go together.");
        RuleFor(query => query.From).NotNull().When(query => query.To is not null).WithMessage("'from' and 'to' go together.");
        RuleFor(query => query.To).Must((query, to) => query.From is not { } from || to is not { } end || from <= end).WithMessage("'from' must not be after 'to'.");
        RuleFor(query => query.To).Must((query, to) => query.From is not { } from || to is not { } end || (end - from).TotalDays <= MaxRangeDays)
            .WithMessage($"The range may cover at most {MaxRangeDays} days.");
        RuleFor(query => query.Stage).IsEnumName(typeof(TrackerStage), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Stage));
        RuleFor(query => query.Attention).IsEnumName(typeof(TrackerAttention), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Attention));
        RuleFor(query => query.View).IsEnumName(typeof(TrackerView), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.View));
        RuleFor(query => query.Search).MaximumLength(MaxSearchLength);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(TrackerSortKeys.Visits);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class GetLabTrackerHandler(
    ILabTrackerReader reader,
    ITenantSettings settings,
    IUserPrivileges privileges,
    ICurrentUser user,
    ILaboratoryTimeZone laboratoryZone,
    ILaboratoryCountry laboratoryCountry,
    IClock clock) : IQueryHandler<GetLabTrackerQuery, LabTrackerPage>
{
    public async Task<LabTrackerPage> Handle(GetLabTrackerQuery query, CancellationToken cancellationToken)
    {
        var page = PageRequest.Normalized(query.Page, query.PageSize);
        var branchId = query.BranchId ?? BranchScope.Require(user);
        var zone = await laboratoryZone.FindAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var today = ReportPeriod.LocalDay(now, zone);
        // a from / to range (laboratory wall-clock, inclusive to the minute) wins over the day; the day echoed is the range's first day
        var day = query.From is { } rangeFrom ? DateOnly.FromDateTime(rangeFrom) : query.Day ?? today;
        var (fromUtc, toUtc) = query is { From: { } from, To: { } to }
            ? (ReportPeriod.InstantUtc(from, zone), ReportPeriod.InstantUtc(to, zone).AddMinutes(1))
            : ReportPeriod.Window(day, day, zone);

        var rules = new TrackerRules(
            await settings.GetAsync(SettingKeys.ReviewBeforeVerify, cancellationToken).ConfigureAwait(false),
            await settings.GetAsync(SettingKeys.DelayWarnPercent, cancellationToken).ConfigureAwait(false),
            await settings.GetAsync(SettingKeys.DelayLatePercent, cancellationToken).ConfigureAwait(false),
            now,
            today);
        var sections = new TrackerSections(
            privileges.Has(PermissionCatalog.Key(PermissionCatalog.Samples, PermissionAction.View)),
            privileges.Has(PermissionCatalog.Key(PermissionCatalog.Results, PermissionAction.View)),
            privileges.Has(PermissionCatalog.Key(PermissionCatalog.Billing, PermissionAction.View)),
            privileges.SpecialRights.CanSeeVip);

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var mobile = search is null ? null : TrackerSearch.MobileDigits(search, (await laboratoryCountry.FindAsync(cancellationToken).ConfigureAwait(false))?.MobileLength);
        var filter = new LabTrackerFilter(branchId, fromUtc, toUtc, search, mobile, sections.SeeVip, ResultRights.VisibilityOf(privileges));
        var rows = await reader.ReadAsync(filter, cancellationToken).ConfigureAwait(false);

        var visits = rows.Select(row => new TrackedVisit(row, VisitProgressRules.Of(TrackerRows.Facts(row), rules.ReviewBeforeVerify, rules.NowUtc, rules.WarnPercent, rules.LatePercent))).ToList();
        var counters = TrackerRows.Count(visits);
        var selected = TrackerRows.Select(visits, Parse<TrackerStage>(query.Stage), Parse<TrackerAttention>(query.Attention), Parse<TrackerView>(query.View));
        var ordered = TrackerRows.Order(selected, ListSort.Of(query.Sort, query.Direction), sections.SeeVip);
        var items = ordered.Skip(page.Skip).Take(page.PageSize).Select(visit => TrackerRows.Item(visit, sections, rules.Today)).ToList();
        return new LabTrackerPage(items, page.Page, page.PageSize, selected.Count, counters, day, branchId, fromUtc, toUtc);
    }

    private static TEnum? Parse<TEnum>(string? value)
        where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<TEnum>(value.Trim(), ignoreCase: true);
}

/// <summary>The tenant rules and the instant the progress is judged at.</summary>
internal sealed record TrackerRules(bool ReviewBeforeVerify, int WarnPercent, int LatePercent, DateTimeOffset NowUtc, DateOnly Today);

/// <summary>Which sections the user may read (<c>samples.view</c>, <c>results.view</c>, <c>billing.view</c>) and the See-VIP right.</summary>
internal sealed record TrackerSections(bool Samples, bool Results, bool Payment, bool SeeVip);

/// <summary>A row of the reader with its derived progress.</summary>
internal sealed record TrackedVisit(LabTrackerRow Row, VisitProgress Progress);

/// <summary>The search box as a mobile number: the digits of a number-only text of at least five digits (trunk 0 dropped as the visits register does).</summary>
internal static class TrackerSearch
{
    public const int MinMobileDigits = 5;

    public static string? MobileDigits(string search, int? mobileLength)
    {
        var folded = Patient.FoldDigits(search);
        if (folded.Any(character => !(char.IsAsciiDigit(character) || character is '+' or ' ' or '-')))
        {
            return null;
        }

        var digits = new string(folded.Where(char.IsAsciiDigit).ToArray());
        return digits.Length < MinMobileDigits ? null : Patient.MobileSearchDigits(digits, mobileLength);
    }
}

/// <summary>Counters, filters, order and the item mapping of the tracker (VIP masking as the visits register: A-03).</summary>
internal static class TrackerRows
{
    /// <summary>The name a masked VIP row shows and sorts by (the clients show their own text from <c>patientMasked</c>).</summary>
    public const string MaskedName = "VIP patient";

    public static VisitProgressFacts Facts(LabTrackerRow row) => new(
        row.LiveLines,
        row.Samples,
        row.VisibleResults,
        row.AllResults,
        row.VisibleOpenClocks,
        row.AllOpenClocks,
        row.VisibleOpenPanics,
        new VisitBalanceFigures(row.PatientShareTotal, row.Paid, row.Refunded).Remaining,
        row.BilledThroughClaims);

    public static LabTrackerCounters Count(IReadOnlyList<TrackedVisit> visits)
    {
        int Stage(TrackerStage stage) => visits.Count(visit => visit.Progress.Stage == stage);
        int Flag(TrackerAttention attention) => visits.Count(visit => visit.Progress.Has(attention));
        return new LabTrackerCounters(
            visits.Count,
            Stage(TrackerStage.Cancelled),
            Stage(TrackerStage.AwaitingSample),
            Stage(TrackerStage.InLab),
            Stage(TrackerStage.AwaitingVerification),
            Stage(TrackerStage.Ready),
            Stage(TrackerStage.Delivered),
            Flag(TrackerAttention.Delayed),
            Flag(TrackerAttention.Panic),
            Flag(TrackerAttention.Rejected),
            Flag(TrackerAttention.ReadyUnpaid));
    }

    public static IReadOnlyList<TrackedVisit> Select(IReadOnlyList<TrackedVisit> visits, TrackerStage? stage, TrackerAttention? attention, TrackerView? view) =>
        [.. visits.Where(visit =>
            (stage is not { } wantedStage || visit.Progress.Stage == wantedStage)
            && (attention is not { } wantedFlag || visit.Progress.Has(wantedFlag))
            && (view is not { } wantedView || visit.Progress.In(wantedView)))];

    /// <summary>
    /// The order of the whole selection before paging. Default (and <c>timeLeft</c> ascending): urgent first, then the
    /// minutes left ascending with visits without a clock last, then registration time; <c>timeLeft</c> descending: the
    /// most time left first, still without-clock last. Ties: accession, then id (stable paging).
    /// </summary>
    public static IReadOnlyList<TrackedVisit> Order(IReadOnlyList<TrackedVisit> visits, ListSort sort, bool seeVip)
    {
        IOrderedEnumerable<TrackedVisit> ordered;
        var descending = sort.Descending;
        switch (sort.Key)
        {
            case null:
                ordered = ByUrgency(visits);
                break;
            case var key when Is(key, TrackerSortKeys.TimeLeft):
                ordered = descending
                    ? visits.OrderBy(visit => visit.Progress.MinutesLeft is null).ThenByDescending(visit => visit.Progress.MinutesLeft).ThenBy(visit => visit.Row.RegisteredAtUtc)
                    : ByUrgency(visits);
                break;
            case var key when Is(key, TrackerSortKeys.RegisteredAt):
                ordered = Direction(visits, visit => visit.Row.RegisteredAtUtc, descending, Comparer<DateTimeOffset>.Default);
                break;
            case var key when Is(key, TrackerSortKeys.Accession):
                ordered = Direction(visits, visit => visit.Row.Accession, descending, StringComparer.OrdinalIgnoreCase);
                break;
            case var key when Is(key, TrackerSortKeys.Patient):
                ordered = Direction(visits, visit => ShownName(visit.Row, seeVip), descending, StringComparer.OrdinalIgnoreCase);
                break;
            case var key when Is(key, TrackerSortKeys.Stage):
                ordered = Direction(visits, visit => (int)visit.Progress.Stage, descending, Comparer<int>.Default);
                break;
            default:
                throw new ArgumentException($"The sort key '{sort.Key}' is not allowed for this list.", nameof(sort));
        }

        return [.. ordered.ThenBy(visit => visit.Row.Accession, StringComparer.Ordinal).ThenBy(visit => visit.Row.VisitId)];
    }

    public static LabTrackerItem Item(TrackedVisit visit, TrackerSections sections, DateOnly today)
    {
        var (row, progress) = (visit.Row, visit.Progress);
        var masked = row.IsVip && !sections.SeeVip;
        var hidden = Math.Max(0, row.AllResults.Total - row.VisibleResults.Total);
        var balance = new VisitBalanceFigures(row.PatientShareTotal, row.Paid, row.Refunded);
        return new LabTrackerItem(
            row.VisitId,
            row.Accession,
            row.PatientId,
            row.PatientNumber,
            ShownName(row, sections.SeeVip),
            masked ? null : row.PatientNameAr,
            masked,
            row.Gender,
            AgeText(row.DateOfBirth, today),
            row.Urgent,
            row.RegisteredAtUtc,
            row.RegisteredByName,
            row.PayerNameEn,
            row.PayerNameAr,
            row.TierNameEn,
            row.TierNameAr,
            row.BranchId,
            progress.Stage.ToString(),
            progress.NextStep.ToString(),
            progress.MinutesLeft,
            progress.DelayState?.ToString(),
            new LabTrackerRegistration(row.LiveLines, row.CancelledLines),
            sections.Samples
                ? new LabTrackerSamples(row.Samples.Total, row.Samples.Ordered, row.Samples.Collected, row.Samples.Received, row.Samples.Rejected, row.FirstRejectionReasonEn, row.FirstRejectionReasonAr)
                : null,
            sections.Results
                ? new LabTrackerResults(
                    row.VisibleResults.Total,
                    row.VisibleResults.Ordered,
                    row.VisibleResults.Complete,
                    row.VisibleResults.Reviewed,
                    row.VisibleResults.Verified,
                    row.VisibleResults.Printed,
                    hidden,
                    row.VisiblePanics,
                    row.VisibleDeltas,
                    row.FirstPanicTestEn,
                    row.FirstPanicTestAr)
                : null,
            sections.Payment ? new LabTrackerPayment(balance.Status.ToString(), row.PatientShareTotal, row.Paid - row.Refunded, balance.Remaining) : null);
    }

    /// <summary>The age on the laboratory day in its largest whole unit: <c>34y</c>, <c>5m</c>, <c>12d</c>; null without a birth date.</summary>
    public static string? AgeText(DateOnly? birth, DateOnly today)
    {
        if (birth is not { } born || born > today)
        {
            return null;
        }

        var age = PatientAge.Between(born, today);
        return age.Years > 0 ? $"{age.Years}y" : age.Months > 0 ? $"{age.Months}m" : $"{age.Days}d";
    }

    private static string ShownName(LabTrackerRow row, bool seeVip) =>
        row.IsVip && !seeVip ? MaskedName : row.PatientName ?? string.Empty;

    private static IOrderedEnumerable<TrackedVisit> ByUrgency(IReadOnlyList<TrackedVisit> visits) =>
        visits.OrderByDescending(visit => visit.Row.Urgent)
            .ThenBy(visit => visit.Progress.MinutesLeft is null)
            .ThenBy(visit => visit.Progress.MinutesLeft)
            .ThenBy(visit => visit.Row.RegisteredAtUtc);

    private static IOrderedEnumerable<TrackedVisit> Direction<TKey>(IReadOnlyList<TrackedVisit> visits, Func<TrackedVisit, TKey> key, bool descending, IComparer<TKey> comparer) =>
        descending ? visits.OrderByDescending(key, comparer) : visits.OrderBy(key, comparer);

    private static bool Is(string key, string name) => string.Equals(key, name, StringComparison.OrdinalIgnoreCase);
}
