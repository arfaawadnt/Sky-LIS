using System.Globalization;
using System.Text;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Billing;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Billing;

/// <summary>The authenticated actor of a billing request, or the coded refusal (01 §3.3).</summary>
internal static class BillingActor
{
    public static Guid Of(ICurrentUser user) =>
        user.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);
}

/// <summary>
/// The special rights of CP-37 (role of the membership, BR-17) that narrow the permission policies: payment, refund, void
/// (cancel) and invoice printing. Checked first in the handler, before anything is read, and refused with 403.
/// </summary>
internal static class BillingRights
{
    public static void RequirePay(IUserPrivileges privileges) =>
        Require(privileges.SpecialRights.CanPay, BillingCodes.PayRightRequired, "Your role does not hold the payment right.");

    public static void RequireRefund(IUserPrivileges privileges) =>
        Require(privileges.SpecialRights.CanRefund, BillingCodes.RefundRightRequired, "Your role does not hold the refund right.");

    public static void RequireCancel(IUserPrivileges privileges) =>
        Require(privileges.SpecialRights.CanCancel, BillingCodes.CancelRightRequired, "Your role does not hold the right to cancel payments.");

    public static void RequirePrintInvoice(IUserPrivileges privileges) =>
        Require(privileges.SpecialRights.CanPrintInvoice, BillingCodes.PrintInvoiceRightRequired, "Your role does not hold the right to print invoices.");

    private static void Require(bool granted, string code, string message)
    {
        if (!granted)
        {
            throw new DomainException(code, message, DomainFault.Refused);
        }
    }
}

/// <summary>Builds the BR-27 facts of a movement from the Treasury module and the session.</summary>
internal static class BillingStations
{
    public static async Task<CashierStation> ResolveAsync(ITreasuryStationLookup stations, Guid treasuryId, Guid actor, string deviceId, Guid sessionBranchId, CancellationToken cancellationToken)
    {
        var device = (deviceId ?? string.Empty).Trim();
        var view = await stations.GetAsync(treasuryId, actor, device, cancellationToken).ConfigureAwait(false) ?? throw BillingNotFound.Treasury();
        return new CashierStation(view.TreasuryId, view.BranchId, view.IsActive, view.ApprovedForDevice, device, actor, sessionBranchId);
    }
}

/// <summary>Coded "not found" refusals of the module (404).</summary>
internal static class BillingNotFound
{
    public static DomainException Visit() => new(BillingCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);

    public static DomainException Payment() => new(BillingCodes.PaymentNotFound, "Payment not found.", DomainFault.NotFound);

    public static DomainException Batch() => new(BillingCodes.BatchNotFound, "Batch not found.", DomainFault.NotFound);

    public static DomainException Refund() => new(BillingCodes.RefundNotFound, "Refund not found.", DomainFault.NotFound);

    public static DomainException Closing() => new(BillingCodes.ClosingNotFound, "Cash closing not found.", DomainFault.NotFound);

    public static DomainException Treasury() => new(BillingCodes.TreasuryNotFound, "Treasury not found.", DomainFault.NotFound);
}

/// <summary>Enum names as they travel (validators have checked them with <c>IsEnumName</c>).</summary>
internal static class BillingNames
{
    public static PaymentMethod Method(string value) => Enum.Parse<PaymentMethod>(value, ignoreCase: true);

    public static PaymentMethod? OptionalMethod(string? value) => string.IsNullOrEmpty(value) ? null : Method(value);

    public static PaymentStatus? OptionalStatus(string? value) => string.IsNullOrEmpty(value) ? null : Enum.Parse<PaymentStatus>(value, ignoreCase: true);
}

/// <summary>Fills the accessions of register rows from Registration (one query per page).</summary>
internal static class BillingAccessions
{
    public static async Task<IReadOnlyList<PaymentListItem>> FillAsync(IBillableVisitLookup visits, IReadOnlyList<PaymentListItem> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        var known = await visits.GetManyAsync(rows.Select(row => row.VisitId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        return rows.Select(row => row with { Accession = known.TryGetValue(row.VisitId, out var visit) ? visit.Accession : null }).ToList();
    }

    public static async Task<IReadOnlyList<RefundListItem>> FillAsync(IBillableVisitLookup visits, IRefundReasonLookup reasons, IReadOnlyList<RefundListItem> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        var known = await visits.GetManyAsync(rows.Select(row => row.VisitId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var names = await reasons.GetManyAsync(rows.Select(row => row.ReasonId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        return rows.Select(row =>
        {
            names.TryGetValue(row.ReasonId, out var reason);
            return row with
            {
                Accession = known.TryGetValue(row.VisitId, out var visit) ? visit.Accession : null,
                ReasonNameEn = reason?.NameEn,
                ReasonNameAr = reason?.NameAr,
            };
        }).ToList();
    }

    public static async Task<IReadOnlyList<DrawerMovement>> FillAsync(IBillableVisitLookup visits, IReadOnlyList<DrawerMovement> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        var known = await visits.GetManyAsync(rows.Select(row => row.VisitId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        return rows.Select(row => row with { Accession = known.TryGetValue(row.VisitId, out var visit) ? visit.Accession : null }).ToList();
    }
}

/// <summary>Maps the domain totals to the view the API returns.</summary>
internal static class BillingViews
{
    public static CashClosingTotalsView Totals(ClosingTotals totals) => new(
        totals.CashPayments,
        totals.CardPayments,
        totals.PosPayments,
        totals.TransferPayments,
        totals.TotalPayments,
        totals.PaymentCount,
        totals.CashRefunds,
        totals.CardRefunds,
        totals.PosRefunds,
        totals.TransferRefunds,
        totals.TotalRefunds,
        totals.RefundCount,
        totals.ExpectedCash,
        totals.OutstandingBalance,
        totals.DiscountTotal,
        totals.VisitCount);
}

/// <summary>
/// CSV of a register (RFC 4180, UTF-8 with BOM so spreadsheet tools read Arabic). Text cells that a spreadsheet would
/// evaluate as a formula are prefixed with an apostrophe (CSV injection); numbers are written invariantly.
/// </summary>
internal static class BillingCsv
{
    public const int MaxRows = 10_000;
    public const string ContentType = "text/csv; charset=utf-8";

    /// <summary>
    /// Builds the file. With <paramref name="zone"/> (the browser's IANA time zone) every date and time is written in that
    /// zone, as the register shows it, and the headers name the zone instead of "UTC" (UAT R1-06); without it, UTC.
    /// </summary>
    public static BillingExportFile Build(string name, DateTimeOffset nowUtc, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<object?>> rows, TimeZoneInfo? zone = null)
    {
        var csv = new StringBuilder();
        AppendLine(csv, header.Select(text => (object?)(zone is null ? text : text.Replace("(UTC)", $"({zone.Id})", StringComparison.Ordinal))).ToList(), null);
        foreach (var row in rows)
        {
            AppendLine(csv, row, zone);
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return new BillingExportFile($"{name}-{nowUtc.UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv", ContentType, bytes);
    }

    /// <summary>BIL-050 when a register exceeds the export limit (the caller asked for <see cref="MaxRows"/> + 1 rows).</summary>
    public static void RequireWithinLimit(int count)
    {
        if (count > MaxRows)
        {
            throw new DomainException(BillingCodes.ExportTooLarge, $"The export is limited to {MaxRows} rows; narrow the filter.", DomainFault.Validation);
        }
    }

    public static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>The export's time zone: <see langword="null"/> (UTC) when none is given; an unknown id is refused by the validators.</summary>
    public static TimeZoneInfo? Zone(string? timeZone) =>
        string.IsNullOrWhiteSpace(timeZone) ? null : TimeZoneInfo.TryFindSystemTimeZoneById(timeZone.Trim(), out var zone) ? zone : null;

    public static bool IsKnownZone(string? timeZone) => string.IsNullOrWhiteSpace(timeZone) || (timeZone.Length <= 64 && Zone(timeZone) is not null);

    private static void AppendLine(StringBuilder csv, IReadOnlyList<object?> cells, TimeZoneInfo? zone)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            if (index > 0)
            {
                csv.Append(',');
            }

            csv.Append(Cell(cells[index], zone));
        }

        csv.Append("\r\n");
    }

    private static string Cell(object? value, TimeZoneInfo? zone) => value switch
    {
        DateTimeOffset instant when zone is not null => TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        null => string.Empty,
        decimal number => number.ToString("0.00", CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        bool flag => flag ? "yes" : "no",
        DateTimeOffset instant => Utc(instant),
        _ => Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
    };

    private static string Text(string text)
    {
        var safe = text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
        return safe.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }
}
