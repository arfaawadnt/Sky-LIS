using System.Globalization;
using System.Text;
using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Audit;
using Platform.Contracts.Account;
using Platform.Contracts.Audit;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Application.Account.Queries;

// The audit log page (CP-68 / SA-09) reads the foundation's GET /api/v1/audit/entries; this module adds the user
// filter's list and the CSV export (foundation intake A7).

/// <summary>The members of the tenant who may appear as actors (CP-68 "user" filter).</summary>
[RequirePermissionPolicy(PermissionCatalog.Audit, PermissionAction.View)]
public sealed record ListAuditActorsQuery : IQuery<IReadOnlyList<AuditActorItem>>;

public sealed class ListAuditActorsHandler(IAccountDirectory directory) : IQueryHandler<ListAuditActorsQuery, IReadOnlyList<AuditActorItem>>
{
    public Task<IReadOnlyList<AuditActorItem>> Handle(ListAuditActorsQuery query, CancellationToken cancellationToken) => directory.ListMembersAsync(cancellationToken);
}

/// <summary>A generated export: UTF-8 bytes with a byte-order mark (spreadsheets read Arabic correctly), the file name and whether the cap cut it.</summary>
public sealed record AuditExportFile(string FileName, byte[] Content, int Rows, bool Truncated);

/// <summary>
/// CP-68 "export": the newest <see cref="AuditCsv.MaxRows"/> entries of the filter as CSV, headers in the requested
/// language. Generated per request and never stored; the export itself is a security event (<c>AUDIT_EXPORTED</c>).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Audit, PermissionAction.Export)]
public sealed record ExportAuditEntriesQuery(DateTimeOffset? From, DateTimeOffset? To, Guid? UserId, string? EntityType, string? EntityId, string? Language, Guid? ClientTenantId = null) : IQuery<AuditExportFile>;

public sealed class ExportAuditEntriesValidator : AbstractValidator<ExportAuditEntriesQuery>
{
    public ExportAuditEntriesValidator()
    {
        RuleFor(query => query.EntityType).MaximumLength(200);
        RuleFor(query => query.EntityId).MaximumLength(200);
        RuleFor(query => query.Language).Must(language => language is null or "en" or "ar").WithMessage("The language must be 'en' or 'ar'.");
        RuleFor(query => query).Must(query => query.From is null || query.To is null || query.From <= query.To).WithMessage("'from' must not be after 'to'.");
    }
}

public sealed class ExportAuditEntriesHandler(IAuditExportReader reader, ISecurityEventLog events, ICurrentUser user, ICurrentTenant tenant, IClock clock)
    : IQueryHandler<ExportAuditEntriesQuery, AuditExportFile>
{
    public async Task<AuditExportFile> Handle(ExportAuditEntriesQuery query, CancellationToken cancellationToken)
    {
        var filter = new AuditEntryFilter(query.From, query.To, query.UserId, query.EntityType, query.EntityId, query.ClientTenantId);
        var rows = await reader.ReadAsync(filter, AuditCsv.MaxRows + 1, cancellationToken);
        var truncated = rows.Count > AuditCsv.MaxRows;
        var exported = truncated ? rows.Take(AuditCsv.MaxRows).ToList() : rows;

        // The platform's log (SA-09) concerns the clients: its CSV carries the Client column the page shows (UAT C-18).
        var platformLog = tenant.TenantId is { } current && TenantIds.IsPlatform(current);
        var content = AuditCsv.Write(exported, query.Language == "ar", platformLog);
        await events.WriteAsync(new SecurityEvent(AccountSecurityEvents.AuditExported, SecurityEvent.Info, tenant.TenantId, user.UserId, $"rows:{exported.Count}"), cancellationToken);

        var fileName = $"audit-log-{clock.UtcNow.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.csv";
        return new AuditExportFile(fileName, content, exported.Count, truncated);
    }
}

/// <summary>
/// The CSV of the audit log (RFC 4180): comma separated, CRLF, quoted where needed, cells that a spreadsheet would read
/// as a formula prefixed with an apostrophe (CSV injection), UTF-8 with a byte-order mark.
/// </summary>
public static class AuditCsv
{
    public const int MaxRows = 5000;

    private static readonly string[] HeadersEn = ["Time (UTC)", "User", "Action", "Entity", "Entity id", "Details", "Reason", "Kind", "Sequence", "Correlation id"];
    private static readonly string[] HeadersAr = ["الوقت (UTC)", "المستخدم", "الإجراء", "الكيان", "معرّف الكيان", "التفاصيل", "السبب", "النوع", "التسلسل", "معرّف الارتباط"];

    private const string ClientHeaderEn = "Client";
    private const string ClientHeaderAr = "العميل";

    /// <summary>
    /// Writes the CSV. <paramref name="includeClient"/> (the platform's log, SA-09) adds a Client column after the user:
    /// the client's name in the export's language, empty for the platform's own records.
    /// </summary>
    public static byte[] Write(IReadOnlyList<AuditEntryItem> entries, bool arabic, bool includeClient = false)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = new StringBuilder();
        var headers = (arabic ? HeadersAr : HeadersEn).ToList();
        if (includeClient)
        {
            headers.Insert(2, arabic ? ClientHeaderAr : ClientHeaderEn);
        }

        AppendRow(builder, headers);
        foreach (var entry in entries)
        {
            var cells = new List<string>
            {
                entry.AtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                entry.ActorName ?? entry.ActorUserId?.ToString() ?? string.Empty,
            };
            if (includeClient)
            {
                cells.Add((arabic ? entry.ClientNameAr ?? entry.ClientNameEn : entry.ClientNameEn ?? entry.ClientNameAr) ?? string.Empty);
            }

            AppendRow(builder,
            [
                .. cells,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                Details(entry),
                entry.Reason ?? string.Empty,
                entry.Kind,
                entry.Sequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                entry.CorrelationId ?? string.Empty,
            ]);
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        var content = new byte[preamble.Length + body.Length];
        preamble.CopyTo(content, 0);
        body.CopyTo(content, preamble.Length);
        return content;
    }

    /// <summary>"Field: before → after; …" for a field change; the event payload for a trail entry.</summary>
    public static string Details(AuditEntryItem entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Changes is { Count: > 0 } changes)
        {
            return string.Join("; ", changes.Select(change => $"{change.Field}: {change.Before ?? "∅"} → {change.After ?? "∅"}"));
        }

        return entry.Payload ?? string.Empty;
    }

    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> cells)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(cells[index]));
        }

        builder.Append("\r\n");
    }
}
