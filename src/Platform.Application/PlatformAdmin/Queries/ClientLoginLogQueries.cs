using System.Globalization;
using System.Text;
using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.PlatformAdmin.Queries;

// SA-08b Client login logs (change request LIS-CR-003 §2): the history of the client users' browser sessions, one row
// per rotation family, with the device read from the stored user agent. Both reads are [platform.view] and platform-only.

/// <summary>
/// The paged history. Windows are inclusive UTC bounds; <paramref name="Device"/> Computer|Mobile|Tablet|Unknown;
/// <paramref name="Status"/> Active|SignedOut|Revoked|Expired; minutes bound the duration. Sorted on the server before
/// paging (<see cref="PlatformAdminSortKeys.ClientLoginLogs"/>; default newest login first).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record ListClientLoginLogsQuery(
    Guid? TenantId,
    string? Search,
    DateTimeOffset? LoginFromUtc,
    DateTimeOffset? LoginToUtc,
    DateTimeOffset? LogoutFromUtc,
    DateTimeOffset? LogoutToUtc,
    string? Device,
    int? MinMinutes,
    int? MaxMinutes,
    string? Status,
    int? Page,
    int? PageSize,
    string? Sort = null,
    string? Direction = null) : IQuery<Paged<ClientLoginLogItem>>, IPlatformAdministrationRequest, IClientLoginLogCriteria;

public sealed class ListClientLoginLogsValidator : AbstractValidator<ListClientLoginLogsQuery>
{
    public ListClientLoginLogsValidator()
    {
        ClientLoginLogRules.Apply(this);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(PlatformAdminSortKeys.ClientLoginLogs);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListClientLoginLogsHandler(IClientLoginLogReader reader, ICurrentTenant currentTenant, IClock clock) : IQueryHandler<ListClientLoginLogsQuery, Paged<ClientLoginLogItem>>
{
    public async Task<Paged<ClientLoginLogItem>> Handle(ListClientLoginLogsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var filter = ClientLoginLogRules.FilterOf(query);
        var sort = ListSort.Of(query.Sort, query.Direction);
        var page = PageRequest.Normalized(query.Page, query.PageSize);
        var device = ClientLoginLogRules.DeviceOf(query.Device);
        var now = clock.UtcNow;

        if (device is null)
        {
            var rows = await reader.ListAsync(filter, sort, page, now, cancellationToken);
            return new Paged<ClientLoginLogItem>(rows.Items.Select(ClientLoginLogRules.ToItem).ToList(), rows.Page, rows.PageSize, rows.TotalCount);
        }

        // The device is read from the user agent, which the database cannot classify: the filtered families (every other
        // filter and the order applied on the server, capped like the export) are classified here and paged in memory.
        var all = await reader.ListAllAsync(filter, sort, ClientLoginLogCsv.MaxRows, now, cancellationToken);
        var matching = all.Take(ClientLoginLogCsv.MaxRows).Select(ClientLoginLogRules.ToItem).Where(item => item.Device == device).ToList();
        return new Paged<ClientLoginLogItem>(matching.Skip(page.Skip).Take(page.PageSize).ToList(), page.Page, page.PageSize, matching.Count);
    }
}

/// <summary>A generated export: UTF-8 bytes with a byte-order mark, the file name and whether the cap cut it.</summary>
public sealed record ClientLoginLogExportFile(string FileName, byte[] Content, int Rows, bool Truncated);

/// <summary>The same history as CSV (headers in the requested language), the first <see cref="ClientLoginLogCsv.MaxRows"/> rows of the filter in the requested order.</summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record ExportClientLoginLogsQuery(
    Guid? TenantId,
    string? Search,
    DateTimeOffset? LoginFromUtc,
    DateTimeOffset? LoginToUtc,
    DateTimeOffset? LogoutFromUtc,
    DateTimeOffset? LogoutToUtc,
    string? Device,
    int? MinMinutes,
    int? MaxMinutes,
    string? Status,
    string? Language,
    string? Sort = null,
    string? Direction = null) : IQuery<ClientLoginLogExportFile>, IPlatformAdministrationRequest, IClientLoginLogCriteria;

public sealed class ExportClientLoginLogsValidator : AbstractValidator<ExportClientLoginLogsQuery>
{
    public ExportClientLoginLogsValidator()
    {
        ClientLoginLogRules.Apply(this);
        RuleFor(query => query.Language).Must(language => language is null or "en" or "ar").WithMessage("The language must be 'en' or 'ar'.");
        RuleFor(query => query.Sort).SortKeyOf(PlatformAdminSortKeys.ClientLoginLogs);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ExportClientLoginLogsHandler(IClientLoginLogReader reader, ICurrentTenant currentTenant, IClock clock) : IQueryHandler<ExportClientLoginLogsQuery, ClientLoginLogExportFile>
{
    public async Task<ClientLoginLogExportFile> Handle(ExportClientLoginLogsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var filter = ClientLoginLogRules.FilterOf(query);
        var device = ClientLoginLogRules.DeviceOf(query.Device);
        var now = clock.UtcNow;

        var rows = await reader.ListAllAsync(filter, ListSort.Of(query.Sort, query.Direction), ClientLoginLogCsv.MaxRows, now, cancellationToken);
        var truncated = rows.Count > ClientLoginLogCsv.MaxRows;
        var items = rows.Take(ClientLoginLogCsv.MaxRows).Select(ClientLoginLogRules.ToItem).Where(item => device is null || item.Device == device).ToList();

        var fileName = $"client-login-logs-{now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.csv";
        return new ClientLoginLogExportFile(fileName, ClientLoginLogCsv.Write(items, query.Language == "ar"), items.Count, truncated);
    }
}

/// <summary>The filter criteria both reads carry (the list adds paging, the export the language).</summary>
public interface IClientLoginLogCriteria
{
    Guid? TenantId { get; }

    string? Search { get; }

    DateTimeOffset? LoginFromUtc { get; }

    DateTimeOffset? LoginToUtc { get; }

    DateTimeOffset? LogoutFromUtc { get; }

    DateTimeOffset? LogoutToUtc { get; }

    string? Device { get; }

    int? MinMinutes { get; }

    int? MaxMinutes { get; }

    string? Status { get; }
}

/// <summary>The shape rules and the mapping shared by the list and the export.</summary>
public static class ClientLoginLogRules
{
    public const int MaxSearchLength = 100;

    /// <summary>Shape only: lengths, known names, ordered windows, non-negative and ordered minutes (VALIDATION-400).</summary>
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : IClientLoginLogCriteria
    {
        ArgumentNullException.ThrowIfNull(validator);
        validator.RuleFor(query => query.Search).MaximumLength(MaxSearchLength);
        validator.RuleFor(query => query.LoginToUtc).Must((query, to) => Ordered(query.LoginFromUtc, to)).WithMessage("'loginFromUtc' must not be after 'loginToUtc'.");
        validator.RuleFor(query => query.LogoutToUtc).Must((query, to) => Ordered(query.LogoutFromUtc, to)).WithMessage("'logoutFromUtc' must not be after 'logoutToUtc'.");
        validator.RuleFor(query => query.Device).Must(value => string.IsNullOrEmpty(value) || ClientLoginLogValues.Devices.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"The device is one of: {string.Join(", ", ClientLoginLogValues.Devices)}.");
        validator.RuleFor(query => query.MinMinutes).GreaterThanOrEqualTo(0).When(query => query.MinMinutes is not null);
        validator.RuleFor(query => query.MaxMinutes).GreaterThanOrEqualTo(0).When(query => query.MaxMinutes is not null);
        validator.RuleFor(query => query.MaxMinutes).Must((query, max) => query.MinMinutes is not { } min || max is not { } bound || min <= bound).WithMessage("'minMinutes' must not exceed 'maxMinutes'.");
        validator.RuleFor(query => query.Status).Must(value => string.IsNullOrEmpty(value) || ClientLoginLogValues.Statuses.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"The status is one of: {string.Join(", ", ClientLoginLogValues.Statuses)}.");
    }

    public static ClientLoginLogFilter FilterOf(IClientLoginLogCriteria query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new ClientLoginLogFilter(
            query.TenantId,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.LoginFromUtc,
            query.LoginToUtc,
            query.LogoutFromUtc,
            query.LogoutToUtc,
            query.MinMinutes,
            query.MaxMinutes,
            string.IsNullOrWhiteSpace(query.Status) ? null : ClientLoginLogValues.Statuses.First(known => string.Equals(known, query.Status.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The canonical device name of the filter (<c>mobile</c> → <c>Mobile</c>), null for none.</summary>
    public static string? DeviceOf(string? device) =>
        string.IsNullOrWhiteSpace(device) ? null : ClientLoginLogValues.Devices.First(known => string.Equals(known, device.Trim(), StringComparison.OrdinalIgnoreCase));

    public static ClientLoginLogItem ToItem(ClientLoginLogRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var agent = UserAgentParser.Parse(row.UserAgent);
        return new ClientLoginLogItem(
            row.FamilyId,
            row.TenantId,
            row.ClientNameEn,
            row.ClientNameAr,
            row.UserId,
            row.Username,
            row.DisplayName,
            row.LoginAtUtc,
            row.EndedAtUtc,
            row.LastSeenAtUtc,
            row.IpAddress,
            agent.Device.ToString(),
            agent.Os,
            agent.Browser,
            row.DurationMinutes,
            row.Status,
            row.RevocationReason);
    }

    private static bool Ordered(DateTimeOffset? from, DateTimeOffset? to) => from is null || to is null || from <= to;
}

/// <summary>
/// The CSV of the login logs (RFC 4180): comma separated, CRLF, quoted where needed, cells a spreadsheet would read as a
/// formula prefixed with an apostrophe, UTF-8 with a byte-order mark. Times are UTC, <c>yyyy-MM-dd HH:mm:ss</c>.
/// </summary>
public static class ClientLoginLogCsv
{
    public const int MaxRows = 10_000;

    private static readonly string[] HeadersEn = ["Client", "User", "Username", "Login (UTC)", "Logout (UTC)", "IP address", "Device", "OS", "Browser", "Duration (min)", "Status", "Reason"];
    private static readonly string[] HeadersAr = ["العميل", "المستخدم", "اسم المستخدم", "الدخول (UTC)", "الخروج (UTC)", "عنوان IP", "الجهاز", "نظام التشغيل", "المتصفح", "المدة (دقيقة)", "الحالة", "السبب"];

    public static byte[] Write(IReadOnlyList<ClientLoginLogItem> items, bool arabic)
    {
        ArgumentNullException.ThrowIfNull(items);
        var builder = new StringBuilder();
        AppendRow(builder, arabic ? HeadersAr : HeadersEn);
        foreach (var item in items)
        {
            AppendRow(builder,
            [
                (arabic ? item.ClientNameAr ?? item.ClientNameEn : item.ClientNameEn ?? item.ClientNameAr) ?? item.TenantId.ToString(),
                item.DisplayName,
                item.Username,
                Stamp(item.LoginAtUtc),
                item.LogoutAtUtc is { } logout ? Stamp(logout) : string.Empty,
                item.IpAddress ?? string.Empty,
                item.Device,
                item.Os,
                item.Browser,
                item.DurationMinutes.ToString(CultureInfo.InvariantCulture),
                item.Status,
                item.RevocationReason ?? string.Empty,
            ]);
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        var content = new byte[preamble.Length + body.Length];
        preamble.CopyTo(content, 0);
        body.CopyTo(content, preamble.Length);
        return content;
    }

    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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
