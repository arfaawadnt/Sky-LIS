namespace Platform.Contracts.PlatformAdmin;

// Client login logs (SA-08b, change request LIS-CR-003 §2): one row per browser-session family of a client user.

/// <summary>
/// One sign-in of a client user and how it ended. Times are UTC. <paramref name="Status"/>: Active (the session still
/// lives), SignedOut (the user signed out: <paramref name="LogoutAtUtc"/> is that moment), Revoked (ended by an
/// administrator, a password change, token reuse or a suspension — <paramref name="RevocationReason"/> says which),
/// Expired (ran out without a sign-out: <paramref name="LogoutAtUtc"/> is the expiry). <paramref name="DurationMinutes"/>
/// is logout − login, or last activity − login while the session is active. <paramref name="Device"/>: Computer, Mobile,
/// Tablet or Unknown; <paramref name="Os"/> and <paramref name="Browser"/> are family + version, or "Unknown".
/// </summary>
public sealed record ClientLoginLogItem(
    Guid FamilyId,
    Guid TenantId,
    string? ClientNameEn,
    string? ClientNameAr,
    Guid UserId,
    string Username,
    string DisplayName,
    DateTimeOffset LoginAtUtc,
    DateTimeOffset? LogoutAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string? IpAddress,
    string Device,
    string Os,
    string Browser,
    int DurationMinutes,
    string Status,
    string? RevocationReason);

/// <summary>The device classes and statuses the list filters by (the strings the API accepts).</summary>
public static class ClientLoginLogValues
{
    public static readonly IReadOnlyList<string> Devices = ["Computer", "Mobile", "Tablet", "Unknown"];
    public static readonly IReadOnlyList<string> Statuses = ["Active", "SignedOut", "Revoked", "Expired"];
}
