namespace Platform.Application.Patients;

/// <summary>
/// The time zone of the current laboratory: the IANA zone of the country of its client record (<c>saas.tenants</c> →
/// <c>saas.countries.time_zone</c>, e.g. Africa/Cairo); without one, the tenant parameter <c>timeZone</c> (the laboratory's,
/// else the platform's value), else the platform default Africa/Cairo (round 2 R2-01: the demo laboratory has no client
/// record and fell back to the UTC day). The Patients module dates a registration, a stated age and the age it shows by the
/// laboratory's calendar day, not the UTC one (validation v0.5.0 R-11). <see langword="null"/> only outside a tenant or
/// when no step names a zone this host knows; the caller then falls back to the UTC date. The adapter is
/// <c>Platform.Infrastructure.Persistence.Patients.LaboratoryTimeZone</c>.
/// </summary>
public interface ILaboratoryTimeZone
{
    Task<TimeZoneInfo?> FindAsync(CancellationToken cancellationToken);
}
