using Microsoft.EntityFrameworkCore;
using Platform.Application.Identity;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Identity;

/// <summary>
/// <see cref="IUserMobileNumbers"/> over the platform's countries (<c>saas.countries</c>, active only) and the laboratory's
/// country (<c>saas.tenants.country_id</c>). The countries are read once per scope. Validation 2026-10-11, C-03.
/// </summary>
internal sealed class UserMobileNumbers(AppDbContext context, IPlatformUserRepository users) : IUserMobileNumbers
{
    private const string Field = "The mobile number";
    private IReadOnlyList<Country>? _countries;

    public async Task<string?> CanonicalAsync(string? input, Guid? tenantId, CancellationToken cancellationToken)
    {
        var generic = PlatformUser.NormaliseMobile(input);   // IDN-005 for anything that is not a phone number at all
        if (generic is null)
        {
            return null;
        }

        var raw = input!.Trim();
        var countries = await CountriesAsync(cancellationToken).ConfigureAwait(false);
        if (IsInternational(raw, out var international))
        {
            var byDial = countries
                .Where(country => international.StartsWith(country.DialCode[1..], StringComparison.Ordinal))
                .OrderByDescending(country => country.DialCode.Length)
                .FirstOrDefault();
            return byDial is null ? "+" + international : byDial.NormaliseMobile("+" + international, IdentityCodes.MobileInvalid, Field);
        }

        if (tenantId is { } tenant && !TenantIds.IsPlatform(tenant))
        {
            var countryId = await context.Set<Tenant>().AsNoTracking()
                .Where(row => row.Id == tenant)
                .Select(row => (Guid?)row.CountryId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var own = countryId is null ? null : countries.FirstOrDefault(country => country.Id == countryId)
                ?? await context.Set<Country>().AsNoTracking().FirstOrDefaultAsync(country => country.Id == countryId, cancellationToken).ConfigureAwait(false);
            if (own is not null)
            {
                return own.NormaliseMobile(raw, IdentityCodes.MobileInvalid, Field);
            }
        }

        var accepting = countries.Select(country => TryNormalise(country, raw)).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return accepting.Count == 1 ? accepting[0] : generic;
    }

    public async Task<IReadOnlyList<string>> SpellingsAsync(string? input, CancellationToken cancellationToken)
    {
        string? generic;
        try
        {
            generic = PlatformUser.NormaliseMobile(input);
        }
        catch (DomainException)
        {
            return [];
        }

        if (generic is null)
        {
            return [];
        }

        var raw = input!.Trim();
        var spellings = new List<string> { generic };
        if (IsInternational(raw, out var international))
        {
            spellings.Add("+" + international);
            spellings.Add("00" + international);
        }

        foreach (var country in await CountriesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (TryNormalise(country, raw) is not { } e164)
            {
                continue;
            }

            var dial = country.DialCode[1..];
            var national = e164[country.DialCode.Length..];
            spellings.Add(e164);
            spellings.Add(national);
            spellings.Add("0" + national);
            spellings.Add("00" + dial + national);
            spellings.Add(dial + national);
        }

        return spellings.Distinct(StringComparer.Ordinal).ToList();
    }

    public async Task<bool> IsTakenAsync(string? input, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var spellings = await SpellingsAsync(input, cancellationToken).ConfigureAwait(false);
        if (spellings.Count == 0)
        {
            return false;
        }

        var holders = await users.ListByMobileSpellingsAsync(spellings, cancellationToken).ConfigureAwait(false);
        return holders.Any(user => user.Id != exceptUserId);
    }

    private async Task<IReadOnlyList<Country>> CountriesAsync(CancellationToken cancellationToken) =>
        _countries ??= await context.Set<Country>().AsNoTracking().Where(country => country.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary><c>+…</c> or <c>00…</c>: the digits after the prefix, otherwise <see langword="false"/>.</summary>
    private static bool IsInternational(string raw, out string digits)
    {
        var all = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (raw.StartsWith('+'))
        {
            digits = all;
            return true;
        }

        if (all.StartsWith("00", StringComparison.Ordinal))
        {
            digits = all[2..];
            return true;
        }

        digits = string.Empty;
        return false;
    }

    private static string? TryNormalise(Country country, string raw)
    {
        try
        {
            return country.NormaliseMobile(raw, IdentityCodes.MobileInvalid, Field);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
