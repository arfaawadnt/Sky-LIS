namespace Platform.Domain.Account;

/// <summary>
/// Refusal codes of the Account &amp; onboarding module (intake LIS-INT-ACC-001 §7–§8): <c>ACC</c> self-service account
/// (preferences, password change, profile), <c>SUP</c> support tickets. 001–099 validation and state, 100–199 conflicts,
/// 403 scope, 404 not found.
/// </summary>
public static class AccountCodes
{
    // Self-service account (A1, A2)
    public const string CurrentPasswordRejected = "ACC-010";
    public const string AccountLocked = "ACC-011";
    public const string PasswordLengthInvalid = "ACC-012";
    public const string PasswordContainsUsername = "ACC-013";
    public const string PasswordUnchanged = "ACC-014";
    public const string SessionActorRequired = "ACC-020";
    public const string MembershipNotFound = "ACC-404";

    /// <summary>CP-05: the dashboard shows today or an earlier day of the user, never a future one (validation A-10).</summary>
    public const string DashboardDayInFuture = "ACC-030";

    // Laboratory logo (top bar, later letterheads): content rules, existence, laboratory scope
    public const string LogoEmpty = "ACC-040";
    public const string LogoTooLarge = "ACC-041";
    public const string LogoTypeNotAllowed = "ACC-042";
    public const string LogoNotFound = "ACC-045";

    /// <summary>A logo belongs to a laboratory: the platform tenant (Super Admin portal) has none.</summary>
    public const string LaboratoryRequired = "ACC-403";

    // Support tickets (S1–S7)
    public const string SubjectInvalid = "SUP-001";
    public const string DescriptionInvalid = "SUP-002";
    public const string ReplyInvalid = "SUP-003";
    public const string PriorityInvalid = "SUP-004";
    public const string TicketClosed = "SUP-010";
    public const string TransitionNotAllowed = "SUP-011";
    public const string TooManyReplies = "SUP-012";

    /// <summary>INV-SUP-001: the platform desk answers tickets, it never opens one for itself.</summary>
    public const string PlatformCannotOpen = "SUP-020";

    /// <summary>The desk endpoints serve the platform tenant only (MODULES.md §1), whatever keys a laboratory role holds.</summary>
    public const string DeskRequiresPlatform = "SUP-403";
    public const string TicketNotFound = "SUP-404";
}
