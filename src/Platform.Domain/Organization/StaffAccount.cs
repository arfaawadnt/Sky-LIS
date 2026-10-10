using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Organization;

/// <summary>
/// Rules of a staff account created by a tenant administrator (SRS CP-38 / SA-07): the username is 4–30 characters of
/// lower-case letters, digits, dots and underscores (INV-ORG-013). Stricter than the platform-wide
/// <see cref="Identity.PlatformUser.NormaliseUsername"/>, which also admits machine and seeded names.
/// </summary>
public static class StaffAccount
{
    public const int MinUsernameLength = 4;
    public const int MaxUsernameLength = 30;

    public static string CleanUsername(string username)
    {
        var value = (username ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length is < MinUsernameLength or > MaxUsernameLength || !value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_'))
        {
            throw new DomainException(OrganizationCodes.UsernameInvalid, $"The username must be {MinUsernameLength} to {MaxUsernameLength} characters: letters, digits, '.' or '_'.", DomainFault.Validation);
        }

        return value;
    }
}
