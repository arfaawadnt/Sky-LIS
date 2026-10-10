namespace Platform.Domain.Treasury;

/// <summary>
/// State table of a treasury access request (intake §6). Pending is the only editable state; Approved is
/// frozen but may still be revoked; Rejected, Revoked and Cancelled are terminal.
/// </summary>
public enum TreasuryAccessStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Revoked = 3,
    Cancelled = 4,
}
