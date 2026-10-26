using Platform.Application.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin;

/// <summary>
/// The fields of the subscription wizard (CP-02) a refusal can name (LIS-CR-014 P-SUB): every refusal of start, verify and
/// complete that concerns one field carries it in <see cref="DomainException.Fields"/>, so the wizard marks the field where it
/// lives — and sends the user back to step 1 when a step-1 field is refused at step 3.
/// </summary>
public static class SubscriptionFields
{
    public const string CountryId = "countryId";
    public const string GovernorateId = "governorateId";
    public const string CityId = "cityId";
    public const string DistrictId = "districtId";
    public const string AreaId = "areaId";
    public const string Mobile = "mobile";
    public const string ManagerMobile = "managerMobile";
    public const string Email = "email";
    public const string Code = "code";
    public const string Username = "username";
    public const string Password = "password";

    /// <summary>The wizard field a refusal code is about, or <see langword="null"/> when it concerns the request as a whole.</summary>
    public static string? Of(string code) => code switch
    {
        PlatformAdminCodes.MobileInvalidForCountry => Mobile,
        PlatformAdminCodes.ManagerMobileInvalidForCountry => ManagerMobile,
        PlatformAdminCodes.EmailInvalid => Email,
        PlatformAdminCodes.CountryNotFound or PlatformAdminCodes.CountryInactive => CountryId,
        PlatformAdminCodes.GovernorateNotFound or PlatformAdminCodes.GovernorateInactive or PlatformAdminCodes.GovernorateNotInCountry => GovernorateId,
        PlatformAdminCodes.LocalityCityNotInGovernorate => CityId,
        PlatformAdminCodes.LocalityDistrictNotInCity => DistrictId,
        PlatformAdminCodes.LocalityAreaNotInDistrict => AreaId,
        PlatformAdminCodes.CodeRejected => Code,
        PlatformAdminCodes.UsernameInvalid or PlatformAdminCodes.UsernameTaken or PlatformAdminCodes.UsernameInUse => Username,
        PlatformAdminCodes.PasswordTooShort or PlatformAdminCodes.PasswordContainsUsername => Password,
        _ => null,
    };

    /// <summary>The same refusal naming its field (<see cref="Of"/>); the refusal itself when it already names one or concerns none.</summary>
    public static DomainException Named(DomainException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return refusal.Fields.Count > 0 || Of(refusal.Code) is not { } field
            ? refusal
            : new DomainException(refusal.Code, refusal.Message, refusal.Fault, refusal) { Fields = [field] };
    }
}

/// <summary>
/// The early checks of the wizard (LIS-CR-014 S1/S2), shared by the check endpoint, start and complete. The manager's mobile
/// becomes the administrator's, and user mobiles are unique (<c>ux_platform_users_mobile</c>, validation C-03): a manager mobile a
/// platform user already holds in any spelling (<see cref="IUserMobileNumbers.IsTakenAsync"/>) is refused SUB-011 — the rule that
/// refused the reviewer at <i>Create account</i> as an anonymous <c>UNIQUE-409</c>. The laboratory's own mobile belongs to no user
/// and is not unique (no such rule exists; open question in the intake).
/// </summary>
public static class SubscriptionChecks
{
    /// <summary>Whether a platform user holds the manager's mobile (normalised, or as typed).</summary>
    public static async Task<bool> ManagerMobileInUseAsync(IUserMobileNumbers mobiles, string? managerMobile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mobiles);
        return !string.IsNullOrWhiteSpace(managerMobile) && await mobiles.IsTakenAsync(managerMobile, null, cancellationToken);
    }

    /// <summary>SUB-011 on the field <c>managerMobile</c> when a platform user holds it.</summary>
    public static async Task EnsureManagerMobileFreeAsync(IUserMobileNumbers mobiles, string? managerMobile, CancellationToken cancellationToken)
    {
        if (await ManagerMobileInUseAsync(mobiles, managerMobile, cancellationToken))
        {
            throw MobileInUse();
        }
    }

    public static DomainException MobileInUse() =>
        new(PlatformAdminCodes.MobileInUse, "This mobile already belongs to an account; sign in or use another number.", DomainFault.Conflict) { Fields = [SubscriptionFields.ManagerMobile] };

    public static DomainException UsernameInUse() =>
        new(PlatformAdminCodes.UsernameInUse, "This username is already used.", DomainFault.Conflict) { Fields = [SubscriptionFields.Username] };
}
