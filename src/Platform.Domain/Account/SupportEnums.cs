namespace Platform.Domain.Account;

/// <summary>How urgent the laboratory considers a ticket (SRS CP-67 "priority").</summary>
public enum SupportTicketPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>
/// State table of a support ticket (intake LIS-INT-ACC-001 §6): Open (raised), InProgress (the desk works on it),
/// Resolved (answered; a laboratory reply reopens it), Closed (terminal, kept for the record, BR-15).
/// </summary>
public enum SupportTicketStatus
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
    Closed = 3,
}
