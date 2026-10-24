using System.Globalization;
using System.Text;
using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.PlatformAdmin.Queries;

// LIS-CR-007 A-01: the clients with no activity for N days (SA-04 "Silent" filter) and their CSV, for the platform tenant
// only (PLT-403). The list is small (one row per silent client) and never paged.

/// <summary><paramref name="Days"/>: 1…90, default 3 (<see cref="ClientSilence"/>).</summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record ListSilentClientsQuery(int? Days) : IQuery<SilentClientList>, IPlatformAdministrationRequest;

public sealed class ListSilentClientsValidator : AbstractValidator<ListSilentClientsQuery>
{
    public ListSilentClientsValidator() => RuleFor(query => query.Days).SilentWindow();
}

public sealed class ListSilentClientsHandler(IClientLastActivityReader reader, IPortalAddressOptions portal, ICurrentTenant currentTenant, IClock clock)
    : IQueryHandler<ListSilentClientsQuery, SilentClientList>
{
    public async Task<SilentClientList> Handle(ListSilentClientsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var now = clock.UtcNow;
        var days = query.Days ?? ClientSilence.DefaultDays;
        var items = await SilentClientFinder.FindAsync(reader, portal.PortalBaseUrl, now, days, cancellationToken);
        return new SilentClientList(days, now, items.Count(item => item.Status == nameof(TenantStatus.Trial)), items);
    }
}

/// <summary>The CSV of the silent clients (UTF-8 with BOM); headers in <paramref name="Language"/> (en / ar).</summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record ExportSilentClientsQuery(int? Days, string? Language) : IQuery<SilentClientsExportFile>, IPlatformAdministrationRequest;

public sealed record SilentClientsExportFile(string FileName, byte[] Content, int Rows);

public sealed class ExportSilentClientsValidator : AbstractValidator<ExportSilentClientsQuery>
{
    public ExportSilentClientsValidator()
    {
        RuleFor(query => query.Days).SilentWindow();
        RuleFor(query => query.Language).Must(language => language is null or "en" or "ar").WithMessage("The language must be 'en' or 'ar'.");
    }
}

public sealed class ExportSilentClientsHandler(IClientLastActivityReader reader, IPortalAddressOptions portal, ICurrentTenant currentTenant, IClock clock)
    : IQueryHandler<ExportSilentClientsQuery, SilentClientsExportFile>
{
    public async Task<SilentClientsExportFile> Handle(ExportSilentClientsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var now = clock.UtcNow;
        var days = query.Days ?? ClientSilence.DefaultDays;
        var items = await SilentClientFinder.FindAsync(reader, portal.PortalBaseUrl, now, days, cancellationToken);
        var fileName = $"silent-clients-{days}d-{now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.csv";
        return new SilentClientsExportFile(fileName, SilentClientsCsv.Write(items, query.Language == "ar"), items.Count);
    }
}

public static class SilentClientRules
{
    /// <summary>VALIDATION-400 for a window outside <see cref="ClientSilence.MinDays"/>…<see cref="ClientSilence.MaxDays"/>.</summary>
    public static IRuleBuilderOptions<T, int?> SilentWindow<T>(this IRuleBuilder<T, int?> rule) =>
        rule.Must(days => days is null || ClientSilence.IsValidDays(days.Value))
            .WithMessage($"'days' must be between {ClientSilence.MinDays} and {ClientSilence.MaxDays}.");
}

/// <summary>
/// The CSV of the silent clients (RFC 4180, CRLF, UTF-8 with BOM; cells escaped as <see cref="ClientLoginLogCsv.Escape"/>
/// does, so a mobile reads <c>'+20…</c> and never runs as a formula). Times are UTC, <c>yyyy-MM-dd HH:mm</c>.
/// </summary>
public static class SilentClientsCsv
{
    private static readonly string[] HeadersEn = ["Client (English)", "Client (Arabic)", "Portal address", "Status", "Created (UTC)", "Last sign-in (UTC)", "Last registration (UTC)", "Last activity (UTC)", "Days silent", "Administrator", "Administrator mobile"];
    private static readonly string[] HeadersAr = ["العميل (إنجليزي)", "العميل (عربي)", "عنوان البوابة", "الحالة", "تاريخ الإنشاء (UTC)", "آخر دخول (UTC)", "آخر تسجيل (UTC)", "آخر نشاط (UTC)", "أيام الانقطاع", "المسؤول", "جوال المسؤول"];

    public static byte[] Write(IReadOnlyList<SilentClientItem> items, bool arabic)
    {
        ArgumentNullException.ThrowIfNull(items);
        var builder = new StringBuilder();
        AppendRow(builder, arabic ? HeadersAr : HeadersEn);
        foreach (var item in items)
        {
            AppendRow(builder,
            [
                item.NameEn,
                item.NameAr,
                item.PortalUrl,
                item.Status,
                Stamp(item.CreatedAtUtc),
                Stamp(item.LastSignInAtUtc),
                Stamp(item.LastRegistrationAtUtc),
                Stamp(item.LastActivityAtUtc),
                item.SilentDays.ToString(CultureInfo.InvariantCulture),
                item.AdministratorName,
                item.AdministratorMobile,
            ]);
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        var content = new byte[preamble.Length + body.Length];
        preamble.CopyTo(content, 0);
        body.CopyTo(content, preamble.Length);
        return content;
    }

    private static string Stamp(DateTimeOffset? at) => at is { } value ? value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty;

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> cells)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(ClientLoginLogCsv.Escape(cells[index]));
        }

        builder.Append("\r\n");
    }
}
