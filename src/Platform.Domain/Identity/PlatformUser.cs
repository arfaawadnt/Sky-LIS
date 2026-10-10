using Platform.Domain.Organization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Identity;

/// <summary>
/// Global credentials (D-06): one row per person across every tenant, stored in <c>identity.platform_users</c>
/// outside row-level security. Everything tenant-specific (role, scope, profile) lives on the
/// <see cref="Tenancy.TenantMembership"/>. Queries against this aggregate are keyed by the actor's own id, a
/// session id or a username at sign-in; never listed across tenants by tenant users.
/// </summary>
public sealed class PlatformUser : AggregateRoot
{
    public const int MaxUsernameLength = 64;
    public const int MaxEmailLength = 254;
    public const int MaxDisplayNameLength = 200;
    public const int MaxPasswordHashLength = 512;
    public const int MaxMobileLength = 20;

    private PlatformUser()
    {
        Username = string.Empty;
        Email = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    private PlatformUser(Guid id, string username, string email, string displayName, string passwordHash, DateTimeOffset nowUtc)
        : base(id)
    {
        Username = username;
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        PasswordChangedAtUtc = nowUtc;
        SecurityStamp = Guid.CreateVersion7();
        IsActive = true;
    }

    /// <summary>Sign-in name, normalised to lower case; unique platform-wide.</summary>
    public string Username { get; private set; }

    /// <summary>Normalised to lower case; unique platform-wide; used for recovery, never carried in a token (D-02).</summary>
    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>
    /// The mobile number password recovery sends its one-time code to (CP-03), normalised to digits with an optional
    /// leading <c>+</c>; unique among users that have one. <see langword="null"/> means recovery by mobile is impossible.
    /// </summary>
    public string? Mobile { get; private set; }

    /// <summary>Argon2id PHC string produced by the hashing port (02 §6.1). The domain never sees a plaintext password.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Rotates on every credential change; a session whose stamp no longer matches is stale.</summary>
    public Guid SecurityStamp { get; private set; }

    public DateTimeOffset PasswordChangedAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Consecutive failed credential <em>checks</em>; 429s and envelope refusals never count (02 §5.1).</summary>
    public int FailedPasswordAttempts { get; private set; }

    public bool IsLockedOut { get; private set; }

    public DateTimeOffset? LockoutEndUtc { get; private set; }

    /// <summary>The next sign-in must replace the password (set by an administrator's reset or at creation; SRS CP-38). Cleared by a password change.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>The access end date of LDM manual §12.6 step 5 ("Password Expire" + "Expire Date"): from this instant the user cannot sign in and live sessions end. <see langword="null"/> = no expiry.</summary>
    public DateTimeOffset? PasswordExpiresAtUtc { get; private set; }

    /// <summary>Locked by an administrator (no end), as opposed to the automatic lockout after failed checks.</summary>
    public bool IsLockedByAdministrator => IsLockedOut && LockoutEndUtc is null;

    /// <summary>Optimistic concurrency token, mapped onto PostgreSQL's <c>xmin</c> system column (01 §4.2 item 9).</summary>
    public uint RowVersion { get; private set; }

    public static PlatformUser Register(string username, string email, string displayName, string passwordHash, DateTimeOffset nowUtc, string? mobile = null)
    {
        var user = new PlatformUser(
            Guid.CreateVersion7(),
            NormaliseUsername(username),
            NormaliseEmail(email),
            CleanDisplayName(displayName),
            RequireHash(passwordHash),
            nowUtc)
        {
            Mobile = NormaliseMobile(mobile),
        };

        user.Raise(new PlatformUserRegistered(user.Id, user.Username, nowUtc));
        return user;
    }

    /// <summary>Sets or clears the recovery mobile. Uniqueness among users is the repository's check and the database's index.</summary>
    public void SetMobile(string? mobile) => Mobile = NormaliseMobile(mobile);

    /// <summary>
    /// CP-03: a one-time code sent to the registered mobile proved possession; the password is replaced and the
    /// security stamp rotates. The caller revokes every live session of the user (02 §4.3) and commits once.
    /// </summary>
    public void RecoverPassword(string newPasswordHash, DateTimeOffset nowUtc)
    {
        Require(IsActive, IdentityCodes.UserInactive, "The account is inactive.");

        PasswordHash = RequireHash(newPasswordHash);
        PasswordChangedAtUtc = nowUtc;
        SecurityStamp = Guid.CreateVersion7();
        FailedPasswordAttempts = 0;
        IsLockedOut = false;
        LockoutEndUtc = null;
        MustChangePassword = false;
        Raise(new PlatformUserPasswordRecovered(Id, nowUtc));
    }

    /// <summary>True while a lockout is in force at <paramref name="nowUtc"/>; an elapsed lockout is cleared on the next successful check.</summary>
    public bool IsLockedOutAt(DateTimeOffset nowUtc) => IsLockedOut && (LockoutEndUtc is null || LockoutEndUtc > nowUtc);

    /// <summary>Records a checked, wrong password. Locks the account when the configured threshold is reached (02 §6.1).</summary>
    public void RecordFailedPasswordCheck(DateTimeOffset nowUtc, int lockoutThreshold, TimeSpan lockoutDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lockoutThreshold, 1);

        FailedPasswordAttempts++;
        if (FailedPasswordAttempts >= lockoutThreshold && !IsLockedOutAt(nowUtc))
        {
            IsLockedOut = true;
            LockoutEndUtc = nowUtc + lockoutDuration;
            Raise(new PlatformUserLockedOut(Id, LockoutEndUtc.Value, nowUtc));
        }
    }

    /// <summary>Records a checked, correct password: clears the failure counter and an elapsed lockout.</summary>
    public void RecordSuccessfulPasswordCheck(DateTimeOffset nowUtc)
    {
        Require(IsActive, IdentityCodes.UserInactive, "The account is inactive.");
        Require(!IsLockedOutAt(nowUtc), IdentityCodes.UserLockedOut, "The account is locked.");

        FailedPasswordAttempts = 0;
        IsLockedOut = false;
        LockoutEndUtc = null;
    }

    /// <summary>Administrative unlock; the counter restarts.</summary>
    public void Unlock(DateTimeOffset nowUtc)
    {
        if (!IsLockedOut)
        {
            return;
        }

        IsLockedOut = false;
        LockoutEndUtc = null;
        FailedPasswordAttempts = 0;
        Raise(new PlatformUserUnlocked(Id, nowUtc));
    }

    /// <summary>Stores a new hash and rotates the security stamp; the caller revokes the user's other sessions (02 §4.1).</summary>
    public void ChangePassword(string newPasswordHash, DateTimeOffset nowUtc)
    {
        Require(IsActive, IdentityCodes.UserInactive, "The account is inactive.");

        PasswordHash = RequireHash(newPasswordHash);
        PasswordChangedAtUtc = nowUtc;
        SecurityStamp = Guid.CreateVersion7();
        FailedPasswordAttempts = 0;
        IsLockedOut = false;
        LockoutEndUtc = null;
        MustChangePassword = false;
        Raise(new PlatformUserPasswordChanged(Id, nowUtc));
    }

    /// <summary>
    /// U9 (SRS CP-38): an administrator sets a temporary password. The stamp rotates, the failure counter and an
    /// automatic lockout clear (an administrator's lock stays), and the next sign-in must change it when
    /// <paramref name="mustChange"/> is set. The caller revokes every live session of the user and commits once.
    /// </summary>
    public void ResetPasswordByAdministrator(string newPasswordHash, bool mustChange, DateTimeOffset nowUtc)
    {
        Require(IsActive, IdentityCodes.UserInactive, "The account is inactive.");

        PasswordHash = RequireHash(newPasswordHash);
        PasswordChangedAtUtc = nowUtc;
        SecurityStamp = Guid.CreateVersion7();
        FailedPasswordAttempts = 0;
        if (!IsLockedByAdministrator)
        {
            IsLockedOut = false;
            LockoutEndUtc = null;
        }

        MustChangePassword = mustChange;
        Raise(new PlatformUserPasswordReset(Id, mustChange, nowUtc));
    }

    /// <summary>U7: an administrator bars the account until unlocked (no end date). The caller revokes the live sessions. Idempotent.</summary>
    public void LockByAdministrator(DateTimeOffset nowUtc)
    {
        if (IsLockedByAdministrator)
        {
            return;
        }

        IsLockedOut = true;
        LockoutEndUtc = null;
        SecurityStamp = Guid.CreateVersion7();
        Raise(new PlatformUserLockedByAdministrator(Id, nowUtc));
    }

    /// <summary>Sets or clears the must-change flag (CP-38 "must-change").</summary>
    public void RequirePasswordChange(bool mustChange) => MustChangePassword = mustChange;

    /// <summary>Sets or clears the access end date; a date in the past is refused (ORG-022).</summary>
    public void SetPasswordExpiry(DateTimeOffset? expiresAtUtc, DateTimeOffset nowUtc)
    {
        Require(expiresAtUtc is null || expiresAtUtc > nowUtc, OrganizationCodes.PasswordExpiryInvalid, "The expiry date must be in the future.", DomainFault.Validation);
        PasswordExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>True from the access end date on (LDM manual §12.6 step 5).</summary>
    public bool IsPasswordExpiredAt(DateTimeOffset nowUtc) => PasswordExpiresAtUtc is { } expiry && expiry <= nowUtc;

    /// <summary>Replaces the e-mail address. Uniqueness is the repository's check and the database's index.</summary>
    public void ChangeEmail(string email) => Email = NormaliseEmail(email);

    /// <summary>Rehash-on-login (02 §6.1): stores a hash with current parameters without rotating the stamp or raising an event; the password itself did not change.</summary>
    public void UpgradePasswordHash(string newPasswordHash) => PasswordHash = RequireHash(newPasswordHash);

    public void Rename(string displayName) => DisplayName = CleanDisplayName(displayName);

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        SecurityStamp = Guid.CreateVersion7();
        Raise(new PlatformUserDeactivated(Id, nowUtc));
    }

    public void Reactivate() => IsActive = true;

    public static string NormaliseUsername(string username)
    {
        var value = (username ?? string.Empty).Trim().ToLowerInvariant();
        Require(
            value.Length is >= 4 and <= MaxUsernameLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '@'),
            IdentityCodes.UsernameInvalid,
            $"The username must be 4 to {MaxUsernameLength} characters: letters, digits, '.', '_', '-' or '@'.",
            DomainFault.Validation);
        return value;
    }

    public static string NormaliseEmail(string email)
    {
        var value = (email ?? string.Empty).Trim().ToLowerInvariant();
        var at = value.IndexOf('@', StringComparison.Ordinal);
        Require(
            value.Length is >= 3 and <= MaxEmailLength && at > 0 && at < value.Length - 1 && value.IndexOf('@', at + 1) < 0 && !value.Any(char.IsWhiteSpace),
            IdentityCodes.EmailInvalid,
            "The e-mail address is not valid.",
            DomainFault.Validation);
        return value;
    }

    /// <summary>Digits only, an optional leading <c>+</c>, 8 to 20 characters; spaces, dashes and parentheses are dropped. <see langword="null"/> or blank clears the mobile.</summary>
    public static string? NormaliseMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return null;
        }

        var compact = new string(mobile.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        var plus = compact.StartsWith('+');
        var digits = plus ? compact[1..] : compact;
        Require(
            digits.Length is >= 8 and <= MaxMobileLength - 1 && digits.All(char.IsAsciiDigit),
            IdentityCodes.MobileInvalid,
            "The mobile number must be 8 to 19 digits, optionally starting with '+'.",
            DomainFault.Validation);
        return plus ? "+" + digits : digits;
    }

    private static string CleanDisplayName(string displayName)
    {
        var value = (displayName ?? string.Empty).Trim();
        Require(value.Length is >= 1 and <= MaxDisplayNameLength, IdentityCodes.DisplayNameInvalid, $"The display name must be 1 to {MaxDisplayNameLength} characters.", DomainFault.Validation);
        return value;
    }

    private static string RequireHash(string passwordHash)
    {
        Require(
            !string.IsNullOrWhiteSpace(passwordHash) && passwordHash.Length <= MaxPasswordHashLength,
            IdentityCodes.PasswordHashMissing,
            "A password hash is required.",
            DomainFault.Validation);
        return passwordHash;
    }
}

/// <summary>U9: an administrator set a temporary password; <paramref name="MustChange"/> forces a change at the next sign-in.</summary>
public sealed record PlatformUserPasswordReset(Guid UserId, bool MustChange, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>U7: an administrator locked the account until it is unlocked.</summary>
public sealed record PlatformUserLockedByAdministrator(Guid UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
