namespace Platform.Application.Identity;

/// <summary>
/// The mobile numbers of platform users (validation 2026-10-11, C-03). A user's mobile is stored in <b>E.164</b>
/// (<c>+201014141421</c>), normalised with the country rules the client forms use (<c>Country.NormaliseMobile</c>: dial
/// code + the country's national length, trunk <c>0</c> and <c>00</c> prefixes accepted). Rows saved before the rule may
/// hold the number as typed (<c>01014141421</c>, <c>1014141421</c>); lookups therefore match every spelling a number may
/// have been stored under, and existing rows are normalised on their next save (no data migration).
/// </summary>
public interface IUserMobileNumbers
{
    /// <summary>
    /// The form to store: <see langword="null"/> for a blank input; otherwise E.164. An international input
    /// (<c>+…</c>, <c>00…</c>) is checked against the country of its dial code; a national one against the country of
    /// <paramref name="tenantId"/> (a laboratory), or — for the platform tenant, which has no country — against the one
    /// active country whose rule accepts it. When no country decides, the number keeps the generic shape check
    /// (<c>PlatformUser.NormaliseMobile</c>). Refusals are <c>IDN-005</c> with the country's rule in the message.
    /// </summary>
    Task<string?> CanonicalAsync(string? input, Guid? tenantId, CancellationToken cancellationToken);

    /// <summary>Every stored spelling the number may have (E.164 for each active country that accepts it, its national, trunk and <c>00</c> forms, and the input's generic form); empty when the input is not a mobile number.</summary>
    Task<IReadOnlyList<string>> SpellingsAsync(string? input, CancellationToken cancellationToken);

    /// <summary>Whether another user (not <paramref name="exceptUserId"/>) holds the number in any spelling: the uniqueness rule of user mobiles.</summary>
    Task<bool> IsTakenAsync(string? input, Guid? exceptUserId, CancellationToken cancellationToken);
}
