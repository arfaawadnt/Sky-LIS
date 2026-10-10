using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Platform.Infrastructure.Persistence.Audit;

/// <summary>
/// The hash of one ledger entry (01 §4.8, C-16): SHA-256 over a canonical, newline-separated record of the stored
/// columns plus the predecessor's hash. Both the writer (the interceptor) and the verifier (the reader) call this with
/// the values exactly as the row stores them: the time truncated to microseconds and the payload in jsonb's own text form.
/// </summary>
public static class AuditChain
{
    public static byte[] Hash(Guid tenantId, long sequence, Guid eventId, string eventType, string aggregateType, string aggregateId, Guid? actorUserId, DateTimeOffset atUtc, string? correlationId, string payload, byte[]? prevHash)
    {
        var canonical = string.Join(
            '\n',
            "lis-audit-v1",
            tenantId.ToString("D"),
            sequence.ToString(CultureInfo.InvariantCulture),
            eventId.ToString("D"),
            eventType,
            aggregateType,
            aggregateId,
            actorUserId?.ToString("D") ?? string.Empty,
            Truncate(atUtc).ToString("O", CultureInfo.InvariantCulture),
            correlationId ?? string.Empty,
            payload,
            prevHash is null ? string.Empty : Convert.ToHexString(prevHash));

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    /// <summary>PostgreSQL stores microseconds; .NET keeps 100-nanosecond ticks. The stored value is the truncated one.</summary>
    public static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
