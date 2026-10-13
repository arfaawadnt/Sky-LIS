using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Patients;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;

namespace Platform.Infrastructure.Persistence.Patients;

/// <summary>
/// Adapter of <see cref="ILaboratoryCountry"/>, read once per scope: the country of the tenant's client record, else the
/// only active country whose time zone is the laboratory's <see cref="SettingKeys.TimeZone"/> parameter. Platform tables
/// not yet migrated (PostgreSQL 42P01) answer <see langword="null"/>, as in <see cref="LaboratoryTimeZone"/>.
/// </summary>
internal sealed class LaboratoryCountryLookup(AppDbContext context, ICurrentTenant tenant, ITenantSettings settings) : ILaboratoryCountry
{
    private bool _loaded;
    private LaboratoryCountry? _country;

    public async Task<LaboratoryCountry?> FindAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return _country;
        }

        _country = tenant.TenantId is { } tenantId ? await ReadAsync(tenantId, cancellationToken).ConfigureAwait(false) : null;
        _loaded = true;
        return _country;
    }

    private async Task<LaboratoryCountry?> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var own = await (
                from row in context.Set<Tenant>().AsNoTracking().Where(row => row.Id == tenantId)
                join country in context.Set<Country>().AsNoTracking() on row.CountryId equals country.Id
                select new LaboratoryCountry(country.IsoCode, country.MobileLength)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (own is not null)
            {
                return own;
            }

            var zone = (await settings.GetAsync(SettingKeys.TimeZone, cancellationToken).ConfigureAwait(false)).Trim();
            var byZone = await context.Set<Country>().AsNoTracking()
                .Where(country => country.IsActive && country.TimeZone == zone)
                .Select(country => new LaboratoryCountry(country.IsoCode, country.MobileLength))
                .Take(2)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return byZone.Count == 1 ? byZone[0] : null;
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return null;
        }
    }
}
