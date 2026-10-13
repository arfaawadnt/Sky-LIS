namespace Platform.Application.Patients;

/// <summary>The laboratory's country as the patient form needs it: ISO code and the digits of a national mobile number (no trunk 0).</summary>
public sealed record LaboratoryCountry(string IsoCode, int MobileLength);

/// <summary>
/// The country of the current laboratory (validation v0.5.0 F-08): the country of its client record (<c>saas.tenants</c> →
/// <c>saas.countries</c>); without one (the demo laboratory, a development tenant), the only active country whose time zone
/// is the laboratory's <c>timeZone</c> parameter (Africa/Cairo → Egypt). The patient form opens on it, so a 10-digit
/// Egyptian number is stored in its trunk form, and the register's mobile search matches a number typed with or without
/// the trunk 0. <see langword="null"/> outside a tenant or when neither step names one country. The adapter is
/// <c>Platform.Infrastructure.Persistence.Patients.LaboratoryCountryLookup</c>.
/// </summary>
public interface ILaboratoryCountry
{
    Task<LaboratoryCountry?> FindAsync(CancellationToken cancellationToken);
}
