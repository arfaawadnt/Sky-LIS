using System.Globalization;
using System.Text;
using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Patients;
using Platform.Contracts.Reporting;
using Platform.Domain.Authorization;
using Platform.Domain.Reporting;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Reporting.Queries;

// Work amount report (change request 2026-10-13 item 3; intake LIS-INT-REP-001 §14b): the income of the visits
// registered in a period of laboratory days, cash and credit, without cancelled services, grouped many ways.

/// <summary>
/// <paramref name="FromDay"/>…<paramref name="ToDay"/> are laboratory-calendar days, inclusive (default: today, or the
/// other bound when only one is given); <paramref name="GroupBy"/> a <see cref="WorkAmountGrouping"/> name (default Day);
/// <paramref name="BranchId"/> null = the session branch (TENANT-004 without one), another branch needs
/// <c>organization.view</c> or the member's assignment to it (REP-003). A period starting before the member's earliest
/// allowed day is refused with REP-001.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Reporting, PermissionAction.View)]
public sealed record WorkAmountReportQuery(DateOnly? FromDay, DateOnly? ToDay, string? GroupBy, Guid? BranchId) : IQuery<WorkAmountReport>;

public sealed class WorkAmountReportValidator : AbstractValidator<WorkAmountReportQuery>
{
    public WorkAmountReportValidator()
    {
        RuleFor(query => query.GroupBy).IsEnumName(typeof(WorkAmountGrouping), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.GroupBy));
        RuleFor(query => query).Must(query => query.FromDay is not { } from || query.ToDay is not { } to || from <= to)
            .OverridePropertyName("toDay").WithMessage("The end of the period must not precede its start.");
    }
}

public sealed class WorkAmountReportHandler(
    IWorkAmountReader reader,
    IWorkAmountNames names,
    IReportLetterhead letterhead,
    ILaboratoryTimeZone laboratoryZone,
    IClock clock,
    ICurrentUser user,
    IUserPrivileges privileges,
    ISessionPrincipalQuery principals) : IQueryHandler<WorkAmountReportQuery, WorkAmountReport>
{
    public Task<WorkAmountReport> Handle(WorkAmountReportQuery query, CancellationToken cancellationToken) =>
        new WorkAmountReportComposer(reader, names, letterhead, laboratoryZone, clock, user, privileges, principals)
            .ComposeAsync(query.FromDay, query.ToDay, query.GroupBy, query.BranchId, cancellationToken);
}

/// <summary>The same report as a CSV file (UTF-8 with BOM, RFC 4180, formula-safe cells); the rows then the total.</summary>
[RequirePermissionPolicy(PermissionCatalog.Reporting, PermissionAction.Export)]
public sealed record ExportWorkAmountReportQuery(DateOnly? FromDay, DateOnly? ToDay, string? GroupBy, Guid? BranchId) : IQuery<ReportExportFile>;

public sealed class ExportWorkAmountReportValidator : AbstractValidator<ExportWorkAmountReportQuery>
{
    public ExportWorkAmountReportValidator()
    {
        RuleFor(query => query.GroupBy).IsEnumName(typeof(WorkAmountGrouping), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.GroupBy));
        RuleFor(query => query).Must(query => query.FromDay is not { } from || query.ToDay is not { } to || from <= to)
            .OverridePropertyName("toDay").WithMessage("The end of the period must not precede its start.");
    }
}

public sealed class ExportWorkAmountReportHandler(
    IWorkAmountReader reader,
    IWorkAmountNames names,
    IReportLetterhead letterhead,
    ILaboratoryTimeZone laboratoryZone,
    IClock clock,
    ICurrentUser user,
    IUserPrivileges privileges,
    ISessionPrincipalQuery principals) : IQueryHandler<ExportWorkAmountReportQuery, ReportExportFile>
{
    public async Task<ReportExportFile> Handle(ExportWorkAmountReportQuery query, CancellationToken cancellationToken)
    {
        var report = await new WorkAmountReportComposer(reader, names, letterhead, laboratoryZone, clock, user, privileges, principals)
            .ComposeAsync(query.FromDay, query.ToDay, query.GroupBy, query.BranchId, cancellationToken)
            .ConfigureAwait(false);
        return WorkAmountCsv.Build(report);
    }
}

/// <summary>A file produced by a report export: name, media type and bytes.</summary>
public sealed record ReportExportFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Builds the report for the view and the export: resolves the period (defaults, zone, days limit), the branch, reads
/// the live lines, groups them, captions the groups through the names port and adds the letterhead.
/// </summary>
internal sealed class WorkAmountReportComposer(
    IWorkAmountReader reader,
    IWorkAmountNames names,
    IReportLetterhead letterhead,
    ILaboratoryTimeZone laboratoryZone,
    IClock clock,
    ICurrentUser user,
    IUserPrivileges privileges,
    ISessionPrincipalQuery principals)
{
    public const WorkAmountGrouping DefaultGrouping = WorkAmountGrouping.Day;

    private const string NoDoctorKey = "none";
    private const string NotGroupedKey = "none";

    public async Task<WorkAmountReport> ComposeAsync(DateOnly? fromDay, DateOnly? toDay, string? groupBy, Guid? branchId, CancellationToken cancellationToken)
    {
        var grouping = string.IsNullOrWhiteSpace(groupBy) ? DefaultGrouping : Enum.Parse<WorkAmountGrouping>(groupBy.Trim(), ignoreCase: true);
        var zone = await laboratoryZone.FindAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var today = ReportPeriod.LocalDay(now, zone);
        var from = fromDay ?? toDay ?? today;
        var to = toDay ?? from;
        ReportPeriod.RequireWithinLimit(from, privileges.SpecialRights.EarliestReportDay(today));
        var (fromUtc, toUtc) = ReportPeriod.Window(from, to, zone);
        var branch = await ResolveBranchAsync(branchId, cancellationToken).ConfigureAwait(false);

        var lines = await reader.ReadAsync(branch, fromUtc, toUtc, cancellationToken).ConfigureAwait(false);
        var rows = await RowsAsync(lines, grouping, zone, cancellationToken).ConfigureAwait(false);
        var head = await letterhead.GetAsync(branch, cancellationToken).ConfigureAwait(false);

        return new WorkAmountReport(
            from,
            to,
            grouping.ToString(),
            branch,
            zone?.Id ?? "UTC",
            head,
            rows,
            WorkAmountAggregator.Total(lines),
            user.DisplayName ?? string.Empty,
            now);
    }

    /// <summary>
    /// The session branch when none is asked for; another branch when the member holds <c>organization.view</c> or the
    /// membership is assigned to it (no assignment = every branch, as the branch-selection list of CP-04 reads it).
    /// </summary>
    private async Task<Guid> ResolveBranchAsync(Guid? requested, CancellationToken cancellationToken)
    {
        if (requested is not { } branch || branch == user.BranchId)
        {
            return BranchScope.Require(user);
        }

        if (privileges.Has(PermissionCatalog.Key(PermissionCatalog.Organization, PermissionAction.View)))
        {
            return branch;
        }

        if (user.UserId is { } userId && user.SessionId is { } sessionId)
        {
            var principal = await principals.ResolveAsync(sessionId, userId, cancellationToken).ConfigureAwait(false);
            if (principal is not null && (principal.BranchIds.Count == 0 || principal.BranchIds.Contains(branch)))
            {
                return branch;
            }
        }

        throw new DomainException(ReportingCodes.ReportBranchNotAllowed, "You may report on your own branches only.", DomainFault.Refused);
    }

    private async Task<IReadOnlyList<WorkAmountRow>> RowsAsync(IReadOnlyList<WorkAmountLine> lines, WorkAmountGrouping grouping, TimeZoneInfo? zone, CancellationToken cancellationToken)
    {
        switch (grouping)
        {
            case WorkAmountGrouping.Day:
                return WorkAmountAggregator.Rows(lines, line => DayKey(line, zone), key => new WorkAmountGroupLabel(key, key), orderByKey: true);

            case WorkAmountGrouping.Service:
            {
                var services = lines.GroupBy(line => line.ServiceId).ToDictionary(group => group.Key, group => group.First());
                return WorkAmountAggregator.Rows(lines, line => line.ServiceId.ToString(), key =>
                {
                    var service = services[Guid.Parse(key)];
                    return new WorkAmountGroupLabel(service.ServiceNameEn, service.ServiceNameAr, service.ServiceCode);
                });
            }

            case WorkAmountGrouping.ClinicalGroup:
            {
                var groupOfService = await names.ClinicalGroupOfServicesAsync(Ids(lines.Select(line => line.ServiceId)), cancellationToken).ConfigureAwait(false);
                var groups = await names.ClinicalGroupsAsync(Ids(groupOfService.Values), cancellationToken).ConfigureAwait(false);
                return WorkAmountAggregator.Rows(
                    lines,
                    line => groupOfService.TryGetValue(line.ServiceId, out var group) ? group.ToString() : NotGroupedKey,
                    key => key == NotGroupedKey ? new WorkAmountGroupLabel("Not grouped", "بدون مجموعة") : Caption(groups, key));
            }

            case WorkAmountGrouping.Doctor:
            {
                var doctors = await names.DoctorsAsync(Ids(lines.Select(line => line.ReferringDoctorId).OfType<Guid>()), cancellationToken).ConfigureAwait(false);
                return WorkAmountAggregator.Rows(
                    lines,
                    line => line.ReferringDoctorId?.ToString() ?? NoDoctorKey,
                    key => key == NoDoctorKey ? new WorkAmountGroupLabel("No referring doctor", "بدون طبيب محوِّل") : Caption(doctors, key));
            }

            case WorkAmountGrouping.Payer:
            {
                var payers = await names.PayersAsync(Ids(lines.Select(line => line.PayerId)), cancellationToken).ConfigureAwait(false);
                return WorkAmountAggregator.Rows(lines, line => line.PayerId.ToString(), key => Caption(payers, key));
            }

            case WorkAmountGrouping.ContractTier:
            {
                var tiers = await names.ContractTiersAsync(Ids(lines.Select(line => line.ContractTierId)), cancellationToken).ConfigureAwait(false);
                return WorkAmountAggregator.Rows(lines, line => line.ContractTierId.ToString(), key => Caption(tiers, key));
            }

            case WorkAmountGrouping.User:
            {
                var users = await names.UsersAsync(Ids(lines.Select(line => line.RegisteredByUserId)), cancellationToken).ConfigureAwait(false);
                return WorkAmountAggregator.Rows(lines, line => line.RegisteredByUserId.ToString(), key => Caption(users, key));
            }

            default:
            {
                var branches = await names.BranchesAsync(Ids(lines.Select(line => line.BranchId)), cancellationToken).ConfigureAwait(false);
                return WorkAmountAggregator.Rows(lines, line => line.BranchId.ToString(), key => Caption(branches, key));
            }
        }
    }

    /// <summary>The laboratory-calendar day of the registration, as an ISO date so the rows sort chronologically.</summary>
    internal static string DayKey(WorkAmountLine line, TimeZoneInfo? zone) =>
        ReportPeriod.LocalDay(line.RegisteredAtUtc, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static WorkAmountGroupLabel Caption(IReadOnlyDictionary<Guid, WorkAmountGroupLabel> labels, string key) =>
        labels.TryGetValue(Guid.Parse(key), out var label) ? label : new WorkAmountGroupLabel(key, key);

    private static List<Guid> Ids(IEnumerable<Guid> ids) => ids.Distinct().ToList();
}

/// <summary>
/// CSV of the work amount report (RFC 4180, UTF-8 with BOM so spreadsheet tools read Arabic, formula-leading text
/// prefixed with an apostrophe, numbers invariant with two decimals): one line per row, then the total.
/// </summary>
internal static class WorkAmountCsv
{
    public const string ContentType = "text/csv; charset=utf-8";

    private static readonly string[] Header = ["Group", "Group (Arabic)", "Code", "Visits", "Services", "Gross", "Discount", "Cash income", "Credit income", "Net"];

    public static ReportExportFile Build(WorkAmountReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var csv = new StringBuilder();
        AppendLine(csv, Header);
        foreach (var row in report.Rows.Append(report.Totals))
        {
            AppendLine(csv, [row.LabelEn, row.LabelAr, row.Code, Number(row.Visits), Number(row.Services), Money(row.Gross), Money(row.Discount), Money(row.CashIncome), Money(row.CreditIncome), Money(row.Net)]);
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        var name = $"work-amount-{report.FromDay:yyyy-MM-dd}-{report.ToDay:yyyy-MM-dd}-{report.GroupBy.ToLowerInvariant()}.csv";
        return new ReportExportFile(name, ContentType, bytes);
    }

    /// <summary>RFC 4180 quoting plus formula neutralisation (a cell starting with = + - @ tab or CR is prefixed with an apostrophe).</summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var safe = value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static void AppendLine(StringBuilder csv, IEnumerable<string?> cells) => csv.Append(string.Join(',', cells.Select(Cell))).Append("\r\n");
}
