namespace Platform.Application.Abstractions.Observability;

/// <summary>
/// What the host knows about the current unit of work that audit rows record (01 §5.2, §4.8): the correlation id
/// echoed or minted per request (the outbox processor sets the event's trace parent), the client address and a hash
/// of the user agent for security events. Filled by the host, read by the persistence layer; never by handlers.
/// </summary>
public interface IRequestContext
{
    string? CorrelationId { get; }

    string? ClientIp { get; }

    /// <summary>SHA-256 of the user agent, so the ledger never stores the string itself.</summary>
    byte[]? UserAgentHash { get; }
}
