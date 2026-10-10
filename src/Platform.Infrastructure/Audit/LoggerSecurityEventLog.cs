using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions.Audit;

namespace Platform.Infrastructure.Audit;

/// <summary>
/// Writes security events (02 §7) to the structured log with the event name, severity, tenant, actor and detail;
/// never credential material. The append-only, hash-chained <c>audit.security_events</c> appender replaces this
/// with the audit ledger work package; the port and the call sites stay the same.
/// </summary>
public sealed partial class LoggerSecurityEventLog(ILogger<LoggerSecurityEventLog> logger) : ISecurityEventLog
{
    public Task WriteAsync(SecurityEvent securityEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);

        var level = securityEvent.Severity switch
        {
            SecurityEvent.Error => LogLevel.Error,
            SecurityEvent.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };

        LogEvent(logger, level, securityEvent.Name, securityEvent.TenantId, securityEvent.ActorUserId, securityEvent.Detail);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 7000, Message = "security event {EventName} tenant={TenantId} actor={ActorUserId} detail={Detail}")]
    private static partial void LogEvent(ILogger logger, LogLevel level, string eventName, Guid? tenantId, Guid? actorUserId, string? detail);
}
