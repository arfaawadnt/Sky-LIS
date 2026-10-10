using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Observability;
using Platform.Infrastructure.Persistence;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Audit;

/// <summary>
/// The security event log of 02 §7: every event goes to the structured log (<see cref="LoggerSecurityEventLog"/>)
/// and is appended to <c>audit.security_events</c> with the correlation id, client address and user-agent hash of
/// the current request. The insert runs as one statement on the scope's connection (auto-committed outside a
/// transaction, part of it inside), never through the unit of work: a refusal path that writes an event and then
/// throws still leaves the row. A ledger that cannot be reached is logged as a warning, never thrown into a request.
/// </summary>
public sealed partial class SecurityEventLedger(AppDbContext context, LoggerSecurityEventLog log, IRequestContext request, IClock clock, ILogger<SecurityEventLedger> logger) : ISecurityEventLog
{
    public async Task WriteAsync(SecurityEvent securityEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        await log.WriteAsync(securityEvent, cancellationToken).ConfigureAwait(false);

        var id = Guid.CreateVersion7();
        var detail = securityEvent.Detail is { Length: > Persistence.Audit.SecurityEventRow.MaxDetailLength } tooLong ? tooLong[..Persistence.Audit.SecurityEventRow.MaxDetailLength] : securityEvent.Detail;
        var clientIp = System.Net.IPAddress.TryParse(request.ClientIp, out var address) ? address : null;
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO audit.security_events (id, tenant_id, name, severity, actor_user_id, client_ip, user_agent_hash, correlation_id, detail, at_utc)
                VALUES ({id}, {securityEvent.TenantId}::uuid, {securityEvent.Name}, {securityEvent.Severity}, {securityEvent.ActorUserId}::uuid, {clientIp}::inet, {request.UserAgentHash}::bytea, {request.CorrelationId}::text, {detail}::text, {clock.UtcNow})
                """, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogLedgerUnavailable(logger, exception, securityEvent.Name);
        }
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning, Message = "security event {EventName} was logged but could not be appended to audit.security_events")]
    private static partial void LogLedgerUnavailable(ILogger logger, Exception exception, string eventName);
}
