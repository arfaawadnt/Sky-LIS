using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Patients;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;

namespace Platform.Infrastructure.Persistence.Patients;

/// <summary>
/// Adapter of <see cref="ILaboratoryTimeZone"/>, read once per scope, in this order (round 2 R2-01):
/// <list type="number">
/// <item>the time zone of the country of the current tenant's client record (<c>saas.tenants</c> → <c>saas.countries</c>,
/// readable by id from a tenant session, as the laboratory profile and the mobile normaliser read them);</item>
/// <item>the tenant parameter <see cref="SettingKeys.TimeZone"/>: the laboratory's value, else the platform's value;</item>
/// <item>the platform default, <see cref="SettingKeys.DefaultTimeZone"/> (the parameter's code default).</item>
/// </list>
/// A tenant without a client record (the demo laboratory, a development tenant), a zone unknown on this host, or platform
/// tables not yet migrated (PostgreSQL 42P01) fall through to the next step. <see langword="null"/> only outside a tenant
/// or when no step names a zone this host knows; the caller then uses the UTC day.
/// </summary>
internal sealed class LaboratoryTimeZone(AppDbContext context, ICurrentTenant tenant, ITenantSettings settings) : ILaboratoryTimeZone
{
    private bool _loaded;
    private TimeZoneInfo? _zone;

    public async Task<TimeZoneInfo?> FindAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return _zone;
        }

        _zone = tenant.TenantId is { } tenantId
            ? Resolve(await ReadAsync(tenantId, cancellationToken).ConfigureAwait(false))
                ?? Resolve(await settings.GetAsync(SettingKeys.TimeZone, cancellationToken).ConfigureAwait(false))
                ?? Resolve(SettingKeys.DefaultTimeZone)
            : null;
        _loaded = true;
        return _zone;
    }

    /// <summary>The zone of an IANA (or Windows) id; <see langword="null"/> when blank or unknown on this host.</summary>
    internal static TimeZoneInfo? Resolve(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone) ? zone : null;

    private async Task<string?> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            return await (
                from row in context.Set<Tenant>().AsNoTracking().Where(row => row.Id == tenantId)
                join country in context.Set<Country>().AsNoTracking() on row.CountryId equals country.Id
                select country.TimeZone).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return null;
        }
    }
}
